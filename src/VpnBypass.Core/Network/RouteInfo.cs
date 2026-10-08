namespace VpnBypass.Core.Network;

// Egy IPv4/IPv6 útvonal a felismeréshez: melyik interfészen, milyen célprefix, milyen metrikával.
// DestinationPrefix: a cél CIDR-alakban (pl. "0.0.0.0/0", "0.0.0.0/1", "::/0").
public record RouteInfo(
    int IfIndex,
    string DestinationPrefix,
    int PrefixLength,
    int RouteMetric);
