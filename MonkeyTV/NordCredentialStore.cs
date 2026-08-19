using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonkeyTV;

public sealed record NordCredentials(string Username, string Password);

public static class NordCredentialStore
{
    private static string FilePath => Path.Combine(PortablePaths.SettingsDirectory, "nord-credentials.dat");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MonkeyTV.Feature.0.01.NordCredentials");

    public static bool HasCredentials => File.Exists(FilePath) && Load() is not null;
    public static NordCredentials? Load()
    {
        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<NordCredentials>(bytes);
        }
        catch { return null; }
    }
    public static void Save(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Both NordVPN service credentials are required.");
        PortablePaths.EnsureCreated();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new NordCredentials(username.Trim(), password));
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        var temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, encrypted); File.Move(temp, FilePath, true);
    }
    public static void Clear() { try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { } }
}
