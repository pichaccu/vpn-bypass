using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VpnBypass.Core.Ipc;
using VpnBypass.Core.Status;

// A WinForms és a WPF ugyanazon neveit a WPF-változatra egyértelműsítjük (ez egy WPF-ablak).
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VpnBypass.Tray;

public partial class MainWindow : Window
{
    private readonly PipeClient _pipe;
    private readonly ObservableCollection<AppRow> _rows = new();
    private readonly DispatcherTimer _timer;
    private bool _paused;

    public MainWindow(PipeClient pipe)
    {
        InitializeComponent();
        _pipe = pipe;
        AppList.ItemsSource = _rows;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _timer.Start();
        _ = RefreshAsync();
    }

    // Bezárás helyett csak elrejtjük (a tálcaikonról újra előhozható).
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private async Task RefreshAsync()
    {
        StatusResponse? status;
        AppsResponse? apps;
        try
        {
            status = await _pipe.GetStatusAsync();
            apps = await _pipe.GetAppsAsync();
        }
        catch
        {
            StatusText.Text = "A szolgáltatás nem elérhető.";
            StatusDot.Foreground = Brushes.Firebrick;
            return;
        }

        if (status is not null)
        {
            _paused = status.Paused;
            PauseButton.Content = _paused ? "Folytatás" : "Szüneteltetés";
            StatusDot.Foreground = status switch
            {
                { Paused: true } => Brushes.Goldenrod,
                { Error: not null } => Brushes.Firebrick,
                { Vpn: not null } => Brushes.SeaGreen,
                _ => Brushes.Gray,
            };
            StatusText.Text = status.Vpn is null
                ? $"Nincs aktív VPN. Saját háló: {status.Internet?.Name ?? "?"}. {status.ExcludedCount} kivétel."
                : $"VPN: {status.Vpn.Name}  ·  saját háló: {status.Internet?.Name ?? "?"}  ·  {status.ExcludedCount} kivétel"
                  + (status.Paused ? "  ·  SZÜNET" : "");
            if (status.Error is not null) StatusText.Text += $"  ·  hiba: {status.Error}";
        }

        if (apps is not null) Merge(apps.Apps);
    }

    // Finom frissítés: a meglévő sorokat módosítjuk (nem építjük újra a listát minden 2 mp-ben,
    // hogy a görgetés és a kattintás ne ugráljon).
    private void Merge(IReadOnlyList<AppEntry> entries)
    {
        var byId = _rows.ToDictionary(r => r.RuleId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int index = 0;
        foreach (var e in entries)
        {
            seen.Add(e.RuleId);
            var label = AppRow.StatusLabel(e.Status, e.Running);
            if (byId.TryGetValue(e.RuleId, out var row))
            {
                row.Suppress = true;
                row.Excluded = e.Pattern is not null;
                row.Suppress = false;
                row.StatusText = label;
                int cur = _rows.IndexOf(row);
                if (cur != index) _rows.Move(cur, index);
            }
            else
            {
                var newRow = new AppRow { RuleId = e.RuleId, Name = e.DisplayName, Suppress = true, Excluded = e.Pattern is not null, StatusText = label };
                newRow.Suppress = false;
                _rows.Insert(Math.Min(index, _rows.Count), newRow);
            }
            index++;
        }

        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!seen.Contains(_rows[i].RuleId)) _rows.RemoveAt(i);
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: AppRow row } cb) return;
        if (row.Suppress) return;
        bool excluded = cb.IsChecked == true;

        var result = await _pipe.SetExcludedAsync(row.RuleId, excluded);
        if (result is { Ok: false })
        {
            MessageBox.Show(this, result.Error ?? "A művelet nem sikerült.", "vpn-bypass",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await RefreshAsync();
    }

    private async void OnPauseClick(object sender, RoutedEventArgs e)
    {
        await _pipe.SetPausedAsync(!_paused);
        await RefreshAsync();
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Programok (*.exe)|*.exe", Title = "Válassz programot a kivételhez" };
        if (dlg.ShowDialog(this) == true)
        {
            var result = await _pipe.SetExcludedAsync(dlg.FileName, true);
            if (result is { Ok: false })
                MessageBox.Show(this, result.Error ?? "Nem sikerült hozzáadni.", "vpn-bypass",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshAsync();
        }
    }
}
