using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonkeyTV;

internal readonly record struct SitePermissionKey(string Origin, CoreWebView2PermissionKind Kind);

/// <summary>
/// Coordinates conservative website permission decisions. Monkey TV keeps WebView2 profile
/// persistence disabled and owns this small portable exact-origin decision store so service and
/// full-browser resets can clear it deterministically.
/// </summary>
internal sealed class SessionPermissionStore
{
    private const int MaximumDecisionCount = 512;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly object _sync = new();
    private readonly string? _filePathOverride;
    private readonly Dictionary<SitePermissionKey, StoredPermissionDecision> _decisions = [];
    private readonly Dictionary<SitePermissionKey, Task<CoreWebView2PermissionState>> _pending = [];

    internal SessionPermissionStore(string? filePath = null)
    {
        _filePathOverride = filePath;
        Load();
    }

    private string FilePath => _filePathOverride ?? Path.Combine(PortablePaths.SettingsDirectory, "site-permissions.json");

    internal static bool TryCreateKey(string? rawUri, CoreWebView2PermissionKind kind, out SitePermissionKey key)
    {
        key = default;
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
            return false;

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.IdnHost.ToLowerInvariant();
        var authorityHost = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        key = new SitePermissionKey($"{scheme}://{authorityHost}{port}", kind);
        return true;
    }

    internal Task<CoreWebView2PermissionState> GetOrPromptAsync(
        SitePermissionKey key,
        Func<Task<CoreWebView2PermissionState>> promptAsync)
    {
        lock (_sync)
        {
            if (_decisions.TryGetValue(key, out var decision)) return Task.FromResult(decision.State);
            if (_pending.TryGetValue(key, out var pending)) return pending;

            var request = ResolveAsync(key, promptAsync);
            _pending[key] = request;
            return request;
        }
    }

    private async Task<CoreWebView2PermissionState> ResolveAsync(
        SitePermissionKey key,
        Func<Task<CoreWebView2PermissionState>> promptAsync)
    {
        CoreWebView2PermissionState decision;
        try { decision = await promptAsync(); }
        catch
        {
            lock (_sync) _pending.Remove(key);
            return CoreWebView2PermissionState.Deny;
        }

        lock (_sync)
        {
            _decisions[key] = new StoredPermissionDecision(decision, DateTime.UtcNow);
            _pending.Remove(key);
            TrimAndSaveLocked();
        }
        return decision;
    }

    internal void ClearForHosts(IEnumerable<string> hosts)
    {
        var normalized = hosts
            .Select(host => host.Trim().TrimStart('.').ToLowerInvariant())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) return;

        lock (_sync)
        {
            foreach (var key in _decisions.Keys.Where(key => MatchesAnyHost(key.Origin, normalized)).ToArray())
                _decisions.Remove(key);
            SaveLocked();
        }
    }

    internal void ClearAll()
    {
        lock (_sync)
        {
            _decisions.Clear();
            _pending.Clear();
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception ex) { AppLog.Write("permission", "Could not clear saved site permissions: " + ex.Message); }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var entries = JsonSerializer.Deserialize<List<PersistedPermissionDecision>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            foreach (var entry in entries
                         .Where(entry => entry.State is CoreWebView2PermissionState.Allow or CoreWebView2PermissionState.Deny)
                         .OrderByDescending(entry => entry.UpdatedUtc)
                         .Take(MaximumDecisionCount))
            {
                if (TryCreateKey(entry.Origin, entry.Kind, out var key))
                    _decisions[key] = new StoredPermissionDecision(entry.State, entry.UpdatedUtc);
            }
        }
        catch (Exception ex)
        {
            _decisions.Clear();
            AppLog.Write("permission", "Saved site permissions could not be read: " + ex.Message);
        }
    }

    private void TrimAndSaveLocked()
    {
        while (_decisions.Count > MaximumDecisionCount)
        {
            var oldest = _decisions.MinBy(pair => pair.Value.UpdatedUtc).Key;
            _decisions.Remove(oldest);
        }
        SaveLocked();
    }

    private void SaveLocked()
    {
        try
        {
            if (_decisions.Count == 0)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var entries = _decisions
                .OrderByDescending(pair => pair.Value.UpdatedUtc)
                .Select(pair => new PersistedPermissionDecision(pair.Key.Origin, pair.Key.Kind,
                    pair.Value.State, pair.Value.UpdatedUtc))
                .ToList();
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) { AppLog.Write("permission", "Saved site permissions could not be updated: " + ex.Message); }
    }

    private static bool MatchesAnyHost(string origin, IReadOnlyList<string> hosts)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return hosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                 uri.Host.EndsWith('.' + host, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record StoredPermissionDecision(CoreWebView2PermissionState State, DateTime UpdatedUtc);
    private sealed record PersistedPermissionDecision(string Origin, CoreWebView2PermissionKind Kind,
        CoreWebView2PermissionState State, DateTime UpdatedUtc);
}
