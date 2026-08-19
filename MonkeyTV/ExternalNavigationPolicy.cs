using System.Diagnostics;
using System.Windows;

namespace MonkeyTV;

public static class ExternalNavigationPolicy
{
    public static bool AskBeforeOpening { get; set; }
    public static bool IsRelatedOrAuthentication(Uri? current, Uri target)
    {
        if (current is not null && (HostContains(current.Host, target.Host) || HostContains(target.Host, current.Host))) return true;
        var host = target.Host.ToLowerInvariant();
        var value = (host + target.AbsolutePath).ToLowerInvariant();
        return value.Contains("login") || value.Contains("signin") || value.Contains("sign-in") ||
               value.Contains("oauth") || value.Contains("authorize") || value.Contains("account") ||
               host.StartsWith("auth.") || host.StartsWith("id.") || host.StartsWith("accounts.");
    }

    public static bool TryHttpUri(string? value, out Uri uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
                    (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);
        uri = parsed ?? new Uri("https://invalid.invalid");
        return valid;
    }

    internal static void OpenInDefaultBrowser(string? value)
    {
        if (!TryHttpUri(value, out var uri)) return;
        try
        {
            if (AskBeforeOpening && MessageBox.Show($"Open {uri.Host} in your Windows default browser?", "Open external link", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Write("external-link", ex.Message); }
    }

    private static bool HostContains(string parent, string candidate) =>
        string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase) ||
        candidate.EndsWith('.' + parent, StringComparison.OrdinalIgnoreCase);
}
