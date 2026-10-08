using VpnBypass.Core.Config;
using VpnBypass.Core.Driver;
using VpnBypass.Core.Ipc;
using VpnBypass.Core.Rules;
using Xunit;

namespace VpnBypass.Core.Tests;

public class IpcContractTests
{
    [Fact]
    public void SetExcluded_request_roundtrips()
    {
        var req = new IpcRequest(IpcRequestType.SetExcluded, Path: @"C:\x\y.exe", Excluded: true);
        var json = IpcJson.Serialize(req);
        var back = IpcJson.DeserializeRequest(json);
        Assert.Equal(req, back); // nincs listamező → record-érték-egyenlőség működik
    }

    [Fact]
    public void Request_type_serializes_as_string_not_number()
    {
        var json = IpcJson.Serialize(new IpcRequest(IpcRequestType.SetPaused, Paused: true));
        Assert.Contains("SetPaused", json);
        Assert.DoesNotContain("\"type\":3", json);
    }

    [Fact]
    public void Status_response_serializes_enum_and_camelcase()
    {
        var resp = new StatusResponse(
            DriverState.Engaged, Paused: false,
            Vpn: new VpnInfo("NordLynx", "10.5.0.2", null),
            Internet: new VpnInfo("Ethernet 3", "192.168.0.51", null),
            ExcludedCount: 3, Error: null);
        var json = IpcJson.Serialize(resp);
        Assert.Contains("Engaged", json);          // enum sztringként
        Assert.DoesNotContain("\"state\":4", json);
        Assert.Contains("\"excludedCount\":3", json); // camelCase property
    }

    [Fact]
    public void Status_response_roundtrips_by_value()
    {
        var resp = new StatusResponse(DriverState.Ready, true, null, null, 0, "hiba");
        var back = IpcJson.DeserializeStatus(IpcJson.Serialize(resp));
        Assert.Equal(resp, back);
    }

    [Fact]
    public void App_config_roundtrips_field_by_field()
    {
        var rule = ExclusionRule.Create(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe");
        var cfg = new AppConfig(new[] { rule }, InternetAdapter: "Ethernet 3", UserSid: "S-1-5-21-1");
        var back = IpcJson.DeserializeConfig(IpcJson.Serialize(cfg))!;
        Assert.Single(back.Rules);
        Assert.Equal(rule, back.Rules[0]); // ExclusionRule record → érték-egyenlőség
        Assert.Equal("Ethernet 3", back.InternetAdapter);
        Assert.Equal("S-1-5-21-1", back.UserSid);
    }

    [Fact]
    public void Empty_config_has_no_rules()
    {
        Assert.Empty(AppConfig.Empty.Rules);
        Assert.Null(AppConfig.Empty.InternetAdapter);
    }
}
