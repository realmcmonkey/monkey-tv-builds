namespace MonkeyTV;

public sealed record NordLocation(NordLocationId Id, string Name, string Host);

public static class NordLocationCatalog
{
    // These are NordVPN's documented SOCKS5 hostnames. Keep this deliberately static: do not
    // scrape or rely on an undocumented Nord API for locations or recommendations.
    private static readonly IReadOnlyList<NordLocation> Items = new NordLocation[]
    {
        new(NordLocationId.UnitedStates, "United States", "us.socks.nordhold.net"),
        new(NordLocationId.Netherlands, "Netherlands", "nl.socks.nordhold.net"),
        new(NordLocationId.Sweden, "Sweden", "se.socks.nordhold.net"),
        new(NordLocationId.Atlanta, "United States — Atlanta", "atlanta.us.socks.nordhold.net"),
        new(NordLocationId.Chicago, "United States — Chicago", "chicago.us.socks.nordhold.net"),
        new(NordLocationId.Dallas, "United States — Dallas", "dallas.us.socks.nordhold.net"),
        new(NordLocationId.LosAngeles, "United States — Los Angeles", "los-angeles.us.socks.nordhold.net"),
        new(NordLocationId.NewYork, "United States — New York", "new-york.us.socks.nordhold.net"),
        new(NordLocationId.Phoenix, "United States — Phoenix", "phoenix.us.socks.nordhold.net"),
        new(NordLocationId.SanFrancisco, "United States — San Francisco", "san-francisco.us.socks.nordhold.net"),
        new(NordLocationId.Stockholm, "Sweden — Stockholm", "stockholm.se.socks.nordhold.net"),
        new(NordLocationId.Amsterdam, "Netherlands — Amsterdam", "amsterdam.nl.socks.nordhold.net")
    };
    public static IReadOnlyList<NordLocation> All => Items;
    public static NordLocation Get(NordLocationId id) => Items.First(item => item.Id == id);
}
