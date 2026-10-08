# Fázis 0 – kézi driver-füstteszt

**Cél:** még a Fázis 2 kódolása előtt bizonyosság, hogy a Mullvad split-tunnel driver
**betöltődik ezen a gépen** (HVCI bekapcsolva, x64), a **vezérlőeszköze elérhető**, és
**válaszol** az állapotlekérdezésre. A teljes „curl a saját IP-t látja" funkcionális
próba a Fázis 2 végén jön (akkor már létezik a WFP-réteg és a teljes vezérlés) — lásd a
terv Task 2.8-at.

**Miért biztonságos ez a füstteszt:** csak megnyitjuk az eszközt és lekérünk egy állapotot
(`IOCTL_ST_GET_STATE`). **Nem** inicializálunk, **nem** állítunk be szűrőt, a driver végig
alapállapotban (`Started`) marad — így a leállítás is tiszta, kékhalál nem várható.

## Előfeltételek

- x64, Windows 10 2004+ / 11 (ez a gép: 11 Pro 26200 ✔).
- **Rendszergazdai** PowerShell a 2–4. lépéshez.
- A **Mullvad VPN app NE legyen telepítve** (foglalná az exkluzív eszközt). A NordVPN maradhat.
- **Rendszer-visszaállítási pont** (0. lépés).

## 0. Rendszer-visszaállítási pont (admin)

```powershell
Checkpoint-Computer -Description "vpn-bypass Fazis 0" -RestorePointType MODIFY_SETTINGS
```

Ha hibát ad, hogy a Rendszervédelem ki van kapcsolva a C:-n, előbb kapcsold be:
Gépház → Rendszer → Névjegy → Speciális rendszerbeállítások → Rendszervédelem → C: → Bekapcsolás.

## 1. Driver letöltése + ellenőrzése (nem kell admin)

```powershell
pwsh -File scripts\fetch-driver.ps1
```

(Ha nincs `pwsh`, akkor `powershell -ExecutionPolicy Bypass -File scripts\fetch-driver.ps1`.)
Elvárt: a 3 fájl a `driver\` mappában, az Authenticode-állapot **Valid** (aláíró: Mullvad),
és első futáskor kiírja + rögzíti a SHA-256 hash-eket a `driver\SHA256SUMS`-ba.
Ezt a fájlt **commitold be** (`git add driver/SHA256SUMS`).

## 2. Driver betöltése (admin)

```powershell
$sys = (Resolve-Path .\driver\mullvad-split-tunnel.sys).Path
sc.exe create vpnbypass-sttest type= kernel start= demand binPath= "$sys"
sc.exe start  vpnbypass-sttest
```

Elvárt: a `start` kimenetében `STATE : 4 RUNNING`.
- Ha a `start` **1275 / 577 / aláírási** hibát ad → a driver az aláírás/HVCI miatt nem tölthető be
  ezen a gépen; **állj meg és másold ide a hibát** (ez pont a kockázat, amit mérünk).
- `sc` helyett MINDIG `sc.exe` (a `sc` a PowerShellben a Set-Content aliasa).

## 3. Füstteszt-kliens (admin)

```powershell
.\tools\StProbe\bin\Release\net10.0-windows\win-x64\st-probe.exe
```

(Ha nincs lefordítva: `dotnet build tools\StProbe\StProbe.csproj -c Release` előbb.)
Elvárt kimenet:

```
Driver állapota: Started (1)
SIKER: a driver betöltött és a vezérlőeszköz válaszol. Alapállapot (Started). ✔
```

A kilépési kód 0, ha `Started`.

## 4. Lebontás (admin)

```powershell
sc.exe stop   vpnbypass-sttest
sc.exe delete vpnbypass-sttest
```

Elvárt: tiszta leállás, kékhalál nélkül. (A tesztszolgáltatás eldobható; a végleges
telepítő saját `vpnbypass` szolgáltatást hoz létre a Fázis 4-ben.)

## Siker-kritérium

- 2. lépés: `RUNNING`.
- 3. lépés: `Started`, kilépési kód 0.
- 4. lépés: tiszta `stop` + `delete`, nincs BSOD.

Ha mindez teljesül → a driver használható ezen a gépen, mehet a **Fázis 2**. Másold ide a
2–4. lépés kimenetét (vagy a hibát), és folytatjuk.
