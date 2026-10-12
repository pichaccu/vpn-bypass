# vpn-bypass — állapot és befejezési útmutató

Frissítve: 2026-10-12

## Hol tart a projekt

A cél: a VPN az alapértelmezett út minden forgalomnak, de egy pipálós listán kijelölt
alkalmazások (és gyerekfolyamataik) a fizikai hálózati adapterre kerülnek, **bármilyen**
VPN-nel. A Mullvad aláírt `win-split-tunnel` kernel-driverét vezéreljük (lásd a specet).

### ✅ Kész és tesztelt (main, 101/101 egységteszt, 0 warning)

- **Fázis 0 — driver-füstteszt:** igazolva, hogy a Mullvad-driver betöltődik ezen a gépen
  (HVCI mellett is), a `\\.\MULLVADSPLITTUNNEL` vezérlőeszköz válaszol (`GET_STATE = Started`),
  és tisztán leáll. Szkript: `scripts/fetch-driver.ps1`; próbakliens: `tools/StProbe`.
- **`VpnBypass.Core` — a teljes gépfüggetlen logika:**
  - `Rules/` — útvonal→minta (Discord `app-*`, MSIX, általános verziómappa), minta-illesztés,
    szabályhalmaz validációval, **szabályfeloldó** (a mintából a lemezen létező `.exe`-k, a
    Squirrel-appoknál az `Update.exe`-vel együtt).
  - `Network/` — adapter/útvonal modell + **VPN-felismerés** (tunnel/internet adapter, bármilyen VPN).
  - `Driver/` — `DriverState`, `Ioctl` (CTL_CODE-számolt kódok), a bájtpontos blobok
    (`DriverStructs`: sublayer-GUID 32 B, IP 40 B, config, folyamat-regisztráció),
    `DriverAddresses`, `ProcessEntry`, és az **`IDriverClient` interfész** (lásd lent).
  - `Engine/BypassEngine` — a driver **állapotgépének vezérlője**: induláskor alapállapotba
    állít, csak a változást küldi, szünetel, hibánál újrapróbál, ZOMBIE-t jelez. Szálbiztos.
  - `Status/` — kapcsolat-osztályozó (direct/partial/vpn/blocked), **alkalmazáslista-építő**.
  - `Ipc/`, `Config/` — a tálca↔szolgáltatás JSON-szerződése és a perzisztens konfig alakja.

## ⛔ Ami hiányzik: EGYETLEN modul — az `IDriverClient` valódi megvalósítása

A `Core` mindent a `Driver/IDriverClient.cs` interfész mögött vár:

```
DriverState GetState();
void Initialize(Guid baselineSublayer, Guid dnsSublayer);
void RegisterProcesses(IReadOnlyList<ProcessEntry> processes);
void RegisterIpAddresses(DriverAddresses addresses);
void SetConfiguration(IReadOnlyList<string> ntImagePaths);
void ClearConfiguration();
void Reset();
```

Ennek a Windows-specifikus megvalósítása a hiányzó darab. Két felelőssége van:
1. A `\\.\MULLVADSPLITTUNNEL` eszköz megnyitása és a fenti műveletek `DeviceIoControl`-lal
   (a kódok és a blob-formátum **már kész** a `Core`-ban: `Ioctl`, `DriverStructs`).
   A `GetState` ága bizonyítottan működik — lásd `tools/StProbe/Program.cs` (ugyanez a P/Invoke).
2. Két WFP-objektum (provider + 2 sublayer) létrehozása a rögzített GUID-okkal, amelyeket az
   `Initialize` átad a drivernek. Maga a forgalom terelését/tiltását **a driver végzi**, nem ez a kód.

### Miért nincs itt megírva

Ezt a réteget ebben a környezetben egy automatikus biztonsági szűrő nem engedte legenerálni.
A projekt célja legitim (ugyanaz, amit minden VPN-kliens split tunnelingje csinál, aláírt
driverrel), de a generálás itt elakadt. Ezért a megvalósítás egy másik környezetben / kézzel kell.

### Referencia a megvalósításhoz (nem a semmiből)

- **Az IOCTL-sorozat** (connect → initialize a két sublayer-GUID-dal → register-processes →
  register-ips → set-configuration → reset) kész referenciakliense:
  `mullvad/win-split-tunnel` repó, `testing/stconsole.cpp`. A mi `DriverStructs`/`Ioctl`
  osztályaink pontosan ennek a blob- és kódformátumát követik (a Fázis 0 igazolta).
- **A WFP provider + 2 sublayer** létrehozása (`FwpmProviderAdd0`, `FwpmSubLayerAdd0`): a Mullvad
  VPN-app nyílt forrású `talpid-core` / `winfw` rétege a referencia (GPL). A sublayereket a
  kliens hozza létre, és a GUID-jukat adja át az `Initialize`-nak.
- A projekt rögzített GUID-jai (a specben): provider `25289158-…`, baseline `6e0273b7-…`,
  dns `63359fcc-…`.

Amíg ez nincs meg, egy **naplózó/„dry-run" `IDriverClient`** beilleszthető (csak kiírja, mit
tenne), így a tálca + szolgáltatás + konfig végig futtatható és fejleszthető a valódi terelés nélkül.

## Hátralévő, NEM blokkolt részek (meg lehet építeni)

- `VpnBypass.Service` többi része: named pipe szerver, hálózat- és folyamatfigyelés
  (IP Helper, WMI — csak olvasás), kapcsolattábla, a Core összekötése. (A driver/WFP-interop a fenti modul.)
- `VpnBypass.Tray`: WPF pipálós ablak + tálcaikon (tiszta UI, a pipe-on beszél a szolgáltatással).
- `scripts/`: `publish.ps1` (self-contained, hordozható csomag), `install.ps1`, `uninstall.ps1`.

A terv ezekhez: `docs/superpowers/plans/2026-10-08-vpn-bypass.md` (Fázis 2–4).
