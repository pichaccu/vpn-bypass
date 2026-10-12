using System.ComponentModel;
using VpnBypass.Core.Ipc;
using VpnBypass.Core.Status;

namespace VpnBypass.Tray;

// A lista egy sora. Az Excluded kétirányú kötésben a checkboxszal; a Suppress jelzi, amikor a
// frissítés programból állítja (ne váltson ki setExcluded kérést).
public sealed class AppRow : INotifyPropertyChanged
{
    public required string RuleId { get; init; }
    public required string Name { get; init; }

    private bool _excluded;
    public bool Excluded
    {
        get => _excluded;
        set { if (_excluded != value) { _excluded = value; OnChanged(nameof(Excluded)); } }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnChanged(nameof(StatusText)); } }
    }

    public bool Suppress { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public static string StatusLabel(AppStatus s, bool running) => s switch
    {
        AppStatus.Direct => "saját hálón ✓",
        AppStatus.Partial => "részben VPN-en – indítsd újra",
        AppStatus.Vpn => "VPN-en",
        AppStatus.Blocked => "blokkolva – VPN kill switch?",
        AppStatus.Idle => "fut",
        AppStatus.NotRunning => "nem fut",
        AppStatus.NotFound => "nem található",
        _ => "",
    };
}
