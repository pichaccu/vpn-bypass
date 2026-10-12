using System.Net;
using VpnBypass.Core.Driver;
using VpnBypass.Core.Engine;
using Xunit;

namespace VpnBypass.Core.Tests;

// Hamis driver: a valódi állapotgépet utánozza (READY→ENGAGED csak kivétel + tunnel-cím esetén).
internal sealed class FakeDriver : IDriverClient
{
    public DriverState State = DriverState.Started;
    public readonly List<string> Calls = new();
    public bool FailReset;
    public bool FailSetConfiguration;
    public IReadOnlyList<string>? Config;
    public DriverAddresses? Addresses;

    public DriverState GetState() => State;

    public void Initialize(Guid baselineSublayer, Guid dnsSublayer)
    {
        Calls.Add("Initialize");
        State = DriverState.Initialized;
    }

    public void RegisterProcesses(IReadOnlyList<ProcessEntry> processes)
    {
        Calls.Add("RegisterProcesses");
        State = DriverState.Ready;
    }

    public void RegisterIpAddresses(DriverAddresses addresses)
    {
        Calls.Add("RegisterIp");
        Addresses = addresses;
        Recompute();
    }

    public void SetConfiguration(IReadOnlyList<string> ntImagePaths)
    {
        if (FailSetConfiguration) throw new InvalidOperationException("SetConfiguration hiba");
        Calls.Add("SetConfig");
        Config = ntImagePaths;
        Recompute();
    }

    public void ClearConfiguration()
    {
        Calls.Add("ClearConfig");
        Config = null;
        Recompute();
    }

    public void Reset()
    {
        Calls.Add("Reset");
        if (FailReset)
        {
            State = DriverState.Zombie;
            throw new InvalidOperationException("reset hiba");
        }
        State = DriverState.Started;
        Config = null;
        Addresses = null;
    }

    private void Recompute()
    {
        if (State is DriverState.Ready or DriverState.Engaged)
            State = Config is { Count: > 0 } && Addresses is { HasTunnel: true }
                ? DriverState.Engaged
                : DriverState.Ready;
    }
}

public class BypassEngineTests
{
    static readonly Guid Baseline = Guid.Parse("6e0273b7-33fd-4cfc-9312-8cffcc27dbdc");
    static readonly Guid Dns = Guid.Parse("63359fcc-b89d-4c49-81c9-c011a3eee3cd");

    static readonly DriverAddresses Vpn = new(
        IPAddress.Parse("10.5.0.2"), IPAddress.Parse("192.168.0.51"), null, null);

    const string Discord = @"\device\harddiskvolume3\users\x\appdata\local\discord\app-1.0.9260\discord.exe";

    static (BypassEngine Engine, FakeDriver Driver) Create(DriverState initial = DriverState.Started)
    {
        var driver = new FakeDriver { State = initial };
        var engine = new BypassEngine(
            driver,
            () => new[] { new ProcessEntry(4, 0, "") },
            Baseline, Dns);
        return (engine, driver);
    }

    [Fact]
    public void Start_from_started_initializes_and_registers_processes()
    {
        var (engine, driver) = Create();
        engine.Start();
        Assert.Equal(new[] { "Initialize", "RegisterProcesses" }, driver.Calls);
        Assert.Equal(DriverState.Ready, driver.State);
        Assert.True(engine.IsRunning);
    }

    [Fact]
    public void Start_from_leftover_engaged_state_resets_first()
    {
        var (engine, driver) = Create(DriverState.Engaged);
        engine.Start();
        Assert.Equal("Reset", driver.Calls[0]);
        Assert.Equal(new[] { "Reset", "Initialize", "RegisterProcesses" }, driver.Calls);
    }

    [Fact]
    public void Start_when_zombie_throws()
    {
        var (engine, _) = Create(DriverState.Zombie);
        Assert.Throws<DriverZombieException>(engine.Start);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Failed_reset_on_start_throws_zombie()
    {
        var (engine, driver) = Create(DriverState.Ready);
        driver.FailReset = true;
        var ex = Assert.Throws<DriverZombieException>(engine.Start);
        Assert.Equal(DriverState.Zombie, ex.State);
    }

    [Fact]
    public void Exclusion_plus_vpn_engages_the_driver()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.SetExclusions(new[] { Discord });
        engine.SetAddresses(Vpn);
        Assert.Equal(DriverState.Engaged, driver.State);
        Assert.Equal(new[] { Discord }, driver.Config);
    }

    [Fact]
    public void Calls_before_start_are_applied_on_start()
    {
        var (engine, driver) = Create();
        engine.SetExclusions(new[] { Discord });
        engine.SetAddresses(Vpn);
        Assert.Empty(driver.Calls); // még nem fut → semmi nem megy ki
        engine.Start();
        Assert.Equal(DriverState.Engaged, driver.State);
    }

    [Fact]
    public void Unchanged_state_sends_nothing_twice()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.SetExclusions(new[] { Discord });
        engine.SetExclusions(new[] { Discord.ToUpperInvariant() }); // kis/nagybetű nem változás
        engine.SetAddresses(Vpn);
        engine.SetAddresses(Vpn with { }); // érték-egyenlő másolat
        Assert.Equal(1, driver.Calls.Count(c => c == "SetConfig"));
        Assert.Equal(1, driver.Calls.Count(c => c == "RegisterIp"));
    }

    [Fact]
    public void Pause_clears_config_and_resume_restores_it()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.SetExclusions(new[] { Discord });
        engine.SetAddresses(Vpn);

        engine.SetPaused(true);
        Assert.Equal(DriverState.Ready, driver.State);
        Assert.Null(driver.Config);
        Assert.True(engine.Paused);

        engine.SetPaused(false);
        Assert.Equal(DriverState.Engaged, driver.State);
        Assert.Equal(new[] { Discord }, driver.Config);
    }

    [Fact]
    public void Vpn_disconnect_disengages()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.SetExclusions(new[] { Discord });
        engine.SetAddresses(Vpn);
        engine.SetAddresses(DriverAddresses.None);
        Assert.Equal(DriverState.Ready, driver.State);
    }

    [Fact]
    public void Removing_last_exclusion_clears_configuration()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.SetExclusions(new[] { Discord });
        engine.SetExclusions(Array.Empty<string>());
        Assert.Contains("ClearConfig", driver.Calls);
        Assert.Null(driver.Config);
    }

    [Fact]
    public void Driver_error_is_recorded_and_retried()
    {
        var (engine, driver) = Create();
        engine.Start();
        driver.FailSetConfiguration = true;
        engine.SetExclusions(new[] { Discord });
        Assert.NotNull(engine.LastError);
        Assert.Null(driver.Config);

        driver.FailSetConfiguration = false;
        engine.Reapply();
        Assert.Null(engine.LastError);
        Assert.Equal(new[] { Discord }, driver.Config);
    }

    [Fact]
    public void Paths_are_normalized_and_deduplicated()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.SetExclusions(new[] { @"\Device\X\B.EXE", @"\device\x\b.exe", @"\device\x\a.exe", " " });
        Assert.Equal(new[] { @"\device\x\a.exe", @"\device\x\b.exe" }, driver.Config);
        Assert.Equal(2, engine.ExclusionCount);
    }

    [Fact]
    public void Stop_resets_the_driver()
    {
        var (engine, driver) = Create();
        engine.Start();
        engine.Stop();
        Assert.Equal("Reset", driver.Calls[^1]);
        Assert.Equal(DriverState.Started, driver.State);
        Assert.False(engine.IsRunning);
    }
}
