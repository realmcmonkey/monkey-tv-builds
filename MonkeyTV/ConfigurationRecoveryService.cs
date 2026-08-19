using System.Text.Json;

namespace MonkeyTV;

public static class ConfigurationRecoveryService
{
    private const int RetainedBackups = 3;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static DateTime? LastKnownGoodUtc => Directory.Exists(PortablePaths.RecoveryDirectory)
        ? Directory.EnumerateFiles(PortablePaths.RecoveryDirectory, "known-good-*.json").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max() : null;

    public static void PreserveDamagedConfiguration()
    {
        if (!File.Exists(PortablePaths.SettingsFile)) return;
        Directory.CreateDirectory(PortablePaths.RecoveryDirectory);
        var target = Path.Combine(PortablePaths.RecoveryDirectory, $"damaged-settings-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.Copy(PortablePaths.SettingsFile, target, overwrite: false);
        Trim("damaged-settings-*.json", 3);
    }

    public static void CreateKnownGood(MonkeySettings settings, bool force = false)
    {
        if (!settings.SetupComplete) return;
        Directory.CreateDirectory(PortablePaths.RecoveryDirectory);
        var latest = LastKnownGoodUtc;
        if (!force && latest is { } last && DateTime.UtcNow - last < TimeSpan.FromSeconds(20)) return;
        var path = Path.Combine(PortablePaths.RecoveryDirectory, $"known-good-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        Trim("known-good-*.json", RetainedBackups);
    }

    public static bool TryRestoreLatest(out MonkeySettings settings, out string? restoredFile)
    {
        settings = new MonkeySettings(); restoredFile = null;
        if (!Directory.Exists(PortablePaths.RecoveryDirectory)) return false;
        foreach (var path in Directory.EnumerateFiles(PortablePaths.RecoveryDirectory, "known-good-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var candidate = JsonSerializer.Deserialize<MonkeySettings>(File.ReadAllText(path), Options) ?? throw new InvalidDataException();
                SettingsStore.ValidateSettings(candidate);
                settings = candidate; restoredFile = path; return true;
            }
            catch { }
        }
        return false;
    }

    private static void Trim(string pattern, int keep)
    {
        foreach (var path in Directory.EnumerateFiles(PortablePaths.RecoveryDirectory, pattern).OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep))
            try { File.Delete(path); } catch { }
    }
}
