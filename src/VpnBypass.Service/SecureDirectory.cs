using System.Security.AccessControl;
using System.Security.Principal;

namespace VpnBypass.Service;

// A C:\ProgramData\vpn-bypass alatti mappákat szigorú ACL-lel kezeli, mert a SYSTEM-ként futó
// szolgáltatás ír beléjük, a ProgramData pedig alapból engedi a sima felhasználóknak a létrehozást.
//
// Fenyegetés: egy nem-admin felhasználó ELŐRE létrehozhatja a mappát a sajátjaként (írhatóan),
// mielőtt a szolgáltatás először fut, és így manipulálhatná a configot (ami azt vezérli, mely appok
// kerülnek kivételre). Ezért létező mappánál NEM bízunk vakon: ellenőrizzük a tulajdonost, szükség
// esetén visszaállítjuk, és minden esetben újrahúzzuk a védett DACL-t (eltávolítva az idegen ACE-ket).
// Reparse pontot az egész útvonalláncban elutasítunk.
public static class SecureDirectory
{
    private static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "vpn-bypass");

    public static void Ensure(string path)
    {
        var full = Path.GetFullPath(path);
        ThrowIfAnyReparsePoint(full);

        var security = BuildSecurity();
        foreach (var level in LevelsFromRootTo(full))
        {
            if (Directory.Exists(level)) HardenExisting(level, security);
            else security.CreateDirectory(level);
        }
    }

    // Írás előtt: ha a cél fájl reparse point, ne kövessük.
    public static void ThrowIfFileReparsePoint(string filePath)
    {
        if (File.Exists(filePath) && (File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"A fájl egy reparse point, biztonsági okból nem írom: {filePath}");
    }

    // A Root-tól a célig minden szint (a Root is), hogy a meglévő közbülső mappákat is megerősítsük.
    private static IEnumerable<string> LevelsFromRootTo(string full)
    {
        yield return Root;
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            yield break;
        var cur = Root;
        foreach (var seg in full[(Root.Length + 1)..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cur = Path.Combine(cur, seg);
            yield return cur;
        }
    }

    private static void HardenExisting(string path, DirectorySecurity hardenedDacl)
    {
        var di = new DirectoryInfo(path);
        var current = di.GetAccessControl(AccessControlSections.Owner);
        var owner = current.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        using var me = WindowsIdentity.GetCurrent();
        var meSid = me.User;

        bool ownerTrusted = owner is not null &&
            (owner.Equals(admins) || owner.Equals(system) || (meSid is not null && owner.Equals(meSid)));

        if (!ownerTrusted)
        {
            // Idegen tulajdonos: visszaállítjuk Administratorsra (SYSTEM-ként megy). Ha nem sikerül, zárunk.
            try
            {
                var ownerFix = new DirectorySecurity();
                ownerFix.SetOwner(admins);
                di.SetAccessControl(ownerFix);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException(
                    $"A(z) {path} mappa tulajdonosa nem megbízható ({owner}), és nem állítható vissza.", ex);
            }
        }

        // A védett DACL újra-alkalmazása: eltávolít minden korábban (pl. támadó által) adott jogot.
        di.SetAccessControl(hardenedDacl);
    }

    private static void ThrowIfAnyReparsePoint(string path)
    {
        var root = Path.GetPathRoot(path);
        var cur = path;
        while (!string.IsNullOrEmpty(cur) && !string.Equals(cur, root, StringComparison.OrdinalIgnoreCase))
        {
            if ((Directory.Exists(cur) || File.Exists(cur)) &&
                (File.GetAttributes(cur) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse point az útvonalban, biztonsági okból elutasítva: {cur}");
            cur = Path.GetDirectoryName(cur);
        }
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

        using var me = WindowsIdentity.GetCurrent();
        if (me.User is { } meSid)
            Allow(meSid, FileSystemRights.FullControl);

        return security;
    }
}
