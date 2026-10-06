namespace MorniLAN.Shared;

/// <summary>Gemeinsame Konstanten für alle Komponenten.</summary>
public static class MorniLanConstants
{
    public const string ProductName = "MorniLAN";

    /// <summary>Name des Windows-Dienstes auf dem Freundes-PC.</summary>
    public const string AgentServiceName = "MorniLAN.Agent";

    /// <summary>Named Pipe zwischen Launcher (Benutzer) und Agent (LocalSystem).</summary>
    public const string LauncherPipeName = "MorniLAN.Launcher";

    /// <summary>Named Pipe mit den freigegebenen Apps für den Launcher.</summary>
    public const string LauncherAppsPipeName = "MorniLAN.Launcher.Apps";

    /// <summary>TCP-Port, auf dem das Admin-Panel Agent-Verbindungen annimmt.</summary>
    public const int AdminPort = 47950;

    /// <summary>UDP-Port für die LAN-Erkennung per Broadcast.</summary>
    public const int DiscoveryPort = 47951;

    /// <summary>mDNS-Dienstname für die LAN-Erkennung.</summary>
    public const string MdnsServiceType = "_mornilan._tcp.local";

    /// <summary>GitHub-Repository für Releases und Auto-Update.</summary>
    public const string GitHubRepository = "MoinMornhart/MorniLAN";
}
