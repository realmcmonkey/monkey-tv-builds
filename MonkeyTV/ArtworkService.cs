using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace MonkeyTV;

public static partial class ArtworkService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    static ArtworkService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd($"MonkeyTV/{App.DisplayVersion} artwork-fetcher");

    public static async Task<string?> TryDownloadBannerAsync(ServiceDefinition service)
    {
        if (!Uri.TryCreate(service.Url, UriKind.Absolute, out var pageUri) || pageUri.Scheme is not ("https" or "http"))
            return null;

        var candidates = new List<Uri>();
        try
        {
            using var response = await Client.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.StartsWith("text/html") == true)
            {
                var html = await response.Content.ReadAsStringAsync();
                foreach (Match match in SocialImageRegex().Matches(html))
                {
                    var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (Uri.TryCreate(pageUri, WebUtility.HtmlDecode(value), out var imageUri))
                        candidates.Add(imageUri);
                }
            }
        }
        catch { }

        foreach (var candidate in candidates.Distinct().Take(8))
        {
            try
            {
                using var response = await Client.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length is < 4_096 or > 8_000_000) continue;
                if (!IsSuitableLandscape(bytes)) continue;

                return PortableAssetImporter.ImportBannerBytes(bytes);
            }
            catch { }
        }

        return null;
    }

    private static bool IsSuitableLandscape(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            return frame.PixelWidth >= 480 && frame.PixelHeight >= 200 && frame.PixelWidth / (double)frame.PixelHeight >= 1.35;
        }
        catch { return false; }
    }

    [GeneratedRegex("<meta[^>]+(?:property|name)=[\\\"'](?:og:image|twitter:image(?::src)?)[\\\"'][^>]+content=[\\\"']([^\\\"']+)[\\\"']|<meta[^>]+content=[\\\"']([^\\\"']+)[\\\"'][^>]+(?:property|name)=[\\\"'](?:og:image|twitter:image(?::src)?)[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex SocialImageRegex();
}
