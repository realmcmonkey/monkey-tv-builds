namespace MonkeyTV;

public static class PortablePaths
{
    public static string AppRoot => AppContext.BaseDirectory;
    public static string DefaultDataRoot => Path.Combine(AppRoot, "Data");
    private static string _dataRoot = DefaultDataRoot;
    private static string DataLocationPointerFile => Path.Combine(DefaultDataRoot, "Settings", "data-location.json");
    public static string DataRoot => _dataRoot;
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
    public static string RecoveryDirectory => Path.Combine(DataRoot, "Recovery");
    public static string StartupStateFile => Path.Combine(SettingsDirectory, "startup-state.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(BannersDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(SharedBrowserUserDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
    }

    public static void InitializeDataRoot()
    {
        _dataRoot = DefaultDataRoot;
        try
        {
            if (!File.Exists(DataLocationPointerFile)) return;
            var pointer = System.Text.Json.JsonSerializer.Deserialize<DataLocationPointer>(File.ReadAllText(DataLocationPointerFile));
            // A portable copy on another PC deliberately returns to its adjacent Data folder.
            if (pointer is null || !string.Equals(pointer.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(pointer.Path) || !Directory.Exists(pointer.Path))
            {
                File.Delete(DataLocationPointerFile); return;
            }
            _dataRoot = Path.GetFullPath(pointer.Path);
        }
        catch { _dataRoot = DefaultDataRoot; }
    }

    public static void ChangeDataRoot(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (string.Equals(destination, _dataRoot, StringComparison.OrdinalIgnoreCase)) return;
        var sourceRoot = Path.GetFullPath(_dataRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The new Data location cannot be inside the current Data folder.");
        if (string.Equals(destination, DefaultDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            CopyDirectory(_dataRoot, DefaultDataRoot);
            ResetDataRootToDefault();
            return;
        }
        Directory.CreateDirectory(destination);
        CopyDirectory(_dataRoot, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(DataLocationPointerFile)!);
        File.WriteAllText(DataLocationPointerFile, System.Text.Json.JsonSerializer.Serialize(new DataLocationPointer(destination, Environment.MachineName)));
        _dataRoot = destination;
        EnsureCreated();
    }

    public static void ResetDataRootToDefault()
    {
        _dataRoot = DefaultDataRoot;
        try { if (File.Exists(DataLocationPointerFile)) File.Delete(DataLocationPointerFile); } catch { }
        EnsureCreated();
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(DataLocationPointerFile), StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
        }
    }

    private sealed record DataLocationPointer(string Path, string MachineName);

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
