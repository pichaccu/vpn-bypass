using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VpnBypass.Core.Config;
using VpnBypass.Core.Engine;
using VpnBypass.Core.Rules;
using VpnBypass.Service.Interop;

namespace VpnBypass.Service;

// A szolgáltatás életciklusa. Ebben a lépésben: betölti a configot, feloldja a kivételeket a
// lemezen létező .exe-kre (NT-útvonalra), és elindítja a (dry-run) motort. A hálózatfelismerés,
// a folyamat- és kapcsolatfigyelés, valamint a named pipe a következő lépésben kapcsolódik be.
public sealed class Worker(
    ILogger<Worker> log,
    BypassEngine engine,
    ConfigStore configStore,
    IFileSystem fileSystem,
    PathResolver paths) : BackgroundService
{
    private AppConfig _config = AppConfig.Empty;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("vpn-bypass szolgáltatás indul (dry-run: a terelés naplózva, nincs tényleges forgalomterelés).");

        _config = configStore.Load();
        log.LogInformation("Konfig betöltve: {Count} szabály.", _config.Rules.Count);

        try
        {
            engine.Start();
            ApplyExclusions();
        }
        catch (DriverZombieException ex)
        {
            log.LogError(ex, "A driver nem állítható alapállapotba – újraindítás szükséges.");
        }

        return Task.CompletedTask;
    }

    // A config szabályaiból a lemezen létező .exe-k → NT-eszközútvonalak → a motornak.
    private void ApplyExclusions()
    {
        var ntPaths = _config.Rules
            .SelectMany(r => RuleResolver.ResolveWithLauncher(r.Pattern, fileSystem))
            .Select(paths.ToNtDevicePath)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        log.LogInformation("{Count} kivétel-folyamatútvonal feloldva.", ntPaths.Count);
        engine.SetExclusions(ntPaths);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("vpn-bypass szolgáltatás leáll.");
        try { engine.Stop(); }
        catch (Exception ex) { log.LogError(ex, "Hiba a motor leállításakor."); }
        await base.StopAsync(cancellationToken);
    }
}
