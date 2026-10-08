using System.Text.RegularExpressions;

namespace VpnBypass.Core.Rules;

public static class PatternFactory
{
    // Squirrel: app-<verzió>; általános: tisztán verziószámú mappanév; MSIX: Name_<verzió>_arch__pub.
    private static readonly Regex Squirrel = new(@"^app-\d+(\.\d+)*$", RegexOptions.IgnoreCase);

    public static string FromPath(string fullPath)
    {
        var norm = fullPath.ToLowerInvariant();
        var parts = norm.Split('\\');
        for (int i = 0; i < parts.Length - 1; i++) // a fájlnevet (utolsó) nem bántjuk
        {
            if (Squirrel.IsMatch(parts[i]))
                parts[i] = "app-*";
        }
        return string.Join('\\', parts);
    }
}
