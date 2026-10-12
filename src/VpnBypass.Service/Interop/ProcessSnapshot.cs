using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using VpnBypass.Core.Driver;

namespace VpnBypass.Service.Interop;

// A futó folyamatok pillanatképe a driver kezdeti regisztrációjához (Toolhelp), NT-eszközútvonallal.
// Amit nem tudunk megnyitni/lekérdezni, azt üres útvonallal vesszük fel (a szülő-gyerek lánchoz kell).
public sealed class ProcessSnapshot(PathResolver paths)
{
    public IReadOnlyList<ProcessEntry> Capture()
    {
        var result = new List<ProcessEntry>();
        nint snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot");
        try
        {
            var pe = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref pe)) return result;
            do
            {
                string nt = "";
                var full = TryGetImagePath(pe.th32ProcessID);
                if (full is not null)
                    nt = paths.ToNtDevicePath(full) ?? "";
                result.Add(new ProcessEntry(pe.th32ProcessID, pe.th32ParentProcessID, nt));
            }
            while (Process32NextW(snap, ref pe));
        }
        finally { CloseHandle(snap); }
        return result;
    }

    // Win32 teljes útvonal egy PID-hez, vagy null (System/védett folyamatok).
    public static string? TryGetImagePath(uint pid)
    {
        if (pid == 0) return null;
        nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int cap = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref cap) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    private const uint TH32CS_SNAPPROCESS = 0x2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly nint INVALID_HANDLE = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(nint hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(nint hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
