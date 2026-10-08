using System.Net;

namespace VpnBypass.Core.Status;

// Egy folyamat egy kapcsolatának mintája: a lokális (kimenő) cím, TCP-állapot és a kapcsolat kora.
public record ConnectionSample(IPAddress LocalAddress, bool IsEstablished, double AgeSeconds, bool IsSyn);

// Tiszta logika: egy folyamat kapcsolataiból + az internet/tunnel lokális címből státuszt képez.
public static class ConnectionClassifier
{
    // Ennyi másodperc beragadt SYN után vélelmezünk kill switch blokkot.
    private const double BlockedSynThresholdSeconds = 5.0;

    public static AppStatus Classify(
        IReadOnlyList<ConnectionSample> conns,
        IPAddress internetAddress,
        IPAddress? tunnelAddress)
    {
        // Csak a külső kapcsolatok számítanak; a loopback (127.x/::1) nem árulkodik az útvonalról.
        var relevant = conns.Where(c => !IPAddress.IsLoopback(c.LocalAddress)).ToList();
        if (relevant.Count == 0)
            return AppStatus.NotRunning;

        bool hasEstablished = relevant.Any(c => c.IsEstablished);

        // Kill switch gyanú: a fizikai címről régóta próbál kapcsolódni, de semmi nem épült fel.
        bool stuckSyn = relevant.Any(c =>
            c.IsSyn && !c.IsEstablished
            && c.AgeSeconds > BlockedSynThresholdSeconds
            && c.LocalAddress.Equals(internetAddress));
        if (!hasEstablished && stuckSyn)
            return AppStatus.Blocked;

        bool onInternet = relevant.Any(c => c.LocalAddress.Equals(internetAddress));
        bool onTunnel = tunnelAddress is not null && relevant.Any(c => c.LocalAddress.Equals(tunnelAddress));

        if (onInternet && onTunnel) return AppStatus.Partial;
        if (onInternet) return AppStatus.Direct;
        if (onTunnel) return AppStatus.Vpn;
        return AppStatus.NotRunning;
    }
}
