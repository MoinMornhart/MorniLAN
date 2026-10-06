namespace MorniLAN.Shared.Models;

/// <summary>Ein Windows-Konto auf dem verwalteten PC.</summary>
/// <param name="Sid">Sicherheitskennung (S-1-5-21-…), bleibt gleich, auch wenn das Konto umbenannt wird.</param>
/// <param name="IsAdministrator">Administratorkonten lassen sich nie einschränken.</param>
public sealed record LocalAccount(string Sid, string Name, bool IsAdministrator);

/// <summary>Was der PC über den Stand seiner Sperren meldet.</summary>
/// <param name="Applied">Sperren sind wie vom Panel gewünscht gesetzt.</param>
/// <param name="RestrictedAccountCount">Wie viele Konten gerade eingeschränkt sind.</param>
/// <param name="AppControlMode">"off", "audit" (nur protokollieren) oder "enforce" (scharf).</param>
/// <param name="Message">Hinweis für das Panel, z. B. ein Fehler oder dass App Control einen Neustart braucht.</param>
/// <param name="KioskAccountCount">
/// Wie viele Konten den Launcher als Desktop (Shell-Ersatz) nutzen. Eigenes, nachträglich ergänztes Feld mit
/// Standardwert 0, damit ein älteres Panel es einfach übergeht.
/// </param>
public sealed record RestrictionState(bool Applied, int RestrictedAccountCount, string AppControlMode, string Message,
    int KioskAccountCount = 0)
{
    public static readonly RestrictionState Idle = new(true, 0, "off", "");
}

/// <summary>
/// Windows-Bereiche, die in eingeschränkten Konten gesperrt werden (Nutzer-Entscheidung M6, 2026-10-06).
/// Als Text statt Enum: Ein Bereich, den eine ältere Version nicht kennt, wird dort einfach übergangen.
/// </summary>
public static class WindowsAreas
{
    public const string Settings = "settings";
    public const string Console = "console";
    public const string Registry = "registry";
    public const string TaskManager = "taskmanager";
    public const string Install = "install";

    public static readonly string[] All = [Settings, Console, Registry, TaskManager, Install];

    public static bool IsKnown(string area) => All.Contains(area);

    public static string Describe(string area) => area switch
    {
        Settings => "Einstellungen und Systemsteuerung",
        Console => "Eingabeaufforderung und PowerShell",
        Registry => "Registry-Editor",
        TaskManager => "Task-Manager",
        Install => "Programme installieren",
        _ => area,
    };
}
