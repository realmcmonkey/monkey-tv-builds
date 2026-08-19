using System.Text.Json;

namespace MonkeyTV;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public MonkeySettings Load()
    {
        if (!File.Exists(PortablePaths.SettingsFile)) return new MonkeySettings();
        try
        {
            var settings = JsonSerializer.Deserialize<MonkeySettings>(File.ReadAllText(PortablePaths.SettingsFile), JsonOptions)
                           ?? new MonkeySettings();
            if (Migrate(settings)) Save(settings);
            return settings;
        }
        catch (Exception ex)
        {
            var backup = PortablePaths.SettingsFile + ".invalid-backup";
            File.Copy(PortablePaths.SettingsFile, backup, true);
            AppLog.Write("settings", ex.Message);
            return new MonkeySettings();
        }
    }

    private static bool Migrate(MonkeySettings settings)
    {
        var changed = false;
        if (settings.Services is null)
        {
            settings.Services = new List<ServiceDefinition>();
            changed = true;
        }
        foreach (var service in settings.Services)
        {
            if (service.AddedUtc == default)
            {
                service.AddedUtc = DateTime.UtcNow;
                changed = true;
            }
            if (service.FutureSettings is null)
            {
                service.FutureSettings = new Dictionary<string, string>();
                changed = true;
            }
            if (service.LegacyUserAgentMode == UserAgentMode.Custom &&
                string.IsNullOrWhiteSpace(service.CustomUserAgentOverride) &&
                !string.IsNullOrWhiteSpace(service.LegacyCustomUserAgent))
            {
                service.CustomUserAgentOverride = service.LegacyCustomUserAgent.Trim();
                changed = true;
            }

            if (service.LegacyUserAgentMode is not null || service.LegacyCustomUserAgent is not null)
            {
                service.LegacyUserAgentMode = null;
                service.LegacyCustomUserAgent = null;
                changed = true;
            }

            // Do not copy cookies, credentials, or protected-media state between Build #1
            // profiles. Keep those folders on disk for manual recovery, but route all new
            // launches through Build #2's one shared WebView2 profile.
            if (service.BrowserStorageMode == BrowserStorageMode.SharedProfile &&
                service.FutureSettings.TryGetValue("SharedProfilePath", out var buildOneProfilePath) &&
                !string.IsNullOrWhiteSpace(buildOneProfilePath) &&
                !service.FutureSettings.ContainsKey("Build1ProfilePath"))
            {
                service.FutureSettings["Build1ProfilePath"] = buildOneProfilePath;
                changed = true;
            }
            if (service.BrowserStorageMode != BrowserStorageMode.UnifiedSharedProfile)
            {
                service.BrowserStorageMode = BrowserStorageMode.UnifiedSharedProfile;
                changed = true;
            }
        }
        return changed;
    }

    public void Save(MonkeySettings settings)
    {
        PortablePaths.EnsureCreated();
        var temp = PortablePaths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, PortablePaths.SettingsFile, true);
    }
}
