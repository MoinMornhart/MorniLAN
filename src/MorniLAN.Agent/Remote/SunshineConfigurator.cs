using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorniLAN.Agent.Remote;

/// <summary>
/// Richtet Sunshine nach der Installation vollautomatisch ein, damit der Fernzugriff <b>ohne</b> Sunshines
/// Web-Oberfläche funktioniert – das war bisher die größte Fehlerquelle:
/// <list type="number">
///   <item>Einmalig Zugangsdaten für Sunshines API setzen (<c>sunshine.exe --creds</c>) und sicher ablegen.</item>
///   <item>Die Moonlight-Kopplung per PIN über Sunshines lokale API (<c>POST /api/pin</c>) annehmen –
///         der Admin tippt die in Moonlight angezeigte PIN einfach ins Panel, statt sie im Browser einzugeben.</item>
/// </list>
/// Läuft als SYSTEM im Dienst; die Zugangsdaten liegen im nur für SYSTEM/Admins lesbaren Datenordner.
/// </summary>
internal sealed class SunshineConfigurator
{
    private const string UserName = "mornilan";
    private readonly string _credentialsFile;
    private readonly Func<string, string[], int> _runSunshine;
    private readonly Action _afterCredentialsCreated;

    /// <summary>Standard: schreibt in den Agent-Datenordner, ruft die echte sunshine.exe, startet danach den Dienst neu.</summary>
    public static SunshineConfigurator Shared { get; } = new(
        Path.Combine(AgentPaths.Data, "sunshine.json"), RunSunshine, SunshineControl.RestartService);

    /// <param name="credentialsFile">Ablage der API-Zugangsdaten.</param>
    /// <param name="runSunshine">Startet sunshine.exe mit Argumenten, gibt den Exitcode zurück (injizierbar für Tests).</param>
    /// <param name="afterCredentialsCreated">
    /// Läuft einmalig, nachdem neue Zugangsdaten gesetzt wurden (Standard: Sunshine-Dienst neu starten, damit er sie lädt).
    /// </param>
    public SunshineConfigurator(string credentialsFile, Func<string, string[], int> runSunshine,
        Action? afterCredentialsCreated = null)
    {
        _credentialsFile = credentialsFile;
        _runSunshine = runSunshine;
        _afterCredentialsCreated = afterCredentialsCreated ?? (() => { });
    }

    /// <summary>
    /// Stellt sicher, dass Sunshine API-Zugangsdaten hat (setzt sie beim ersten Mal per <c>--creds</c>) und gibt sie
    /// zurück. Null, wenn sunshine.exe fehlt oder das Setzen fehlschlug.
    /// </summary>
    public Credentials? EnsureCredentials()
    {
        if (Load() is { } existing)
            return existing;

        var created = new Credentials(UserName, GeneratePassword());
        // sunshine.exe --creds <user> <pass> setzt die Web-/API-Zugangsdaten nicht-interaktiv und beendet sich wieder.
        if (_runSunshine(SunshineControl.ExecutablePath ?? "sunshine.exe", ["--creds", created.User, created.Password]) != 0)
            return null;
        Store(created);
        _afterCredentialsCreated(); // Dienst neu starten, damit die frischen Zugangsdaten gelten (einmalig)
        return created;
    }

    /// <summary>Nimmt eine Moonlight-PIN über Sunshines lokale API an. Echte Variante mit eigenem HttpClient.</summary>
    public Task<bool> AcceptPinAsync(string pin, string deviceName, CancellationToken cancellationToken) =>
        AcceptPinAsync(pin, deviceName, SendAsync, cancellationToken);

    /// <summary>Testbarer Kern: der eigentliche HTTPS-Aufruf ist injizierbar.</summary>
    internal async Task<bool> AcceptPinAsync(string pin, string deviceName,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        if (!IsValidPin(pin))
            return false;
        if (EnsureCredentials() is not { } credentials)
            return false;

        using var request = BuildPinRequest(credentials, pin, deviceName);
        using var response = await send(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePinAccepted(body);
    }

    /// <summary>Moonlight-PINs sind vierstellige Zahlen – alles andere gar nicht erst senden.</summary>
    internal static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    /// <summary>Baut die Kopplungs-Anfrage: POST an die lokale API, Basic-Auth, JSON mit PIN und Name.</summary>
    internal static HttpRequestMessage BuildPinRequest(Credentials credentials, string pin, string deviceName)
    {
        var payload = JsonSerializer.Serialize(new PinBody(pin, deviceName), SunshineJson.Default.PinBody);
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://localhost:{SunshineControl.ApiPort}/api/pin")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.User}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return request;
    }

    /// <summary>Sunshine antwortet mit <c>{"status":"true"}</c>, wenn die PIN angenommen wurde.</summary>
    internal static bool ParsePinAccepted(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.String => string.Equals(status.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                    _ => false,
                };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private Credentials? Load()
    {
        try
        {
            if (!File.Exists(_credentialsFile))
                return null;
            return JsonSerializer.Deserialize(File.ReadAllText(_credentialsFile), SunshineJson.Default.Credentials);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Store(Credentials credentials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_credentialsFile)!);
        File.WriteAllText(_credentialsFile, JsonSerializer.Serialize(credentials, SunshineJson.Default.Credentials));
    }

    private static string GeneratePassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static int RunSunshine(string exe, string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe)
            {
                Arguments = string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return -1;
            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return -1;
            }
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }

    // Eigener HttpClient für die lokale API: Sunshine nutzt ein selbstsigniertes Zertifikat auf localhost – das wird
    // hier bewusst akzeptiert (nur localhost, keine Netzwerk-Gegenstelle).
    private static readonly HttpClient Local = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Local.SendAsync(request, cancellationToken);

    /// <summary>API-Zugangsdaten für Sunshines lokale Oberfläche.</summary>
    public sealed record Credentials(string User, string Password);

    internal sealed record PinBody(
        [property: JsonPropertyName("pin")] string Pin,
        [property: JsonPropertyName("name")] string Name);
}

[JsonSerializable(typeof(SunshineConfigurator.Credentials))]
[JsonSerializable(typeof(SunshineConfigurator.PinBody))]
internal sealed partial class SunshineJson : JsonSerializerContext;
