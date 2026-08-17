using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MonkeyTV;

public static partial class IconService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    static IconService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("MonkeyTV/0.0.1 icon-fetcher");

    public static async Task<string?> TryDownloadAsync(ServiceDefinition service)
    {
        if (!Uri.TryCreate(service.Url, UriKind.Absolute, out var pageUri) || (pageUri.Scheme != "https" && pageUri.Scheme != "http")) return null;
        var candidates = new List<Uri>();
        try
        {
            using var response = await Client.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.StartsWith("text/html") == true)
            {
                var html = await response.Content.ReadAsStringAsync();
                foreach (Match match in IconLinkRegex().Matches(html))
                {
                    var href = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (Uri.TryCreate(pageUri, WebUtility.HtmlDecode(href), out var iconUri)) candidates.Add(iconUri);
                }
            }
        }
        catch { }

        candidates.Add(new Uri(pageUri, "/favicon.ico"));
        foreach (var candidate in candidates.Distinct())
        {
            try
            {
                using var response = await Client.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length is < 32 or > 5_000_000) continue;
                var extension = ExtensionFor(response.Content.Headers.ContentType?.MediaType, candidate);
                var file = Path.Combine(PortablePaths.AssetsDirectory, service.Id.ToString("N") + extension);
                await File.WriteAllBytesAsync(file, bytes);
                return Path.GetRelativePath(PortablePaths.AppRoot, file);
            }
            catch { }
        }
        return null;
    }

    private static string ExtensionFor(string? mime, Uri uri) => mime switch
    {
        "image/png" => ".png", "image/jpeg" => ".jpg", "image/svg+xml" => ".svg",
        "image/webp" => ".webp", _ => Path.GetExtension(uri.AbsolutePath) is { Length: > 1 } ext && ext.Length <= 5 ? ext : ".ico"
    };

    [GeneratedRegex("<link[^>]+rel=[\\\"'][^\\\"']*(?:icon|apple-touch-icon)[^\\\"']*[\\\"'][^>]+href=[\\\"']([^\\\"']+)[\\\"']|<link[^>]+href=[\\\"']([^\\\"']+)[\\\"'][^>]+rel=[\\\"'][^\\\"']*(?:icon|apple-touch-icon)[^\\\"']*[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex IconLinkRegex();
}
