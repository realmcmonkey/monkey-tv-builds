using System.Text.Json;

namespace MonkeyTV;

public static class SessionMarker
{
    private sealed record StartupState(int ConsecutiveFailures);
    public static StartupStateInfo Begin()
    {
        var unclean = File.Exists(PortablePaths.SessionMarkerFile);
        var failures = 0;
        try
        {
            if (File.Exists(PortablePaths.StartupStateFile))
                failures = JsonSerializer.Deserialize<StartupState>(File.ReadAllText(PortablePaths.StartupStateFile))?.ConsecutiveFailures ?? 0;
        }
        catch { }
        if (unclean) failures++;
        else failures = 0;
        File.WriteAllText(PortablePaths.StartupStateFile, JsonSerializer.Serialize(new StartupState(failures)));
        File.WriteAllText(PortablePaths.SessionMarkerFile, JsonSerializer.Serialize(new { Active = true }));
        return new StartupStateInfo(unclean, failures);
    }

    public static void MarkStartupSuccessful()
    {
        try { File.WriteAllText(PortablePaths.StartupStateFile, JsonSerializer.Serialize(new StartupState(0))); }
        catch { }
    }

    public static void MarkClean()
    {
        try { if (File.Exists(PortablePaths.SessionMarkerFile)) File.Delete(PortablePaths.SessionMarkerFile); }
        catch (Exception ex) { AppLog.Write("shutdown", ex.Message); }
    }
}

public readonly record struct StartupStateInfo(bool Unclean, int ConsecutiveFailures);
