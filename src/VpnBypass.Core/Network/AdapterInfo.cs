using System.Net;

namespace VpnBypass.Core.Network;

// Egy hálózati adapter a felismeréshez szükséges tulajdonságai.
// IsHardware: fizikai kártya (Ethernet/Wi-Fi) vs. virtuális (VPN-tunnel, Hyper-V, WSL).
// InterfaceMetric: az interfész metrikája; az effektív útvonal-metrika ennek és a route metrikájának összege.
public record AdapterInfo(
    int IfIndex,
    string Name,
    bool IsHardware,
    bool IsOperational,
    int InterfaceMetric,
    IPAddress? Ipv4,
    IPAddress? Ipv6);
