namespace MorniLAN.Admin.Server;

/// <summary>Ablageorte des Admin-Panels unter %LOCALAPPDATA%\MorniLAN.</summary>
internal static class AdminPaths
{
    private static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorniLAN");

    public static string Data => Path.Combine(Root, "admin");

    public static string Logs => Path.Combine(Root, "logs");
}
