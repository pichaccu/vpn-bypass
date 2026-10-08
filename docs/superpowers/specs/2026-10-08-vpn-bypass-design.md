# vpn-bypass – tervezési dokumentum

- **Dátum:** 2026-10-08
- **Állapot:** jóváhagyva (a user a kész programon módosít, ha kell)
- **Repó:** `D:\dev\vpn-bypass` (helyi git; GitHubra csak kérésre)

## 1. Cél

Windowson a VPN marad az alapértelmezett út minden forgalomnak, de egy
**pipálós listán kiválasztott programok** (és az általuk indított folyamatok) a
**saját hálózaton** (fizikai Ethernet/Wi-Fi adapter) mennek ki. Bármilyen
VPN-klienssel működjön, amelyik ezt nem tiltja (NordVPN, OpenVPN, WireGuard stb.).

### Hatókörön belül

- Programonkénti kivétel TCP-re és UDP-re (hang, játék is), a gyerekfolyamatokra öröklődve.
- VPN-független felismerés: melyik adapter a VPN, melyik a saját háló.
- A frissítéskor verziószámos mappába költöző appok követése (Discord, MSIX/Store-appok).
- Tálcaikon és pipálós ablak, élő ellenőrzéssel.
- Telepítő és eltávolító szkriptek.
- **Hordozhatóság:** a user több Windows-gépén (x64) is telepíthető legyen. Ehhez
  **önálló (self-contained) csomag**: a .NET-futtatókörnyezetet is tartalmazza, így a
  célgépen nem kell SDK/Visual Studio/forráskód. Minden gépspecifikus érték (hálózati
  adapter, felhasználói SID, útvonalak) **telepítéskor vagy futásidőben** dől el, semmi
  sincs a binárisba égetve. A driver a célgépen töltődik le és ellenőrződik.

### Hatókörön kívül

- Céges VPN-ek által kényszerített „tunnel-all” kijátszása (pl. Cisco Secure Client).
  Nem cél: más programok tűzfal-tiltásait nem írjuk felül.
- Útvonaltábla-módosítás; a helyi háló elérése a nem kipipált appoknak.
- DNS-kivétel: a névfeloldás a VPN-en marad (lásd 9. fejezet).
- Fordított mód („csak a kiválasztott appok menjenek VPN-en”).
- ARM64, Windows 10 2004 előtti rendszerek, saját driver írása vagy aláírása.

## 2. Döntések

| Döntés | Indoklás |
|---|---|
| A Mullvad `win-split-tunnel` driver **1.3.0.0** aláírt binárisa, változatlanul | VPN-független, folyamatfa-öröklés, TCP+UDP, nem injektál a programokba; 1.3.0.0-tól a WFP-sublayer GUID-okat a kliens adja át, így nem kell a Mullvad tűzfala |
| Forrás: `mullvad/mullvadvpn-app-binaries`, commit `5b6f46cde692` („Update binaries for win-split-tunnel to 1.3.0.0”), `x86_64-pc-windows-msvc/split-tunnel/` | Hivatalos, aláírt csomag; a driver forrása ugyanott submodule-ként: `win-split-tunnel@0a0eb97f67d1` |
| C# / .NET 10 | Megvan a gépen; WPF a felülethez, Windows-szolgáltatás a háttérhez |
| Elvetve: WinDivert-alapú címfordítás | saját NAT-hibák, késleltetés, bejelentett EasyAntiCheat-ütközés |
| Elvetve: proxy alapú (driver nélkül) | hang és játék nem megy át proxyn |
| Elvetve: DLL-injektálás, saját driver | vírusirtó-riasztás és ban-kockázat; aláírás nélkül nem tölthető be, a test-signing mód letiltaná az anti-cheatet |
| A NordVPN saját split tunnelingje kikapcsolva marad, Mullvad app nem lehet fent | két split-tunnel driver ütközne; a Mullvad-eszköz exkluzív |
| Self-contained `dotnet publish` (RID `win-x64`), hordozható csomag + `publish.ps1` | másik gépen ne kelljen SDK; a saját exe-k nincsenek saját tanúsítvánnyal aláírva → első indításkor SmartScreen-figyelmeztetés lehet (a driver a Mullvad-aláírással viszont rendben betölt) |

Mért tények a fejlesztőgépen (2026-10-08): Windows 11 Pro 26200, x64, **HVCI be van
kapcsolva** (a drivernek HVCI-kompatibilisnek kell lennie); a NordLynx, a TAP-NordVPN
és az OpenVPN DCO adapter `HardwareInterface = False` (IfType 53), az Ethernet és a
Wi-Fi `HardwareInterface = True`.

## 3. Felépítés

```
[VpnBypass.Tray]  --named pipe (JSON sorok)-->  [VpnBypass.Service (SYSTEM)]
  (a user nevében)                                 |-- DeviceIoControl --> mullvad-split-tunnel.sys
                                                   |-- WFP: provider + 2 sublayer (perzisztens)
                                                   |-- IP Helper: adapterek, útvonalak, változásértesítés
                                                   |-- WMI: Win32_ProcessStartTrace (folyamatindulás)
                                                   '-- C:\ProgramData\vpn-bypass\ (config, napló)
```

```
vpn-bypass/
  src/VpnBypass.Core/          tiszta logika: config, szabályok/minták, adapterválasztás,
                               státusz-osztályozás, IPC-szerződés, IOCTL-struktúrák szerializálása
  src/VpnBypass.Service/       Windows-szolgáltatás: driver, WFP, hálózat- és folyamatfigyelés, pipe-szerver
  src/VpnBypass.Tray/          WPF tálcaalkalmazás
  tests/VpnBypass.Core.Tests/  xUnit
  scripts/                     fetch-driver.ps1, install.ps1, uninstall.ps1
  driver/SHA256SUMS            a rögzített driverfájlok hash-e (maguk a fájlok .gitignore-ban)
  docs/
```

A `Core` nem hív Windows API-t; minden, ami a gépet érinti, a `Service`-ben vagy a
`Tray`-ben van, vékony interfészek mögött, hogy a logika gép nélkül tesztelhető legyen.

## 4. A driver vezérlése

- **Eszköz:** `\\.\MULLVADSPLITTUNNEL`; exkluzív, csak SYSTEM és rendszergazda nyithatja.
- **Állapotok:** `STARTED → INITIALIZED → READY → ENGAGED`, hiba esetén `ZOMBIE`.
  - `IOCTL_ST_INITIALIZE` (a két sublayer GUID-dal: Baseline, Dns) → `INITIALIZED`
  - `IOCTL_ST_REGISTER_PROCESSES` (folyamat-pillanatkép) → `READY`
  - `IOCTL_ST_REGISTER_IP_ADDRESSES` + `IOCTL_ST_SET_CONFIGURATION` → `ENGAGED`
    (ha van kivétel és érvényes tunnel+internet címpár)
  - `IOCTL_ST_CLEAR_CONFIGURATION` → vissza `READY`
  - `IOCTL_ST_RESET` → `STARTED` (sikertelenségnél `ZOMBIE`, csak újraindítás segít)
- **A handle bezárása NEM állítja vissza a drivert** – a beállítások a `RESET`-ig vagy a
  driver kirakásáig élnek. Ezért a szolgáltatás indításkor `IOCTL_ST_GET_STATE`-tel
  kezd, és ha nem `STARTED`, előbb `RESET`-et küld.
- **Kirakás csak `STARTED` állapotban:** ha a driver más állapotban rakódik ki, a saját
  `Reset`-jét hívja, és annak hibájára kékhalált dob.
- **Események:** külön szál `IOCTL_ST_DEQUEUE_EVENT`-tel (inverted call), naplózásra.
- **Folyamat-pillanatkép:** PID, szülő PID, létrehozási idő, image NT-eszközútvonala.
- **Útvonalak:** a meghajtóbetűs útvonalat NT-eszközútvonallá alakítjuk (`QueryDosDevice`).
- **Címek:** tunnel IPv4/IPv6 és internet IPv4/IPv6 (ami létezik).
- **Bájtpontos struktúrák** a `win-split-tunnel@0a0eb97` `src/public.h` és `src/defs/*.h`
  fájljai alapján; referencia-kliens a `testing/stconsole`.
- **WFP:** saját provider és két sublayer (Baseline, Dns), **perzisztens**, telepítéskor
  jön létre, **alacsony súllyal** – így más programok (pl. VPN kill switch) tiltásai
  nyernek. Csak sikeres `RESET` után törölhetők (a driver szűrői hivatkoznak rájuk).

## 5. VPN-felismerés (bármilyen VPN-nel)

1. Lekérdezés: interfészek (`GetIfTable2`), útvonalak (`GetIpForwardTable2`), címek
   (`GetUnicastIpAddressTable`), IPv4-re és IPv6-ra.
2. **Internet-útvonal nyertese:** a `0.0.0.0/0` és az OpenVPN-féle `0.0.0.0/1` +
   `128.0.0.0/1` pár (IPv6-nál `::/0`, `::/1` + `8000::/1`) közül a leghosszabb prefix,
   azon belül a legkisebb effektív metrika (útvonal-metrika + interfész-metrika) nyer.
3. Ha a nyertes interfész **nem hardveres** (`HardwareInterface == false`), az a
   **tunnel**. Ha hardveres, **nincs VPN** → a driver `READY` marad (kivétel nincs érvényben).
4. **Internet-adapter:** a hardveres, működő interfészek közül az, amelyiknek van
   alapértelmezett útvonala, a legkisebb effektív metrikával. A Hyper-V/WSL/Docker
   `vEthernet` adapterek ki vannak zárva. Kézi felülírás: `internetAdapter` a configban.
5. **Változásfigyelés:** `NotifyIpInterfaceChange`, `NotifyRouteChange2`,
   `NotifyUnicastIpAddressChange` → 1 s csillapítás → újraszámolás → ha változott, új
   címek a drivernek (vagy `CLEAR_CONFIGURATION`, ha megszűnt a VPN).

## 6. Kivétel-szabályok

- **Szabály** = konkrét `.exe` útvonal **vagy minta**. Ha az útvonal valamelyik
  mappanevében verzió van, azt a szegmenst mintává alakítjuk:
  - Squirrel-appok: `app-1.0.9260` → `app-*` (Discord, Slack stb.);
  - MSIX/Store: `Claude_2.26454.2.0_x64__<publisherId>` → `Claude_*_x64__<publisherId>`;
  - általános: verziószámú mappanév (`\d+(\.\d+){1,3}`) → `*`.
- **Squirrel-öröklés:** ha a minta `app-*` mappát tartalmaz, és a szülőmappában
  `Update.exe` van, az is a szabály része → az új verziót indító folyamat már kivétel.
- **Feloldás:** induláskor, config-változáskor és **folyamatinduláskor**
  (`Win32_ProcessStartTrace`): ha egy induló image illeszkedik egy mintára, és még
  nincs a driver listáján, felvesszük és `SET_CONFIGURATION` – a driver a már futó
  folyamatra is alkalmazza (a VPN-es kapcsolatait lezárja, az app újracsatlakozik).
- **Validáció:** csak létező `.exe` (minta esetén legalább egy illeszkedő fájl a
  felvételkor), legfeljebb 256 szabály.

## 7. IPC (tálcaapp ↔ szolgáltatás)

- Named pipe: `\\.\pipe\vpn-bypass`, üzenetenként egy JSON-sor (UTF-8).
- **ACL:** SYSTEM és Administrators: teljes; a telepítő user SID-je: olvasás/írás;
  mindenki más: semmi.
- Kérések:
  - `getStatus` → `{ state, paused, vpn: {name, ipv4, ipv6} | null, internet: {...} | null, excludedCount, error }`
  - `getApps` → `[{ ruleId, displayName, pattern, matchedPaths, running, status }]`,
    ahol `status` ∈ `direct | partial | vpn | blocked | notRunning | notFound`
  - `setExcluded { path, excluded }` – a minta-képzés a szolgáltatásban történik
  - `setPaused { paused }`
  - `setInternetAdapter { name | null }`
- Push: `statusChanged` esemény, ha az állapot változik.

## 8. Felület (VpnBypass.Tray)

- **Tálcaikon:** zöld = VPN van és a kivételek élnek; szürke = nincs VPN (készenlét);
  sárga = szüneteltetve; piros = hiba. Tooltip: VPN-adapter, saját adapter, kivételek száma.
  Jobb klikk: Megnyitás / Szüneteltetés / Kilépés (csak az ikon lép ki).
- **Ablak:**

```
vpn-bypass
● VPN: NordLynx  ·  saját háló: Ethernet 3  ·  3 kivétel      [Szüneteltetés]
[ Keresés…                                   ]                 [Tallózás…]
☑  Discord (minden verzió)     saját hálón ✓
☑  TeamSpeak 3                 nem fut
☑  Claude (minden verzió)      részben VPN-en – indítsd újra
☐  Google Chrome               VPN-en
```

- Elöl a kipipáltak, utánuk a futó programok ábécérendben; `C:\Windows\` alatti
  folyamatok rejtve; egy program = egy sor (több példány esetén is).
- A pipálás azonnal érvényes (nincs Mentés gomb).
- **Élő ellenőrzés** (2 s-onként, a szolgáltatás számolja `GetExtendedTcpTable` /
  `GetExtendedUdpTable` alapján, PID szerint):
  - minden kapcsolat lokális címe az internet-adapteré → „saját hálón ✓” (`direct`)
  - vegyes → „részben VPN-en – indítsd újra” (`partial`)
  - csak tunnel-cím → „VPN-en” (`vpn`)
  - a fizikai címről induló TCP-kapcsolat > 5 s-ig `SYN_SENT`, és nincs `ESTABLISHED`
    → „blokkolva – VPN kill switch?” (`blocked`)
- Tallózás: nem futó `.exe` felvétele. Eltűnt program: „nem található”, kivehető.
- Magyar felület, a Windows világos/sötét témáját követi (WPF `ThemeMode="System"`).

## 9. Hibakezelés és biztonság

Alapelv: hiba esetén minden a VPN-en menjen, és semmi ne maradjon félkész állapotban.

- **Szolgáltatás-összeomlás:** a Windows 5 s után újraindítja (helyreállítási beállítás,
  3×); induláskor `GET_STATE` → szükség esetén `RESET` → teljes újraépítés.
  `ZOMBIE` → piros ikon, „Újraindítás szükséges”.
- **Szüneteltetés és leállítás:** `CLEAR_CONFIGURATION` (szünet) / `RESET` (leállítás).
- **Kékhalál-védelem:** a driver-szolgáltatást csak `STARTED` állapotban állítjuk le;
  ha ez nem biztosítható, az eltávolító a gép újraindítása utánra halasztja.
- **Más tiltások:** alacsony súlyú sublayerek, a kill switch nyer; a felület ezt
  `blocked` státusszal jelzi.
- **Ütközések:** foglalt eszköz (pl. Mullvad app) → érthető hibaüzenet; a NordVPN
  split tunnelingjét a dokumentáció kikapcsolásra kéri.
- **Jogosultság:** pipe-ACL (7. fejezet); a szolgáltatás csak létező `.exe`-t fogad el.
  Ugyanaz a bizalmi szint, mint a VPN-kliensek saját listájánál: ami a user nevében
  fut, kérhet kivételt. A config (`C:\ProgramData\vpn-bypass\config.json`) írása csak
  SYSTEM/Administrators joggal.
- **Napló:** `C:\ProgramData\vpn-bypass\logs\`, napi fájl, 7 napig; hibák a Windows
  eseménynaplóba is.
- **Ismert korlát – DNS:** a névfeloldás a Windows közös DNS-szolgáltatásán át megy,
  ezért a kipipált appoknál is a VPN-en. A forgalom maga a saját hálón halad; a
  VPN-szolgáltató csak a keresett szervernevet látja.

## 10. Telepítés és eltávolítás

- **`scripts/fetch-driver.ps1`:** a rögzített commitról letölti a `.sys`, `.inf`, `.cat`
  fájlt a `driver/` mappába; SHA-256 egyezés a `driver/SHA256SUMS`-szal; Authenticode-
  ellenőrzés (`Get-AuthenticodeSignature`). Letöltés csak a user engedélyével.
- **`scripts/publish.ps1`** (a fejlesztőgépen, egyszer): `dotnet publish -c Release -r win-x64
  --self-contained` a Service-re és a Tray-re, majd a kimenet + a `scripts/` + az ellenőrzött
  `driver/` egy hordozható `dist/vpn-bypass/` mappába (opcionálisan zip). Ezt a mappát kell a
  másik gépre átmásolni — ott nincs szükség SDK-ra, forrásra, sem buildre.
- **`scripts/install.ps1`** (a célgépen, rendszergazdaként, a user futtatja a `dist` mappából):
  1. Előfeltételek: x64, Windows 10 2004+ / 11, nincs Mullvad app; ha a `driver/` hiányzik vagy
     nem ellenőrzött, lefuttatja a `fetch-driver.ps1`-t.
  2. A self-contained kimenet bemásolása `C:\Program Files\vpn-bypass\`-ba (service, tray, driver).
  3. Driver telepítése (INF-ből vagy SCM kernel-szolgáltatásként – a 0. fázis dönti el).
  4. WFP provider + 2 sublayer létrehozása (perzisztens, alacsony súly).
  5. `VpnBypass.Service` regisztrálása: automatikus indulás, helyreállítás: újraindítás 5 s után.
  6. Tálcaapp automatikus indítása (HKCU `Run`), a telepítő user SID-jének mentése a configba.
  A gépspecifikus értékek (adapter, SID) itt és futásidőben dőlnek el, nem a csomagban.
- **`scripts/uninstall.ps1`:** tálcaapp leállítása → szolgáltatás leállítása (`RESET`)
  → állapot-ellenőrzés → driver-szolgáltatás leállítása és törlése (csak `STARTED`
  állapotban) → WFP-objektumok törlése → fájlok törlése (a config megtartható).
- Az első telepítés előtt **rendszer-visszaállítási pont** (a user készíti).

## 11. Tesztelés

- **Unit (xUnit, `VpnBypass.Core.Tests`):**
  - adapterválasztás fixture-ökkel: NordLynx, OpenVPN TAP/DCO (`/1`-es útvonalpár),
    WireGuard, nincs VPN, Ethernet + Wi-Fi, `vEthernet` jelenléte, kézi felülírás;
  - mintaképzés és -illesztés: Discord `app-*`, MSIX, általános verziómappa, verzió nélküli útvonal;
  - IOCTL-struktúrák bájtpontos szerializálása a driver fejlécei alapján;
  - config-validáció és -mentés; élő státusz-osztályozás;
  - állapotgép hamis driverrel: induláskori `RESET`, szünet, VPN eltűnése, `ZOMBIE`.
- **Kézi, ezen a gépen, NordVPN-nel (NordLynx és OpenVPN):**
  1. VPN nélkül: semmi nem változik, ikon szürke.
  2. VPN-nel: egy kipipált `curl`-másolat a saját publikus IP-t látja, a sima `curl` a NordVPN-ét.
  3. Discord-hívás működik, UDP-kapcsolatai a fizikai címről mennek.
  4. A kipipált app eléri a helyi hálót (Pi `192.168.0.129:3000`).
  5. Szerverváltás / újracsatlakozás / protokollváltás után magától újra érvényes.
  6. Szüneteltetés → minden a VPN-en; a szolgáltatás kilövése után automatikus helyreállás.
  7. Discord- és MSIX-frissítés (új verziómappa) után is kivétel marad.
  8. Gép-újraindítás után minden magától indul.
  9. Futó Docker Desktop/WSL mellett is helyes a felismerés.
  10. Egy anti-cheates játék elindul betöltött driver mellett.
  11. Eltávolítás tisztán, kékhalál nélkül; utána a NordVPN normálisan működik.

## 12. Megvalósítási sorrend

0. **Kockázatcsökkentő próba:** driver letöltése és ellenőrzése, INF vizsgálata,
   telepítés, konzolos próbakliens: egy `curl`-másolat kivétele NordVPN mellett.
1. `Core` + unit tesztek.
2. `Service`: driver-réteg, WFP, hálózatfigyelés, folyamatfigyelés, pipe-szerver.
3. `Tray`.
4. Telepítő/eltávolító, végső kézi tesztek.

## 13. Licenc

Saját kód: MIT (publikáláskor átgondolandó). A driver (GPL-3.0 / MPL-2.0) nem része a
repónak, a `fetch-driver.ps1` tölti le. Az IOCTL-definíciók C# portja a driver MPL-2.0
fejlécei alapján készül; a fájl fejléce ezt jelzi.
