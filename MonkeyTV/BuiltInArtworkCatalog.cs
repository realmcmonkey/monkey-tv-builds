using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MonkeyTV;

public sealed record BuiltInArtwork(string Id, string Name, string Category, string ResourcePath, params string[] Aliases)
{
    public string Reference => BuiltInArtworkCatalog.ReferencePrefix + Id;
}

public static class BuiltInArtworkCatalog
{
    public const string ReferencePrefix = "builtin:";

    public static IReadOnlyList<BuiltInArtwork> All { get; } = new[]
    {
        Art("canada/cbc-gem", "CBC Gem", "Canada", "cbc gem", "gem.cbc.ca"),
        Art("canada/citytv", "Citytv", "Canada", "city tv", "citytv.com"),
        Art("canada/crave", "Crave", "Canada", "crave.ca"),
        Art("canada/ctv", "CTV", "Canada", "ctv.ca"),
        Art("canada/global-tv", "Global TV", "Canada", "globaltv", "globaltv.com"),
        Art("canada/ici-tou-tv", "ICI TOU.TV", "Canada", "tou.tv", "ici tou tv"),
        Art("canada/sportsnet", "Sportsnet", "Canada", "sports net", "sportsnet.ca"),
        Art("canada/tsn", "TSN", "Canada", "tsn.ca"),

        Art("general/apple-tv", "Apple TV", "General", "apple tv+", "tv.apple.com"),
        Art("general/crunchyroll", "Crunchyroll", "General", "crunchyroll.com"),
        Art("general/discovery-plus", "Discovery+", "General", "discovery plus", "discoveryplus.com"),
        Art("general/disney-plus", "Disney+", "General", "disney plus", "disneyplus.com"),
        Art("general/fubo", "Fubo", "General", "fubo tv", "fubo.tv"),
        Art("general/hbo-max", "HBO Max", "General", "max", "max.com", "hbomax"),
        Art("general/hulu", "Hulu", "General", "hulu.com"),
        Art("general/netflix", "Netflix", "General", "netflix.com"),
        Art("general/paramount-plus", "Paramount+", "General", "paramount plus", "paramountplus.com"),
        Art("general/peacock", "Peacock", "General", "peacocktv", "peacocktv.com"),
        Art("general/philo", "Philo", "General", "philo.com"),
        Art("general/plex", "Plex", "General", "plex.tv"),
        Art("general/pluto-tv", "Pluto TV", "General", "plutotv", "pluto.tv"),
        Art("general/prime-video", "Prime Video", "General", "amazon prime video", "primevideo.com"),
        Art("general/sling", "Sling", "General", "sling tv", "sling.com"),
        Art("general/tubi", "Tubi", "General", "tubitv", "tubitv.com"),
        Art("general/youtube-tv", "YouTube TV", "General", "tv.youtube.com", "youtube television"),
        Art("general/youtube", "YouTube", "General", "youtube.com"),

        Art("uk/channel-4", "Channel 4", "United Kingdom", "channel4.com", "all 4"),
        Art("uk/channel-5", "Channel 5", "United Kingdom", "channel5.com", "my5"),
        Art("uk/itvx", "ITVX", "United Kingdom", "itv x", "itv.com"),
        Art("uk/sky-go", "Sky Go", "United Kingdom", "skygo", "sky.com"),

        Art("usa/amc-plus", "AMC+", "United States", "amc plus", "amcplus.com"),
        Art("usa/bet-plus", "BET+", "United States", "bet plus", "bet.plus"),
        Art("usa/britbox-plus", "BritBox+", "United States", "britbox", "britbox.com"),
        Art("usa/espn", "ESPN", "United States", "espn.com"),
        Art("usa/fox-one", "FOX One", "United States", "fox", "fox.com"),
        Art("usa/hallmark-plus", "Hallmark+", "United States", "hallmark plus", "hallmarkplus.com"),
        Art("usa/mgm-plus", "MGM+", "United States", "mgm plus", "mgmplus.com"),
        Art("usa/pbs-kids", "PBS Kids", "United States", "pbskids", "pbskids.org"),
        Art("usa/roku-channel", "The Roku Channel", "United States", "roku channel", "therokuchannel.roku.com"),
        Art("usa/starz", "STARZ", "United States", "starz.com"),

        Art("web/dailymotion", "Dailymotion", "Web", "dailymotion.com"),
        Art("web/nebula", "Nebula", "Web", "nebula.tv"),
        Art("web/samsung-tv-plus", "Samsung TV Plus", "Web", "samsung tv+", "samsungtvplus.com"),
        Art("web/twitch", "Twitch", "Web", "twitch.tv"),
        Art("web/vimeo", "Vimeo", "Web", "vimeo.com"),
        Art("web/xumo-play", "Xumo Play", "Web", "xumo", "play.xumo.com")
    };

    public static bool IsBuiltInReference(string? value) =>
        value?.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase) == true;

    public static BuiltInArtwork? TryGet(string? reference)
    {
        if (!IsBuiltInReference(reference)) return null;
        var id = reference![ReferencePrefix.Length..];
        return All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public static BitmapImage? TryLoad(string? reference)
    {
        var item = TryGet(reference);
        if (item is null) return null;
        try
        {
            var uri = new Uri($"pack://application:,,,/MonkeyTV;component/{item.ResourcePath}", UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = uri;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    public static BuiltInArtwork? FindBestMatch(string serviceName, string serviceUrl)
    {
        var normalizedName = Normalize(serviceName);
        var host = Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) ? Normalize(uri.Host) : "";
        return All.FirstOrDefault(item => CandidateTerms(item).Any(term =>
            normalizedName == term || (!string.IsNullOrEmpty(host) && (host == term || host.Contains(term, StringComparison.Ordinal)))));
    }

    private static IEnumerable<string> CandidateTerms(BuiltInArtwork item) =>
        new[] { item.Name }.Concat(item.Aliases).Select(Normalize).Where(value => value.Length >= 3);

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(character)) builder.Append(character);
        return builder.ToString();
    }

    private static BuiltInArtwork Art(string id, string name, string category, params string[] aliases) =>
        new(id, name, category, $"Assets/DefaultArtwork/{id}.jpg", aliases);
}
