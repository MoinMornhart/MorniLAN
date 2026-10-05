using System.Reflection;

namespace MorniLAN.Shared;

/// <summary>
/// Liefert die zentrale Version aus Directory.Build.props, so wie sie in alle Assemblies eingebaut wird.
/// </summary>
public static class VersionInfo
{
    private static readonly string Informational =
        typeof(VersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>SemVer ohne Build-Metadaten, z. B. "0.1.0" oder "0.2.0-beta.1".</summary>
    public static string Version { get; } = StripMetadata(Informational);

    /// <summary>Kurzer Commit-Hash aus den Build-Metadaten, falls vorhanden.</summary>
    public static string? Commit { get; } = ExtractCommit(Informational);

    /// <summary>Anzeigetext für UIs, z. B. "v0.1.0 (a1b2c3d)".</summary>
    public static string Display => Commit is null ? $"v{Version}" : $"v{Version} ({Commit})";

    internal static string StripMetadata(string informational)
    {
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    internal static string? ExtractCommit(string informational)
    {
        var plus = informational.IndexOf('+');
        if (plus < 0 || plus == informational.Length - 1)
            return null;
        var meta = informational[(plus + 1)..];
        return meta.Length > 7 ? meta[..7] : meta;
    }
}
