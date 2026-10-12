# Letölti a rögzített Mullvad split-tunnel driver aláírt binárisait és ellenőrzi.
# Nem igényel rendszergazdai jogot (csak letöltés + ellenőrzés). A driver betöltése külön lépés.
#
# Ellenőrzés:
#   1) Authenticode: a katalógus (.cat) aláírásának érvényessége és aláírója (Mullvad).
#   2) SHA-256: ha a driver/SHA256SUMS már tartalmaz hash-eket, azokhoz hasonlít (eltérésnél hiba).
#      Ha még üres (első futás), kiszámolja és beleírja (TOFU-rögzítés) – ezt commitold be.

$ErrorActionPreference = 'Stop'
# A régi Windows PowerShell 5.1 alapból TLS 1.0/1.1-et használ, amit a GitHub elutasít → TLS 1.2.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue' # 5.1-ben a letöltés-progress sáv nagyon lassít
$commit = '5b6f46cde692acb77ee74b37b9fd3f1678c45a52'
$base   = "https://raw.githubusercontent.com/mullvad/mullvadvpn-app-binaries/$commit/x86_64-pc-windows-msvc/split-tunnel"
$files  = 'mullvad-split-tunnel.sys', 'mullvad-split-tunnel.inf', 'mullvad-split-tunnel.cat'
$dst    = Join-Path $PSScriptRoot '..\driver'
$sumsPath = Join-Path $dst 'SHA256SUMS'

New-Item -ItemType Directory -Force -Path $dst | Out-Null

Write-Host "Letöltés a rögzített commitról: $commit"
foreach ($f in $files) {
    $out = Join-Path $dst $f
    Invoke-WebRequest "$base/$f" -OutFile $out
    Write-Host ("  {0}  ({1:N0} bájt)" -f $f, (Get-Item $out).Length)
}

# 1) Authenticode a katalógusfájlon (a .sys aláírása a .cat-ban van, telepítés előtt a .sys maga NotSigned lehet).
$catPath = Join-Path $dst 'mullvad-split-tunnel.cat'
$sig = Get-AuthenticodeSignature $catPath
Write-Host ""
Write-Host "Authenticode (.cat): állapot=$($sig.Status); aláíró=$($sig.SignerCertificate.Subject)"
if ($sig.Status -ne 'Valid') {
    throw "A katalógus Authenticode-állapota NEM 'Valid' ($($sig.Status)). Ne töltsd be a drivert!"
}
if ($sig.SignerCertificate.Subject -notmatch 'Mullvad') {
    Write-Warning "Az aláíró nem tartalmazza a 'Mullvad' nevet – ellenőrizd kézzel: $($sig.SignerCertificate.Subject)"
}

# 2) SHA-256.
$existing = @{}
if (Test-Path $sumsPath) {
    foreach ($line in Get-Content $sumsPath) {
        if ($line -match '^\s*([0-9a-fA-F]{64})\s+(.+?)\s*$') { $existing[$Matches[2]] = $Matches[1].ToLower() }
    }
}

if ($existing.Count -gt 0) {
    Write-Host ""
    Write-Host "SHA-256 ellenőrzés a rögzített SHA256SUMS alapján…"
    foreach ($f in $files) {
        $have = (Get-FileHash (Join-Path $dst $f) -Algorithm SHA256).Hash.ToLower()
        if (-not $existing.ContainsKey($f)) { Write-Warning "  $f nincs a SHA256SUMS-ban"; continue }
        if ($existing[$f] -ne $have) { throw "SHA-256 ELTÉRÉS: $f (várt $($existing[$f]), kapott $have)" }
        Write-Host "  OK: $f"
    }
} else {
    Write-Host ""
    Write-Host "SHA256SUMS üres/hiányzik – első futás, rögzítem a hash-eket (TOFU). Nézd át és commitold be."
    $lines = foreach ($f in $files) {
        $h = (Get-FileHash (Join-Path $dst $f) -Algorithm SHA256).Hash.ToLower()
        "$h  $f"
    }
    Set-Content -Path $sumsPath -Value $lines -Encoding ascii
    $lines | ForEach-Object { Write-Host "  $_" }
}

Write-Host ""
Write-Host "Kész. A driverfájlok a driver/ mappában, ellenőrizve."
