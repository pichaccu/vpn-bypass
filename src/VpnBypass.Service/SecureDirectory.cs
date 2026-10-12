using System.Security.AccessControl;
using System.Security.Principal;

namespace VpnBypass.Service;

// A C:\ProgramData\vpn-bypass alatti mappákat szigorú ACL-lel hozza létre, mert a SYSTEM-ként futó
// szolgáltatás ír beléjük, a ProgramData pedig alapból engedi a sima felhasználóknak a létrehozást.
// Szabály: SYSTEM + Administrators + a futtató felhasználó teljes jog; a Users csak olvas; az
// öröklés letiltva (hogy a ProgramData "Users létrehozhat" ACE-je ne szivárogjon be). A reparse
// pointot elutasítjuk (megakadályozza az átirányítás-trükköket).
public static class SecureDirectory
{
    public static void Ensure(string path)
    {
        if (Directory.Exists(path))
        {
            ThrowIfReparsePoint(path);
            return;
        }

        var security = BuildSecurity();

        // A hiányzó szinteket föntről lefelé, mindegyiket a szigorú ACL-lel.
        var toCreate = new Stack<string>();
        string? current = path;
        while (current is not null && !Directory.Exists(current))
        {
            toCreate.Push(current);
            current = Path.GetDirectoryName(current);
        }
        while (toCreate.Count > 0)
        {
            var dir = toCreate.Pop();
            security.CreateDirectory(dir); // FileSystemAclExtensions: a megadott ACL-lel hozza létre
        }
    }

    // Írás előtt: ha a cél fájl reparse point, ne kövessük (biztonsági átirányítás elleni védelem).
    public static void ThrowIfFileReparsePoint(string filePath)
    {
        if (File.Exists(filePath) && (File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"A fájl egy reparse point, biztonsági okból nem írom: {filePath}");
    }

    private static void ThrowIfReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"A mappa egy reparse point, biztonsági okból nem használom: {path}");
    }

    private static DirectorySecurity BuildSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        void Allow(SecurityIdentifier sid, FileSystemRights rights) =>
            security.AddAccessRule(new FileSystemAccessRule(sid, rights, inherit, PropagationFlags.None, AccessControlType.Allow));

        Allow(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl);
        Allow(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl);
        Allow(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute);

        // A futtató felhasználó (SYSTEM-ként = LocalSystem, konzol/dry-run módban a user) is kapjon teljes jogot.
        using var me = WindowsIdentity.GetCurrent();
        if (me.User is { } meSid)
            Allow(meSid, FileSystemRights.FullControl);

        return security;
    }
}
