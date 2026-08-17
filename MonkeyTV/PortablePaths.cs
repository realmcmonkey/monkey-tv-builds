namespace MonkeyTV;

public static class PortablePaths
{
    public static string AppRoot => AppContext.BaseDirectory;
    public static string DataRoot => Path.Combine(AppRoot, "Data");
    public static string SettingsDirectory => Path.Combine(DataRoot, "Settings");
    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    public static string AssetsDirectory => Path.Combine(DataRoot, "Assets");
    public static string BannersDirectory => Path.Combine(AssetsDirectory, "Banners");
    public static string BrowserDirectory => Path.Combine(DataRoot, "Browser");
    public static string SharedBrowserUserDataDirectory => Path.Combine(BrowserDirectory, "Shared");
    public static string ProfilesDirectory => Path.Combine(BrowserDirectory, "Profiles");
    public static string CacheDirectory => Path.Combine(DataRoot, "Cache");
    public static string LogsDirectory => Path.Combine(DataRoot, "Logs");
    public static string SessionMarkerFile => Path.Combine(SettingsDirectory, "session-active.json");
    public static string PendingSharedProfileDeletesFile => Path.Combine(SettingsDirectory, "pending-shared-profile-deletes.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(BannersDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(SharedBrowserUserDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public static string LegacyProfileFor(Guid serviceId)
    {
        var path = Path.Combine(ProfilesDirectory, serviceId.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    // Retained as a source-compatible name for legacy isolated service profiles.
    public static string ProfileFor(Guid serviceId) => LegacyProfileFor(serviceId);

    public static bool HasLegacyProfile(Guid serviceId) => Directory.Exists(Path.Combine(ProfilesDirectory, serviceId.ToString("N")));
}
