using VpnBypass.Core.Rules;

namespace VpnBypass.Core.Config;

// A perzisztens beállítás (C:\ProgramData\vpn-bypass\config.json). A tényleges fájl-IO a
// szolgáltatásé (Fázis 2 ConfigStore); itt csak a séma + a szerializálható alak van, hogy a
// Core platformfüggetlen és tesztelhető maradjon.
public record AppConfig(
    IReadOnlyList<ExclusionRule> Rules,
    string? InternetAdapter,
    string? UserSid)
{
    public static AppConfig Empty { get; } = new(new List<ExclusionRule>(), null, null);
}
