using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VpnBypass.Core.Config;
using VpnBypass.Core.Driver;
using VpnBypass.Core.Engine;
using VpnBypass.Core.Ipc;
using VpnBypass.Core.Network;
using VpnBypass.Core.Rules;
using VpnBypass.Core.Status;
using VpnBypass.Service.Interop;
using VpnBypass.Service.Ipc;

namespace VpnBypass.Service;

// A szolgáltatás életciklusa és összekötése: config betöltés → (dry-run) motor indítása →
// időszakos frissítés (VPN-felismerés, kivétel-feloldás, app-lista) → named pipe kiszolgálás a tálcának.
public sealed class Worker(
    ILogger<Worker> log,
    ILogger<PipeServer> pipeLog,
    BypassEngine engine,
    ConfigStore configStore,
    IFileSystem fileSystem,
    PathResolver paths,
    NetworkMonitor network,
    ConnectionTable connections) : BackgroundService
{
    private readonly object _gate = new();
    private readonly string _windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private AppConfig _config = AppConfig.Empty;
    private bool _paused;
    private DetectionResult _detection = new(null, null);
    private IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("vpn-bypass szolgáltatás indul (dry-run: a terelés naplózva, tényleges forgalomterelés nincs).");
        _config = configStore.Load();
        log.LogInformation("Konfig betöltve: {Count} szabály.", _config.Rules.Count);

        try { engine.Start(); }
        catch (DriverZombieException ex) { log.LogError(ex, "A driver nem állítható alapállapotba."); }

        network.Changed += Refresh; // VPN csatlakozás/bontás → azonnali frissítés
        Refresh();

        var pipe = new PipeServer(pipeLog, HandleRequest);
        var pipeTask = pipe.RunAsync(_config.UserSid, stoppingToken);
        log.LogInformation("Named pipe kiszolgálás indul (\\\\.\\pipe\\{Pipe}).", PipeServer.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { break; }

            engine.Reapply();   // korábbi driver-hiba újrapróbálása
            Refresh();
        }

        await pipeTask;
    }

    // Egy teljes állapot-frissítés: VPN-felismerés, címek + kivételek a motornak, app-lista összeállítása.
    private void Refresh()
    {
        lock (_gate)
        {
            _detection = network.Detect(_config.InternetAdapter);
            engine.SetAddresses(DriverAddresses.From(_detection));
            ApplyExclusionsLocked();
            _apps = BuildAppsLocked();
        }
    }

    private void ApplyExclusionsLocked()
    {
        var ntPaths = _config.Rules
            .SelectMany(r => RuleResolver.ResolveWithLauncher(r.Pattern, fileSystem))
            .Select(paths.ToNtDevicePath)
            .Where(p => p is not null).Select(p => p!)
            .ToList();
        engine.SetExclusions(ntPaths);
    }

    private IReadOnlyList<AppEntry> BuildAppsLocked()
    {
        var running = new List<RunningProcess>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var win32 = ProcessSnapshot.TryGetImagePath((uint)p.Id);
                running.Add(new RunningProcess((uint)p.Id, win32, p.ProcessName));
            }
            catch { /* kilépett folyamat */ }
            finally { p.Dispose(); }
        }

        var samples = connections.ByPid();
        var internetV4 = _detection.Internet?.Ipv4;
        var tunnelV4 = _detection.Tunnel?.Ipv4;

        return AppListBuilder.Build(
            _config.Rules, running, samples, internetV4, tunnelV4,
            r => RuleResolver.ResolveWithLauncher(r.Pattern, fileSystem).Count > 0,
            _windowsDir);
    }

    // --- Named pipe kérés-feldolgozás (kérés-sor → válasz-sor) ---
    private string HandleRequest(string line)
    {
        var req = IpcJson.DeserializeRequest(line);
        if (req is null) return IpcJson.Serialize(new IpcResult(false, "értelmezhetetlen kérés"));

        switch (req.Type)
        {
            case IpcRequestType.GetStatus:
                return IpcJson.Serialize(BuildStatus());

            case IpcRequestType.GetApps:
                lock (_gate) return IpcJson.Serialize(new AppsResponse(_apps));

            case IpcRequestType.SetExcluded:
                return IpcJson.Serialize(SetExcluded(req.Path, req.Excluded ?? false));

            case IpcRequestType.SetPaused:
                engine.SetPaused(req.Paused ?? false);
                lock (_gate) _paused = req.Paused ?? false;
                log.LogInformation("Szüneteltetés: {Paused}", _paused);
                return IpcJson.Serialize(new IpcResult(true));

            case IpcRequestType.SetInternetAdapter:
                lock (_gate) { _config = _config with { InternetAdapter = string.IsNullOrWhiteSpace(req.AdapterName) ? null : req.AdapterName }; }
                Save();
                Refresh();
                return IpcJson.Serialize(new IpcResult(true));

            default:
                return IpcJson.Serialize(new IpcResult(false, "ismeretlen kérés"));
        }
    }

    private StatusResponse BuildStatus()
    {
        lock (_gate)
        {
            VpnInfo? Info(AdapterInfo? a) =>
                a is null ? null : new VpnInfo(a.Name, a.Ipv4?.ToString(), a.Ipv6?.ToString());

            return new StatusResponse(
                engine.GetDriverState(), _paused,
                Info(_detection.Tunnel), Info(_detection.Internet),
                _config.Rules.Count, engine.LastError);
        }
    }

    private IpcResult SetExcluded(string? pathOrPattern, bool excluded)
    {
        if (string.IsNullOrWhiteSpace(pathOrPattern))
            return new IpcResult(false, "hiányzó útvonal");

        lock (_gate)
        {
            var rules = _config.Rules.ToList();
            if (excluded)
            {
                if (!pathOrPattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(pathOrPattern))
                    return new IpcResult(false, "csak létező .exe adható hozzá");
                var rule = ExclusionRule.Create(pathOrPattern);
                if (!rules.Any(r => string.Equals(r.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    if (rules.Count >= RuleSet.MaxRules)
                        return new IpcResult(false, $"legfeljebb {RuleSet.MaxRules} kivétel");
                    rules.Add(rule);
                }
            }
            else
            {
                rules.RemoveAll(r =>
                    string.Equals(r.Pattern, pathOrPattern, StringComparison.OrdinalIgnoreCase)
                    || PatternMatcher.IsMatch(r.Pattern, pathOrPattern));
            }
            _config = _config with { Rules = rules };
        }
        Save();
        Refresh();
        return new IpcResult(true);
    }

    private void Save()
    {
        try { lock (_gate) configStore.Save(_config); }
        catch (Exception ex) { log.LogError(ex, "A konfig mentése sikertelen."); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("vpn-bypass szolgáltatás leáll.");
        try { engine.Stop(); } catch (Exception ex) { log.LogError(ex, "Hiba a motor leállításakor."); }
        await base.StopAsync(cancellationToken);
    }
}
