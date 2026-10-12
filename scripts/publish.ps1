# Hordozható, self-contained csomagot készít a dist\ alá, amit másik (x64 Windows) gépre is átmásolhatsz.
# A célgépen nincs szükség SDK-ra, Visual Studióra vagy forráskódra.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist\vpn-bypass'

Write-Host "Csomagolás ide: $dist"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet publish (Join-Path $root 'src\VpnBypass.Service') -c Release -r win-x64 --self-contained -o (Join-Path $dist 'service')
if ($LASTEXITCODE -ne 0) { throw "A szolgáltatás publish lépése hibára futott." }
dotnet publish (Join-Path $root 'src\VpnBypass.Tray') -c Release -r win-x64 --self-contained -o (Join-Path $dist 'tray')
if ($LASTEXITCODE -ne 0) { throw "A tálca publish lépése hibára futott." }

Copy-Item (Join-Path $root 'scripts') (Join-Path $dist 'scripts') -Recurse
if (Test-Path (Join-Path $root 'driver')) {
    Copy-Item (Join-Path $root 'driver') (Join-Path $dist 'driver') -Recurse
}

Compress-Archive -Path (Join-Path $dist '*') -DestinationPath (Join-Path $root 'dist\vpn-bypass.zip') -Force
Write-Host "Kész: $dist  (és dist\vpn-bypass.zip)"
Write-Host "Telepítés a célgépen: rendszergazda PowerShellből  .\scripts\install.ps1"
