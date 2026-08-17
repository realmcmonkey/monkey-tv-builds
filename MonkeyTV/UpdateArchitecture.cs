namespace MonkeyTV;

public enum ReleaseChannel { Alpha, Beta, Stable }

public sealed record UpdateMetadata(
    string LatestVersion,
    ReleaseChannel Channel,
    Uri? ReleaseNotesUrl,
    Uri? DownloadUrl);

public sealed record UpdateCheckResult(string CurrentVersion, UpdateMetadata? Latest, bool UpdateAvailable, string Status);

public interface IUpdateProvider
{
    Task<UpdateMetadata?> GetLatestAsync(ReleaseChannel channel, CancellationToken cancellationToken = default);
}

public sealed class UpdateService(IUpdateProvider provider)
{
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, ReleaseChannel channel, CancellationToken cancellationToken = default)
    {
        var latest = await provider.GetLatestAsync(channel, cancellationToken);
        if (latest is null) return new(currentVersion, null, false, "No update information is available.");
        var available = PrereleaseVersion.Compare(latest.LatestVersion, currentVersion) > 0;
        return new(currentVersion, latest, available, available ? "An update is available." : "Monkey TV is up to date.");
    }
}

public static class PrereleaseVersion
{
    public static int Compare(string left, string right)
    {
        static (Version Core, string Pre) Parse(string value)
        {
            value = value.Trim().TrimStart('v', 'V');
            var parts = value.Split('-', 2);
            return (Version.TryParse(parts[0], out var version) ? version : new Version(0, 0), parts.Length > 1 ? parts[1] : "");
        }
        var a = Parse(left); var b = Parse(right);
        var core = a.Core.CompareTo(b.Core);
        if (core != 0) return core;
        if (a.Pre.Length == 0 && b.Pre.Length > 0) return 1;
        if (a.Pre.Length > 0 && b.Pre.Length == 0) return -1;
        return string.Compare(a.Pre, b.Pre, StringComparison.OrdinalIgnoreCase);
    }
}
