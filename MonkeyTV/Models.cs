using System.Text.Json.Serialization;

namespace MonkeyTV;

public enum UserAgentMode { Default, Custom }
public enum ServiceExperience { Default, TV }
public enum TileSize { Small, Medium, Large }
public enum ServiceCardStyle { Banner, Square }
public enum BannerFit { Fill, Fit }
public enum BrowserStorageMode { LegacyIsolated, SharedProfile, UnifiedSharedProfile }
public enum AppTheme { System, Light, Dark }
public enum AppAccent { System, Blue, Purple, Green, Orange, Red }
public enum CurrentLocationState { Direct, Connecting, Connected, Failed }
public enum NordLocationId { UnitedStates, Netherlands, Sweden, Atlanta, Chicago, Dallas, LosAngeles, NewYork, Phoenix, SanFrancisco, Stockholm, Amsterdam }

public sealed class ServiceDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? IconPath { get; set; }
    public string? BannerPath { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BannerFit BannerFit { get; set; } = BannerFit.Fill;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServiceExperience Experience { get; set; } = ServiceExperience.Default;
    public string? CustomUserAgentOverride { get; set; }
    public string ProfileConfiguration { get; set; } = "Persistent";
    // Build #2 uses one shared browser profile. Earlier modes remain only to preserve
    // configuration and on-disk data during migration; they are no longer selected for launch.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrowserStorageMode? BrowserStorageMode { get; set; }
    public Dictionary<string, string> FutureSettings { get; set; } = new();

    // Legacy v0.0.1-alpha fields retained only for lossless settings migration.
    [JsonPropertyName("UserAgentMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UserAgentMode? LegacyUserAgentMode { get; set; }

    [JsonPropertyName("CustomUserAgent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyCustomUserAgent { get; set; }
}

public sealed class MonkeySettings
{
    public bool SetupComplete { get; set; }
    public string? DismissedAlphaVersion { get; set; }
    public TileSize TileSize { get; set; } = TileSize.Medium;
    public ServiceCardStyle CardStyle { get; set; } = ServiceCardStyle.Banner;
    public bool ShowServiceNames { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppAccent Accent { get; set; } = AppAccent.System;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool CurrentLocationEnabled { get; set; }
    // This is a convenience only. Startup always begins on Direct and never reconnects it.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NordLocationId? LastUsedNordLocation { get; set; }
    public List<ServiceDefinition> Services { get; set; } = new();
}
