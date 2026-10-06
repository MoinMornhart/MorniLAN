using System.Diagnostics;
using System.Text;

namespace MorniLAN.Admin.Platform;

/// <summary>Ein Treffer der winget-Suche.</summary>
public sealed record WingetHit(string Name, string Id);

/// <summary>
/// Durchsucht winget auf dem Admin-PC, um Paket-IDs zu finden. Installiert wird dann auf dem verwalteten PC (dort
/// läuft dasselbe winget-Repository). Die Ausgabe von winget ist eine Tabelle; wir schneiden an den Spaltenköpfen.
/// </summary>
public static class WingetSearch
{
    public static async Task<IReadOnlyList<WingetHit>> SearchAsync(string term, CancellationToken cancellationToken)
    {
        if (term.Trim().Length < 2)
            return [];
        var output = await RunAsync($"search --query \"{term.Trim()}\" --source winget --accept-source-agreements", cancellationToken);
        return Parse(output);
    }

    /// <summary>Spaltenweise zerlegen: Kopfzeile „Name  Id  Version …“ liefert die Startspalten.</summary>
    internal static IReadOnlyList<WingetHit> Parse(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        var headerIndex = Array.FindIndex(lines, l => l.StartsWith("Name", StringComparison.Ordinal) && l.Contains("Id"));
        if (headerIndex < 0)
            return [];
        var header = lines[headerIndex];
        var idColumn = header.IndexOf("Id", StringComparison.Ordinal);
        var versionColumn = header.IndexOf("Version", StringComparison.Ordinal);
        if (idColumn < 0 || versionColumn <= idColumn)
            return [];

        var hits = new List<WingetHit>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            if (line.Length < versionColumn || line.StartsWith('-') || line.Trim().Length == 0)
                continue;
            var name = line[..idColumn].Trim();
            var id = line[idColumn..Math.Min(versionColumn, line.Length)].Trim();
            if (name.Length > 0 && id.Length > 0 && !id.Contains(' '))
                hits.Add(new WingetHit(name, id));
            if (hits.Count >= 15)
                break;
        }
        return hits;
    }

    private static async Task<string> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("winget", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            })!;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ""; // winget nicht vorhanden
        }
    }
}
