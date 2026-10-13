# vpn-bypass

Alkalmazásonkénti VPN-megkerülés (per-app split tunneling) Windowson, **bármilyen** VPN-nel.
A VPN marad az alapértelmezett út minden forgalomnak, de egy pipálós listán kijelölt
alkalmazások (és a gyerekfolyamataik) a fizikai hálózati adapterre kerülnek — így például a
hangchat vagy a játék a saját, megbízhatóbb hálózatodon mehet, miközben minden más a VPN-en.

A terelést a Mullvad nyílt forrású, Microsoft által aláírt `win-split-tunnel` kernel-driver
végzi; ez a projekt azt vezérli egy háttérszolgáltatásból, és egy tálcás felületet ad hozzá.

> **Állapot:** a teljes alkalmazás működik **dry-run** módban (felismerés, lista, beállítás,
> naplózás), de a *tényleges* forgalomterelést végző modul (`IDriverClient` éles megvalósítása:
> driver-IOCTL + WFP) még nincs beépítve. Részletek és befejezési útmutató: [`docs/STATUS.md`](docs/STATUS.md).

## Felépítés

- **`VpnBypass.Core`** – gépfüggetlen, egységtesztelt logika: minta-/szabálymotor, VPN-felismerés,
  driver-adatszerkezetek, állapotgép (`BypassEngine`), státusz-osztályozás, IPC-szerződés.
- **`VpnBypass.Service`** – SYSTEM-ként futó Windows-szolgáltatás: driver-vezérlés, named pipe,
  hálózat-/folyamat-/kapcsolatfigyelés, konfigkezelés (szigorú ACL-lel).
- **`VpnBypass.Tray`** – WPF tálcaalkalmazás a pipálós listával.
- **`scripts/`** – `fetch-driver.ps1` (driver letöltés + ellenőrzés), `publish.ps1`
  (hordozható, self-contained csomag), `install.ps1` / `uninstall.ps1`.

## Követelmények

- 64 bites (x64) Windows 10 2004+ / 11
- .NET 10 SDK (fejlesztéshez); a kiadott csomag self-contained, a célgépen nem kell SDK

## Build és futtatás

```powershell
dotnet test -c Release                 # Core egységtesztek
.\scripts\publish.ps1                  # hordozható csomag a dist\ alá
.\dist\vpn-bypass\scripts\install.ps1  # telepítés a célgépen (rendszergazda)
```

## Licenc

A projekt saját kódja MIT-licencű (lásd [`LICENSE`](LICENSE)). A Mullvad `win-split-tunnel`
driver nem része a repónak (a `fetch-driver.ps1` tölti le), és a Mullvad GPL-3.0 / MPL-2.0
feltételei vonatkoznak rá.
