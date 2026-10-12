namespace VpnBypass.Core.Driver;

// Egy futó folyamat a driver kezdeti folyamat-regisztrációjához (IOCTL_ST_REGISTER_PROCESSES).
// DevicePath: NT-eszközútvonal (\Device\HarddiskVolumeN\...); üres is lehet (pl. System, Idle) —
// az útvonal nélküli folyamat is kell, mert a driver a szülő-gyerek láncot ebből építi fel.
public sealed record ProcessEntry(uint ProcessId, uint ParentProcessId, string DevicePath);
