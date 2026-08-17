namespace MonkeyTV;

public static class AppLog
{
    private const long MaxFileBytes = 128 * 1024;
    private const int RetainedFiles = 3;
    private static readonly object Gate = new();
    public const long MaximumRetainedBytes = MaxFileBytes * RetainedFiles;

    public static void Write(string area, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(PortablePaths.LogsDirectory);
                var file = Path.Combine(PortablePaths.LogsDirectory, "monkey.log");
                var safe = message.Replace('\r', ' ').Replace('\n', ' ');
                if (safe.Length > 2000) safe = safe[..2000];
                var line = $"{DateTime.UtcNow:O} [{area}] {safe}{Environment.NewLine}";
                var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line);
                if (File.Exists(file) && new FileInfo(file).Length + lineBytes > MaxFileBytes) Rotate(file);
                File.AppendAllText(file, line);
            }
        }
        catch { }
    }

    private static void Rotate(string file)
    {
        var oldest = file + ".2";
        if (File.Exists(oldest)) File.Delete(oldest);
        if (File.Exists(file + ".1")) File.Move(file + ".1", oldest, true);
        File.Move(file, file + ".1", true);
    }
}
