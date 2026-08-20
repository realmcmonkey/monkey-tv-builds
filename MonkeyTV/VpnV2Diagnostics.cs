using System.Text;
using System.Text.RegularExpressions;

namespace MonkeyTV;

/// <summary>Small, bounded, secret-free diagnostics for the opt-in V2 experiment.</summary>
internal static partial class VpnV2Diagnostics
{
    private const long MaxFileBytes = 128 * 1024;
    private const int RetainedFiles = 3;
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(PortablePaths.LogsDirectory, "vpn-v2.log");

    public static void Write(string stage, string detail = "")
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(PortablePaths.LogsDirectory);
                var line = $"{DateTime.UtcNow:O} [{Sanitize(stage)}] {Sanitize(detail)}{Environment.NewLine}";
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + Encoding.UTF8.GetByteCount(line) > MaxFileBytes)
                    Rotate();
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Dependency(string label, string path)
    {
        // All V2 payload files must be beside MonkeyTV.exe. Record a portable-relative
        // path, never a developer-machine path.
        var relative = Path.GetRelativePath(PortablePaths.AppRoot, path);
        Write("dependency", $"{label} path={relative} exists={File.Exists(path)}");
    }

    public static string ManagedConfigPath(VpnV2ImportedConfig config) =>
        Path.Combine("Data", "VpnV2", "Configs", Path.GetFileName(config.FullPath));

    private static void Rotate()
    {
        var oldest = FilePath + ".2";
        if (File.Exists(oldest)) File.Delete(oldest);
        if (File.Exists(FilePath + ".1")) File.Move(FilePath + ".1", oldest, true);
        File.Move(FilePath, FilePath + ".1", true);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = value.Replace('\r', ' ').Replace('\n', ' ');
        safe = Ipv4Regex().Replace(safe, "[ip]");
        safe = SecretRegex().Replace(safe, "$1=[redacted]");
        if (safe.Length > 900) safe = safe[..900];
        return safe;
    }

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"(?i)\b(username|password|token|credential)\s*[:=]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();
}
