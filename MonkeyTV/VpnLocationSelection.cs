namespace MonkeyTV;

/// <summary>Public picker result. Routing implementation remains an internal detail.</summary>
public sealed record VpnLocationSelection(VpnLocationKind Kind, NordLocation? Socks5Location = null,
    string? Country = null, VpnV2ImportedConfig? OpenVpnConfig = null)
{
    public static VpnLocationSelection Direct { get; } = new(VpnLocationKind.Direct);
    public static VpnLocationSelection ForSocks5(NordLocation location) => new(VpnLocationKind.Socks5, Socks5Location: location, Country: location.Country);
    public static VpnLocationSelection ForOpenVpn(string country, VpnV2ImportedConfig? config = null) => new(VpnLocationKind.OpenVpn, Country: country, OpenVpnConfig: config);
}
