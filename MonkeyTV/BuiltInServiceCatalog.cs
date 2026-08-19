namespace MonkeyTV;

public sealed record BuiltInService(string Id, string Name, string Url, string Region, string ArtworkReference, ServiceExperience Experience = ServiceExperience.Default, int Revision = 1)
{
    public ServiceDefinition Create() => new()
    {
        BuiltInServiceId = Id, BuiltInDefinitionRevision = Revision, Name = Name, Url = Url,
        BannerPath = ArtworkReference, Experience = Experience, AddedUtc = DateTime.UtcNow,
        BrowserStorageMode = BrowserStorageMode.UnifiedSharedProfile
    };
}

public static class BuiltInServiceCatalog
{
    private static readonly IReadOnlyList<BuiltInService> Items = new[]
    {
        S("canada/cbc-gem","CBC Gem","https://gem.cbc.ca/","Canada"), S("canada/citytv","Citytv","https://www.citytv.com/","Canada"), S("canada/crave","Crave","https://www.crave.ca/","Canada"), S("canada/ctv","CTV","https://www.ctv.ca/","Canada"), S("canada/global-tv","Global TV","https://www.globaltv.com/","Canada"), S("canada/ici-tou-tv","ICI TOU.TV","https://ici.tou.tv/","Canada"), S("canada/sportsnet","Sportsnet","https://www.sportsnet.ca/","Canada"), S("canada/tsn","TSN","https://www.tsn.ca/","Canada"),
        S("general/apple-tv","Apple TV","https://tv.apple.com/","Global"), S("general/crunchyroll","Crunchyroll","https://www.crunchyroll.com/","Global"), S("general/discovery-plus","Discovery+","https://www.discoveryplus.com/","Global"), S("general/disney-plus","Disney+","https://www.disneyplus.com/","Global"), S("general/fubo","Fubo","https://www.fubo.tv/","Global"), S("general/hbo-max","HBO Max","https://www.max.com/","Global"), S("general/hulu","Hulu","https://www.hulu.com/","Global"), S("general/netflix","Netflix","https://www.netflix.com/","Global"), S("general/paramount-plus","Paramount+","https://www.paramountplus.com/","Global"), S("general/peacock","Peacock","https://www.peacocktv.com/","Global"), S("general/philo","Philo","https://www.philo.com/","Global"), S("general/plex","Plex","https://app.plex.tv/","Global"), S("general/pluto-tv","Pluto TV","https://pluto.tv/","Global"), S("general/prime-video","Prime Video","https://www.primevideo.com/","Global"), S("general/sling","Sling","https://www.sling.com/","Global"), S("general/tubi","Tubi","https://tubitv.com/","Global"), S("general/youtube-tv","YouTube TV","https://tv.youtube.com/","Global",ServiceExperience.TV), S("general/youtube","YouTube","https://www.youtube.com/","Global"),
        S("uk/channel-4","Channel 4","https://www.channel4.com/","UK"), S("uk/channel-5","Channel 5","https://www.channel5.com/","UK"), S("uk/itvx","ITVX","https://www.itv.com/","UK"), S("uk/sky-go","Sky Go","https://www.sky.com/watch/sky-go","UK"),
        S("usa/amc-plus","AMC+","https://www.amcplus.com/","USA"), S("usa/bet-plus","BET+","https://www.bet.plus/","USA"), S("usa/britbox-plus","BritBox+","https://www.britbox.com/","USA"), S("usa/espn","ESPN","https://www.espn.com/watch/","USA"), S("usa/fox-one","FOX One","https://www.fox.com/","USA"), S("usa/hallmark-plus","Hallmark+","https://www.hallmarkplus.com/","USA"), S("usa/mgm-plus","MGM+","https://www.mgmplus.com/","USA"), S("usa/pbs-kids","PBS Kids","https://pbskids.org/video/","USA"), S("usa/roku-channel","The Roku Channel","https://therokuchannel.roku.com/","USA"), S("usa/starz","STARZ","https://www.starz.com/","USA"),
        S("web/dailymotion","Dailymotion","https://www.dailymotion.com/","Other"), S("web/nebula","Nebula","https://nebula.tv/","Other"), S("web/samsung-tv-plus","Samsung TV Plus","https://www.samsungtvplus.com/","Other"), S("web/twitch","Twitch","https://www.twitch.tv/","Other"), S("web/vimeo","Vimeo","https://vimeo.com/","Other"), S("web/xumo-play","Xumo Play","https://play.xumo.com/","Other")
    };
    public static IReadOnlyList<BuiltInService> All => Items;
    public static BuiltInService? Find(string? id) => Items.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public static bool IsCustomized(ServiceDefinition service, BuiltInService template) => service.Name != template.Name || !UrlsEqual(service.Url, template.Url) || service.BannerPath != template.ArtworkReference || service.Experience != template.Experience;
    public static void RestoreDefaults(ServiceDefinition service, BuiltInService template) { service.Name = template.Name; service.Url = template.Url; service.BannerPath = template.ArtworkReference; service.Experience = template.Experience; service.BuiltInDefinitionRevision = template.Revision; }
    public static ServiceDefinition? FindProbableCustomMatch(IEnumerable<ServiceDefinition> services, BuiltInService template) => services.FirstOrDefault(service => service.BuiltInServiceId is null && (UrlsEqual(service.Url, template.Url) || string.Equals(service.Name, template.Name, StringComparison.OrdinalIgnoreCase)));
    private static BuiltInService S(string id, string name, string url, string region, ServiceExperience experience = ServiceExperience.Default) => new(id, name, url, region, BuiltInArtworkCatalog.ReferencePrefix + id, experience);
    private static bool UrlsEqual(string left, string right) => Uri.TryCreate(left, UriKind.Absolute, out var a) && Uri.TryCreate(right, UriKind.Absolute, out var b) && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.AbsolutePath.TrimEnd('/') == b.AbsolutePath.TrimEnd('/');
}
