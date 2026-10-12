using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnBypass.Core.Driver;

// A driver IOCTL-kódjai. A CTL_CODE képlettel SZÁMÍTVA, nem beégetve:
// CTL_CODE = (DeviceType<<16) | (Access<<14) | (Function<<2) | Method.
// DeviceType=0x8000, Access=FILE_ANY_ACCESS=0, Method: BUFFERED=0, NEITHER=3 (a driver defs/ioctl.h szerint).
public static class Ioctl
{
    private const uint DeviceType = 0x8000;
    private const uint FileAnyAccess = 0;
    private const uint MethodBuffered = 0;
    private const uint MethodNeither = 3;

    private static uint Ctl(uint function, uint method) =>
        (DeviceType << 16) | (FileAnyAccess << 14) | (function << 2) | method;

    public static readonly uint Initialize = Ctl(1, MethodBuffered);          // 0x80000004
    public static readonly uint DequeueEvent = Ctl(2, MethodBuffered);        // 0x80000008
    public static readonly uint RegisterProcesses = Ctl(3, MethodBuffered);   // 0x8000000C
    public static readonly uint RegisterIpAddresses = Ctl(4, MethodBuffered); // 0x80000010
    public static readonly uint GetIpAddresses = Ctl(5, MethodBuffered);      // 0x80000014
    public static readonly uint SetConfiguration = Ctl(6, MethodBuffered);    // 0x80000018
    public static readonly uint GetConfiguration = Ctl(7, MethodBuffered);    // 0x8000001C
    public static readonly uint ClearConfiguration = Ctl(8, MethodNeither);   // 0x80000023
    public static readonly uint GetState = Ctl(9, MethodBuffered);            // 0x80000024
    public static readonly uint QueryProcess = Ctl(10, MethodBuffered);       // 0x80000028
    public static readonly uint Reset = Ctl(11, MethodNeither);               // 0x8000002F
}

// A driverhez küldött bináris blobok összeállítása (x64, natív igazítás). A kiosztás a
// win-split-tunnel defs/config.h, process.h és ipaddr.h szerkezeteit követi.
public static class DriverStructs
{
    // ST_SUBLAYER_GUIDS { GUID Baseline; GUID Dns; } = 16 + 16 = 32 bájt.
    public static byte[] BuildSublayerGuids(Guid baseline, Guid dns)
    {
        var buf = new byte[32];
        baseline.ToByteArray().CopyTo(buf, 0);
        dns.ToByteArray().CopyTo(buf, 16);
        return buf;
    }

    // ST_IP_ADDRESSES { IN_ADDR TunnelIpv4; IN_ADDR InternetIpv4; IN6_ADDR TunnelIpv6; IN6_ADDR InternetIpv6; }
    // = 4 + 4 + 16 + 16 = 40 bájt. A driver PONTOSAN ennyit vár (bufferLength != 40 → STATUS_INVALID_PARAMETER).
    // A hiányzó (null) címek helyén nulla marad; a driver az üres tartományt "nincs cím"-ként kezeli.
    public static byte[] BuildIpAddresses(
        IPAddress? tunnelV4, IPAddress? internetV4, IPAddress? tunnelV6, IPAddress? internetV6)
    {
        var buf = new byte[40];
        WriteV4(buf, 0, tunnelV4);
        WriteV4(buf, 4, internetV4);
        WriteV6(buf, 8, tunnelV6);
        WriteV6(buf, 24, internetV6);
        return buf;
    }

    private static void WriteV4(byte[] buf, int offset, IPAddress? addr)
    {
        if (addr is null || addr.AddressFamily != AddressFamily.InterNetwork) return;
        addr.GetAddressBytes().CopyTo(buf, offset); // 4 bájt, hálózati sorrend (= IN_ADDR)
    }

    private static void WriteV6(byte[] buf, int offset, IPAddress? addr)
    {
        if (addr is null || addr.AddressFamily != AddressFamily.InterNetworkV6) return;
        addr.GetAddressBytes().CopyTo(buf, offset); // 16 bájt (= IN6_ADDR)
    }

    // ST_PROCESS_DISCOVERY_HEADER { SIZE_T NumEntries; SIZE_T TotalLength; } = 16 bájt.
    // ST_PROCESS_DISCOVERY_ENTRY  { HANDLE ProcessId; HANDLE ParentProcessId;
    //                               SIZE_T ImageNameOffset; USHORT ImageNameLength; } → 8-ra igazítva 32 bájt.
    // Útvonal nélküli folyamatnál offset = 0 és hossz = 0 (a referenciakliens is így küldi).
    public static byte[] BuildProcessDiscovery(IReadOnlyList<ProcessEntry> processes)
    {
        if (processes.Count == 0)
            throw new ArgumentException("A folyamatlista nem lehet üres (a driver elutasítaná).", nameof(processes));

        const int headerSize = 16;
        const int entrySize = 32;

        int stringBytes = 0;
        foreach (var p in processes)
            stringBytes += p.DevicePath.Length * sizeof(char);

        int total = headerSize + entrySize * processes.Count + stringBytes;
        var buf = new byte[total];

        BitConverter.TryWriteBytes(buf.AsSpan(0), (long)processes.Count); // NumEntries
        BitConverter.TryWriteBytes(buf.AsSpan(8), (long)total);          // TotalLength

        int stringBase = headerSize + entrySize * processes.Count;
        int stringOffset = 0;
        for (int i = 0; i < processes.Count; i++)
        {
            var p = processes[i];
            int entryAt = headerSize + i * entrySize;
            BitConverter.TryWriteBytes(buf.AsSpan(entryAt), (long)p.ProcessId);           // HANDLE ProcessId
            BitConverter.TryWriteBytes(buf.AsSpan(entryAt + 8), (long)p.ParentProcessId); // HANDLE ParentProcessId
            if (p.DevicePath.Length == 0)
                continue; // offset/hossz marad 0

            int len = p.DevicePath.Length * sizeof(char);
            BitConverter.TryWriteBytes(buf.AsSpan(entryAt + 16), (long)stringOffset);       // ImageNameOffset
            BitConverter.TryWriteBytes(buf.AsSpan(entryAt + 24), (ushort)len);              // ImageNameLength
            Encoding.Unicode.GetBytes(p.DevicePath, 0, p.DevicePath.Length, buf, stringBase + stringOffset);
            stringOffset += len;
        }

        return buf;
    }

    // ST_CONFIGURATION_HEADER { SIZE_T NumEntries; SIZE_T TotalLength; } = 16 bájt.
    // ST_CONFIGURATION_ENTRY  { SIZE_T ImageNameOffset; USHORT ImageNameLength; } → 8-ra igazítva 16 bájt.
    // Kiosztás: fejléc, majd N bejegyzés, majd a null-terminátor nélküli UTF-16 stringpuffer.
    // Az ImageNameOffset a STRINGPUFFER elejéhez relatív, bájtban.
    public static byte[] BuildConfiguration(IReadOnlyList<string> ntImagePaths)
    {
        const int headerSize = 16;
        const int entrySize = 16;

        int stringBytes = 0;
        foreach (var p in ntImagePaths)
            stringBytes += p.Length * sizeof(char);

        int total = headerSize + entrySize * ntImagePaths.Count + stringBytes;
        var buf = new byte[total];

        BitConverter.TryWriteBytes(buf.AsSpan(0), (long)ntImagePaths.Count); // NumEntries
        BitConverter.TryWriteBytes(buf.AsSpan(8), (long)total);              // TotalLength

        int stringBase = headerSize + entrySize * ntImagePaths.Count;
        int stringOffset = 0;
        for (int i = 0; i < ntImagePaths.Count; i++)
        {
            string p = ntImagePaths[i];
            int len = p.Length * sizeof(char);
            int entryAt = headerSize + i * entrySize;
            BitConverter.TryWriteBytes(buf.AsSpan(entryAt), (long)stringOffset);        // ImageNameOffset
            BitConverter.TryWriteBytes(buf.AsSpan(entryAt + 8), (ushort)len);           // ImageNameLength
            // a maradék 6 bájt (igazítás) nulla
            Encoding.Unicode.GetBytes(p, 0, p.Length, buf, stringBase + stringOffset);  // UTF-16LE
            stringOffset += len;
        }

        return buf;
    }
}
