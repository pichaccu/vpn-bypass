using System.Net;
using System.Runtime.InteropServices;
using VpnBypass.Core.Status;

namespace VpnBypass.Service.Interop;

// PID-enkénti kimenő kapcsolatok lokális címei (csak olvasás, mint a netstat): GetExtendedTcpTable
// (az állapottal, pl. SYN_SENT a kill-switch-detektáláshoz) és GetExtendedUdpTable. IPv4 + IPv6.
public sealed class ConnectionTable
{
    public IReadOnlyDictionary<uint, IReadOnlyList<ConnectionSample>> ByPid()
    {
        var map = new Dictionary<uint, List<ConnectionSample>>();
        ReadTcp(AF_INET, map);
        ReadTcp(AF_INET6, map);
        ReadUdp(AF_INET, map);
        ReadUdp(AF_INET6, map);
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ConnectionSample>)kv.Value);
    }

    private static void Add(Dictionary<uint, List<ConnectionSample>> map, uint pid, ConnectionSample s)
    {
        if (!map.TryGetValue(pid, out var list)) map[pid] = list = new List<ConnectionSample>();
        list.Add(s);
    }

    private static void ReadTcp(int family, Dictionary<uint, List<ConnectionSample>> map)
    {
        int size = 0;
        GetExtendedTcpTable(nint.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return;
        nint buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return;
            int count = Marshal.ReadInt32(buf);
            nint row = buf + 4;
            for (int i = 0; i < count; i++)
            {
                if (family == AF_INET)
                {
                    var r = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(row);
                    bool syn = r.state == MIB_TCP_STATE_SYN_SENT;
                    bool est = r.state == MIB_TCP_STATE_ESTAB;
                    Add(map, r.owningPid, new ConnectionSample(new IPAddress(r.localAddr), est, 0, syn));
                    row += Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
                }
                else
                {
                    var r = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(row);
                    bool syn = r.state == MIB_TCP_STATE_SYN_SENT;
                    bool est = r.state == MIB_TCP_STATE_ESTAB;
                    Add(map, r.owningPid, new ConnectionSample(new IPAddress(r.localAddr), est, 0, syn));
                    row += Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void ReadUdp(int family, Dictionary<uint, List<ConnectionSample>> map)
    {
        int size = 0;
        GetExtendedUdpTable(nint.Zero, ref size, false, family, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) return;
        nint buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buf, ref size, false, family, UDP_TABLE_OWNER_PID, 0) != 0) return;
            int count = Marshal.ReadInt32(buf);
            nint row = buf + 4;
            for (int i = 0; i < count; i++)
            {
                if (family == AF_INET)
                {
                    var r = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(row);
                    Add(map, r.owningPid, new ConnectionSample(new IPAddress(r.localAddr), false, 0, false));
                    row += Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                }
                else
                {
                    var r = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(row);
                    Add(map, r.owningPid, new ConnectionSample(new IPAddress(r.localAddr), false, 0, false));
                    row += Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const uint MIB_TCP_STATE_SYN_SENT = 3;
    private const uint MIB_TCP_STATE_ESTAB = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID { public uint state; public uint localAddr; public uint localPort; public uint remoteAddr; public uint remotePort; public uint owningPid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId; public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] remoteAddr;
        public uint remoteScopeId; public uint remotePort; public uint state; public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID { public uint localAddr; public uint localPort; public uint owningPid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId; public uint localPort; public uint owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(nint pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(nint pUdpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);
}
