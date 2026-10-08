using System.Text.RegularExpressions;

namespace VpnBypass.Core.Rules;

public static class PatternFactory
{
    // Squirrel: app-<verzió>; általános: tisztán verziószámú mappanév; MSIX: Name_<verzió>_arch__pub.
    private static readonly Regex Squirrel = new(@"^app-\d+(\.\d+)*$", RegexOptions.IgnoreCase);
    private static readonly Regex Msix = new(@"^(?<name>[^_]+)_\d+(\.\d+)*_(?<arch>[^_]+)__(?<pub>[A-Za-z0-9]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex PlainVersion = new(@"^\d+(\.\d+){1,3}$");

    public static string FromPath(string fullPath)
    {
        var norm = fullPath.ToLowerInvariant();
        var parts = norm.Split('\\');
        for (int i = 0; i < parts.Length - 1; i++) // a fájlnevet (utolsó) nem bántjuk
        {
            if (Squirrel.IsMatch(parts[i]))
                parts[i] = "app-*";
            else if (Msix.Match(parts[i]) is { Success: true } m)
                parts[i] = $"{m.Groups["name"].Value}_*_{m.Groups["arch"].Value}__{m.Groups["pub"].Value}";
            else if (PlainVersion.IsMatch(parts[i]))
                parts[i] = "*";
        }
        return string.Join('\\', parts);
    }
}
