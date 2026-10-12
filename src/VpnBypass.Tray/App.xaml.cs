using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VpnBypass.Core.Driver;
using WinForms = System.Windows.Forms;

namespace VpnBypass.Tray;

public partial class App
{
    private Mutex? _singleInstance;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _window;
    private readonly PipeClient _pipe = new();
    private DispatcherTimer? _timer;
    private bool _paused;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\vpn-bypass-tray", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _window = new MainWindow(_pipe);

        _tray = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "vpn-bypass",
            Icon = MakeIcon(Color.Gray),
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Megnyitás", null, (_, _) => ShowWindow());
        var pauseItem = new WinForms.ToolStripMenuItem("Szüneteltetés") { CheckOnClick = true };
        pauseItem.Click += async (_, _) => { _paused = pauseItem.Checked; await _pipe.SetPausedAsync(_paused); await UpdateTrayAsync(); };
        menu.Items.Add(pauseItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Kilépés", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await UpdateTrayAsync();
        _timer.Start();

        ShowWindow();
        _ = UpdateTrayAsync();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task UpdateTrayAsync()
    {
        if (_tray is null) return;
        try
        {
            var status = await _pipe.GetStatusAsync();
            if (status is null)
            {
                _tray.Icon = MakeIcon(Color.Firebrick);
                _tray.Text = "vpn-bypass – a szolgáltatás nem elérhető";
                return;
            }

            _paused = status.Paused;
            Color color = status switch
            {
                { Paused: true } => Color.Goldenrod,
                { State: DriverState.Zombie } => Color.Firebrick,
                { Error: not null } => Color.Firebrick,
                { Vpn: not null, State: DriverState.Engaged } => Color.SeaGreen,
                { Vpn: not null } => Color.SeaGreen,
                _ => Color.Gray,
            };
            _tray.Icon = MakeIcon(color);
            _tray.Text = status.Vpn is null
                ? $"vpn-bypass – nincs VPN (kijárat: {status.Internet?.Name ?? "?"})"
                : $"vpn-bypass – VPN: {status.Vpn.Name}, {status.ExcludedCount} kivétel"
                  + (status.Paused ? " (szünet)" : "");
        }
        catch
        {
            _tray.Icon = MakeIcon(Color.Firebrick);
            _tray.Text = "vpn-bypass – a szolgáltatás nem elérhető";
        }
    }

    // Egyszerű, egyszínű tálcaikon az állapot jelzésére (nincs külső erőforrásfájl).
    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ExitApp()
    {
        _timer?.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }
}
