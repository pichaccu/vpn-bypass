using System.Net;
using VpnBypass.Core.Network;

namespace VpnBypass.Core.Driver;

// A drivernek regisztrált címnégyes (ST_IP_ADDRESSES). A null = "nincs ilyen cím" (nullákkal megy ki).
// Record-egyenlőség: az IPAddress érték szerint hasonlít, így a "változott-e" döntés megbízható.
public sealed record DriverAddresses(
    IPAddress? TunnelV4,
    IPAddress? InternetV4,
    IPAddress? TunnelV6,
    IPAddress? InternetV6)
{
    public static DriverAddresses None { get; } = new(null, null, null, null);

    // A driver csak akkor lép ENGAGED-be, ha van érvényes tunnel-cím (v4 vagy v6).
    public bool HasTunnel => TunnelV4 is not null || TunnelV6 is not null;

    public byte[] ToBlob() => DriverStructs.BuildIpAddresses(TunnelV4, InternetV4, TunnelV6, InternetV6);

    // A felismerésből: ha nincs VPN vagy nincs fizikai kijárat → None (a driver tétlen marad,
    // minden forgalom az útvonaltábla szerint megy — hiba esetén ez a biztonságos irány).
    public static DriverAddresses From(DetectionResult detection)
    {
        if (detection.Tunnel is null || detection.Internet is null)
            return None;
        return new DriverAddresses(
            detection.Tunnel.Ipv4, detection.Internet.Ipv4,
            detection.Tunnel.Ipv6, detection.Internet.Ipv6);
    }
}
