using Microsoft.Extensions.Logging;

namespace VpnBypass.Service.Logging;

// Minimális, függőség nélküli fájlnapló: napi fájl a C:\ProgramData\vpn-bypass\logs\ alá, 7 napig.
// Best-effort: ha az írás nem megy, csendben elnyeli (a napló sosem állíthatja le a szolgáltatást).
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory)
    {
        _dir = directory;
        try
        {
            System.IO.Directory.CreateDirectory(_dir);
            Cleanup();
        }
        catch { /* best-effort */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    internal void Write(string line)
    {
        try
        {
            var file = Path.Combine(_dir, $"vpn-bypass-{DateTime.Now:yyyyMMdd}.log");
            lock (_gate) File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* best-effort */ }
    }

    private void Cleanup()
    {
        var cutoff = DateTime.Now.AddDays(-7);
        foreach (var f in Directory.EnumerateFiles(_dir, "vpn-bypass-*.log"))
            try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); } catch { }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var shortCat = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel,-4}] {shortCat}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Write(line);
        }
    }
}
