// Fázis 0 füstteszt-kliens: megnyitja a Mullvad split-tunnel driver vezérlőeszközét,
// lekérdezi az állapotát (IOCTL_ST_GET_STATE), és kiírja. NEM módosít semmit a driverben.
// Rendszergazdaként futtatandó, miután a driver be van töltve (lásd docs/phase0-manual-test.md).
//
// Ez a Fázis 2 SplitTunnelDevice rétegének a magja is egyben.

using System.ComponentModel;
using System.Runtime.InteropServices;
using VpnBypass.Core.Driver;

const string DeviceName = @"\\.\MULLVADSPLITTUNNEL";

const uint GENERIC_READ = 0x80000000;
const uint GENERIC_WRITE = 0x40000000;
const uint OPEN_EXISTING = 3;
nint INVALID_HANDLE = -1;

Console.WriteLine("vpn-bypass — Fázis 0 driver-füstteszt");
Console.WriteLine($"Eszköz: {DeviceName}");
Console.WriteLine($"GET_STATE IOCTL: 0x{Ioctl.GetState:X8}");
Console.WriteLine();

nint handle = CreateFileW(DeviceName, GENERIC_READ | GENERIC_WRITE, 0, nint.Zero, OPEN_EXISTING, 0, nint.Zero);
if (handle == INVALID_HANDLE)
{
    int err = Marshal.GetLastWin32Error();
    Console.Error.WriteLine($"HIBA: az eszköz nem nyitható meg (Win32 {err}: {new Win32Exception(err).Message}).");
    Console.Error.WriteLine(err switch
    {
        2 => "  → A driver valószínűleg NINCS betöltve (sc start kimaradt?).",
        5 => "  → Hozzáférés megtagadva – rendszergazdaként futtasd.",
        32 => "  → Az eszköz foglalt (exkluzív) – fut a Mullvad app, vagy egy másik kliens tartja?",
        _ => "  → Ismeretlen ok; lásd a Win32 hibakódot.",
    });
    return 2;
}

try
{
    var outBuf = new byte[8]; // SIZE_T
    bool ok = DeviceIoControl(handle, Ioctl.GetState, nint.Zero, 0, outBuf, (uint)outBuf.Length, out uint returned, nint.Zero);
    if (!ok)
    {
        int err = Marshal.GetLastWin32Error();
        Console.Error.WriteLine($"HIBA: a GET_STATE hívás nem sikerült (Win32 {err}: {new Win32Exception(err).Message}).");
        return 3;
    }

    long raw = BitConverter.ToInt64(outBuf, 0);
    var state = (DriverState)raw;
    Console.WriteLine($"Visszatért bájtok: {returned}");
    Console.WriteLine($"Driver állapota: {state} ({raw})");
    Console.WriteLine();
    Console.WriteLine(state switch
    {
        DriverState.Started => "SIKER: a driver betöltött és a vezérlőeszköz válaszol. Alapállapot (Started). ✔",
        DriverState.Initialized or DriverState.Ready or DriverState.Engaged =>
            $"A driver válaszol, de nem alapállapotban van ({state}) – egy korábbi kliens hagyta így; RESET-tel vissza lehet állítani.",
        DriverState.Zombie => "FIGYELEM: a driver ZOMBIE állapotban van – újraindítás szükséges.",
        _ => $"A driver válaszol, állapot: {state}.",
    });
    return state == DriverState.Started ? 0 : 1;
}
finally
{
    CloseHandle(handle);
}

[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
static extern nint CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
    nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool DeviceIoControl(nint hDevice, uint dwIoControlCode,
    nint lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
    out uint lpBytesReturned, nint lpOverlapped);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CloseHandle(nint hObject);
