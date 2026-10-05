using System.Security.AccessControl;
using System.Security.Principal;

namespace MorniLAN.Agent;

/// <summary>Ablageorte des Agents unter %ProgramData%\MorniLAN.</summary>
internal static class AgentPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MorniLAN");

    public static string Logs => Path.Combine(Root, "logs");

    public static string Data => Path.Combine(Root, "data");

    /// <summary>
    /// Legt den Datenordner an. Läuft der Agent als Dienst (LocalSystem), dürfen nur SYSTEM und
    /// Administratoren hinein, damit der Freund Pin und Zertifikat weder lesen noch austauschen kann.
    /// Im Konsolenmodus (Entwicklung) bleiben die Standardrechte, sonst sperrt man sich selbst aus.
    /// </summary>
    public static void EnsureDataDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        using var current = WindowsIdentity.GetCurrent();
        if (!current.IsSystem)
            return;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        directory.SetAccessControl(security);
    }
}
