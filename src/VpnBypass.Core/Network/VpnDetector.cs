namespace VpnBypass.Core.Network;

// A VPN-felismerés eredménye. Tunnel == null → nincs aktív VPN (minden a fizikai hálón megy).
public record DetectionResult(AdapterInfo? Tunnel, AdapterInfo? Internet);

// Tiszta logika: adapterek + útvonalak alapján eldönti, melyik adapter a VPN-tunnel és melyik a
// fizikai internet-kijárat. VPN-független: nem a termék nevét nézi, hanem az alapértelmezett
// útvonalakat és a hardveres/virtuális jelleget.
public static class VpnDetector
{
    // Az "internetforgalmat vivő" alapértelmezett útvonalak: a teljes /0, és az OpenVPN-féle /1-es felezés.
    private static readonly string[] DefaultRoutes =
        { "0.0.0.0/0", "0.0.0.0/1", "128.0.0.0/1", "::/0", "::/1", "8000::/1" };

    public static DetectionResult Detect(
        IReadOnlyList<AdapterInfo> adapters,
        IReadOnlyList<RouteInfo> routes,
        string? manualInternetAdapterName = null)
    {
        var byIndex = new Dictionary<int, AdapterInfo>();
        foreach (var a in adapters)
            byIndex[a.IfIndex] = a;

        // Az alapértelmezett útvonalak az adapterükkel és effektív metrikájukkal párosítva.
        var defaults = new List<(AdapterInfo Adapter, int Effective, int PrefixLength)>();
        foreach (var r in routes)
        {
            if (!DefaultRoutes.Contains(r.DestinationPrefix)) continue;
            if (!byIndex.TryGetValue(r.IfIndex, out var a)) continue;
            if (!a.IsOperational) continue;
            defaults.Add((a, r.RouteMetric + a.InterfaceMetric, r.PrefixLength));
        }

        if (defaults.Count == 0)
            return new DetectionResult(null, null);

        // Nyertes útvonal: leghosszabb prefix (a /1 legyőzi a /0-t), azon belül a legkisebb effektív metrika.
        var winner = defaults
            .OrderByDescending(d => d.PrefixLength)
            .ThenBy(d => d.Effective)
            .First();

        // Internet-adapter: a kézi felülírás, vagy a hardveres, alapértelmezett útvonalú adapterek közül
        // a legkisebb effektív metrikájú.
        AdapterInfo? internet = manualInternetAdapterName is not null
            ? adapters.FirstOrDefault(a =>
                string.Equals(a.Name, manualInternetAdapterName, StringComparison.OrdinalIgnoreCase))
            : defaults
                .Where(d => d.Adapter.IsHardware)
                .OrderBy(d => d.Effective)
                .Select(d => d.Adapter)
                .FirstOrDefault();

        // Ha a nyertes nem hardveres → az a VPN-tunnel. Ha hardveres → nincs VPN, ő maga az internet-kijárat.
        return winner.Adapter.IsHardware
            ? new DetectionResult(null, winner.Adapter)
            : new DetectionResult(winner.Adapter, internet);
    }
}
