using Microsoft.Extensions.Logging;
using VpnBypass.Core.Config;
using VpnBypass.Core.Ipc;

namespace VpnBypass.Service;

// A perzisztens konfig olvasása/írása: C:\ProgramData\vpn-bypass\config.json.
// Hibatűrő: hiányzó/sérült fájlnál üres konfiggal indul; az írás atomi (temp + csere).
public sealed class ConfigStore(ILogger<ConfigStore> log)
{
    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "vpn-bypass");

    private static string FilePath => Path.Combine(Directory, "config.json");
    private readonly object _gate = new();

    public AppConfig Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return AppConfig.Empty;
                SecureDirectory.ThrowIfFileReparsePoint(FilePath);
                var json = File.ReadAllText(FilePath);
                return IpcJson.DeserializeConfig(json) ?? AppConfig.Empty;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "A konfig nem olvasható ({Path}) – üres konfiggal indulok.", FilePath);
                return AppConfig.Empty;
            }
        }
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            SecureDirectory.Ensure(Directory);
            var tmp = FilePath + ".tmp";
            SecureDirectory.ThrowIfFileReparsePoint(tmp);
            SecureDirectory.ThrowIfFileReparsePoint(FilePath);
            File.WriteAllText(tmp, IpcJson.Serialize(config));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
