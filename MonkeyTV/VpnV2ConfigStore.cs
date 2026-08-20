using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonkeyTV;

public sealed record VpnV2ImportedConfig(Guid Id, string Country, string ServerName, string Protocol, string FileName)
{
    public string DisplayName => $"{Country} — {ServerName}";
    public string FullPath => Path.Combine(PortablePaths.VpnV2ConfigDirectory, FileName);
}

public static partial class VpnV2ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<VpnV2ImportedConfig> Load()
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<VpnV2ImportedConfig>>(File.ReadAllText(PortablePaths.VpnV2IndexFile), JsonOptions) ?? [];
            return items.Where(item => item.Id != Guid.Empty &&
                    VpnV2LocationCatalog.All.Any(location => location.Country == item.Country) &&
                    File.Exists(item.FullPath))
                .OrderBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ServerName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return []; }
    }

    public static VpnV2ImportedConfig Import(string source)
    {
        var info = new FileInfo(source);
        if (!info.Exists || info.Length is < 64 or > 256 * 1024) throw new InvalidDataException("Choose an official Nord .ovpn configuration file.");
        var match = OfficialCanadaName().Match(info.Name);
        if (!match.Success || !match.Groups["server"].Value.Equals("ca1968", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This first experimental build accepts only the proven official ca1968 Nord Canada configuration.");
        var text = File.ReadAllText(source);
        var required = new[] { "client", "dev tun", "remote-cert-tls server", "auth-user-pass", "<ca>", "</ca>", "<tls-auth>", "</tls-auth>" };
        if (required.Any(value => !text.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
            !Regex.IsMatch(text, @"(?mi)^\s*proto\s+udp\s*$") ||
            !Regex.IsMatch(text, @"(?mi)^\s*remote\s+187\.13\.240\.179\s+53\s*$"))
            throw new InvalidDataException("The configuration does not match the Nord Canada profile proven by this experiment.");
        var externalPathDirectives = new HashSet<string>(new[] { "auth-user-pass", "ca", "cert", "key", "pkcs12", "tls-auth" }, StringComparer.OrdinalIgnoreCase);
        var externalReference = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith(';'))
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length > 1 && externalPathDirectives.Contains(parts[0]));
        if (externalReference is not null)
            throw new InvalidDataException($"Only self-contained official configurations are accepted; '{externalReference[0]}' references an external file.");
        PortablePaths.EnsureCreated();
        var existing = Load().ToList();
        var prior = existing.FirstOrDefault(item => item.ServerName.Equals("ca1968.nordvpn.com", StringComparison.OrdinalIgnoreCase));
        var item = prior ?? new VpnV2ImportedConfig(Guid.NewGuid(), "Canada", "ca1968.nordvpn.com", "UDP", Guid.NewGuid().ToString("N") + ".ovpn");
        File.Copy(source, item.FullPath, true);
        if (prior is null) existing.Add(item);
        Save(existing);
        return item;
    }

    public static int ReplaceCountry(string country, IReadOnlyList<(string ServerName, byte[] Content)> configs)
    {
        if (!VpnV2LocationCatalog.All.Any(item => item.Country == country) || configs.Count == 0)
            throw new InvalidDataException("No trusted country configurations were supplied.");
        foreach (var config in configs)
        {
            if (!Regex.IsMatch(config.ServerName, @"^(?:us|ca|uk)\d+\.nordvpn\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Nord's archive contained an unexpected server name.");
            ValidateSelfContainedConfiguration(System.Text.Encoding.UTF8.GetString(config.Content));
        }
        PortablePaths.EnsureCreated();
        var existing = Load().ToList();
        var oldCountry = existing.Where(item => item.Country.Equals(country, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var old in oldCountry) existing.Remove(old);
        var staged = new List<VpnV2ImportedConfig>(configs.Count);
        foreach (var config in configs)
        {
            var prior = oldCountry.FirstOrDefault(item => item.ServerName.Equals(config.ServerName, StringComparison.OrdinalIgnoreCase));
            var item = prior ?? new VpnV2ImportedConfig(Guid.NewGuid(), country, config.ServerName, "UDP", Guid.NewGuid().ToString("N") + ".ovpn");
            var temporary = item.FullPath + ".tmp";
            File.WriteAllBytes(temporary, config.Content);
            File.Move(temporary, item.FullPath, true);
            staged.Add(item); existing.Add(item);
        }
        Save(existing);
        foreach (var obsolete in oldCountry.Where(old => staged.All(item => item.Id != old.Id)))
            try { File.Delete(obsolete.FullPath); } catch { }
        return staged.Count;
    }

    private static void ValidateSelfContainedConfiguration(string text)
    {
        var required = new[] { "client", "dev tun", "remote-cert-tls server", "auth-user-pass", "<ca>", "</ca>", "<tls-auth>", "</tls-auth>" };
        if (required.Any(value => !text.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
            !Regex.IsMatch(text, @"(?mi)^\s*proto\s+udp\s*$") ||
            !Regex.IsMatch(text, @"(?mi)^\s*remote\s+(?:\d{1,3}\.){3}\d{1,3}\s+\d+\s*$"))
            throw new InvalidDataException("The download is not a supported self-contained Nord UDP configuration.");
        var externalPathDirectives = new HashSet<string>(new[] { "auth-user-pass", "ca", "cert", "key", "pkcs12", "tls-auth" }, StringComparer.OrdinalIgnoreCase);
        var externalReference = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith(';'))
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length > 1 && externalPathDirectives.Contains(parts[0]));
        if (externalReference is not null)
            throw new InvalidDataException($"Only self-contained official configurations are accepted; '{externalReference[0]}' references an external file.");
    }

    public static void Remove(Guid id)
    {
        var items = Load().ToList();
        var item = items.FirstOrDefault(candidate => candidate.Id == id);
        if (item is null) return;
        items.Remove(item);
        try { File.Delete(item.FullPath); } catch { }
        Save(items);
    }

    private static void Save(IReadOnlyList<VpnV2ImportedConfig> items)
    {
        PortablePaths.EnsureCreated();
        var temporary = PortablePaths.VpnV2IndexFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temporary, PortablePaths.VpnV2IndexFile, true);
    }

    [GeneratedRegex(@"^(?<server>ca\d+)\.nordvpn\.com\.udp_2\.6\.ovpn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OfficialCanadaName();
}
