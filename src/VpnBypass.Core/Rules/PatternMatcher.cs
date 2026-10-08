namespace VpnBypass.Core.Rules;

// Kontraktus: a pattern a PatternFactory kimenete, de a kézzel szerkesztett/deszerializált nagybetűs mintát
// is elfogadjuk: pattern és útvonal egyaránt kisbetűsítve, szegmensenként, ordinálisan hasonlítódik össze.
// Önálló "*" = pontosan egy mappaszegmens; szegmensen belüli "*" (pl. "app-*") = legalább 1 karakter.
public static class PatternMatcher
{
    public static bool IsMatch(string pattern, string fullPath)
    {
        var pp = pattern.ToLowerInvariant().Split('\\');
        var fp = fullPath.ToLowerInvariant().Split('\\');
        if (pp.Length != fp.Length) return false;
        for (int i = 0; i < pp.Length; i++)
        {
            if (pp[i] == "*") continue;
            if (pp[i].Contains('*')) // „app-*” jellegű prefix (és suffix)
            {
                var prefix = pp[i][..pp[i].IndexOf('*')];
                var suffix = pp[i][(pp[i].IndexOf('*') + 1)..];
                // A hossz-feltétel egyszerre zárja ki az üres "*"-ot és az átfedő prefixet/suffixet.
                if (!(fp[i].Length > prefix.Length + suffix.Length
                      && fp[i].StartsWith(prefix, StringComparison.Ordinal)
                      && fp[i].EndsWith(suffix, StringComparison.Ordinal))) return false;
            }
            else if (pp[i] != fp[i]) return false;
        }
        return true;
    }
}
