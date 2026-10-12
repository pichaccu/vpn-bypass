# vpn-bypass telepítő (rendszergazdaként futtatandó, a dist\vpn-bypass csomag mappájából).
# Regisztrálja a háttérszolgáltatást (auto-indítás + összeomlás utáni újraindítás) és a tálcát (belépéskor).
# A jelenlegi (dry-run) build NEM tölti be a kernel-drivert — a terelés naplózva van; az éles terelést az
# IDriverClient valódi megvalósítása kapcsolja be (lásd docs\STATUS.md). A szolgáltatás így is SYSTEM-ként fut.
$ErrorActionPreference = 'Stop'

# --- Rendszergazda? ---
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Rendszergazdaként futtasd (emelt jogú PowerShell)." }

# --- Előfeltételek ---
if ([Environment]::Is64BitOperatingSystem -ne $true) { throw "Csak 64 bites (x64) Windows támogatott." }
if ([Environment]::OSVersion.Version.Build -lt 19041) { throw "Windows 10 2004 (build 19041) vagy újabb szükséges." }

$pkg = $PSScriptRoot | Split-Path -Parent   # a csomag gyökere (a scripts\ szülője)
$svcSrc = Join-Path $pkg 'service'
$traySrc = Join-Path $pkg 'tray'
if (-not (Test-Path (Join-Path $svcSrc 'VpnBypass.Service.exe'))) {
    throw "Nem találom a publikált szolgáltatást: $svcSrc. Előbb futtasd a publish.ps1-t, és a dist\vpn-bypass mappából telepíts."
}

$dest = Join-Path $env:ProgramFiles 'vpn-bypass'
$svcDest = Join-Path $dest 'service'
$trayDest = Join-Path $dest 'tray'
$svcExe = Join-Path $svcDest 'VpnBypass.Service.exe'
$trayExe = Join-Path $trayDest 'VpnBypass.Tray.exe'

# --- Korábbi példány leállítása ---
taskkill /IM VpnBypass.Tray.exe /F 2>$null | Out-Null
if (sc.exe query vpnbypass 2>$null) { sc.exe stop vpnbypass 2>$null | Out-Null; Start-Sleep 1; sc.exe delete vpnbypass 2>$null | Out-Null }

# --- Fájlok másolása a Program Files alá ---
New-Item -ItemType Directory -Force -Path $svcDest, $trayDest | Out-Null
Copy-Item (Join-Path $svcSrc '*') $svcDest -Recurse -Force
Copy-Item (Join-Path $traySrc '*') $trayDest -Recurse -Force

# --- A tálcát futtató (telepítő) felhasználó SID-je a configba (a named pipe jogosultságához) ---
$sid = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
$cfgDir = Join-Path $env:ProgramData 'vpn-bypass'
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
$cfg = [ordered]@{ rules = @(); internetAdapter = $null; userSid = $sid } | ConvertTo-Json
Set-Content -Path (Join-Path $cfgDir 'config.json') -Value $cfg -Encoding UTF8

# --- Szolgáltatás regisztrálása ---
sc.exe create vpnbypass binPath= "`"$svcExe`"" start= auto DisplayName= "vpn-bypass" | Out-Null
sc.exe description vpnbypass "Alkalmazásonkénti VPN-megkerülés (split tunneling)." | Out-Null
sc.exe failure vpnbypass reset= 0 actions= restart/5000/restart/5000/restart/5000 | Out-Null
sc.exe start vpnbypass | Out-Null

# --- Tálca belépéskor + most ---
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'vpn-bypass' -Value "`"$trayExe`""
Start-Process $trayExe

Write-Host "Telepítve. A szolgáltatás fut, a tálcaikon elindult."
Write-Host "FIGYELEM: ez a build dry-run — a terelés egyelőre csak naplózva (C:\ProgramData\vpn-bypass\logs)."
