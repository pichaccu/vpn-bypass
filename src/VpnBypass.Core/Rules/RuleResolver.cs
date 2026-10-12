namespace VpnBypass.Core.Rules;

// Fájlrendszer-absztrakció a szabályfeloldáshoz, hogy a logika gép nélkül tesztelhető legyen.
public interface IFileSystem
{
    bool FileExists(string path);

    // A mappa közvetlen almappáinak NEVEI (nem teljes útvonal); üres, ha nem létezik / nem olvasható.
    IEnumerable<string> GetSubdirectoryNames(string path);
}

// Egy kivétel-minta feloldása a lemezen ténylegesen létező .exe-kre (a driver pontos útvonalat kér).
// Szegmensenként halad: a "*"-os mappaszegmensnél felsorolja az almappákat és illeszt.
public static class RuleResolver
{
    public static IReadOnlyList<string> Resolve(string pattern, IFileSystem fs)
    {
        var segments = pattern.Split('\\');
        if (segments.Length < 2) return Array.Empty<string>();

        var current = new List<string> { segments[0] }; // pl. "c:"
        for (int i = 1; i < segments.Length; i++)
        {
            var seg = segments[i];
            bool isFileName = i == segments.Length - 1;
            var next = new List<string>();

            foreach (var basePath in current)
            {
                if (seg.Contains('*'))
                {
                    if (isFileName) continue; // a PatternFactory fájlnévbe nem tesz '*'-ot
                    foreach (var name in fs.GetSubdirectoryNames(basePath + "\\"))
                        if (PatternMatcher.IsMatch(seg, name))
                            next.Add(basePath + "\\" + name);
                }
                else
                {
                    next.Add(basePath + "\\" + seg);
                }
            }

            current = next;
            if (current.Count == 0) break;
        }

        return current.Where(fs.FileExists).ToList();
    }

    // Mint a Resolve, de a Squirrel-appoknál (Discord, Slack stb.) a verziómappa melletti
    // Update.exe-t is hozzáveszi: ő indítja az új verziót, így a gyerekfolyamat örökli a kivételt.
    public static IReadOnlyList<string> ResolveWithLauncher(string pattern, IFileSystem fs)
    {
        var result = new List<string>(Resolve(pattern, fs));

        var segments = pattern.Split('\\');
        int appDir = Array.IndexOf(segments, "app-*");
        if (appDir > 0)
        {
            var updateExe = string.Join('\\', segments.Take(appDir)) + "\\update.exe";
            if (fs.FileExists(updateExe) && !result.Contains(updateExe, StringComparer.OrdinalIgnoreCase))
                result.Add(updateExe);
        }

        return result;
    }
}
