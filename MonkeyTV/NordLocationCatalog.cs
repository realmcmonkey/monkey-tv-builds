namespace MonkeyTV;

public sealed record NordLocation(NordLocationId Id, string Name, string Host, string Country, string? City = null);

public static class NordLocationCatalog
{
    // These are NordVPN's documented SOCKS5 hostnames. Keep this deliberately static: do not
    // scrape or rely on an undocumented Nord API for locations or recommendations.
    private static readonly IReadOnlyList<NordLocation> Items = new NordLocation[]
    {
        new(NordLocationId.UnitedStates, "United States", "us.socks.nordhold.net", "United States"),
        new(NordLocationId.Netherlands, "Netherlands", "nl.socks.nordhold.net", "Netherlands"),
        new(NordLocationId.Sweden, "Sweden", "se.socks.nordhold.net", "Sweden"),
        new(NordLocationId.Atlanta, "United States — Atlanta", "atlanta.us.socks.nordhold.net", "United States", "Atlanta"),
        new(NordLocationId.Chicago, "United States — Chicago", "chicago.us.socks.nordhold.net", "United States", "Chicago"),
        new(NordLocationId.Dallas, "United States — Dallas", "dallas.us.socks.nordhold.net", "United States", "Dallas"),
        new(NordLocationId.LosAngeles, "United States — Los Angeles", "los-angeles.us.socks.nordhold.net", "United States", "Los Angeles"),
        new(NordLocationId.NewYork, "United States — New York", "new-york.us.socks.nordhold.net", "United States", "New York"),
        new(NordLocationId.Phoenix, "United States — Phoenix", "phoenix.us.socks.nordhold.net", "United States", "Phoenix"),
        new(NordLocationId.SanFrancisco, "United States — San Francisco", "san-francisco.us.socks.nordhold.net", "United States", "San Francisco"),
        new(NordLocationId.Stockholm, "Sweden — Stockholm", "stockholm.se.socks.nordhold.net", "Sweden", "Stockholm"),
        new(NordLocationId.Amsterdam, "Netherlands — Amsterdam", "nl.socks.nordhold.net", "Netherlands", "Amsterdam")
    };
    public static IReadOnlyList<NordLocation> All => Items;
    public static NordLocation Get(NordLocationId id) => Items.First(item => item.Id == id);
    public static NordLocation? TryGet(NordLocationId id) => Items.FirstOrDefault(item => item.Id == id);
    public static IReadOnlyList<string> Countries => Items.Select(item => item.Country).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public static IReadOnlyList<NordLocation> ForCountry(string country) => Items.Where(item => item.Country.Equals(country, StringComparison.CurrentCultureIgnoreCase)).OrderBy(item => item.City is null ? "" : item.City, StringComparer.CurrentCultureIgnoreCase).ToList();

    // Keep the picker limited to the countries requested by Monkey TV. Nord's documented
    // SOCKS5 list currently provides U.S. endpoints only; Canada and the United Kingdom are
    // shown as unavailable instead of being inferred from Nord's normal VPN server list.
    public static IReadOnlyList<string> PickerCountries { get; } = new[]
    {
        "United States", "Canada", "United Kingdom"
    };

    public static IReadOnlyList<NordLocation> ForPickerCountry(string country) =>
        PickerCountries.Contains(country, StringComparer.OrdinalIgnoreCase)
            ? ForCountry(country)
            : Array.Empty<NordLocation>();

    public static bool IsPickerLocation(NordLocation location) =>
        PickerCountries.Contains(location.Country, StringComparer.OrdinalIgnoreCase);
}
