using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// Liest die echten Windows-Benutzerkonten (S-1-5-21-…) aus der ProfileList und erkennt, welche davon
/// Administratoren sind. Dienstkonten, das Standard-, Gast- und das WDAGUtility-Konto werden übergangen.
/// </summary>
internal static class AccountScanner
{
    public static IReadOnlyList<LocalAccount> Scan()
    {
        var admins = AdministratorSids();
        var accounts = new List<LocalAccount>();
        using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (list is null)
            return accounts;

        foreach (var sid in list.GetSubKeyNames().Where(IsRealUserSid))
        {
            var name = ResolveName(sid);
            if (name is null || IsBuiltInName(name))
                continue;
            accounts.Add(new LocalAccount(sid, name, admins.Contains(sid)));
        }
        return [.. accounts.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Nur persönliche Konten (S-1-5-21-…), nicht die Dienst- oder bekannten Systemkonten.</summary>
    internal static bool IsRealUserSid(string sid) => sid.StartsWith("S-1-5-21-", StringComparison.Ordinal);

    private static bool IsBuiltInName(string name) =>
        name.Equals("defaultuser0", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("WDAGUtility", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kontoname aus der SID; null, wenn Windows die SID nicht (mehr) auflösen kann.</summary>
    private static string? ResolveName(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value is { } full
                ? full.Contains('\\') ? full[(full.IndexOf('\\') + 1)..] : full
                : null;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    /// <summary>SIDs aller Mitglieder der lokalen Gruppe „Administratoren“ (S-1-5-32-544).</summary>
    private static HashSet<string> AdministratorSids()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupName = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
            .Translate(typeof(NTAccount)).Value;
        var shortName = groupName.Contains('\\') ? groupName[(groupName.IndexOf('\\') + 1)..] : groupName;

        if (NetLocalGroupGetMembers(null, shortName, 0, out var buffer, -1, out var read, out _, IntPtr.Zero) != 0)
            return result;
        try
        {
            var size = Marshal.SizeOf<LocalGroupMembersInfo0>();
            for (var i = 0; i < read; i++)
            {
                var entry = Marshal.PtrToStructure<LocalGroupMembersInfo0>(buffer + i * size);
                if (entry.Sid != IntPtr.Zero && ConvertSidToStringSid(entry.Sid, out var sidString))
                    result.Add(sidString);
            }
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo0
    {
        public IntPtr Sid;
    }

    // CharSet.Unicode ist Pflicht: Die NetApi-Funktionen kennen nur Unicode. Ohne den Hinweis käme der
    // Gruppenname als ANSI an, die Abfrage schlüge fehl und niemand würde als Administrator erkannt.
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(string? serverName, string groupName, int level,
        out IntPtr buffer, int prefMaxLen, out int entriesRead, out int totalEntries, IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out string sidString);
}
