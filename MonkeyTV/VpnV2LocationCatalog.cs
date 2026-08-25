using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MonkeyTV;

public sealed record VpnV2CountryDownload(string Country, string ServerPrefix)
{
    public string DisplayName => $"{Country} — download all official UDP configs";
}

public sealed record VpnV2CountryDownloadResult(string Country, int InstalledCount);

public static partial class VpnV2LocationCatalog
{
    private static readonly Uri OfficialArchiveUri = new("https://downloads.nordcdn.com/configs/archives/servers/ovpn.zip");
    public static IReadOnlyList<VpnV2CountryDownload> All { get; } =
    [
        new("Canada", "ca"),
        new("United Kingdom", "uk")
    ];

    public static async Task<VpnV2CountryDownloadResult> DownloadCountryAsync(VpnV2CountryDownload country, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var trusted = All.FirstOrDefault(item => item == country)
            ?? throw new InvalidOperationException("That country is not in Monkey TV's trusted Nord download catalog.");
        PortablePaths.EnsureCreated();
        var archivePath = Path.Combine(PortablePaths.VpnV2Directory, "nord-ovpn-download.tmp");
        try
        {
            progress?.Report("Downloading Nord's official OpenVPN archive…");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            using var response = await client.GetAsync(OfficialArchiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
                throw new InvalidDataException("Nord redirected the archive download. Monkey TV stopped instead of following it to an unverified host.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 100 * 1024 * 1024)
                throw new InvalidDataException("Nord returned an unexpectedly large configuration archive.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > 100 * 1024 * 1024) throw new InvalidDataException("Nord returned an unexpectedly large configuration archive.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            progress?.Report("Validating and installing " + trusted.Country + " configurations…");
            using var archive = ZipFile.OpenRead(archivePath);
            var matching = archive.Entries.Where(entry =>
                entry.FullName.Replace('\\', '/').StartsWith("ovpn_udp/", StringComparison.OrdinalIgnoreCase) &&
                CountryFileName().IsMatch(entry.Name) &&
                entry.Name.StartsWith(trusted.ServerPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count == 0) throw new InvalidDataException("Nord's archive contained no UDP configurations for " + trusted.Country + ".");

            var configs = new List<(string ServerName, byte[] Content)>(matching.Count);
            foreach (var entry in matching)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Length is < 64 or > 256 * 1024) throw new InvalidDataException("Nord's archive contained an invalid configuration size.");
                using var entryStream = entry.Open();
                using var content = new MemoryStream((int)entry.Length);
                entryStream.CopyTo(content);
                configs.Add((entry.Name[..^".udp.ovpn".Length], content.ToArray()));
            }
            var installed = VpnV2ConfigStore.ReplaceCountry(trusted.Country, configs);
            return new VpnV2CountryDownloadResult(trusted.Country, installed);
        }
        finally
        {
            try { File.Delete(archivePath); } catch { }
        }
    }

    [GeneratedRegex(@"^(?:ca|uk)\d+\.nordvpn\.com\.udp\.ovpn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountryFileName();
}
