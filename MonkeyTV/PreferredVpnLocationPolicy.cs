namespace MonkeyTV;

public static class PreferredVpnLocationPolicy
{
    public static string DisplayName(PreferredVpnLocation location) => location switch
    {
        PreferredVpnLocation.Direct => "Direct",
        PreferredVpnLocation.USA => "USA",
        PreferredVpnLocation.Canada => "Canada",
        PreferredVpnLocation.UnitedKingdom => "United Kingdom",
        _ => "None"
    };

    public static bool Matches(PreferredVpnLocation preference, CurrentLocationState state,
        NordLocation? socks5Location, VpnV2ImportedConfig? vpnLocation)
    {
        if (preference == PreferredVpnLocation.None) return true;
        if (preference == PreferredVpnLocation.Direct) return state == CurrentLocationState.Direct;
        if (state != CurrentLocationState.Connected) return false;
        var activeCountry = vpnLocation?.Country ?? socks5Location?.Country;
        return preference switch
        {
            PreferredVpnLocation.USA => string.Equals(activeCountry, "United States", StringComparison.OrdinalIgnoreCase),
            PreferredVpnLocation.Canada => string.Equals(activeCountry, "Canada", StringComparison.OrdinalIgnoreCase),
            PreferredVpnLocation.UnitedKingdom => string.Equals(activeCountry, "United Kingdom", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
