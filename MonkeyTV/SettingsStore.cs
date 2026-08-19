using System.Text.Json;

namespace MonkeyTV;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    public SettingsLoadOutcome LastLoadOutcome { get; private set; }
    public string? LastRecoveryFile { get; private set; }

    public MonkeySettings Load()
    {
        LastLoadOutcome = SettingsLoadOutcome.Valid; LastRecoveryFile = null;
        if (!File.Exists(PortablePaths.SettingsFile)) return new MonkeySettings();
        try
        {
            var settings = JsonSerializer.Deserialize<MonkeySettings>(File.ReadAllText(PortablePaths.SettingsFile), JsonOptions)
                           ?? new MonkeySettings();
            ValidateSettings(settings);
            if (Migrate(settings))
            {
                ConfigurationRecoveryService.CreateKnownGood(settings, force: true);
                Save(settings);
            }
            return settings;
        }
        catch (Exception ex)
        {
            ConfigurationRecoveryService.PreserveDamagedConfiguration();
            AppLog.Write("settings", ex.Message);
            if (ConfigurationRecoveryService.TryRestoreLatest(out var recovered, out var backup))
            {
                LastLoadOutcome = SettingsLoadOutcome.Recovered; LastRecoveryFile = backup;
                Save(recovered); return recovered;
            }
            LastLoadOutcome = SettingsLoadOutcome.Unrecoverable;
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
            // Build #5 renamed only the untouched legacy built-in YouTube template so the
            // desktop and Smart TV entries are unambiguous. Any edited/custom service is left
            // exactly as the user configured it.
            if (service.BuiltInServiceId == "general/youtube" && service.BuiltInDefinitionRevision < 2 &&
                service.Name == "YouTube" && service.Url == "https://www.youtube.com/" &&
                service.BannerPath == "builtin:general/youtube" && service.Experience == ServiceExperience.Default)
            {
                BuiltInServiceCatalog.RestoreDefaults(service, BuiltInServiceCatalog.Find("general/youtube")!);
                changed = true;
            }
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
        ConfigurationRecoveryService.CreateKnownGood(settings);
    }

    internal static void ValidateSettings(MonkeySettings settings)
    {
        if (settings.Services is null || settings.Services.Count > 500) throw new InvalidDataException("The service list is invalid.");
        if (settings.Services.Select(s => s.Id).Distinct().Count() != settings.Services.Count) throw new InvalidDataException("Duplicate service identifiers.");
        foreach (var service in settings.Services)
            if (service.Id == Guid.Empty || string.IsNullOrWhiteSpace(service.Name) || service.Name.Length > 200 ||
                !Uri.TryCreate(service.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("An invalid service was found.");
    }
}

public enum SettingsLoadOutcome { Valid, Recovered, Unrecoverable }
