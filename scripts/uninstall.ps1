# vpn-bypass eltávolító (rendszergazdaként). A -KeepConfig kapcsolóval a beállítás megmarad.
param([switch]$KeepConfig)
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Rendszergazdaként futtasd (emelt jogú PowerShell)." }

# Tálca leállítása + auto-indítás törlése
taskkill /IM VpnBypass.Tray.exe /F 2>$null | Out-Null
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'vpn-bypass' -ErrorAction SilentlyContinue

# Szolgáltatás leállítása + törlése (a leállás a motort alapállapotba teszi)
if (sc.exe query vpnbypass 2>$null) {
    sc.exe stop vpnbypass 2>$null | Out-Null
    Start-Sleep 2
    sc.exe delete vpnbypass 2>$null | Out-Null
}

# Fájlok törlése
$dest = Join-Path $env:ProgramFiles 'vpn-bypass'
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue

# Beállítás/napló
if (-not $KeepConfig) {
    Remove-Item (Join-Path $env:ProgramData 'vpn-bypass') -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Eltávolítva$(if ($KeepConfig) { ' (a beállítás megmaradt)' })."
