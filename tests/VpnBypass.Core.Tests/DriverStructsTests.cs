using System.Net;
using VpnBypass.Core.Driver;
using Xunit;

namespace VpnBypass.Core.Tests;

public class DriverStructsTests
{
    [Fact]
    public void Ip_blob_is_40_bytes_with_ipv4_in_place()
    {
        // ST_IP_ADDRESSES = 4+4+16+16 = 40 bájt (NEM 44 — a driver pontosan ennyit vár).
        var blob = DriverStructs.BuildIpAddresses(
            IPAddress.Parse("10.5.0.2"), IPAddress.Parse("192.168.0.51"), null, null);
        Assert.Equal(40, blob.Length);
        Assert.Equal(new byte[] { 10, 5, 0, 2 }, blob[0..4]);     // TunnelIpv4
        Assert.Equal(new byte[] { 192, 168, 0, 51 }, blob[4..8]); // InternetIpv4
        // a tunnel/internet IPv6 nulla
        Assert.All(blob[8..40], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Ip_blob_places_ipv6_after_the_two_ipv4_slots()
    {
        var v6 = IPAddress.Parse("fe80::1");
        var blob = DriverStructs.BuildIpAddresses(null, null, v6, null);
        Assert.Equal(40, blob.Length);
        Assert.Equal(v6.GetAddressBytes(), blob[8..24]); // TunnelIpv6 a 8. bájttól
    }

    [Theory]
    [InlineData("GetState", 0x80000024u)]
    [InlineData("Initialize", 0x80000004u)]
    [InlineData("SetConfiguration", 0x80000018u)]
    public void Ioctl_codes_match_ctl_code_formula(string name, uint expected)
    {
        uint actual = name switch
        {
            "GetState" => Ioctl.GetState,
            "Initialize" => Ioctl.Initialize,
            "SetConfiguration" => Ioctl.SetConfiguration,
            _ => 0u,
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Neither_method_ioctls_have_low_bits_set()
    {
        // ClearConfiguration (idx 8) és Reset (idx 11) METHOD_NEITHER → alsó 2 bit = 3.
        Assert.Equal(0x80000023u, Ioctl.ClearConfiguration);
        Assert.Equal(0x8000002Fu, Ioctl.Reset);
        Assert.Equal(3u, Ioctl.ClearConfiguration & 0x3);
        Assert.Equal(3u, Ioctl.Reset & 0x3);
    }

    [Fact]
    public void Config_blob_layout_is_correct_for_one_entry()
    {
        string nt = @"\device\harddiskvolume3\a\b.exe";
        var blob = DriverStructs.BuildConfiguration(new[] { nt });
        Assert.Equal(1, BitConverter.ToInt64(blob, 0));           // NumEntries
        Assert.Equal(blob.Length, BitConverter.ToInt64(blob, 8)); // TotalLength
        // egy bejegyzés: ImageNameOffset=0 a 16. bájttól, ImageNameLength a 24. bájttól
        Assert.Equal(0, BitConverter.ToInt64(blob, 16));
        Assert.Equal((ushort)(nt.Length * 2), BitConverter.ToUInt16(blob, 24));
        // a stringpuffer a fejléc (16) + egy bejegyzés (16) után kezdődik
        int stringBase = 16 + 16;
        var str = System.Text.Encoding.Unicode.GetString(blob, stringBase, nt.Length * 2);
        Assert.Equal(nt, str);
        Assert.Equal(stringBase + nt.Length * 2, blob.Length);
    }

    [Fact]
    public void Config_blob_two_entries_have_sequential_offsets()
    {
        var blob = DriverStructs.BuildConfiguration(new[] { @"\device\hv1\a.exe", @"\device\hv1\bb.exe" });
        Assert.Equal(2, BitConverter.ToInt64(blob, 0));
        // első bejegyzés offsetje 0; másodiké az első string bájthossza
        Assert.Equal(0, BitConverter.ToInt64(blob, 16));
        int firstLen = @"\device\hv1\a.exe".Length * 2;
        Assert.Equal(firstLen, BitConverter.ToInt64(blob, 32)); // második bejegyzés a 32. bájttól
    }

    [Fact]
    public void Sublayer_guids_blob_is_32_bytes()
    {
        var baseline = Guid.Parse("6e0273b7-33fd-4cfc-9312-8cffcc27dbdc");
        var dns = Guid.Parse("63359fcc-b89d-4c49-81c9-c011a3eee3cd");
        var blob = DriverStructs.BuildSublayerGuids(baseline, dns);
        Assert.Equal(32, blob.Length);
        Assert.Equal(baseline.ToByteArray(), blob[0..16]);
        Assert.Equal(dns.ToByteArray(), blob[16..32]);
    }
}
