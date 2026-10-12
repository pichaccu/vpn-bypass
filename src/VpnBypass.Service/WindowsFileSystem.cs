using VpnBypass.Core.Rules;

namespace VpnBypass.Service;

// A RuleResolver IFileSystem-adaptere a valódi lemezhez. Hibatűrő: nem olvasható mappánál üres lista.
public sealed class WindowsFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> GetSubdirectoryNames(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Select(Path.GetFileName)!.Where(n => n is not null)!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
    }
}
