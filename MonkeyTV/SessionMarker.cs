using System.Text.Json;

namespace MonkeyTV;

public static class SessionMarker
{
    public static bool Begin()
    {
        var unclean = File.Exists(PortablePaths.SessionMarkerFile);
        File.WriteAllText(PortablePaths.SessionMarkerFile, JsonSerializer.Serialize(new { Active = true }));
        return unclean;
    }

    public static void MarkClean()
    {
        try { if (File.Exists(PortablePaths.SessionMarkerFile)) File.Delete(PortablePaths.SessionMarkerFile); }
        catch (Exception ex) { AppLog.Write("shutdown", ex.Message); }
    }
}
