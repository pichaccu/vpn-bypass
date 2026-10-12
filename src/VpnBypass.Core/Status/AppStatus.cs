namespace VpnBypass.Core.Status;

// Egy alkalmazás pillanatnyi forgalmi státusza a felületen.
public enum AppStatus
{
    Direct,     // minden kapcsolata a fizikai hálón megy – a kivétel él
    Partial,    // vegyes: van még VPN-es kapcsolata is – indítsd újra
    Vpn,        // csak a VPN-en megy (nincs kivéve, vagy nem lépett életbe)
    Blocked,    // a fizikai címről indított kapcsolat beragadt – vélhetően a VPN kill switch blokkol
    NotRunning, // nincs osztályozható (külső) kapcsolata
    NotFound,   // a program útvonala már nem létezik
    Idle,       // fut, de most nincs külső kapcsolata (nem lehet megmondani, merre menne)
}
