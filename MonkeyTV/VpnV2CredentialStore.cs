using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonkeyTV;

internal static class VpnV2CredentialStore
{
    private static string FilePath => Path.Combine(PortablePaths.SettingsDirectory, "nord-vpn-v2-credentials.dat");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MonkeyTV.Experimental.VpnLocationV2.NordCredentials");

    public static bool HasCredentials => Load() is not null;

    public static NordCredentials? Load()
    {
        try
        {
            var protectedBytes = File.ReadAllBytes(FilePath);
            var clear = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<NordCredentials>(clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch { return null; }
    }

    public static void Save(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Both NordVPN service credentials are required.");
        PortablePaths.EnsureCreated();
        var clear = JsonSerializer.SerializeToUtf8Bytes(new NordCredentials(username.Trim(), password));
        try
        {
            var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            var temporary = FilePath + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, FilePath, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
