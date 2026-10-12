namespace VpnBypass.Core.Driver;

// A split-tunnel driver vezérlőfelülete típusos alakban. A szolgáltatás a valódi eszközzel
// (DeviceIoControl) valósítja meg, a tesztek hamis driverrel — így az állapotgép driver nélkül tesztelhető.
// Hiba esetén minden művelet kivételt dob.
public interface IDriverClient
{
    DriverState GetState();
    void Initialize(Guid baselineSublayer, Guid dnsSublayer);
    void RegisterProcesses(IReadOnlyList<ProcessEntry> processes);
    void RegisterIpAddresses(DriverAddresses addresses);
    void SetConfiguration(IReadOnlyList<string> ntImagePaths);
    void ClearConfiguration();
    void Reset();
}
