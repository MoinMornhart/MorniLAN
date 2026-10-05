using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MorniLAN.Shared.Updates;

/// <summary>Eine Datei in einem Release. <paramref name="Sha256"/> stammt aus dem "digest"-Feld von GitHub.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256);

public sealed record ReleaseInfo(string Tag, SemanticVersion Version, bool IsPrerelease, Uri Page, string Notes,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Welches Setup zu welcher App gehört.</summary>
public enum UpdateProduct
{
    Admin,
    Device,
}

/// <summary>Ein gefundenes Update: Version und das passende Setup.</summary>
public sealed record AvailableUpdate(ReleaseInfo Release, ReleaseAsset Setup)
{
    public SemanticVersion Version => Release.Version;
}

/// <summary>
/// Liest die Releases des öffentlichen Repos (ohne Token, GitHub erlaubt 60 Abfragen pro Stunde und Adresse).
/// </summary>
public static class GitHubReleases
{
    private static readonly HttpClient Http = CreateClient();

    public static string SetupPrefix(UpdateProduct product) =>
        product == UpdateProduct.Admin ? "MorniLAN-Admin-Setup-" : "MorniLAN-Geraete-Setup-";

    public static async Task<IReadOnlyList<ReleaseInfo>> FetchAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{MorniLanConstants.GitHubRepository}/releases?per_page=15";
        await using var stream = await Http.GetStreamAsync(url, cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Parse(json.RootElement);
    }

    /// <summary>Entwürfe und Releases ohne gültige Versionsnummer werden übergangen.</summary>
    internal static IReadOnlyList<ReleaseInfo> Parse(JsonElement releases)
    {
        var result = new List<ReleaseInfo>();
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!SemanticVersion.TryParse(tag, out var version))
                continue;
            var assets = new List<ReleaseAsset>();
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                assets.Add(new ReleaseAsset(
                    asset.GetProperty("name").GetString() ?? "",
                    new Uri(asset.GetProperty("browser_download_url").GetString()!),
                    asset.GetProperty("size").GetInt64(),
                    digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null));
            }
            result.Add(new ReleaseInfo(tag, version,
                release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(),
                new Uri(release.GetProperty("html_url").GetString()!),
                release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                assets));
        }
        return result;
    }

    /// <summary>Neueste Version über <paramref name="current"/>, die ein Setup für das Produkt mitbringt.</summary>
    public static AvailableUpdate? FindUpdate(IEnumerable<ReleaseInfo> releases, SemanticVersion current, UpdateProduct product,
        bool includePrereleases)
    {
        var prefix = SetupPrefix(product);
        return releases
            .Where(r => r.Version > current && (includePrereleases || !r.IsPrerelease))
            .OrderByDescending(r => r.Version)
            .Select(r => r.Assets.FirstOrDefault(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                                     && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) is { } setup
                ? new AvailableUpdate(r, setup)
                : null)
            .FirstOrDefault(u => u is not null);
    }

    /// <summary>
    /// Lädt das Setup herunter und prüft die SHA-256-Prüfsumme. Ohne Prüfsumme von GitHub wird nicht installiert.
    /// </summary>
    public static async Task<string> DownloadAsync(AvailableUpdate update, string folder, CancellationToken cancellationToken)
    {
        if (update.Setup.Sha256 is not { Length: 64 } expected)
            throw new InvalidOperationException("Das Release enthält keine Prüfsumme für das Setup, Update abgebrochen.");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(update.Setup.Name));
        var temp = target + ".download";

        await using (var output = File.Create(temp))
        await using (var input = await Http.GetStreamAsync(update.Setup.DownloadUrl, cancellationToken))
            await input.CopyToAsync(output, cancellationToken);

        string actual;
        await using (var check = File.OpenRead(temp))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, cancellationToken));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            throw new InvalidOperationException("Prüfsumme des Setups stimmt nicht, Update abgebrochen.");
        }
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>Startet das Setup still. Es beendet die laufende App selbst und startet sie danach neu.</summary>
    public static Process? StartSilentInstall(string setupPath, string logPath) =>
        Process.Start(new ProcessStartInfo(setupPath,
            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LOG=\"{logPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MorniLAN", VersionInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
