namespace VpnBypass.Core.Driver;

// A Mullvad split-tunnel driver állapotgépe (a driver defs/state.h-jával egyező értékek).
// Átmenetek: Started → Initialized → Ready → Engaged; hiba esetén Zombie (csak újraindítás segít).
public enum DriverState
{
    None = 0,
    Started = 1,
    Initialized = 2,
    Ready = 3,
    Engaged = 4,
    Zombie = 5,
}
