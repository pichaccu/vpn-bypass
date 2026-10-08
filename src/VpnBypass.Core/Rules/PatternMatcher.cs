namespace VpnBypass.Core.Rules;

public static class PatternMatcher
{
    public static bool IsMatch(string pattern, string fullPath)
    {
        var pp = pattern.Split('\\');
        var fp = fullPath.ToLowerInvariant().Split('\\');
        if (pp.Length != fp.Length) return false;
        for (int i = 0; i < pp.Length; i++)
        {
            if (pp[i] == "*") continue;
            if (pp[i].Contains('*')) // „app-*” jellegű prefix
            {
                var prefix = pp[i][..pp[i].IndexOf('*')];
                var suffix = pp[i][(pp[i].IndexOf('*') + 1)..];
                if (!(fp[i].StartsWith(prefix) && fp[i].EndsWith(suffix))) return false;
            }
            else if (pp[i] != fp[i]) return false;
        }
        return true;
    }
}
