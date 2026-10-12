using System.Runtime.InteropServices;
using System.Text;

namespace VpnBypass.Service.Interop;

// Win32 útvonal (C:\...) ↔ NT-eszközútvonal (\Device\HarddiskVolumeN\...) átváltás.
// A driver az NT-eszközútvonalat kéri; a meghajtóbetű→eszköz leképezést gyorsítótárazzuk és
// meghajtó-változáskor frissítjük. Kisbetűsítve adjuk vissza (a Core/driver is kisbetűs mintákkal dolgozik).
public sealed class PathResolver
{
    private readonly object _gate = new();
    private Dictionary<string, string> _driveToDevice = new(StringComparer.OrdinalIgnoreCase); // "C:" → "\device\harddiskvolume3"
    private Dictionary<string, string> _deviceToDrive = new(StringComparer.OrdinalIgnoreCase);

    public PathResolver() => Refresh();

    public void Refresh()
    {
        var toDev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var toDrive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var di in DriveInfo.GetDrives())
        {
            var drive = di.Name.TrimEnd('\\'); // "C:"
            if (drive.Length != 2 || drive[1] != ':') continue;
            var target = QueryDevice(drive);
            if (target is null) continue;
            toDev[drive] = target.ToLowerInvariant();
            toDrive[target.ToLowerInvariant()] = drive.ToLowerInvariant();
        }
        lock (_gate) { _driveToDevice = toDev; _deviceToDrive = toDrive; }
    }

    // "C:\Users\x\app.exe" → "\device\harddiskvolume3\users\x\app.exe" (kisbetűs). null, ha nem meghajtóbetűs.
    public string? ToNtDevicePath(string win32Path)
    {
        if (win32Path.Length < 3 || win32Path[1] != ':' || win32Path[2] != '\\') return null;
        var drive = win32Path[..2];
        string? device;
        lock (_gate)
        {
            if (!_driveToDevice.TryGetValue(drive, out device))
            {
                var q = QueryDevice(drive);
                if (q is null) return null;
                device = q.ToLowerInvariant();
                _driveToDevice[drive] = device;
                _deviceToDrive[device] = drive.ToLowerInvariant();
            }
        }
        return device + win32Path[2..].ToLowerInvariant();
    }

    // "\device\harddiskvolume3\users\x\app.exe" → "c:\users\x\app.exe". null, ha nem ismert eszköz.
    public string? ToWin32Path(string ntPath)
    {
        var lower = ntPath.ToLowerInvariant();
        lock (_gate)
        {
            foreach (var (device, drive) in _deviceToDrive)
            {
                if (lower.StartsWith(device + "\\", StringComparison.Ordinal))
                    return drive + lower[device.Length..];
            }
        }
        return null;
    }

    private static string? QueryDevice(string drive)
    {
        var sb = new StringBuilder(260);
        uint len = QueryDosDeviceW(drive, sb, (uint)sb.Capacity);
        if (len == 0)
        {
            if (Marshal.GetLastWin32Error() != 122) return null; // 122 = ERROR_INSUFFICIENT_BUFFER
            sb = new StringBuilder(2048);
            if (QueryDosDeviceW(drive, sb, (uint)sb.Capacity) == 0) return null;
        }
        // Több cél esetén az első az aktuális leképezés.
        var s = sb.ToString();
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDeviceW(string lpDeviceName, StringBuilder lpTargetPath, uint ucchMax);
}
