using System.Net;
using VpnBypass.Core.Ipc;
using VpnBypass.Core.Rules;

namespace VpnBypass.Core.Status;

// Egy futó folyamat a listához: Win32 útvonal (null, ha nem kérdezhető le) és megjelenítendő név.
public sealed record RunningProcess(uint Pid, string? Win32Path, string DisplayName);

// A pipálós ablak listája: elöl a kivétel-szabályok (kipipálva), utánuk a többi futó program.
// A RuleId-t küldi vissza a felület a SetExcluded kérésben: szabálynál a minta, programnál az útvonal.
public static class AppListBuilder
{
    public static IReadOnlyList<AppEntry> Build(
        IReadOnlyList<ExclusionRule> rules,
        IReadOnlyList<RunningProcess> running,
        IReadOnlyDictionary<uint, IReadOnlyList<ConnectionSample>> samplesByPid,
        IPAddress? internetAddress,
        IPAddress? tunnelAddress,
        Func<ExclusionRule, bool> ruleExistsOnDisk,
        string windowsDirectory)
    {
        var claimed = new HashSet<uint>();
        var ruleEntries = new List<AppEntry>();

        foreach (var rule in rules)
        {
            var matched = running
                .Where(p => p.Win32Path is not null && PatternMatcher.IsMatch(rule.Pattern, p.Win32Path))
                .ToList();
            foreach (var p in matched) claimed.Add(p.Pid);

            AppStatus status = matched.Count == 0
                ? (ruleExistsOnDisk(rule) ? AppStatus.NotRunning : AppStatus.NotFound)
                : StatusOf(matched, samplesByPid, internetAddress, tunnelAddress);

            var name = rule.Pattern.Contains('*') ? $"{rule.DisplayName} (minden verzió)" : rule.DisplayName;
            ruleEntries.Add(new AppEntry(rule.Pattern, name, rule.Pattern, matched.Count > 0, status));
        }

        var appEntries = running
            .Where(p => !claimed.Contains(p.Pid) && IsListable(p.Win32Path, windowsDirectory))
            .GroupBy(p => p.Win32Path!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppEntry(
                g.Key,
                g.First().DisplayName,
                null,
                true,
                StatusOf(g.ToList(), samplesByPid, internetAddress, tunnelAddress)))
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        return ruleEntries
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Concat(appEntries)
            .ToList();
    }

    // A Windows saját folyamatai, az útvonal nélküliek és a vpn-bypass saját exe-i nem kerülnek a listára.
    private static bool IsListable(string? path, string windowsDirectory)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.StartsWith(windowsDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        var file = path[(path.LastIndexOf('\\') + 1)..];
        return !file.StartsWith("vpnbypass.", StringComparison.OrdinalIgnoreCase);
    }

    private static AppStatus StatusOf(
        IReadOnlyList<RunningProcess> processes,
        IReadOnlyDictionary<uint, IReadOnlyList<ConnectionSample>> samplesByPid,
        IPAddress? internetAddress,
        IPAddress? tunnelAddress)
    {
        var samples = processes
            .SelectMany(p => samplesByPid.TryGetValue(p.Pid, out var s) ? s : Array.Empty<ConnectionSample>())
            .ToList();

        if (internetAddress is null) // nincs ismert kijárat → nincs mihez mérni
            return samples.Count == 0 ? AppStatus.Idle : AppStatus.Direct;

        var status = ConnectionClassifier.Classify(samples, internetAddress, tunnelAddress);
        // A folyamat fut, csak nincs külső kapcsolata → ne mutassuk "nem fut"-nak.
        return status == AppStatus.NotRunning ? AppStatus.Idle : status;
    }
}
