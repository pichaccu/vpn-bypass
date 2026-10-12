using System.Net;
using VpnBypass.Core.Driver;
using VpnBypass.Core.Network;
using Xunit;

namespace VpnBypass.Core.Tests;

public class ProcessDiscoveryTests
{
    [Fact]
    public void Two_entries_have_correct_layout()
    {
        string a = @"\device\harddiskvolume3\a.exe";
        string b = @"\device\harddiskvolume3\bb.exe";
        var blob = DriverStructs.BuildProcessDiscovery(new[]
        {
            new ProcessEntry(100, 4, a),
            new ProcessEntry(200, 100, b),
        });

        Assert.Equal(2, BitConverter.ToInt64(blob, 0));           // NumEntries
        Assert.Equal(blob.Length, BitConverter.ToInt64(blob, 8)); // TotalLength

        // 0. bejegyzés a 16. bájttól (32 bájtos bejegyzések)
        Assert.Equal(100, BitConverter.ToInt64(blob, 16));        // ProcessId
        Assert.Equal(4, BitConverter.ToInt64(blob, 24));          // ParentProcessId
        Assert.Equal(0, BitConverter.ToInt64(blob, 32));          // ImageNameOffset
        Assert.Equal((ushort)(a.Length * 2), BitConverter.ToUInt16(blob, 40));

        // 1. bejegyzés a 48. bájttól
        Assert.Equal(200, BitConverter.ToInt64(blob, 48));
        Assert.Equal(100, BitConverter.ToInt64(blob, 56));
        Assert.Equal(a.Length * 2, BitConverter.ToInt64(blob, 64)); // a második string az első után

        int stringBase = 16 + 2 * 32;
        Assert.Equal(a, System.Text.Encoding.Unicode.GetString(blob, stringBase, a.Length * 2));
        Assert.Equal(b, System.Text.Encoding.Unicode.GetString(blob, stringBase + a.Length * 2, b.Length * 2));
        Assert.Equal(stringBase + (a.Length + b.Length) * 2, blob.Length);
    }

    [Fact]
    public void Process_without_path_has_zero_offset_and_length()
    {
        var blob = DriverStructs.BuildProcessDiscovery(new[] { new ProcessEntry(4, 0, "") });
        Assert.Equal(16 + 32, blob.Length);             // nincs stringpuffer
        Assert.Equal(0, BitConverter.ToInt64(blob, 32));  // offset
        Assert.Equal(0, BitConverter.ToUInt16(blob, 40)); // hossz
    }

    [Fact]
    public void Empty_process_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DriverStructs.BuildProcessDiscovery(Array.Empty<ProcessEntry>()));
    }

    [Fact]
    public void Driver_addresses_from_detection_require_both_tunnel_and_internet()
    {
        var eth = new AdapterInfo(17, "Ethernet 3", true, true, 20, IPAddress.Parse("192.168.0.51"), null);
        var nord = new AdapterInfo(40, "NordLynx", false, true, 5, IPAddress.Parse("10.5.0.2"), null);

        var engaged = DriverAddresses.From(new DetectionResult(nord, eth));
        Assert.Equal(IPAddress.Parse("10.5.0.2"), engaged.TunnelV4);
        Assert.Equal(IPAddress.Parse("192.168.0.51"), engaged.InternetV4);
        Assert.True(engaged.HasTunnel);

        Assert.Equal(DriverAddresses.None, DriverAddresses.From(new DetectionResult(null, eth)));
        Assert.Equal(DriverAddresses.None, DriverAddresses.From(new DetectionResult(nord, null)));
        Assert.Equal(40, DriverAddresses.None.ToBlob().Length);
    }
}
