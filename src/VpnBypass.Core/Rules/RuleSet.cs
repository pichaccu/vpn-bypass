namespace VpnBypass.Core.Rules;

// A kivétel-szabályok gyűjteménye validációval: nincs duplikált minta, legfeljebb MaxRules szabály.
public class RuleSet
{
    public const int MaxRules = 256;

    private readonly List<ExclusionRule> _rules = new();

    // A Windows-útvonalak kis/nagybetű-függetlenek, ezért a minták egyezését is így nézzük.
    private readonly HashSet<string> _patterns = new(StringComparer.OrdinalIgnoreCase);

    // Csak olvasható nézet: a hívó nem kerülheti meg a TryAdd validációját.
    public IReadOnlyList<ExclusionRule> Rules => _rules.AsReadOnly();

    public bool TryAdd(ExclusionRule r, out string? error)
    {
        // Üres/whitespace minta értelmetlen szabály (a létezés/.exe ellenőrzés a service dolga).
        if (string.IsNullOrWhiteSpace(r.Pattern))
        {
            error = "A szabály mintája nem lehet üres.";
            return false;
        }
        if (_patterns.Contains(r.Pattern))
        {
            error = $"Ez a minta már szerepel a szabályok között: {r.Pattern}";
            return false;
        }
        if (_rules.Count >= MaxRules)
        {
            error = $"Legfeljebb {MaxRules} szabály vehető fel.";
            return false;
        }

        _patterns.Add(r.Pattern);
        _rules.Add(r);
        error = null;
        return true;
    }

    public bool RemoveByPattern(string pattern)
    {
        if (!_patterns.Remove(pattern)) return false;
        _rules.RemoveAll(x => string.Equals(x.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        return true;
    }
}
