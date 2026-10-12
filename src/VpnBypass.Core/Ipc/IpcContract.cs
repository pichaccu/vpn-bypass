using VpnBypass.Core.Driver;
using VpnBypass.Core.Status;

namespace VpnBypass.Core.Ipc;

// A tálcaalkalmazás → szolgáltatás kérés típusa (a boríték diszkriminátora).
public enum IpcRequestType
{
    GetStatus,
    GetApps,
    SetExcluded,
    SetPaused,
    SetInternetAdapter,
}

// Egységes kérés-boríték: soronként egy JSON-objektum a named pipe-on. A Type dönti el,
// mely mezők relevánsak (a többi null). Egy boríték egyszerűsíti a forrásgenerált JSON-t.
public record IpcRequest(
    IpcRequestType Type,
    string? Path = null,
    bool? Excluded = null,
    bool? Paused = null,
    string? AdapterName = null);

// Egy adapter rövid leírása a státuszban.
public record VpnInfo(string Name, string? Ipv4, string? Ipv6);

// A getStatus válasza.
public record StatusResponse(
    DriverState State,
    bool Paused,
    VpnInfo? Vpn,
    VpnInfo? Internet,
    int ExcludedCount,
    string? Error);

// A listában egy program.
public record AppEntry(
    string RuleId,
    string DisplayName,
    string? Pattern,
    bool Running,
    AppStatus Status);

// A getApps válasza.
public record AppsResponse(IReadOnlyList<AppEntry> Apps);

// A módosító kérések (setExcluded, setPaused, setInternetAdapter) válasza.
public record IpcResult(bool Ok, string? Error = null);
