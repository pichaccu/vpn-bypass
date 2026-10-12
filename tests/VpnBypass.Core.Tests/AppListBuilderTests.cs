using System.Net;
using VpnBypass.Core.Ipc;
using VpnBypass.Core.Rules;
using VpnBypass.Core.Status;
using Xunit;

namespace VpnBypass.Core.Tests;

public class AppListBuilderTests
{
    static readonly IPAddress Internet = IPAddress.Parse("192.168.0.51");
    static readonly IPAddress Tunnel = IPAddress.Parse("10.5.0.2");
    const string Win = @"C:\Windows";

    const string Discord1 = @"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe";
    const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    static IReadOnlyDictionary<uint, IReadOnlyList<ConnectionSample>> Samples(
        params (uint Pid, IPAddress Local)[] items) =>
        items.GroupBy(i => i.Pid).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<ConnectionSample>)g.Select(i => new ConnectionSample(i.Local, true, 3, false)).ToList());

    static IReadOnlyList<AppEntry> Build(
        IReadOnlyList<ExclusionRule> rules,
        IReadOnlyList<RunningProcess> running,
        IReadOnlyDictionary<uint, IReadOnlyList<ConnectionSample>> samples,
        bool exists = true) =>
        AppListBuilder.Build(rules, running, samples, Internet, Tunnel, _ => exists, Win);

    [Fact]
    public void Excluded_running_app_on_internet_is_direct_and_listed_first()
    {
        var rules = new[] { ExclusionRule.Create(Discord1) };
        var running = new[]
        {
            new RunningProcess(10, Chrome, "Google Chrome"),
            new RunningProcess(20, Discord1, "Discord"),
        };
        var list = Build(rules, running, Samples((20, Internet), (10, Tunnel)));

        Assert.Equal(2, list.Count);
        Assert.Equal("Discord (minden verzió)", list[0].DisplayName);
        Assert.NotNull(list[0].Pattern);          // kipipálva
        Assert.Equal(AppStatus.Direct, list[0].Status);
        Assert.Null(list[1].Pattern);             // Chrome nincs kipipálva
        Assert.Equal(AppStatus.Vpn, list[1].Status);
        Assert.Equal(Chrome, list[1].RuleId);     // a felület ezt küldi vissza bekapcsoláskor
    }

    [Fact]
    public void Pattern_rule_aggregates_all_matching_versions()
    {
        const string discord2 = @"C:\Users\x\AppData\Local\Discord\app-1.0.9300\Discord.exe";
        var rules = new[] { ExclusionRule.Create(Discord1) };
        var running = new[]
        {
            new RunningProcess(20, Discord1, "Discord"),
            new RunningProcess(21, discord2, "Discord"),
        };
        var list = Build(rules, running, Samples((20, Internet), (21, Tunnel)));

        Assert.Single(list); // mindkét verzió a szabályhoz tartozik, nem jelenik meg külön
        Assert.Equal(AppStatus.Partial, list[0].Status);
    }

    [Fact]
    public void Excluded_app_not_running_is_not_running_or_not_found()
    {
        var rules = new[] { ExclusionRule.Create(Discord1) };
        Assert.Equal(AppStatus.NotRunning, Build(rules, Array.Empty<RunningProcess>(), Samples(), exists: true)[0].Status);
        Assert.Equal(AppStatus.NotFound, Build(rules, Array.Empty<RunningProcess>(), Samples(), exists: false)[0].Status);
    }

    [Fact]
    public void Running_app_without_connections_is_idle_not_missing()
    {
        var rules = new[] { ExclusionRule.Create(Discord1) };
        var list = Build(rules, new[] { new RunningProcess(20, Discord1, "Discord") }, Samples());
        Assert.True(list[0].Running);
        Assert.Equal(AppStatus.Idle, list[0].Status);
    }

    [Fact]
    public void Windows_processes_pathless_and_own_exes_are_hidden_and_duplicates_merged()
    {
        var running = new[]
        {
            new RunningProcess(1, @"C:\Windows\System32\svchost.exe", "svchost"),
            new RunningProcess(2, null, "System"),
            new RunningProcess(3, @"C:\Program Files\vpn-bypass\tray\VpnBypass.Tray.exe", "vpn-bypass"),
            new RunningProcess(10, Chrome, "Google Chrome"),
            new RunningProcess(11, Chrome, "Google Chrome"),
        };
        var list = Build(Array.Empty<ExclusionRule>(), running, Samples());
        Assert.Single(list);
        Assert.Equal("Google Chrome", list[0].DisplayName);
    }

    [Fact]
    public void Without_known_internet_address_running_apps_count_as_direct()
    {
        var rules = new[] { ExclusionRule.Create(Discord1) };
        var list = AppListBuilder.Build(
            rules, new[] { new RunningProcess(20, Discord1, "Discord") },
            Samples((20, Internet)), internetAddress: null, tunnelAddress: null, _ => true, Win);
        Assert.Equal(AppStatus.Direct, list[0].Status);
    }

    [Fact]
    public void Ipc_result_roundtrips()
    {
        var r = new IpcResult(false, "Nem létező fájl");
        Assert.Equal(r, IpcJson.DeserializeResult(IpcJson.Serialize(r)));
    }
}
