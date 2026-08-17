namespace MonkeyTV;

public static class BrowserIdentity
{
    // Centralized so compatibility testing can replace the built-in identity in one place.
    public const string TvUserAgent = "Mozilla/5.0 (Linux; Android 11; SHIELD Android TV Build/RQ1A.210105.003; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/120.0.0.0 Safari/537.36";

    public static string? ResolveUserAgent(ServiceDefinition service)
    {
        if (!string.IsNullOrWhiteSpace(service.CustomUserAgentOverride))
            return service.CustomUserAgentOverride.Trim();

        return service.Experience == ServiceExperience.TV ? TvUserAgent : null;
    }
}
