using System.Net;
using VpnBypass.Core.Status;
using Xunit;

namespace VpnBypass.Core.Tests;

public class ConnectionClassifierTests
{
    static readonly IPAddress Internet = IPAddress.Parse("192.168.0.51");
    static readonly IPAddress Tunnel = IPAddress.Parse("10.5.0.2");

    [Fact]
    public void All_internet_connections_are_direct()
    {
        var conns = new[]
        {
            new ConnectionSample(Internet, true, 2, false),
            new ConnectionSample(Internet, true, 1, false),
        };
        Assert.Equal(AppStatus.Direct, ConnectionClassifier.Classify(conns, Internet, Tunnel));
    }

    [Fact]
    public void Mixed_connections_are_partial()
    {
        var conns = new[]
        {
            new ConnectionSample(Internet, true, 2, false),
            new ConnectionSample(Tunnel, true, 2, false),
        };
        Assert.Equal(AppStatus.Partial, ConnectionClassifier.Classify(conns, Internet, Tunnel));
    }

    [Fact]
    public void Only_tunnel_connections_are_vpn()
    {
        var conns = new[] { new ConnectionSample(Tunnel, true, 3, false) };
        Assert.Equal(AppStatus.Vpn, ConnectionClassifier.Classify(conns, Internet, Tunnel));
    }

    [Fact]
    public void Stuck_syn_from_internet_with_no_established_is_blocked()
    {
        var conns = new[] { new ConnectionSample(Internet, false, 7, true) };
        Assert.Equal(AppStatus.Blocked, ConnectionClassifier.Classify(conns, Internet, Tunnel));
    }

    [Fact]
    public void Fresh_syn_is_not_yet_blocked()
    {
        // 2 mp még a küszöb alatt → nem blokkolt; a fizikai címről megy, tehát Direct.
        var conns = new[] { new ConnectionSample(Internet, false, 2, true) };
        Assert.Equal(AppStatus.Direct, ConnectionClassifier.Classify(conns, Internet, Tunnel));
    }

    [Fact]
    public void No_external_connections_is_not_running()
    {
        var conns = new[] { new ConnectionSample(IPAddress.Loopback, true, 1, false) };
        Assert.Equal(AppStatus.NotRunning, ConnectionClassifier.Classify(conns, Internet, Tunnel));
    }

    [Fact]
    public void Null_tunnel_address_still_classifies_internet_as_direct()
    {
        var conns = new[] { new ConnectionSample(Internet, true, 1, false) };
        Assert.Equal(AppStatus.Direct, ConnectionClassifier.Classify(conns, Internet, null));
    }
}
