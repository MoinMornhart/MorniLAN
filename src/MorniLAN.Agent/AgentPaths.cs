namespace MorniLAN.Agent;

/// <summary>Ablageorte des Agents unter %ProgramData%\MorniLAN.</summary>
internal static class AgentPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MorniLAN");

    public static string Logs => Path.Combine(Root, "logs");

    public static string Data => Path.Combine(Root, "data");
}
