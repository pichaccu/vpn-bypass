using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnBypass.Core.Network;

namespace VpnBypass.Service.Interop;

// A hálózat (csak olvasás): adapterek + alapértelmezett útvonalak a VpnDetector számára, plusz
// változás-értesítés. A "hardveres-e" jelzést a WMI MSFT_NetAdapter.HardwareInterface adja (ugyanaz,
// amit a Get-NetAdapter is) — ez különíti el megbízhatóan a fizikai kártyát a VPN-tuneltől.
//
// Metrika: valós interfész-metrika helyett arányos helyettesítő (hardveres=100, virtuális=1), így a
// VpnDetector tie-break-je a VPN-tunelt választja, ha az aktív. A tényleges terelést úgyis a driver végzi.
public sealed class NetworkMonitor : IDisposable
{
    private readonly ILogger<NetworkMonitor> _log;
    public event Action? Changed;

    public NetworkMonitor(ILogger<NetworkMonitor> log)
    {
        _log = log;
        NetworkChange.NetworkAddressChanged += OnChanged;
        NetworkChange.NetworkAvailabilityChanged += OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke();
    private void OnChanged(object? sender, NetworkAvailabilityEventArgs e) => Changed?.Invoke();

    public DetectionResult Detect(string? manualInternetAdapter)
    {
        var (adapters, routes) = Read();
        return VpnDetector.Detect(adapters, routes, manualInternetAdapter);
    }

    private (IReadOnlyList<AdapterInfo> Adapters, IReadOnlyList<RouteInfo> Routes) Read()
    {
        var hardwareByIndex = QueryHardwareFlags();
        var adapters = new List<AdapterInfo>();
        var routes = new List<RouteInfo>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var props = ni.GetIPProperties();
            int ifIndex = TryIndex(props);
            if (ifIndex == 0) continue;

            // vEthernet (Hyper-V/WSL/Docker) kizárása: ezeket soha nem vesszük internet-kijáratnak.
            bool excludedVirtual = ni.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)
                                   || ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase);

            bool isHardware = !excludedVirtual
                              && hardwareByIndex.TryGetValue(ifIndex, out var hw) && hw;

            var ipv4 = props.UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a));
            var ipv6 = props.UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal);

            adapters.Add(new AdapterInfo(ifIndex, ni.Name, isHardware, true, isHardware ? 100 : 1, ipv4, ipv6));

            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address is { } a && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any));
            if (hasGateway && !excludedVirtual)
                routes.Add(new RouteInfo(ifIndex, "0.0.0.0/0", 0, 0));
        }

        return (adapters, routes);
    }

    private static bool IsLinkLocal(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    private static int TryIndex(IPInterfaceProperties props)
    {
        try { return props.GetIPv4Properties()?.Index ?? 0; } catch { }
        try { return props.GetIPv6Properties()?.Index ?? 0; } catch { }
        return 0;
    }

    private Dictionary<int, bool> QueryHardwareFlags()
    {
        var map = new Dictionary<int, bool>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\StandardCimv2", "SELECT InterfaceIndex, HardwareInterface FROM MSFT_NetAdapter");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                try
                {
                    int idx = Convert.ToInt32(mo["InterfaceIndex"]);
                    bool hw = mo["HardwareInterface"] is bool b && b;
                    map[idx] = hw;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MSFT_NetAdapter lekérdezés sikertelen – a hardver-jelzés hiányos lehet.");
        }
        return map;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnChanged;
    }
}
