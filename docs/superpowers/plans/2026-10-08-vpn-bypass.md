# vpn-bypass megvalósítási terv

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows-program, amely a VPN-t hagyja alapértelmezett útnak, de egy pipálós listán kijelölt alkalmazásokat (és gyerekfolyamataikat) a fizikai hálózati adapterre terel, bármilyen VPN-klienssel.

**Architecture:** A Mullvad aláírt `win-split-tunnel` kernel-driverét (1.3.0.0, változatlanul) vezérli egy SYSTEM-jogú Windows-szolgáltatás `DeviceIoControl`-on át; a szolgáltatás hozza létre a WFP-providert és -sublayereket, figyeli a hálózatot és a folyamatokat, és named pipe-on szolgálja ki a felhasználói WPF-tálcaalkalmazást. A gépfüggetlen logika egy `Core` könyvtárban van, hogy driver nélkül is tesztelhető legyen.

**Tech Stack:** C# / .NET 10, WPF (tálca), Windows Service (`Microsoft.Extensions.Hosting.WindowsServices`), P/Invoke (iphlpapi, fwpuclnt, kernel32, advapi32), WMI (`System.Management`), xUnit, PowerShell (telepítő szkriptek).

**Spec:** `docs/superpowers/specs/2026-10-08-vpn-bypass-design.md`

## Global Constraints

- **Platform:** x64, Windows 10 2004+ / Windows 11; a fejlesztőgép HVCI-bekapcsolt (a driver HVCI-kompatibilis).
- **Hordozhatóság (másik gépen is működjön):** a Service és a Tray `dotnet publish -c Release -r win-x64 --self-contained` kimenetként települ (a .NET-futtatókörnyezet bele van csomagolva), így a célgépen nincs szükség SDK-ra vagy forráskódra. Semmilyen gépspecifikus érték (adapter neve, felhasználói SID, útvonal) nincs a binárisba vagy a kódba égetve — mind telepítéskor vagy futásidőben dől el. A driver a célgépen töltődik le/ellenőrződik. A saját exe-k nincsenek kódaláírva → a célgépen első indításkor SmartScreen-figyelmeztetés lehet (ez elfogadható, a driver a Mullvad-aláírással rendben betölt).
- **Driver verzió:** `win-split-tunnel` **1.3.0.0**, a `mullvad/mullvadvpn-app-binaries` repóból, rögzített commit `5b6f46cde692acb77ee74b37b9fd3f1678c45a52`, `x86_64-pc-windows-msvc/split-tunnel/` (`.sys`, `.inf`, `.cat`). A fájlok **nem** kerülnek a git-repóba; `fetch-driver.ps1` tölti le, SHA-256 + Authenticode ellenőrzéssel.
- **Driver nem módosítható**; csak a dokumentált IOCTL-felületen vezéreljük.
- **Projektnevek:** kisbetűs kebab-case a repó/termék szintjén (`vpn-bypass`); a .NET-projektek `VpnBypass.*` PascalCase névtérben.
- **Nyelv:** a felhasználói felület és a felhasználónak szóló szövegek magyarul; a kód és a kódkommentek magyarul, a Mullvad-eredetű típusnevek az eredeti angol néven.
- **Git:** gyakori commit; **soha** ne tegyél Claude/Anthropic co-author trailert a commitüzenetbe (a szerző kizárólag `pichaccu <pichaccu@gmail.com>`).
- **Eszköz:** `\\.\MULLVADSPLITTUNNEL`, exkluzív megnyitás, `FILE_FLAG_OVERLAPPED`, `GENERIC_READ | GENERIC_WRITE`.
- **A driver állapotgépe:** `STARTED(1) → INITIALIZED(2) → READY(3) → ENGAGED(4)`, hiba esetén `ZOMBIE(5)`. ENGAGED csak akkor, ha van legalább egy kivétel **és** érvényes tunnel-cím (IPv4 vagy IPv6). A handle bezárása nem állítja vissza; csak `IOCTL_ST_RESET`.
- **WFP:** a szolgáltatás saját providert + két sublayert (Baseline, Dns) hoz létre, és a sublayer-GUID-okat az `IOCTL_ST_INITIALIZE` bemenetében adja át a drivernek.

### Rögzített GUID-ok (a projekté, ne a Mullvadé)

```
Provider:          25289158-e7eb-4de2-a4c1-ac1babf1fbf5
Baseline sublayer: 6e0273b7-33fd-4cfc-9312-8cffcc27dbdc
Dns sublayer:      63359fcc-b89d-4c49-81c9-c011a3eee3cd
```

### Driver-IOCTL-ök (device type 0x8000, CTL_CODE függvény-indexek)

| IOCTL | idx | method | be | ki |
|---|---|---|---|---|
| `IOCTL_ST_INITIALIZE` | 1 | BUFFERED | `ST_SUBLAYER_GUIDS` (32 B) | – |
| `IOCTL_ST_DEQUEUE_EVENT` | 2 | BUFFERED | – | `ST_EVENT_HEADER`+payload |
| `IOCTL_ST_REGISTER_PROCESSES` | 3 | BUFFERED | discovery blob | – |
| `IOCTL_ST_REGISTER_IP_ADDRESSES` | 4 | BUFFERED | `ST_IP_ADDRESSES` (40 B) | – |
| `IOCTL_ST_GET_IP_ADDRESSES` | 5 | BUFFERED | – | `ST_IP_ADDRESSES` |
| `IOCTL_ST_SET_CONFIGURATION` | 6 | BUFFERED | config blob | – |
| `IOCTL_ST_GET_CONFIGURATION` | 7 | BUFFERED | – | méret vagy config blob |
| `IOCTL_ST_CLEAR_CONFIGURATION` | 8 | NEITHER | – | – |
| `IOCTL_ST_GET_STATE` | 9 | BUFFERED | – | `SIZE_T` állapot |
| `IOCTL_ST_QUERY_PROCESS` | 10 | BUFFERED | `ST_QUERY_PROCESS` | `ST_QUERY_PROCESS_RESPONSE` |
| `IOCTL_ST_RESET` | 11 | NEITHER | – | – |

`CTL_CODE = (DeviceType<<16) | (Access<<14) | (Function<<2) | Method`, `METHOD_BUFFERED=0`, `METHOD_NEITHER=3`, `FILE_ANY_ACCESS=0`.
Pl. `IOCTL_ST_GET_STATE = (0x8000<<16)|(9<<2)|0 = 0x80000024`.

### Driver-struktúrák (x64, natív igazítás; `SIZE_T`/`HANDLE` = 8 B, `USHORT` = 2 B)

```
ST_SUBLAYER_GUIDS      { GUID Baseline; GUID Dns; }                 // 16+16 = 32 B
ST_IP_ADDRESSES        { IN_ADDR TunnelIpv4; IN_ADDR InternetIpv4;  // 4+4
                         IN6_ADDR TunnelIpv6; IN6_ADDR InternetIpv6; } // 4+4+16+16 = 40 B
ST_CONFIGURATION_HEADER{ SIZE_T NumEntries; SIZE_T TotalLength; }   // 16 B
ST_CONFIGURATION_ENTRY { SIZE_T ImageNameOffset; USHORT ImageNameLength; } // 8+2 → 16 B igazítva
ST_PROCESS_DISCOVERY_HEADER { SIZE_T NumEntries; SIZE_T TotalLength; } // 16 B
ST_PROCESS_DISCOVERY_ENTRY  { HANDLE Pid; HANDLE ParentPid;
                              SIZE_T ImageNameOffset; USHORT ImageNameLength; } // 8+8+8+2 → 32 B igazítva
ST_QUERY_PROCESS          { HANDLE ProcessId; }                    // 8 B
ST_QUERY_PROCESS_RESPONSE { HANDLE Pid; HANDLE ParentPid; BOOLEAN Split;
                            USHORT ImageNameLength; WCHAR ImageName[]; }
ST_EVENT_HEADER        { int EventId; SIZE_T EventSize; UCHAR EventData[]; }
```
A blob-oknál: fejléc, utána N bejegyzés tömbként, utána a stringpuffer; a `ImageNameOffset` a **stringpuffer** elejéhez relatív, a hossz **null-terminátor nélküli** bájthossz. A képútvonal a kis-/nagybetűben a driver kéri le kisbetűsre, de a konzol-referencia az eredeti kisbetűsített **NT-eszközútvonalat** küldi (`\Device\HarddiskVolumeN\...`). Igazítás: a bejegyzésméreteket 8-ra kerekítve kell egymás után tenni (a referenciakód a natív `sizeof`-ot használja, ami 16 ill. 32).

### Állapotgép-részletek (a driverből igazolva)

- `IOCTL_ST_REGISTER_IP_ADDRESSES` bemenete pontosan 40 B kell legyen (4+4+16+16).
- READY→ENGAGED csak ha: config nem üres **és** `VpnActive` (van érvényes tunnel v4 vagy v6). ENGAGED-ben a config kiürítése vagy a tunnel-cím eltűnése visszavisz READY-be.
- Induláskor `IOCTL_ST_GET_STATE`; ha nem `STARTED`, előbb `IOCTL_ST_RESET` (siker → STARTED, hiba → ZOMBIE).

---

## Fájlszerkezet

```
vpn-bypass/
  VpnBypass.sln
  Directory.Build.props                      közös: net10.0-windows, x64, nullable, LangVersion
  .gitignore                                 bin/ obj/ driver/*.sys|*.inf|*.cat|*.pdb
  src/
    VpnBypass.Core/
      Ipc/IpcContract.cs                     rekordok: kérések/válaszok/státuszok (JSON)
      Ipc/IpcJson.cs                         System.Text.Json forrásgenerált kontextus
      Rules/ExclusionRule.cs                 szabály: eredeti útvonal + származtatott minta
      Rules/PatternFactory.cs                útvonal → minta (Squirrel/MSIX/általános verzió)
      Rules/PatternMatcher.cs                minta illesztése konkrét útvonalra
      Rules/RuleSet.cs                        szabályok gyűjteménye + validáció (max 256)
      Config/AppConfig.cs                     szerializálható konfig (szabályok, internetAdapter, userSid)
      Network/AdapterInfo.cs                  adapter leíró (név, hardveres?, metrika, címek)
      Network/RouteInfo.cs                    útvonal leíró (prefix, metrika, ifIndex)
      Network/VpnDetector.cs                  tiszta logika: adapterek+útvonalak → (tunnel, internet) | nincs VPN
      Driver/DriverState.cs                   enum (None..Zombie)
      Driver/DriverStructs.cs                 IOCTL-kódok + blob-szerializálók (config, discovery, ip)
      Status/AppStatus.cs                     enum: Direct/Partial/Vpn/Blocked/NotRunning/NotFound
      Status/ConnectionClassifier.cs          PID-kapcsolatok + címek → AppStatus
    VpnBypass.Service/
      Program.cs                              host + Windows service
      Worker.cs                               életciklus, komponensek összekötése
      Driver/SplitTunnelDevice.cs             P/Invoke: CreateFile, DeviceIoControl (overlapped), IOCTL-ek
      Driver/DriverController.cs              állapotgép-vezérlés: ensure/engage/clear/reset + esemény-szál
      Firewall/WfpProvider.cs                 P/Invoke fwpuclnt: provider + 2 sublayer létrehozás/törlés
      Network/NetworkMonitor.cs               iphlpapi: táblák lekérése + Notify* változásfigyelés
      Network/AddressQuery.cs                 adapter → IN_ADDR/IN6_ADDR
      Processes/ProcessSnapshot.cs            Toolhelp pillanatkép → discovery blob
      Processes/ProcessWatcher.cs             WMI Win32_ProcessStartTrace
      Processes/PathResolver.cs               Win32 útvonal → NT-eszközútvonal (QueryDosDevice gyorsítótár)
      Connections/ConnectionTable.cs          GetExtended{Tcp,Udp}Table → PID-enkénti lokális címek
      Ipc/PipeServer.cs                        named pipe szerver + ACL
      ConfigStore.cs                           C:\ProgramData\vpn-bypass\config.json olvas/ír
      Logging/*                                Serilog fájl + EventLog
    VpnBypass.Tray/
      App.xaml / App.xaml.cs                  WPF app, egyetlen példány
      TrayIcon.cs                             NotifyIcon (H.NotifyIcon.Wpf), állapotszín
      MainWindow.xaml / .cs                    pipálós lista + fejléc
      PipeClient.cs                            named pipe kliens
      Theme/*                                  világos/sötét követés
  tests/
    VpnBypass.Core.Tests/                     xUnit: minták, VPN-felismerés, blob-szerializálás, státusz, config
  scripts/
    fetch-driver.ps1
    install.ps1
    uninstall.ps1
  driver/
    SHA256SUMS                                a 3 driverfájl elvárt hash-e (a fájlok gitignore-ban)
  docs/...
```

---

## Fázis 0 – Kockázatcsökkentő kézi próba

> Cél: még a kódolás előtt bizonyosság, hogy a driver betölthető és vezérelhető ezen a gépen, NordVPN mellett. Ez a user közreműködésével, rendszergazdai jogokkal zajlik; nem automatizáljuk.

### Task 0.1: Driver letöltése és ellenőrzése

**Files:**
- Create: `scripts/fetch-driver.ps1`
- Create: `driver/SHA256SUMS`
- Create: `.gitignore`

**Interfaces:**
- Produces: a `driver/` mappában `mullvad-split-tunnel.{sys,inf,cat}`, ellenőrzött hash-sel.

- [ ] **Step 1: `.gitignore`**

```gitignore
bin/
obj/
driver/*.sys
driver/*.inf
driver/*.cat
driver/*.pdb
*.user
```

- [ ] **Step 2: `SHA256SUMS` rögzítése a pinned commitról**

A hash-eket a letöltött fájlokból vedd (lásd Step 3), majd írd ide, pl.:
```
<sha256>  mullvad-split-tunnel.sys
<sha256>  mullvad-split-tunnel.inf
<sha256>  mullvad-split-tunnel.cat
```

- [ ] **Step 3: `fetch-driver.ps1`**

```powershell
# Letölti a rögzített Mullvad split-tunnel driver binárisait és ellenőrzi.
$ErrorActionPreference = 'Stop'
$commit = '5b6f46cde692acb77ee74b37b9fd3f1678c45a52'
$base   = "https://raw.githubusercontent.com/mullvad/mullvadvpn-app-binaries/$commit/x86_64-pc-windows-msvc/split-tunnel"
$dst    = Join-Path $PSScriptRoot '..\driver'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($f in 'mullvad-split-tunnel.sys','mullvad-split-tunnel.inf','mullvad-split-tunnel.cat') {
  Invoke-WebRequest "$base/$f" -OutFile (Join-Path $dst $f)
}
# SHA-256 egyezés
$sums = Get-Content (Join-Path $dst 'SHA256SUMS')
foreach ($line in $sums) {
  if ($line -match '^\s*([0-9a-fA-F]{64})\s+(.+)$') {
    $want = $Matches[1].ToLower(); $name = $Matches[2].Trim()
    $have = (Get-FileHash (Join-Path $dst $name) -Algorithm SHA256).Hash.ToLower()
    if ($want -ne $have) { throw "Hash eltérés: $name" }
  }
}
# Authenticode: a .cat legyen érvényesen aláírva (Mullvad / Microsoft attestation)
$sig = Get-AuthenticodeSignature (Join-Path $dst 'mullvad-split-tunnel.sys')
if ($sig.Status -ne 'Valid') { Write-Warning "A .sys Authenticode állapota: $($sig.Status) (katalógus-aláírás a .cat-ban lehet)" }
Write-Host 'Driver letöltve és ellenőrizve.'
```

- [ ] **Step 4: Futtatás (a user, rendszergazdaként nem szükséges a letöltéshez)**

Run:
```powershell
pwsh -File scripts/fetch-driver.ps1
```
Expected: a 3 fájl a `driver/` mappában, hash OK.

- [ ] **Step 5: Commit**

```bash
git add .gitignore scripts/fetch-driver.ps1 driver/SHA256SUMS
git commit -m "chore: driver-letolto szkript es hash-ellenorzes"
```

### Task 0.2: Kézi driver-próba (dokumentálás, a user futtatja)

**Files:**
- Create: `docs/phase0-manual-test.md`

- [ ] **Step 1: Teszt-protokoll leírása**

A `docs/phase0-manual-test.md` tartalmazza: rendszer-visszaállítási pont készítése; `sc create vpnbypass-stdrv type= kernel binPath= <abszolút .sys>` vagy INF-telepítés `pnputil /add-driver`-rel; `sc start`; a referencia `stconsole.exe` (külön, a Mullvad repóból fordítva) vagy egy minimális C# próbakliens lépései: connect → initialize (a projekt GUID-jaival) → register-processes → register-ips (Ethernet + NordLynx címmel) → set-config (egy `curl`-másolat NT-útvonalával) → engaged ellenőrzése. Siker-kritérium: a kivett `curl` a saját publikus IP-t látja, a sima `curl` a NordVPN-ét; majd reset + `sc stop` + `sc delete` tisztán.

- [ ] **Step 2: Commit**

```bash
git add docs/phase0-manual-test.md
git commit -m "docs: fazis 0 kezi driver-teszt protokoll"
```

**STOP checkpoint:** a Fázis 1 csak akkor indul, ha a user visszajelezte, hogy a kézi próba sikeres volt ezen a gépen.

---

## Fázis 1 – Core + unit tesztek (driver és gép nélkül)

### Task 1.1: Megoldás és Core-projekt váza

**Files:**
- Create: `VpnBypass.sln`, `Directory.Build.props`, `src/VpnBypass.Core/VpnBypass.Core.csproj`, `tests/VpnBypass.Core.Tests/VpnBypass.Core.Tests.csproj`

**Interfaces:**
- Produces: buildelhető megoldás, futó (üres) xUnit projekt.

- [ ] **Step 1: Projektek létrehozása**

```bash
dotnet new sln -n VpnBypass
dotnet new classlib -n VpnBypass.Core -o src/VpnBypass.Core -f net10.0
dotnet new xunit -n VpnBypass.Core.Tests -o tests/VpnBypass.Core.Tests -f net10.0
dotnet sln add src/VpnBypass.Core tests/VpnBypass.Core.Tests
dotnet add tests/VpnBypass.Core.Tests reference src/VpnBypass.Core
```

- [ ] **Step 2: `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Töröld a sablon `Class1.cs` / `UnitTest1.cs` fájlokat**

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: PASS (0 figyelmeztetés a props-ból adódó beállításokkal).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: VpnBypass megoldas es Core vaz"
```

### Task 1.2: Minta-gyár (PatternFactory) – Squirrel

**Files:**
- Create: `src/VpnBypass.Core/Rules/PatternFactory.cs`
- Test: `tests/VpnBypass.Core.Tests/PatternFactoryTests.cs`

**Interfaces:**
- Produces: `static string PatternFactory.FromPath(string fullPath)` → az útvonal, amelyben a verziós mappanevek `*`-ra cserélve; ha nincs verziós szegmens, az eredeti útvonal (kisbetűsítve a `C:` stb., de a `*` megmarad). Csak a mappaneveket érinti, a fájlnevet nem.

- [ ] **Step 1: Failing test**

```csharp
using VpnBypass.Core.Rules;
using Xunit;

public class PatternFactoryTests
{
    [Fact]
    public void Discord_squirrel_version_folder_becomes_wildcard()
    {
        var p = PatternFactory.FromPath(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe");
        Assert.Equal(@"c:\users\x\appdata\local\discord\app-*\discord.exe", p);
    }
}
```

- [ ] **Step 2: Fut és elbukik**

Run: `dotnet test --filter PatternFactoryTests`
Expected: FAIL (nincs `PatternFactory`).

- [ ] **Step 3: Minimális implementáció**

```csharp
using System.Text.RegularExpressions;

namespace VpnBypass.Core.Rules;

public static class PatternFactory
{
    // Squirrel: app-<verzió>; általános: tisztán verziószámú mappanév; MSIX: Name_<verzió>_arch__pub.
    private static readonly Regex Squirrel = new(@"^app-\d+(\.\d+)*$", RegexOptions.IgnoreCase);

    public static string FromPath(string fullPath)
    {
        var norm = fullPath.ToLowerInvariant();
        var parts = norm.Split('\\');
        for (int i = 0; i < parts.Length - 1; i++) // a fájlnevet (utolsó) nem bántjuk
        {
            if (Squirrel.IsMatch(parts[i]))
                parts[i] = "app-*";
        }
        return string.Join('\\', parts);
    }
}
```

- [ ] **Step 4: Fut és átmegy**

Run: `dotnet test --filter PatternFactoryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VpnBypass.Core/Rules/PatternFactory.cs tests/VpnBypass.Core.Tests/PatternFactoryTests.cs
git commit -m "feat(core): Squirrel verzio-mappa mintava alakitasa"
```

### Task 1.3: PatternFactory – MSIX és általános verziómappa

**Files:**
- Modify: `src/VpnBypass.Core/Rules/PatternFactory.cs`
- Test: `tests/VpnBypass.Core.Tests/PatternFactoryTests.cs`

**Interfaces:**
- Consumes: `PatternFactory.FromPath`.
- Produces: ugyanaz a szignatúra; MSIX-mappa (`Name_<verzió>_<arch>__<publisherId>`) → `name_*_<arch>__<publisherId>`; csupán verziószámú mappanév (`1.2.3`) → `*`.

- [ ] **Step 1: Failing tesztek**

```csharp
[Fact]
public void Msix_folder_keeps_name_arch_and_publisher()
{
    var p = PatternFactory.FromPath(@"C:\Program Files\WindowsApps\Claude_2.26454.2.0_x64__abc123\Claude.exe");
    Assert.Equal(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe", p);
}

[Fact]
public void Plain_version_folder_becomes_wildcard()
{
    var p = PatternFactory.FromPath(@"C:\Apps\Tool\1.4.2\tool.exe");
    Assert.Equal(@"c:\apps\tool\*\tool.exe", p);
}

[Fact]
public void No_version_folder_returns_lowercased_path()
{
    var p = PatternFactory.FromPath(@"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe");
    Assert.Equal(@"c:\program files\teamspeak 3 client\ts3client_win64.exe", p);
}
```

- [ ] **Step 2: Fut és elbukik**

Run: `dotnet test --filter PatternFactoryTests`
Expected: az új 3 FAIL.

- [ ] **Step 3: Implementáció bővítése**

```csharp
private static readonly Regex Msix = new(@"^(?<name>[^_]+)_\d+(\.\d+)*_(?<arch>[^_]+)__(?<pub>[A-Za-z0-9]+)$", RegexOptions.IgnoreCase);
private static readonly Regex PlainVersion = new(@"^\d+(\.\d+){1,3}$");

// a FromPath ciklusában, a Squirrel ág után:
else if (Msix.Match(parts[i]) is { Success: true } m)
    parts[i] = $"{m.Groups["name"].Value}_*_{m.Groups["arch"].Value}__{m.Groups["pub"].Value}";
else if (PlainVersion.IsMatch(parts[i]))
    parts[i] = "*";
```

- [ ] **Step 4: Fut és átmegy**

Run: `dotnet test --filter PatternFactoryTests`
Expected: PASS (mind).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): MSIX es altalanos verzio-mappa mintak"
```

### Task 1.4: PatternMatcher

**Files:**
- Create: `src/VpnBypass.Core/Rules/PatternMatcher.cs`
- Test: `tests/VpnBypass.Core.Tests/PatternMatcherTests.cs`

**Interfaces:**
- Produces: `static bool PatternMatcher.IsMatch(string pattern, string fullPath)` — a `pattern` a PatternFactory kimenete (kisbetűs, `*` szegmensekkel), a `fullPath` tetszőleges kis/nagybetűvel; egy `*` pontosan egy mappaszegmensre illeszkedik.

- [ ] **Step 1: Failing test**

```csharp
using VpnBypass.Core.Rules;
using Xunit;

public class PatternMatcherTests
{
    [Theory]
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1.0.9300\Discord.exe", true)]
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1.0.9300\Update.exe", false)]
    [InlineData(@"c:\apps\tool\*\tool.exe",
                @"C:\Apps\Tool\1.5.0\sub\tool.exe", false)] // a * egyetlen szegmens
    public void Matches(string pattern, string path, bool expected)
        => Assert.Equal(expected, PatternMatcher.IsMatch(pattern, path));
}
```

- [ ] **Step 2: Fut és elbukik** — Run: `dotnet test --filter PatternMatcherTests` → FAIL.

- [ ] **Step 3: Implementáció**

```csharp
namespace VpnBypass.Core.Rules;

public static class PatternMatcher
{
    public static bool IsMatch(string pattern, string fullPath)
    {
        var pp = pattern.Split('\\');
        var fp = fullPath.ToLowerInvariant().Split('\\');
        if (pp.Length != fp.Length) return false;
        for (int i = 0; i < pp.Length; i++)
        {
            if (pp[i] == "*") continue;
            if (pp[i].Contains('*')) // „app-*” jellegű prefix
            {
                var prefix = pp[i][..pp[i].IndexOf('*')];
                var suffix = pp[i][(pp[i].IndexOf('*') + 1)..];
                if (!(fp[i].StartsWith(prefix) && fp[i].EndsWith(suffix))) return false;
            }
            else if (pp[i] != fp[i]) return false;
        }
        return true;
    }
}
```

- [ ] **Step 4: Fut és átmegy** — Run: `dotnet test --filter PatternMatcherTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): minta-illeszto"
```

### Task 1.5: ExclusionRule + RuleSet (validációval)

**Files:**
- Create: `src/VpnBypass.Core/Rules/ExclusionRule.cs`, `src/VpnBypass.Core/Rules/RuleSet.cs`
- Test: `tests/VpnBypass.Core.Tests/RuleSetTests.cs`

**Interfaces:**
- Produces:
  - `record ExclusionRule(string DisplayName, string OriginalPath, string Pattern)`; `static ExclusionRule Create(string fullPath)` → a `Pattern` a `PatternFactory.FromPath`-ból, `DisplayName` a fájlnév kiterjesztés nélkül.
  - `class RuleSet { IReadOnlyList<ExclusionRule> Rules; bool TryAdd(ExclusionRule r, out string? error); bool RemoveByPattern(string pattern); const int MaxRules = 256; }` — duplikált minta és a 256-os korlát hibát ad.

- [ ] **Step 1: Failing tesztek**

```csharp
using VpnBypass.Core.Rules;
using Xunit;

public class RuleSetTests
{
    [Fact]
    public void Create_derives_name_and_pattern()
    {
        var r = ExclusionRule.Create(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe");
        Assert.Equal("Discord", r.DisplayName);
        Assert.Equal(@"c:\users\x\appdata\local\discord\app-*\discord.exe", r.Pattern);
    }

    [Fact]
    public void Duplicate_pattern_is_rejected()
    {
        var set = new RuleSet();
        Assert.True(set.TryAdd(ExclusionRule.Create(@"C:\a\app-1.0.1\x.exe"), out _));
        Assert.False(set.TryAdd(ExclusionRule.Create(@"C:\a\app-2.0.0\x.exe"), out var err));
        Assert.NotNull(err);
    }
}
```

- [ ] **Step 2: Fut és elbukik** — Run: `dotnet test --filter RuleSetTests` → FAIL.

- [ ] **Step 3: Implementáció** (az `ExclusionRule.Create` a `Path.GetFileNameWithoutExtension`-t használja a névhez; a `RuleSet` belső listát és minta-halmazt tart; `TryAdd` ellenőrzi a duplikációt és a `MaxRules`-t).

- [ ] **Step 4: Fut és átmegy** — Run: `dotnet test --filter RuleSetTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): kivetel-szabaly es szabalyhalmaz validacioval"
```

### Task 1.6: VPN-felismerés adatmodellje (AdapterInfo, RouteInfo)

**Files:**
- Create: `src/VpnBypass.Core/Network/AdapterInfo.cs`, `src/VpnBypass.Core/Network/RouteInfo.cs`

**Interfaces:**
- Produces:
  - `record AdapterInfo(int IfIndex, string Name, bool IsHardware, bool IsOperational, int InterfaceMetric, System.Net.IPAddress? Ipv4, System.Net.IPAddress? Ipv6)`
  - `record RouteInfo(int IfIndex, string DestinationPrefix, int PrefixLength, int RouteMetric)`

- [ ] **Step 1: Típusok megírása** (csak rekordok; nincs logika).
- [ ] **Step 2: Build** — Run: `dotnet build` → PASS.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(core): halozati adatmodell (adapter, utvonal)"
```

### Task 1.7: VpnDetector (tiszta logika)

**Files:**
- Create: `src/VpnBypass.Core/Network/VpnDetector.cs`
- Test: `tests/VpnBypass.Core.Tests/VpnDetectorTests.cs`

**Interfaces:**
- Consumes: `AdapterInfo`, `RouteInfo`.
- Produces:
  - `record DetectionResult(AdapterInfo? Tunnel, AdapterInfo? Internet)` — `Tunnel == null` → nincs VPN.
  - `static DetectionResult VpnDetector.Detect(IReadOnlyList<AdapterInfo> adapters, IReadOnlyList<RouteInfo> routes, string? manualInternetAdapterName = null)`.
- Szabály: a default-útvonalak (`0.0.0.0/0`, vagy `0.0.0.0/1`+`128.0.0.0/1`; v6 analóg) nyertese a leghosszabb prefix, azon belül a legkisebb effektív metrika (`RouteMetric + InterfaceMetric`). Ha a nyertes adapter nem hardveres → az a tunnel; az internet a hardveres, default-útvonalas adapterek közül a legkisebb effektív metrikájú (vagy a `manualInternetAdapterName`). Ha a nyertes hardveres → nincs VPN (`Tunnel=null`, `Internet` = a nyertes).

- [ ] **Step 1: Failing tesztek**

```csharp
using System.Net;
using VpnBypass.Core.Network;
using Xunit;

public class VpnDetectorTests
{
    static AdapterInfo Eth(int i=17) => new(i,"Ethernet 3",true,true,20,IPAddress.Parse("192.168.0.51"),null);
    static AdapterInfo Nord(int i=40) => new(i,"NordLynx",false,true,5,IPAddress.Parse("10.5.0.2"),null);

    [Fact]
    public void NordLynx_full_tunnel_is_detected()
    {
        var res = VpnDetector.Detect(
            new[]{ Eth(), Nord() },
            new[]{ new RouteInfo(40,"0.0.0.0/0",0,0), new RouteInfo(17,"0.0.0.0/0",0,0) });
        Assert.Equal("NordLynx", res.Tunnel!.Name);
        Assert.Equal("Ethernet 3", res.Internet!.Name);
    }

    [Fact]
    public void OpenVpn_half_routes_are_detected_as_tunnel()
    {
        var tap = new AdapterInfo(6,"OpenVPN",false,true,1,IPAddress.Parse("10.8.0.3"),null);
        var res = VpnDetector.Detect(
            new[]{ Eth(), tap },
            new[]{ new RouteInfo(6,"0.0.0.0/1",1,0), new RouteInfo(6,"128.0.0.0/1",1,0),
                   new RouteInfo(17,"0.0.0.0/0",0,0) });
        Assert.Equal("OpenVPN", res.Tunnel!.Name);  // /1 hosszabb prefix, mint a /0
    }

    [Fact]
    public void No_vpn_when_hardware_wins()
    {
        var res = VpnDetector.Detect(new[]{ Eth() }, new[]{ new RouteInfo(17,"0.0.0.0/0",0,0) });
        Assert.Null(res.Tunnel);
        Assert.Equal("Ethernet 3", res.Internet!.Name);
    }
}
```

- [ ] **Step 2: Fut és elbukik** — Run: `dotnet test --filter VpnDetectorTests` → FAIL.

- [ ] **Step 3: Implementáció** a fenti szabály szerint (default-útvonal felismerés prefixhossz és effektív metrika alapján; a `vEthernet`/WSL kizárás a hívó `AdapterInfo`-listájában történik, ide már szűrten érkezik, de a `Detect` a `IsHardware=false` adaptereket amúgy sem veszi internetnek).

- [ ] **Step 4: Fut és átmegy** — Run: `dotnet test --filter VpnDetectorTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): VPN-felismeres (tunnel/internet adapter)"
```

### Task 1.8: Driver-struktúrák és IOCTL-kódok + blob-szerializálók

**Files:**
- Create: `src/VpnBypass.Core/Driver/DriverState.cs`, `src/VpnBypass.Core/Driver/DriverStructs.cs`
- Test: `tests/VpnBypass.Core.Tests/DriverStructsTests.cs`

**Interfaces:**
- Produces:
  - `enum DriverState { None=0, Started=1, Initialized=2, Ready=3, Engaged=4, Zombie=5 }`
  - `static class Ioctl` konstansok: `Initialize=0x80000004, DequeueEvent=0x80000008, RegisterProcesses=0x8000000C, RegisterIpAddresses=0x80000010, GetIpAddresses=0x80000014, SetConfiguration=0x80000018, GetConfiguration=0x8000001C, ClearConfiguration=0x80000023 (NEITHER), GetState=0x80000024, QueryProcess=0x80000028, Reset=0x8000002F (NEITHER)`. **Kötelező: a konstansokat a `CTL_CODE` képlettel számítsd** egy `private static uint Ctl(uint function, uint method)` segéddel (`DeviceType=0x8000`, `Access=FILE_ANY_ACCESS=0`, `METHOD_BUFFERED=0`, `METHOD_NEITHER=3`), ne égesd be a hexet. A teszt ellenőrzi `GetState==0x80000024`-et és azt is, hogy `ClearConfiguration` és `Reset` METHOD_NEITHER (alsó 2 bit = 3), azaz `0x80000023` és `0x8000002F`.
  - `static byte[] DriverStructs.BuildConfiguration(IReadOnlyList<string> ntImagePaths)` — `ST_CONFIGURATION_HEADER` + bejegyzések (16 B igazítva) + UTF-16 stringpuffer; offset a stringpuffer elejéhez relatív; hossz null nélkül.
  - `static byte[] DriverStructs.BuildIpAddresses(IPAddress? tunnelV4, IPAddress? internetV4, IPAddress? tunnelV6, IPAddress? internetV6)` — pontosan 40 bájt (4+4+16+16).
  - `static byte[] DriverStructs.BuildSublayerGuids(Guid baseline, Guid dns)` — 32 bájt.

- [ ] **Step 1: Failing tesztek**

```csharp
using System.Net;
using VpnBypass.Core.Driver;
using Xunit;

public class DriverStructsTests
{
    [Fact]
    public void Ip_blob_is_44_bytes_with_ipv4_in_place()
    {
        var blob = DriverStructs.BuildIpAddresses(
            IPAddress.Parse("10.5.0.2"), IPAddress.Parse("192.168.0.51"), null, null);
        Assert.Equal(44, blob.Length);
        Assert.Equal(new byte[]{10,5,0,2}, blob[0..4]);        // TunnelIpv4
        Assert.Equal(new byte[]{192,168,0,51}, blob[4..8]);    // InternetIpv4
    }

    [Fact]
    public void Get_state_ioctl_code_matches_ctl_code_formula()
    {
        Assert.Equal(0x80000024u, Ioctl.GetState);
    }

    [Fact]
    public void Config_blob_layout_is_correct_for_one_entry()
    {
        var blob = DriverStructs.BuildConfiguration(new[]{ @"\device\harddiskvolume3\a\b.exe" });
        // header: NumEntries=1, TotalLength=blob.Length
        Assert.Equal(1, BitConverter.ToInt64(blob, 0));
        Assert.Equal(blob.Length, BitConverter.ToInt64(blob, 8));
    }
}
```

- [ ] **Step 2: Fut és elbukik** — Run: `dotnet test --filter DriverStructsTests` → FAIL.

- [ ] **Step 3: Implementáció** — a `Ioctl` konstansokat a `CTL_CODE` képlettel számold (ne beégetve, hanem egy `private static uint Ctl(uint fn, uint method)` segéddel); a blob-építők a Fázis 0-ban és a referencia-konzolban látott kiosztást kövessék (bejegyzések 8-ra igazítva).

- [ ] **Step 4: Fut és átmegy** — Run: `dotnet test --filter DriverStructsTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): driver IOCTL-kodok es blob-szerializalok"
```

### Task 1.9: Kapcsolat-osztályozó (ConnectionClassifier)

**Files:**
- Create: `src/VpnBypass.Core/Status/AppStatus.cs`, `src/VpnBypass.Core/Status/ConnectionClassifier.cs`
- Test: `tests/VpnBypass.Core.Tests/ConnectionClassifierTests.cs`

**Interfaces:**
- Produces:
  - `enum AppStatus { Direct, Partial, Vpn, Blocked, NotRunning, NotFound }`
  - `record ConnectionSample(IPAddress LocalAddress, bool IsEstablished, double AgeSeconds, bool IsSyn)`
  - `static AppStatus ConnectionClassifier.Classify(IReadOnlyList<ConnectionSample> conns, IPAddress internetAddress, IPAddress? tunnelAddress)` — minden lokális cím internet → Direct; vegyes → Partial; csak tunnel → Vpn; nincs kapcsolat, de van >5 s SYN az internet-címről, ESTABLISHED nélkül → Blocked; üres → NotRunning.

- [ ] **Step 1: Failing tesztek** (Direct, Partial, Vpn, Blocked esetekre egy-egy `[Fact]`).

- [ ] **Step 2: Fut és elbukik** — Run: `dotnet test --filter ConnectionClassifierTests` → FAIL.

- [ ] **Step 3: Implementáció** a fenti szabállyal.

- [ ] **Step 4: Fut és átmegy** — PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): kapcsolat-statusz osztalyozo"
```

### Task 1.10: IPC-szerződés + konfig (JSON, forrásgenerált)

**Files:**
- Create: `src/VpnBypass.Core/Ipc/IpcContract.cs`, `src/VpnBypass.Core/Ipc/IpcJson.cs`, `src/VpnBypass.Core/Config/AppConfig.cs`
- Test: `tests/VpnBypass.Core.Tests/IpcContractTests.cs`

**Interfaces:**
- Produces:
  - Kérés-rekordok: `GetStatusRequest`, `GetAppsRequest`, `SetExcludedRequest(string Path, bool Excluded)`, `SetPausedRequest(bool Paused)`, `SetInternetAdapterRequest(string? Name)`; közös `IpcMessage` boríték `Type` diszkriminátorral.
  - Válaszok: `StatusResponse(DriverState State, bool Paused, VpnInfo? Vpn, VpnInfo? Internet, int ExcludedCount, string? Error)`, `AppsResponse(IReadOnlyList<AppEntry> Apps)`, `AppEntry(string RuleId, string DisplayName, string? Pattern, bool Running, AppStatus Status)`, `VpnInfo(string Name, string? Ipv4, string? Ipv6)`.
  - `AppConfig(IReadOnlyList<ExclusionRule> Rules, string? InternetAdapter, string? UserSid)` + `static` JSON olvasás/írás.
  - `IpcJson.Serialize/Deserialize` a `JsonSerializerContext`-tel (AOT-barát).

- [ ] **Step 1: Failing test** — egy `SetExcludedRequest` oda-vissza (serialize→deserialize) azonos; `StatusResponse` enumja sztringként szerializálódik.

- [ ] **Step 2: Fut és elbukik** — Run: `dotnet test --filter IpcContractTests` → FAIL.

- [ ] **Step 3: Implementáció** — rekordok + `System.Text.Json` `[JsonSerializable]` kontextus; enumok `JsonStringEnumConverter`-rel.

- [ ] **Step 4: Fut és átmegy** — PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): IPC-szerzodes es konfig JSON"
```

### Task 1.11: Core önellenőrzés

- [ ] **Step 1:** `dotnet test` (teljes Core.Tests) → PASS, és `dotnet build -warnaserror` → 0 warning.
- [ ] **Step 2: Commit** (ha volt javítás).

---

## Fázis 2 – Service (driver, WFP, hálózat, folyamatok, IPC)

> A Service projekt Windows-only; csak a célgépen fut értelmesen. A P/Invoke rétegek köré vékony interfész kerül, hogy a `Core` tesztelhető maradjon. A Service-hez integrációs teszt helyett a Fázis 0 kézi protokoll + a Fázis 4 kézi tesztek adják a bizonyosságot; ahol ésszerű, a logikát a Core-ba húzzuk és ott teszteljük.

### Task 2.1: Service-projekt + host váz

**Files:**
- Create: `src/VpnBypass.Service/VpnBypass.Service.csproj`, `Program.cs`, `Worker.cs`
- Modify: `VpnBypass.sln`

**Interfaces:**
- Produces: futtatható worker, amely konzolként és szolgáltatásként is indul (`UseWindowsService`), Serilog fájl- és EventLog-naplóval. Egyelőre csak naplóz („Service indul/leáll”).

- [ ] **Step 1:** `dotnet new worker -o src/VpnBypass.Service -f net10.0`; `TargetFramework` = `net10.0-windows`; csomagok: `Microsoft.Extensions.Hosting.WindowsServices`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`, `Serilog.Sinks.EventLog`, `System.Management`. Referencia a `Core`-ra. Add a sln-hez.
- [ ] **Step 2:** `Program.cs` — host build `UseWindowsService(o => o.ServiceName="vpnbypass")`, Serilog a `C:\ProgramData\vpn-bypass\logs\`-ba (napi, 7 nap).
- [ ] **Step 3:** Build — Run: `dotnet build src/VpnBypass.Service` → PASS.
- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(service): worker host vaz naplozassal"
```

### Task 2.2: PathResolver (Win32 ↔ NT-eszközútvonal)

**Files:**
- Create: `src/VpnBypass.Service/Processes/PathResolver.cs`

**Interfaces:**
- Produces: `string ToNtDevicePath(string win32Path)` (`C:\...` → `\Device\HarddiskVolumeN\...`, `QueryDosDevice` gyorsítótárral, kisbetűsítve); `string? ToWin32Path(string ntPath)` a fordított irány a státusz-megjelenítéshez.

- [ ] **Step 1:** Implementáció `QueryDosDeviceW` P/Invoke-kal, meghajtóbetű→eszköz gyorsítótár, változásra frissül.
- [ ] **Step 2:** Kézi füstpróba egy kis `Main`-ággal vagy ideiglenes logsorral a célgépen (a leképezés helyes a `C:`-re és `D:`-re). Dokumentáld a logban.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): Win32<->NT utvonal lekepezes"
```

### Task 2.3: SplitTunnelDevice (P/Invoke IOCTL-réteg)

**Files:**
- Create: `src/VpnBypass.Service/Driver/SplitTunnelDevice.cs`

**Interfaces:**
- Consumes: `Ioctl`, `DriverState`, `DriverStructs` (Core).
- Produces: `IDisposable` eszközfogantyú: `bool TryOpen()`, `DriverState GetState()`, `void Initialize(Guid baseline, Guid dns)`, `void RegisterProcesses(byte[] blob)`, `void RegisterIpAddresses(byte[] blob)`, `void SetConfiguration(byte[] blob)`, `void ClearConfiguration()`, `void Reset()`, `byte[]? DequeueEvent(int timeoutMs)` — mind overlapped `DeviceIoControl`-lal; a NEITHER-metódusú hívások (`ClearConfiguration`, `Reset`) nulla pufferrel.

- [ ] **Step 1:** P/Invoke: `CreateFileW`, `DeviceIoControl`, `GetOverlappedResult`, `CreateEventW` (a referencia `SendIoControl` mintájára). `TryOpen` → `OPEN_EXISTING`, `FILE_FLAG_OVERLAPPED`.
- [ ] **Step 2:** Kézi próba a célgépen a már betöltött driverrel (Fázis 0 után): open → GetState naplóz. Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): driver IOCTL P/Invoke reteg"
```

### Task 2.4: WfpProvider (provider + 2 sublayer)

**Files:**
- Create: `src/VpnBypass.Service/Firewall/WfpProvider.cs`

**Interfaces:**
- Produces: `void EnsureObjects()` — létrehozza (ha hiányzik) a providert és a két sublayert a rögzített GUID-okkal, **perzisztensen**, `ST_HIGH_FILTER_WEIGHT`-nél jóval **alacsonyabb** súllyal nem itt dől el (a súlyokat a driver adja a saját szűrőinek; mi csak a sublayer-konténereket hozzuk létre, a sublayer `weight`-je legyen alacsony, hogy más providerek sublayerei fölékerülhessenek); `void RemoveObjects()` — törli őket (csak sikeres driver-`Reset` után hívható). GUID-ok a Core-ból vagy egy közös `Constants`-ból.

- [ ] **Step 1:** P/Invoke `fwpuclnt.dll`: `FwpmEngineOpen0`, `FwpmProviderAdd0`, `FwpmSubLayerAdd0` (×2, `FWPM_SUBLAYER_FLAG_PERSISTENT`), `FwpmSubLayerDeleteByKey0`, `FwpmProviderDeleteByKey0`. A két sublayer kulcsa a rögzített Baseline/Dns GUID.
- [ ] **Step 2:** Kézi próba: `EnsureObjects` után `netsh wfp show state` (vagy a logban visszaolvasás) mutatja a providert és a 2 sublayert; `RemoveObjects` törli. Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): WFP provider es ket sublayer"
```

### Task 2.5: NetworkMonitor (táblák + változásfigyelés) és AddressQuery

**Files:**
- Create: `src/VpnBypass.Service/Network/NetworkMonitor.cs`, `src/VpnBypass.Service/Network/AddressQuery.cs`

**Interfaces:**
- Produces:
  - `IReadOnlyList<AdapterInfo> GetAdapters()` és `IReadOnlyList<RouteInfo> GetRoutes()` — `GetAdaptersAddresses` / `GetIpForwardTable2` / `GetIfEntry2` alapján, a `vEthernet (WSL)`, Hyper-V, Docker adapterek `IsHardware=false`-ként vagy kihagyva; a hardveresség a `IfType` és a `GetAdaptersAddresses` alapján.
  - `event Action Changed` — `NotifyIpInterfaceChange` + `NotifyRouteChange2` + `NotifyUnicastIpAddressChange`, 1 s csillapítással.
  - `AddressQuery` segít az `AdapterInfo` → `IN_ADDR`/`IN6_ADDR` kinyeréshez a driver blob-hoz.

- [ ] **Step 1:** Implementáció (P/Invoke iphlpapi). A `GetAdapters` eredménye a Core `VpnDetector.Detect`-jébe megy.
- [ ] **Step 2:** Kézi próba a célgépen: NordVPN be/ki, szerverváltás → a `Changed` tüzel, az új adapterlista helyes (logban). Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): halozatfigyeles es cim-lekerdezes"
```

### Task 2.6: ProcessSnapshot + ProcessWatcher

**Files:**
- Create: `src/VpnBypass.Service/Processes/ProcessSnapshot.cs`, `src/VpnBypass.Service/Processes/ProcessWatcher.cs`

**Interfaces:**
- Produces:
  - `byte[] ProcessSnapshot.BuildRegisterBlob()` — Toolhelp pillanatkép (PID, szülő PID NT-korrekcióval, NT-útvonal) → `ST_PROCESS_DISCOVERY_*` blob (a referencia `proc.cpp` logikája C#-ban).
  - `ProcessWatcher` — `event Action<int,string> Started` WMI `Win32_ProcessStartTrace` alapján (PID + NT-útvonal).

- [ ] **Step 1:** Implementáció; a `BuildRegisterBlob` igazítása a Core `DriverStructs` segédeivel.
- [ ] **Step 2:** Kézi próba: egy új folyamat indulásakor a `Started` tüzel helyes NT-útvonallal (log). Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): folyamat-pillanatkep es -figyelo"
```

### Task 2.7: ConnectionTable

**Files:**
- Create: `src/VpnBypass.Service/Connections/ConnectionTable.cs`

**Interfaces:**
- Produces: `IReadOnlyDictionary<int, List<ConnectionSample>> ByPid()` — `GetExtendedTcpTable` + `GetExtendedUdpTable` (v4/v6), PID-enként a lokális címek + TCP-állapot + kor; a `ConnectionSample` a Core-ból.

- [ ] **Step 1:** Implementáció (P/Invoke iphlpapi, `TCP_TABLE_OWNER_PID_ALL`, `UDP_TABLE_OWNER_PID`).
- [ ] **Step 2:** Kézi próba: egy ismert PID kapcsolatai helyesen jelennek meg (log). Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): PID-enkenti kapcsolattabla"
```

### Task 2.8: DriverController (állapotgép-vezérlés)

**Files:**
- Create: `src/VpnBypass.Service/Driver/DriverController.cs`

**Interfaces:**
- Consumes: `SplitTunnelDevice`, `WfpProvider`, `ProcessSnapshot`, `DriverStructs`, `DriverState`.
- Produces:
  - `void Start()` — `WfpProvider.EnsureObjects`; `device.TryOpen`; `GetState`; ha nem `Started` → `Reset` (ZOMBIE → kivétel); `Initialize(Baseline,Dns)`; `RegisterProcesses(snapshot)`. Elindít egy esemény-szálat (`DequeueEvent` ciklus, naplóz).
  - `void ApplyConfig(IReadOnlyList<string> ntPaths)` — `SetConfiguration` vagy üresnél `ClearConfiguration`.
  - `void ApplyAddresses(IPAddress? tV4, IPAddress? iV4, IPAddress? tV6, IPAddress? iV6)` — `RegisterIpAddresses`.
  - `void Pause()` / `void Stop()` — `ClearConfiguration` / `Reset` (+ a WFP-objektumok megtartva; eltávolításkor külön `RemoveObjects` sikeres Reset után).
  - `DriverState State { get; }`.

- [ ] **Step 1:** Implementáció a Global Constraints állapotgépe szerint; a ZOMBIE-t külön jelzi (esemény vagy állapot).
- [ ] **Step 2:** Kézi próba a célgépen: Start → Initialize → Ready; config+ip → Engaged; egy kivett `curl` a saját IP-t látja. Dokumentáld (ez a Fázis 0 automatizált megfelelője).
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): driver allapotgep-vezerles"
```

### Task 2.9: PipeServer (named pipe + ACL)

**Files:**
- Create: `src/VpnBypass.Service/Ipc/PipeServer.cs`

**Interfaces:**
- Consumes: `IpcContract`, `IpcJson` (Core).
- Produces: `PipeServer(Func<IpcMessage, object> handler)`; `\\.\pipe\vpn-bypass`, `PipeSecurity`: SYSTEM + Administrators teljes, a config `UserSid` olvas/ír, más semmi; soronkénti JSON; több egyidejű kliens.

- [ ] **Step 1:** Implementáció `NamedPipeServerStream` + `PipeSecurity`; a `handler` a Worker-ből jön.
- [ ] **Step 2:** Kézi próba: egy kis kliens (vagy `powershell` pipe) kap `getStatus` választ; jogosulatlan felhasználó nem. Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(service): named pipe szerver ACL-lel"
```

### Task 2.10: Worker összekötés + ConfigStore

**Files:**
- Create: `src/VpnBypass.Service/ConfigStore.cs`
- Modify: `src/VpnBypass.Service/Worker.cs`

**Interfaces:**
- Consumes: az összes fenti Service-komponens + Core (`VpnDetector`, `RuleSet`, `ConnectionClassifier`).
- Produces: a teljes működés: induláskor config betöltés → `DriverController.Start`; `NetworkMonitor.Changed` → `VpnDetector.Detect` → `ApplyAddresses` vagy (nincs VPN) `ClearConfiguration`; `ProcessWatcher.Started` → minta-illesztés → szükség esetén `ApplyConfig`; 2 s-os időzítő → `ConnectionTable` + `ConnectionClassifier` → státusz-gyorsítótár; a pipe `handler` a kéréseket kiszolgálja és `setExcluded`/`setPaused`/`setInternetAdapter` esetén frissít + ment.

- [ ] **Step 1:** `ConfigStore` (olvas/ír `C:\ProgramData\vpn-bypass\config.json`, SYSTEM/Administrators ACL).
- [ ] **Step 2:** `Worker` összekötés a fentiek szerint; a `handler` leképezi a kéréseket.
- [ ] **Step 3:** Kézi, teljes próba a célgépen: a pipe-on `setExcluded` a Discordra → a Discord a saját IP-t látja; VPN ki/be → státusz követ. Dokumentáld.
- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(service): teljes eletciklus osszekotes"
```

---

## Fázis 3 – Tálcaalkalmazás (WPF)

### Task 3.1: Tray-projekt + PipeClient

**Files:**
- Create: `src/VpnBypass.Tray/VpnBypass.Tray.csproj`, `App.xaml(.cs)`, `PipeClient.cs`
- Modify: `VpnBypass.sln`

**Interfaces:**
- Consumes: `IpcContract`, `IpcJson` (Core).
- Produces: WPF app (`net10.0-windows`, `UseWPF`), egyetlen példány (mutex); `PipeClient` a `\\.\pipe\vpn-bypass`-ra, `Task<StatusResponse> GetStatusAsync()`, `Task<AppsResponse> GetAppsAsync()`, `Task SetExcludedAsync(string path, bool excluded)`, `Task SetPausedAsync(bool)`.

- [ ] **Step 1:** Projekt + `H.NotifyIcon.Wpf` csomag; referencia a Core-ra; add a sln-hez.
- [ ] **Step 2:** `PipeClient` implementáció.
- [ ] **Step 3:** Build — Run: `dotnet build src/VpnBypass.Tray` → PASS.
- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(tray): WPF vaz es pipe-kliens"
```

### Task 3.2: TrayIcon + állapotszín

**Files:**
- Create: `src/VpnBypass.Tray/TrayIcon.cs`
- Modify: `App.xaml.cs`

**Interfaces:**
- Produces: tálcaikon (zöld/szürke/sárga/piros a `StatusResponse.State`+`Paused`+`Error` alapján), tooltip (VPN- és saját adapter neve, kivételek száma), menü: Megnyitás / Szüneteltetés / Kilépés; 2 s-onként `GetStatusAsync` pollozás.

- [ ] **Step 1:** Implementáció; a Megnyitás a `MainWindow`-t mutatja.
- [ ] **Step 2:** Füstpróba: az ikon megjelenik, színt vált a Service állapotával (a Service fusson). Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(tray): allapotjelzo ikon es menu"
```

### Task 3.3: MainWindow – pipálós lista

**Files:**
- Create: `src/VpnBypass.Tray/MainWindow.xaml(.cs)`, `Theme/` (világos/sötét)

**Interfaces:**
- Consumes: `PipeClient`, `AppsResponse`.
- Produces: fejléc (VPN, saját háló, kivételek száma, Szüneteltetés gomb), lista: elöl a kipipáltak, utánuk a futó appok ábécérendben; checkbox azonnal `SetExcludedAsync`; státuszcímke appos színnel/szöveggel; „Tallózás…” nem futó `.exe`-hez; magyar szövegek; a Windows témáját követi.

- [ ] **Step 1:** XAML + nézetmodell; 2 s-os frissítés `GetAppsAsync`-kal.
- [ ] **Step 2:** Füstpróba a célgépen: a lista megjelenik, pipálás hat a Service-re, a státusz frissül. Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(tray): pipalos lista es statusz"
```

---

## Fázis 4 – Csomagolás, telepítő, eltávolító, végső tesztek

### Task 4.0: publish.ps1 (hordozható, self-contained csomag)

**Files:**
- Create: `scripts/publish.ps1`
- Modify: `src/VpnBypass.Service/VpnBypass.Service.csproj`, `src/VpnBypass.Tray/VpnBypass.Tray.csproj` (publish-tulajdonságok)

**Interfaces:**
- Produces: `dist/vpn-bypass/` hordozható mappa (opcionálisan `dist/vpn-bypass.zip`), amely tartalmazza a Service és a Tray self-contained kimenetét, a `scripts/` mappát (install/uninstall/fetch-driver) és az ellenőrzött `driver/`-t. A másik gépre EZT kell átmásolni; ott nincs SDK/forrás/build.

- [ ] **Step 1: Publish-tulajdonságok a két csproj-ba**

Mindkét alkalmazás-projektbe (Service, Tray) a `<PropertyGroup>`-ba:
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>false</PublishSingleFile>
```

- [ ] **Step 2: `publish.ps1`**

```powershell
# Hordozható, self-contained csomagot készít a dist/ alá a másik gépre.
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$dist = Join-Path $root 'dist\vpn-bypass'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null
dotnet publish (Join-Path $root 'src\VpnBypass.Service') -c Release -r win-x64 --self-contained -o (Join-Path $dist 'service')
dotnet publish (Join-Path $root 'src\VpnBypass.Tray')    -c Release -r win-x64 --self-contained -o (Join-Path $dist 'tray')
Copy-Item (Join-Path $root 'scripts') (Join-Path $dist 'scripts') -Recurse
Copy-Item (Join-Path $root 'driver')  (Join-Path $dist 'driver')  -Recurse -ErrorAction SilentlyContinue
Compress-Archive -Path "$dist\*" -DestinationPath (Join-Path $root 'dist\vpn-bypass.zip') -Force
Write-Host "Kesz: $dist (+ dist\vpn-bypass.zip)"
```

- [ ] **Step 3: Futtatás a fejlesztőgépen**

Run: `pwsh -File scripts/publish.ps1`
Expected: `dist/vpn-bypass/` létrejön service+tray+scripts+driver tartalommal; a service/tray mappában ott a `*.exe` és a bele csomagolt .NET-futtatókörnyezet.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(scripts): hordozhato self-contained csomagolas"
```

### Task 4.1: install.ps1

**Files:**
- Create: `scripts/install.ps1`

**Interfaces:**
- Produces: rendszergazdai telepítő, amely a **csomag mappájából** (a saját `$PSScriptRoot`-ja melletti `service/`, `tray/`, `driver/`) telepít, build nélkül: előfeltételek (x64, Win10 2004+, nincs Mullvad app; ha a `driver/` hiányzik/nem ellenőrzött → `fetch-driver.ps1`); a self-contained kimenet bemásolása `C:\Program Files\vpn-bypass\`-ba; driver telepítése (a Fázis 0-ban bevált módon: INF `pnputil` vagy `sc create ... type= kernel`); WFP objektumok a Service első indulásakor jönnek létre; `vpnbypass` szolgáltatás regisztráció (`sc create`, `start= auto`, `sc failure ... reset= 0 actions= restart/5000`); tálcaapp a HKCU `Run`-ba; a telepítő user SID-je a configba. Semmi gépspecifikus nincs beégetve — minden itt/futásidőben dől el, ezért a csomag bármely x64 Windows-gépen települ.

- [ ] **Step 1:** Szkript megírása; forrásként a csomag `service/`/`tray/`/`driver/` mappái (NEM `dotnet build`); a driver-telepítés a Fázis 0 tapasztalata szerint.
- [ ] **Step 2:** Futtatás a célgépen (a user, rendszergazdaként) a `dist` csomagból. Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(scripts): telepito a hordozhato csomagbol"
```

### Task 4.2: uninstall.ps1

**Files:**
- Create: `scripts/uninstall.ps1`

**Interfaces:**
- Produces: eltávolító: tálcaapp leállítása; `vpnbypass` szolgáltatás leállítása (a Service `Stop`-ja `Reset`-et küld) → állapot-ellenőrzés; csak `STARTED` esetén a driver-szolgáltatás leállítása/törlése + WFP-objektumok törlése; ha nem biztonságos (ZOMBIE/ENGAGED), a driver-eltávolítás a következő újraindításra halasztva (figyelmeztetéssel); fájlok törlése (a config megtartható kapcsolóval).

- [ ] **Step 1:** Szkript megírása.
- [ ] **Step 2:** Futtatás a célgépen; utána a NordVPN normálisan működik, nincs kékhalál. Dokumentáld.
- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(scripts): eltavolito"
```

### Task 4.3: Végső kézi teszt-mátrix

**Files:**
- Create: `docs/acceptance-tests.md`

- [ ] **Step 1:** A spec 11. fejezetének 1–11. pontja mint elfogadási lista; mindegyikhez eredmény. A user közreműködésével, NordVPN NordLynx és OpenVPN módban is.
- [ ] **Step 2: Commit**

```bash
git add docs/acceptance-tests.md
git commit -m "docs: elfogadasi tesztek eredmenye"
```

---

## Önellenőrzés (a terv ↔ spec)

- **Spec-lefedettség:** 3. felépítés → F1–F3; 4. driver → 1.8, 2.3, 2.8; 5. VPN-felismerés → 1.6, 1.7, 2.5; 6. szabályok → 1.2–1.5, 2.2, 2.6; 7. IPC → 1.10, 2.9; 8. felület → F3; 9. hibakezelés/biztonság → 2.8 (állapotgép, ZOMBIE), 2.9 (ACL), 4.2 (kékhalál-védelem); 10. telepítés → F4; 11. tesztelés → 1.* unit + 4.3. **Hiány nincs.**
- **Placeholder-ellenőrzés:** minden lépésnek konkrét tartalma van; a Service P/Invoke-rétegeknél szándékosan kézi próba a verifikáció (nincs „írj tesztet ide” felirat kód nélkül), mert ezek gép- és driverfüggők, és a Core-ba húzott logika tesztelt.
- **Típus-konzisztencia:** `DriverState`, `Ioctl`, `AppStatus`, `ConnectionSample`, `AdapterInfo`, `RouteInfo`, `ExclusionRule`, `IpcContract`-rekordok egységesen a Core-ban; a Service és a Tray csak hivatkozik rájuk.
