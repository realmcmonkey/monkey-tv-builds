using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace MonkeyTV;

public sealed record ServiceStorage(Guid ServiceId, long Bytes);
public sealed record StorageSnapshot(long SettingsBytes, long ArtworkBytes, long BrowserPersistentBytes,
    long BrowserTemporaryBytes, long LogBytes, long OtherBytes, long TotalBytes)
{
    public long BrowserTotalBytes => BrowserPersistentBytes + BrowserTemporaryBytes;
}

public static class StorageService
{
    // These are Chromium/WebView2 runtime caches, metrics, shaders, or crash artifacts. They
    // deliberately exclude Cookies, Network, Local Storage, IndexedDB, CacheStorage and service
    // workers, because clearing those can disrupt real streaming sites and sign-in state.
    private static readonly HashSet<string> DisposableFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnWebGPUCache", "DawnGraphiteCache",
        "GrShaderCache", "ShaderCache", "GPUPersistentCache", "Media Cache", "BrowserMetrics",
        "Crashpad", "Crash Reports"
    };

    // Runtime-delivered lists and language/filter resources are safe to recreate, but unlike
    // ordinary cache they are retained while within budget to avoid needless re-downloads.
    private static readonly HashSet<string> ReplaceableComponentFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Subresource Filter", "Speech Recognition", "hyphen-data", "Trust Protection Lists",
        "PKIMetadata", "CertificateRevocation", "AutoLaunchProtocolsComponent",
        "TrustTokenKeyCommitments", "OriginTrials", "MEIPreload", "extensions_crx_cache", "component_crx_cache"
    };

    public const long TemporaryCacheBudgetBytes = 192L * 1024 * 1024;
    public const long AbnormalTemporaryCacheBytes = 768L * 1024 * 1024;

    public static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }); }
        catch { return 0; }
    }

    public static IReadOnlyList<ServiceStorage> MeasureServices(IEnumerable<ServiceDefinition> services) =>
        services.Select(s => new ServiceStorage(s.Id, DirectorySize(Path.Combine(PortablePaths.ProfilesDirectory, s.Id.ToString("N"))))).ToList();

    public static StorageSnapshot GetSnapshot()
    {
        var settings = DirectorySize(PortablePaths.SettingsDirectory);
        var artwork = DirectorySize(PortablePaths.AssetsDirectory);
        var logs = DirectorySize(PortablePaths.LogsDirectory);
        var browser = DirectorySize(PortablePaths.BrowserDirectory);
        var temporary = GetTemporaryBrowserBytes() + DirectorySize(PortablePaths.CacheDirectory);
        temporary = Math.Min(browser + DirectorySize(PortablePaths.CacheDirectory), temporary);
        var persistent = Math.Max(0, browser - Math.Min(browser, GetTemporaryBrowserBytes()));
        var total = DirectorySize(PortablePaths.DataRoot);
        var other = Math.Max(0, total - settings - artwork - logs - browser - DirectorySize(PortablePaths.CacheDirectory));
        return new(settings, artwork, persistent, temporary, logs, other, total);
    }

    public static long GetTemporaryBrowserBytes() => GetDisposablePaths(PortablePaths.BrowserDirectory, includeReplaceableComponents: true).Sum(path =>
        Directory.Exists(path) ? DirectorySize(path) : SafeFileLength(path));

    public static void ClearTemporaryData(Guid serviceId)
    {
        // Legacy profiles are isolated. Shared profiles use shared runtime caches, so a safe
        // temporary cleanup is intentionally global rather than guessing at profile folders.
        var legacyProfile = Path.Combine(PortablePaths.ProfilesDirectory, serviceId.ToString("N"));
        if (Directory.Exists(legacyProfile)) ClearDisposableCaches(legacyProfile);
        else ClearAllTemporaryData();
    }

    // Call only when no WebView2 using these UDFs is active, such as startup or after Dispose.
    public static void ClearAllTemporaryData(bool includeReplaceableComponents = false)
    {
        try
        {
            var before = GetTemporaryBrowserBytes();
            var overBudget = before > TemporaryCacheBudgetBytes;
            if (before > AbnormalTemporaryCacheBytes)
                AppLog.Write("storage-cleanup", $"Abnormally large temporary browser data detected: {FormatBytes(before)}. Clearing safe cache data.");
            else if (overBudget)
                AppLog.Write("storage-cleanup", $"Temporary browser data was {FormatBytes(before)}; clearing safe cache data.");
            foreach (var path in GetDisposablePaths(PortablePaths.BrowserDirectory, includeReplaceableComponents || overBudget))
            {
                if (Directory.Exists(path)) TryDeleteDirectory(path);
                else TryDelete(path);
            }
        }
        catch (Exception ex) { AppLog.Write("storage-cleanup", ex.Message); }

        ClearDirectoryContents(PortablePaths.CacheDirectory);
        TryDelete(Path.Combine(PortablePaths.DataRoot, "startup-error.txt"));
    }

    public static async Task ClearActiveDiskCacheAsync(CoreWebView2Profile? profile)
    {
        if (profile is null) return;
        try { await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); }
        catch (Exception ex) { AppLog.Write("storage-cleanup", "Disk-cache API: " + ex.Message); }
    }

    public static bool ShouldClearActiveDiskCache() => GetTemporaryBrowserBytes() > TemporaryCacheBudgetBytes;

    public static void ClearServiceData(Guid serviceId)
    {
        var profile = Path.Combine(PortablePaths.ProfilesDirectory, serviceId.ToString("N"));
        if (Directory.Exists(profile)) Directory.Delete(profile, true);
    }

    public static void ResetBrowserData()
    {
        if (Directory.Exists(PortablePaths.BrowserDirectory)) Directory.Delete(PortablePaths.BrowserDirectory, true);
        Directory.CreateDirectory(PortablePaths.ProfilesDirectory);
        Directory.CreateDirectory(PortablePaths.SharedBrowserUserDataDirectory);
        TryDelete(PortablePaths.PendingSharedProfileDeletesFile);
    }

    public static void QueueSharedProfileDeletion(string? relativeProfilePath)
    {
        if (!TryResolveSharedProfilePath(relativeProfilePath, out var path)) return;
        var entries = ReadPendingSharedProfileDeletes();
        if (!entries.Contains(path, StringComparer.OrdinalIgnoreCase)) entries.Add(path);
        WritePendingSharedProfileDeletes(entries);
    }

    // Runs only before a browser is created, or after all views have been disposed. Failed locked
    // deletions remain queued for the next clean startup rather than risking a live UDF.
    public static void CleanupQueuedSharedProfileDeletes()
    {
        var remaining = new List<string>();
        foreach (var path in ReadPendingSharedProfileDeletes())
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { remaining.Add(path); }
        }
        WritePendingSharedProfileDeletes(remaining);
    }

    public static void ResetMonkeyTvData()
    {
        if (Directory.Exists(PortablePaths.DataRoot)) Directory.Delete(PortablePaths.DataRoot, true);
        PortablePaths.EnsureCreated();
    }

    public static void CleanupOrphanAssets(MonkeySettings settings)
    {
        var referenced = settings.Services.SelectMany(s => new[] { s.IconPath, s.BannerPath })
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(Path.Combine(PortablePaths.AppRoot, p!)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { PortablePaths.AssetsDirectory, PortablePaths.BannersDirectory })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
                if (!referenced.Contains(Path.GetFullPath(file))) TryDelete(file);
        }
    }

    private static IReadOnlyList<string> GetDisposablePaths(string root, bool includeReplaceableComponents = false)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        try
        {
            var candidates = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Where(d => DisposableFolderNames.Contains(Path.GetFileName(d)) ||
                    (includeReplaceableComponents && ReplaceableComponentFolderNames.Contains(Path.GetFileName(d))))
                .OrderBy(d => d.Length)
                .ToList();
            // A selected parent owns all nested selected directories, so count/delete it once.
            var selected = candidates.Where(path => !candidates.Any(parent => parent.Length < path.Length && path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
            selected.AddRange(Directory.EnumerateFiles(root, "*.dmp", SearchOption.AllDirectories));
            selected.AddRange(Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
            return selected;
        }
        catch { return Array.Empty<string>(); }
    }

    private static void ClearDisposableCaches(string profile)
    {
        foreach (var path in GetDisposablePaths(profile))
        {
            if (Directory.Exists(path)) TryDeleteDirectory(path);
            else TryDelete(path);
        }
    }

    private static void ClearDirectoryContents(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path).ToArray()) TryDelete(file);
            foreach (var directory in Directory.EnumerateDirectories(path).ToArray()) TryDeleteDirectory(directory);
        }
        catch (Exception ex) { AppLog.Write("storage-cleanup", ex.Message); }
    }

    private static long SafeFileLength(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static void TryDelete(string file) { try { File.Delete(file); } catch { } }
    private static void TryDeleteDirectory(string path) { try { Directory.Delete(path, true); } catch { } }

    private static List<string> ReadPendingSharedProfileDeletes()
    {
        try
        {
            if (!File.Exists(PortablePaths.PendingSharedProfileDeletesFile)) return new();
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PortablePaths.PendingSharedProfileDeletesFile))?
                .Where(path => TryResolveSharedProfilePath(Path.GetRelativePath(PortablePaths.AppRoot, path), out _)).ToList() ?? new();
        }
        catch { return new(); }
    }

    private static void WritePendingSharedProfileDeletes(List<string> paths)
    {
        try
        {
            if (paths.Count == 0) { TryDelete(PortablePaths.PendingSharedProfileDeletesFile); return; }
            File.WriteAllText(PortablePaths.PendingSharedProfileDeletesFile, JsonSerializer.Serialize(paths.Distinct(StringComparer.OrdinalIgnoreCase)));
        }
        catch { }
    }

    private static bool TryResolveSharedProfilePath(string? relativePath, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        try
        {
            path = Path.GetFullPath(Path.Combine(PortablePaths.AppRoot, relativePath));
            var root = Path.GetFullPath(PortablePaths.SharedBrowserUserDataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
