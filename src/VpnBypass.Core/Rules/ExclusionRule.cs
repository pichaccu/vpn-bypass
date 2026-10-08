namespace VpnBypass.Core.Rules;

// Egy kivétel-szabály: a felhasználó által megadott útvonal + a belőle képzett minta.
public record ExclusionRule(string DisplayName, string OriginalPath, string Pattern)
{
    // A megjelenített név a fájlnév kiterjesztés nélkül; a minta a PatternFactory kimenete.
    public static ExclusionRule Create(string fullPath) =>
        new(Path.GetFileNameWithoutExtension(fullPath), fullPath, PatternFactory.FromPath(fullPath));
}
