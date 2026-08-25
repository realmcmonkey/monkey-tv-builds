namespace MonkeyTV;

// Presentation-only mapping for the launch overlay. Routing remains owned by
// CurrentLocationRouter; this keeps normal UI free of implementation details.
internal static class ServiceLaunchProgress
{
    internal static string ForRoute(LocationRouteProgress progress)
    {
        var location = progress.LocationName == "United States" ? "USA" : progress.LocationName;
        return progress.Stage switch
        {
            LocationRouteProgressStage.Switching => $"Switching VPN Location to {location ?? "selected location"}…",
            LocationRouteProgressStage.Connecting => $"Connecting to {location ?? "selected location"}…",
            LocationRouteProgressStage.TryingAnotherConnection => $"Trying another {location ?? "VPN"} connection…",
            LocationRouteProgressStage.Verifying => "Verifying VPN Location…",
            LocationRouteProgressStage.Connected => $"{location ?? "VPN Location"} connected",
            LocationRouteProgressStage.Direct => "Direct connection verified",
            LocationRouteProgressStage.Failed => "VPN Location connection failed",
            _ => "Checking VPN Location…"
        };
    }

    internal static string ForExperience(ServiceDefinition service) =>
        !string.IsNullOrWhiteSpace(service.CustomUserAgentOverride) ? "Applying custom browser identity…" :
        service.Experience == ServiceExperience.TV ? "Applying TV Experience…" : "Preparing browser…";
}
