using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using VpnBypass.Core.Driver;
using VpnBypass.Core.Engine;
using VpnBypass.Core.Rules;
using VpnBypass.Service;
using VpnBypass.Service.Driver;
using VpnBypass.Service.Interop;
using VpnBypass.Service.Logging;

// Konzol módban (kézi indítás) a magyar szöveg helyes megjelenéséhez.
if (!WindowsServiceHelpers.IsWindowsService())
    try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* best-effort */ }

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "vpnbypass");

// Napló: fájlba (szolgáltatásként nincs konzol); konzolra csak ha kézből indítják.
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(ConfigStore.Directory, "logs")));
if (!WindowsServiceHelpers.IsWindowsService())
    builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

builder.Services.AddSingleton<PathResolver>();
builder.Services.AddSingleton<ProcessSnapshot>();
builder.Services.AddSingleton<IFileSystem, WindowsFileSystem>();
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<IDriverClient, DryRunDriverClient>();

builder.Services.AddSingleton(sp =>
{
    var snapshot = sp.GetRequiredService<ProcessSnapshot>();
    return new BypassEngine(
        sp.GetRequiredService<IDriverClient>(),
        snapshot.Capture,
        DriverConstants.BaselineSublayer,
        DriverConstants.DnsSublayer);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
