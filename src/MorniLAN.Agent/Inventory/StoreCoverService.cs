using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MorniLAN.Agent.Inventory;

/// <summary>
/// Holt Cover für Spiele, die lokal keins haben, aus dem Steam-Shop und legt sie im Datenordner ab.
/// <list type="bullet">
/// <item>Steam-Spiele über ihre AppID, Spiele anderer Launcher über eine Namenssuche im Shop. Übernommen wird nur
///   ein exakter Namenstreffer (ohne ™/®, Satzzeichen, Groß-/Kleinschreibung), damit kein falsches Cover erscheint.</item>
/// <item>Bevorzugt das Hochformat (600×900). Bei neuen Spielen liefert die feste Adresse nur einen Platzhalter,
///   dann das Querformat aus den Shop-Details.</item>
/// <item>Jedes Spiel wird nur einmal geladen. Erfolglose Suchen werden 7 Tage nicht wiederholt, je Durchlauf
///   höchstens <see cref="MaxLookupsPerRun"/> Anfragen. Ohne Internet bleibt es eben beim Icon.</item>
/// </list>
/// Übertragen wird nur der Spielname, keine Daten über den PC oder den Nutzer.
/// </summary>
internal sealed partial class StoreCoverService
{
    public const int MaxLookupsPerRun = 20;
    internal static readonly TimeSpan RetryMissesAfter = TimeSpan.FromDays(7);

    /// <summary>Kleiner als das ist ein Platzhalterbild, kein echtes Cover.</summary>
    private const int MinimumCoverBytes = 4 * 1024;

    private const string Store = "https://store.steampowered.com";
    private const string Assets = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps";

    private readonly string _folder;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StoreCoverService(string folder, HttpMessageHandler? handler = null, Func<DateTimeOffset>? now = null)
    {
        _folder = folder;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MorniLAN-Agent");
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    private string MissesFile => Path.Combine(_folder, "misses.txt");

    /// <summary>Trägt bei Spielen ohne Cover das (geladene oder zwischengespeicherte) Cover ein.</summary>
    public async Task<IReadOnlyList<InventoryItem>> FillAsync(IReadOnlyList<InventoryItem> items, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_folder);
            var misses = LoadMisses();
            var budget = MaxLookupsPerRun;
            var offline = false;
            var result = new List<InventoryItem>(items.Count);
            foreach (var item in items)
            {
                if (!item.App.IsGame || (item.Images.CoverFile is { } existing && File.Exists(existing)))
                {
                    result.Add(item);
                    continue;
                }
                var key = CacheKey(item);
                var file = Path.Combine(_folder, key + ".jpg");
                if (!File.Exists(file) && !offline && budget > 0
                    && !(misses.TryGetValue(key, out var checkedAt) && _now() - checkedAt < RetryMissesAfter))
                {
                    budget--;
                    try
                    {
                        if (await DownloadAsync(item, file, cancellationToken))
                            misses.Remove(key);
                        else
                            misses[key] = _now();
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                                   && !cancellationToken.IsCancellationRequested)
                    {
                        offline = true; // kein Netz oder Shop gestört: nicht weiter anfragen, nichts als "fehlt" merken
                    }
                }
                result.Add(File.Exists(file) ? item with { Images = item.Images with { CoverFile = file } } : item);
            }
            SaveMisses(misses);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Dateiname im Zwischenspeicher: Steam über die AppID, sonst über den Namen.</summary>
    internal static string CacheKey(InventoryItem item) => item.App.SteamAppId is { } appId
        ? $"steam-{appId}"
        : "name-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NameKey(item.App.Name))).AsSpan(0, 8));

    private async Task<bool> DownloadAsync(InventoryItem item, string file, CancellationToken cancellationToken)
    {
        var appId = item.App.SteamAppId ?? await SearchAsync(item.App.Name, cancellationToken);
        if (appId is null)
            return false;
        var data = await GetImageAsync($"{Assets}/{appId}/library_600x900.jpg", cancellationToken)
                   ?? (await HeaderImageUrlAsync(appId.Value, cancellationToken) is { } header
                       ? await GetImageAsync(header, cancellationToken)
                       : null);
        if (data is null)
            return false;
        var temp = file + ".tmp";
        await File.WriteAllBytesAsync(temp, data, cancellationToken);
        File.Move(temp, file, overwrite: true);
        return true;
    }

    /// <summary>AppID des Spiels mit genau diesem Namen, sonst null.</summary>
    private async Task<int?> SearchAsync(string name, CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync(
            $"{Store}/api/storesearch/?term={Uri.EscapeDataString(SearchTerm(name))}&l=english&cc=US", cancellationToken);
        return MatchSearchResult(json, name);
    }

    /// <summary>
    /// Exakter Namenstreffer, sonst derselbe Name mit anderem Editionszusatz („— Remastered“ zu „Game of the Year
    /// Edition“). Andere Zusätze (DLC, Soundtrack, Bundles) passen nie.
    /// </summary>
    internal static int? MatchSearchResult(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;
        var candidates = new List<(string Name, int Id)>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() != "app")
                continue;
            if (item.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } text
                && item.TryGetProperty("id", out var id) && id.TryGetInt32(out var appId) && appId > 0)
                candidates.Add((text, appId));
        }
        var exact = NameKey(name);
        var withoutEdition = NameKey(WithoutEdition(name));
        return candidates.Where(c => NameKey(c.Name) == exact).Select(c => (int?)c.Id).FirstOrDefault()
               ?? candidates.Where(c => NameKey(WithoutEdition(c.Name)) == withoutEdition).Select(c => (int?)c.Id).FirstOrDefault();
    }

    /// <summary>Name ohne Editionszusatz am Ende, z. B. „The Witcher 3: Wild Hunt — Remastered“ → „The Witcher 3: Wild Hunt“.</summary>
    internal static string WithoutEdition(string name) => EditionSuffix().Replace(name, "").Trim();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"[\s\-–—:]*\b((Game of the Year|GOTY|Definitive|Complete|Enhanced|Deluxe|Standard|Remastered|Director['’]?s Cut)(\s+Edition)?|(Ultimate|Gold|Anniversary)\s+Edition)\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex EditionSuffix();

    private async Task<string?> HeaderImageUrlAsync(int appId, CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync($"{Store}/api/appdetails?appids={appId}&filters=basic", cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var app)
               && app.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
               && data.TryGetProperty("header_image", out var header) && header.GetString() is { Length: > 0 } url
               && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url
            : null;
    }

    /// <summary>Bilddaten, oder null bei 404, Nicht-Bild oder Platzhalter. Andere Fehler fliegen weiter.</summary>
    private async Task<byte[]?> GetImageAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png"))
            return null;
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return data.Length >= MinimumCoverBytes && data.Length <= 4 * 1024 * 1024 ? data : null;
    }

    /// <summary>Für den Vergleich: nur Buchstaben und Ziffern, klein, ohne ™ ® ©.</summary>
    internal static string NameKey(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Suchbegriff ohne Markenzeichen und Editionszusatz und mit geradem Apostroph, damit der Shop das Spiel
    /// findet (die Suche kennt „Game of the Year Edition“ oft nicht unter diesem Namen).
    /// </summary>
    internal static string SearchTerm(string name) =>
        WithoutEdition(new string(name.Where(c => c is not ('™' or '®' or '©')).Select(c => c is '’' or '‘' ? '\'' : c).ToArray()));

    private Dictionary<string, DateTimeOffset> LoadMisses()
    {
        var misses = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(MissesFile))
                return misses;
            foreach (var line in File.ReadAllLines(MissesFile))
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                    misses[parts[0]] = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return misses;
    }

    private void SaveMisses(Dictionary<string, DateTimeOffset> misses)
    {
        try
        {
            File.WriteAllLines(MissesFile, misses.Select(m =>
                $"{m.Key}\t{m.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
