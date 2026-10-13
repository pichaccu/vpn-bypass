# vpn-bypass

Per-app VPN bypass (split tunneling) for Windows that works with **any** VPN. The VPN stays the
default route for all traffic, but applications you tick on a checklist — and their child
processes — are routed over your physical network adapter instead. That lets things like voice
chat or a game run on your own, more trusted network while everything else stays on the VPN.

The actual traffic steering is done by Mullvad's open-source, Microsoft-signed `win-split-tunnel`
kernel driver; this project drives it from a background service and adds a tray UI on top.

> **Status:** the full application works in **dry-run** mode (VPN detection, app list, configuration,
> logging), but the module that performs the *actual* traffic steering (the live `IDriverClient`
> implementation: driver IOCTLs + WFP) is not wired in yet. Details and a completion guide are in
> [`docs/STATUS.md`](docs/STATUS.md).

## Architecture

- **`VpnBypass.Core`** — machine-independent, unit-tested logic: pattern/rule engine, VPN detection,
  driver data structures, the driver state machine (`BypassEngine`), connection-status classification
  and the IPC contract.
- **`VpnBypass.Service`** — a Windows service running as SYSTEM: driver control, a named pipe,
  network/process/connection monitoring, and configuration storage (with a hardened ACL).
- **`VpnBypass.Tray`** — a WPF tray application with the checklist window.
- **`scripts/`** — `fetch-driver.ps1` (download + verify the driver), `publish.ps1` (portable,
  self-contained package), `install.ps1` / `uninstall.ps1`.

## Requirements

- 64-bit (x64) Windows 10 2004+ / 11
- .NET 10 SDK (for development); the released package is self-contained, so the target machine
  needs no SDK

## How it works

1. The VPN remains the default route; nothing changes for traffic you don't exclude.
2. You tick the apps that should bypass the VPN. The service resolves each rule to the executables
   actually present on disk (including the `Update.exe` launcher for Squirrel apps such as Discord,
   so new versions stay excluded automatically) and hands their paths to the driver.
3. The driver binds those processes' traffic to the physical adapter and blocks their in-tunnel
   traffic, so existing connections reconnect over your own network.

Note: DNS still resolves through the VPN (it goes through the shared Windows DNS service); the
traffic itself travels over your physical network.

## Build and run

```powershell
dotnet test -c Release                 # Core unit tests
.\scripts\publish.ps1                  # portable package under dist\
.\dist\vpn-bypass\scripts\install.ps1  # install on the target machine (as Administrator)
```

## Security

Configuration and logs live under `C:\ProgramData\vpn-bypass`, created with a strict ACL
(full control for SYSTEM, Administrators and the installing user; read-only for Users;
inheritance disabled). The service verifies the directory's owner, rejects reparse points,
discards any untrusted pre-planted config before reading it, and owns the control pipe name
continuously (`FirstPipeInstance`) to prevent pipe squatting.

## License

This project's own code is MIT-licensed (see [`LICENSE`](LICENSE)). Mullvad's `win-split-tunnel`
driver is **not** part of this repository (`fetch-driver.ps1` downloads it) and is covered by
Mullvad's GPL-3.0 / MPL-2.0 terms.
