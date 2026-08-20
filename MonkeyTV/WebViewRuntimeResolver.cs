using Microsoft.Web.WebView2.Core;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MonkeyTV;

/// <summary>
/// Selects an app-owned Fixed Version runtime when its separately installed pack
/// is present. Evergreen is intentionally never treated as the v2 WFP identity.
/// </summary>
public static class WebViewRuntimeResolver
{
    public const string FixedRuntimeRelativeDirectory = "Data\\Requirements\\WebView2Fixed";
    public const string FixedRuntimeExecutableName = "msedgewebview2.exe";

    public static bool IsVpnV2DependencyPackInstalled =>
        File.Exists(Path.Combine(PortablePaths.FixedWebView2RuntimeDirectory, FixedRuntimeExecutableName)) &&
        File.Exists(Path.Combine(PortablePaths.VpnV2RequirementsDirectory, "MonkeyTV.UserSpaceOpenVpnProof.exe"));

    public static WebViewRuntimeSelection Select()
    {
        // V2's WFP identity is valid only when this exact app-owned pack travels with the
        // executable. Never probe a source checkout, an ancestor folder, or a developer
        // environment variable: a packaged build must be self-contained and deterministic.
        var inspected = InspectFixedRuntime(PortablePaths.FixedWebView2RuntimeDirectory);
        return inspected.IsFixed
            ? inspected
            : new WebViewRuntimeSelection(WebViewRuntimeKind.Evergreen, null, null, inspected.Detail);
    }

    public static WebViewRuntimeSelection InspectFixedRuntime(string runtimeRoot)
    {
        try
        {
            var root = Path.GetFullPath(runtimeRoot);
            var executable = Path.Combine(root, FixedRuntimeExecutableName);
            if (!File.Exists(executable)) return Unavailable(root, "The separately installed Fixed Version WebView2 Runtime Pack was not found.");

            var version = CoreWebView2Environment.GetAvailableBrowserVersionString(root);
            if (string.IsNullOrWhiteSpace(version)) return Unavailable(root, "WebView2 could not load the installed fixed runtime.");

            var certificate = X509Certificate.CreateFromSignedFile(executable);
            if (certificate is null || !(certificate.Subject ?? string.Empty).Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                return Unavailable(root, "The fixed runtime executable is not signed by Microsoft.");

            return new WebViewRuntimeSelection(WebViewRuntimeKind.Fixed, root, version, "Validated app-owned Fixed Version WebView2 runtime.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            return Unavailable(runtimeRoot, "The fixed runtime could not be validated: " + ex.Message);
        }
    }

    public static bool TryRequireFixedForCurrentLocationV2(out WebViewRuntimeSelection selection, out string message)
    {
        selection = Select();
        if (selection.IsFixed) { message = string.Empty; return true; }
        message = "Current Location v2 requires the separately installed Monkey TV Fixed Version WebView2 Runtime Pack. " + selection.Detail;
        return false;
    }

    private static WebViewRuntimeSelection Unavailable(string root, string detail) =>
        new(WebViewRuntimeKind.FixedUnavailable, Path.GetFullPath(root), null, detail);
}

public enum WebViewRuntimeKind { Evergreen, Fixed, FixedUnavailable }

public sealed record WebViewRuntimeSelection(WebViewRuntimeKind Kind, string? BrowserExecutableFolder, string? Version, string Detail)
{
    public bool IsFixed => Kind == WebViewRuntimeKind.Fixed;
}
