using System.Net;
using VpnBypass.Core.Network;
using Xunit;

namespace VpnBypass.Core.Tests;

public class VpnDetectorTests
{
    static AdapterInfo Eth(int i = 17) =>
        new(i, "Ethernet 3", true, true, 20, IPAddress.Parse("192.168.0.51"), null);

    static AdapterInfo Nord(int i = 40) =>
        new(i, "NordLynx", false, true, 5, IPAddress.Parse("10.5.0.2"), null);

    [Fact]
    public void NordLynx_full_tunnel_is_detected()
    {
        var res = VpnDetector.Detect(
            new[] { Eth(), Nord() },
            new[] { new RouteInfo(40, "0.0.0.0/0", 0, 0), new RouteInfo(17, "0.0.0.0/0", 0, 0) });
        Assert.Equal("NordLynx", res.Tunnel!.Name);
        Assert.Equal("Ethernet 3", res.Internet!.Name);
    }

    [Fact]
    public void OpenVpn_half_routes_are_detected_as_tunnel()
    {
        var tap = new AdapterInfo(6, "OpenVPN", false, true, 1, IPAddress.Parse("10.8.0.3"), null);
        var res = VpnDetector.Detect(
            new[] { Eth(), tap },
            new[]
            {
                new RouteInfo(6, "0.0.0.0/1", 1, 0), new RouteInfo(6, "128.0.0.0/1", 1, 0),
                new RouteInfo(17, "0.0.0.0/0", 0, 0)
            });
        Assert.Equal("OpenVPN", res.Tunnel!.Name); // a /1 hosszabb prefix, mint a /0
        Assert.Equal("Ethernet 3", res.Internet!.Name);
    }

    [Fact]
    public void No_vpn_when_hardware_wins()
    {
        var res = VpnDetector.Detect(new[] { Eth() }, new[] { new RouteInfo(17, "0.0.0.0/0", 0, 0) });
        Assert.Null(res.Tunnel);
        Assert.Equal("Ethernet 3", res.Internet!.Name);
    }

    [Fact]
    public void No_default_route_means_no_vpn_and_no_internet()
    {
        var res = VpnDetector.Detect(new[] { Eth() }, new RouteInfo[0]);
        Assert.Null(res.Tunnel);
        Assert.Null(res.Internet);
    }

    [Fact]
    public void Manual_internet_adapter_overrides_detection()
    {
        // Két hardveres kijárat (Ethernet + Wi-Fi), a user a Wi-Fi-t jelölte internetnek.
        var wifi = new AdapterInfo(20, "Wi-Fi", true, true, 25, IPAddress.Parse("192.168.0.60"), null);
        var res = VpnDetector.Detect(
            new[] { Eth(), wifi, Nord() },
            new[]
            {
                new RouteInfo(40, "0.0.0.0/0", 0, 0),
                new RouteInfo(17, "0.0.0.0/0", 0, 0),
                new RouteInfo(20, "0.0.0.0/0", 0, 0)
            },
            manualInternetAdapterName: "Wi-Fi");
        Assert.Equal("NordLynx", res.Tunnel!.Name);
        Assert.Equal("Wi-Fi", res.Internet!.Name);
    }

    [Fact]
    public void Non_operational_tunnel_is_ignored()
    {
        // A NordLynx adapter létezik, de nincs feljövő (leállított VPN) → nincs tunnel.
        var downNord = new AdapterInfo(40, "NordLynx", false, false, 5, null, null);
        var res = VpnDetector.Detect(
            new[] { Eth(), downNord },
            new[] { new RouteInfo(17, "0.0.0.0/0", 0, 0) });
        Assert.Null(res.Tunnel);
        Assert.Equal("Ethernet 3", res.Internet!.Name);
    }
}
