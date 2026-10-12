using Microsoft.Extensions.Logging;
using VpnBypass.Core.Driver;

namespace VpnBypass.Service.Driver;

// „Próba" driver-vezérlő: NEM nyitja meg az eszközt és NEM terel semmit — csak naplózza, mit tenne,
// és utánozza a driver állapotgépét, hogy a BypassEngine, a szolgáltatás és a tálca végig futtatható
// legyen a valódi terelés nélkül. A kész termékhez ezt váltja le az IDriverClient éles megvalósítása
// (lásd docs/STATUS.md). Így az app logikája itt és most teljesen kipróbálható.
public sealed class DryRunDriverClient(ILogger<DryRunDriverClient> log) : IDriverClient
{
    private readonly object _gate = new();
    private DriverState _state = DriverState.Started;
    private bool _hasConfig;
    private bool _hasTunnel;

    public DriverState GetState()
    {
        lock (_gate) return _state;
    }

    public void Initialize(Guid baselineSublayer, Guid dnsSublayer)
    {
        lock (_gate)
        {
            log.LogInformation("[dry-run] Initialize (baseline={Baseline}, dns={Dns})", baselineSublayer, dnsSublayer);
            _state = DriverState.Initialized;
        }
    }

    public void RegisterProcesses(IReadOnlyList<ProcessEntry> processes)
    {
        lock (_gate)
        {
            log.LogInformation("[dry-run] RegisterProcesses ({Count} folyamat)", processes.Count);
            _state = DriverState.Ready;
            Recompute();
        }
    }

    public void RegisterIpAddresses(DriverAddresses addresses)
    {
        lock (_gate)
        {
            _hasTunnel = addresses.HasTunnel;
            log.LogInformation("[dry-run] RegisterIpAddresses (tunnelV4={T4}, internetV4={I4}, tunnel={HasT})",
                addresses.TunnelV4, addresses.InternetV4, _hasTunnel);
            Recompute();
        }
    }

    public void SetConfiguration(IReadOnlyList<string> ntImagePaths)
    {
        lock (_gate)
        {
            _hasConfig = ntImagePaths.Count > 0;
            log.LogInformation("[dry-run] SetConfiguration ({Count} kivétel): {Paths}",
                ntImagePaths.Count, string.Join(", ", ntImagePaths));
            Recompute();
        }
    }

    public void ClearConfiguration()
    {
        lock (_gate)
        {
            _hasConfig = false;
            log.LogInformation("[dry-run] ClearConfiguration");
            Recompute();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            log.LogInformation("[dry-run] Reset");
            _state = DriverState.Started;
            _hasConfig = false;
            _hasTunnel = false;
        }
    }

    // READY↔ENGAGED ugyanazon szabály szerint, mint a valódi driver: kell kivétel ÉS tunnel-cím.
    private void Recompute()
    {
        if (_state is DriverState.Ready or DriverState.Engaged)
            _state = _hasConfig && _hasTunnel ? DriverState.Engaged : DriverState.Ready;
    }
}
