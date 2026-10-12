using VpnBypass.Core.Driver;

namespace VpnBypass.Core.Engine;

// A driver ZOMBIE állapotban van (a reset nem sikerült) — csak a gép újraindítása segít.
public sealed class DriverZombieException(DriverState state)
    : Exception($"A driver nem állítható alapállapotba (állapot: {state}). Újraindítás szükséges.")
{
    public DriverState State { get; } = state;
}

// A split-tunnel driver állapotgépének vezérlője. Szálbiztos: a hálózatfigyelő, a folyamatfigyelő
// és a pipe-szerver szálai egyszerre hívhatják.
//
// Elvek:
// - Induláskor ha a driver nem alapállapotú (egy korábbi, összeomlott példány hagyta így), előbb RESET.
// - A kívánt állapotot (kivételek, címek, szünet) tárolja, és csak a VÁLTOZÁST küldi a drivernek.
// - Hiba esetén nem dől össze: a hibát megjegyzi, és a következő Apply/Reapply újrapróbálja.
// - Szünetben és VPN nélkül a driver tétlen (READY) → minden forgalom az útvonaltábla szerint megy.
public sealed class BypassEngine
{
    private readonly IDriverClient _driver;
    private readonly Func<IReadOnlyList<ProcessEntry>> _processSnapshot;
    private readonly Guid _baselineSublayer;
    private readonly Guid _dnsSublayer;
    private readonly object _gate = new();

    // Kívánt állapot.
    private IReadOnlyList<string> _wantPaths = Array.Empty<string>();
    private DriverAddresses _wantAddresses = DriverAddresses.None;
    private bool _paused;

    // Ami ténylegesen ki is ment a drivernek.
    private IReadOnlyList<string> _sentPaths = Array.Empty<string>();
    private DriverAddresses _sentAddresses = DriverAddresses.None;

    public BypassEngine(
        IDriverClient driver,
        Func<IReadOnlyList<ProcessEntry>> processSnapshot,
        Guid baselineSublayer,
        Guid dnsSublayer)
    {
        _driver = driver;
        _processSnapshot = processSnapshot;
        _baselineSublayer = baselineSublayer;
        _dnsSublayer = dnsSublayer;
    }

    public bool IsRunning { get; private set; }

    public string? LastError { get { lock (_gate) return _lastError; } }
    private string? _lastError;

    public bool Paused { get { lock (_gate) return _paused; } }

    public int ExclusionCount { get { lock (_gate) return _wantPaths.Count; } }

    public void Start()
    {
        lock (_gate)
        {
            var state = _driver.GetState();
            if (state == DriverState.Zombie)
                throw new DriverZombieException(state);

            if (state != DriverState.Started)
            {
                // Egy korábbi példány nyitva hagyta: a handle bezárása nem állítja vissza a drivert.
                try { _driver.Reset(); }
                catch { /* az utána lekért állapot dönt */ }

                var after = _driver.GetState();
                if (after != DriverState.Started)
                    throw new DriverZombieException(after);
            }

            _driver.Initialize(_baselineSublayer, _dnsSublayer);
            _driver.RegisterProcesses(_processSnapshot());

            _sentPaths = Array.Empty<string>();
            _sentAddresses = DriverAddresses.None;
            IsRunning = true;
            _lastError = null;
            ApplyLocked();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning) return;
            try { _driver.Reset(); }
            finally { IsRunning = false; }
        }
    }

    public void SetExclusions(IEnumerable<string> ntImagePaths)
    {
        lock (_gate)
        {
            _wantPaths = Normalize(ntImagePaths);
            ApplyLocked();
        }
    }

    public void SetAddresses(DriverAddresses addresses)
    {
        lock (_gate)
        {
            _wantAddresses = addresses;
            ApplyLocked();
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
            ApplyLocked();
        }
    }

    // Egy korábbi hiba utáni újrapróbálás (pl. időzítőből).
    public void Reapply()
    {
        lock (_gate) ApplyLocked();
    }

    public DriverState GetDriverState()
    {
        lock (_gate) return _driver.GetState();
    }

    private void ApplyLocked()
    {
        if (!IsRunning) return;

        var paths = _paused ? Array.Empty<string>() : _wantPaths;
        var addresses = _paused ? DriverAddresses.None : _wantAddresses;

        try
        {
            if (!paths.SequenceEqual(_sentPaths, StringComparer.Ordinal))
            {
                if (paths.Count == 0) _driver.ClearConfiguration();
                else _driver.SetConfiguration(paths);
                _sentPaths = paths;
            }

            if (addresses != _sentAddresses)
            {
                _driver.RegisterIpAddresses(addresses);
                _sentAddresses = addresses;
            }

            _lastError = null;
        }
        catch (Exception ex)
        {
            // A "_sent" nem frissült → a következő Apply újra megpróbálja.
            _lastError = ex.Message;
        }
    }

    // Kisbetűs, egyedi, rendezett lista → stabil összehasonlítás (a driver úgyis kisbetűsít).
    private static IReadOnlyList<string> Normalize(IEnumerable<string> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p))
             .Select(p => p.ToLowerInvariant())
             .Distinct(StringComparer.Ordinal)
             .OrderBy(p => p, StringComparer.Ordinal)
             .ToList();
}
