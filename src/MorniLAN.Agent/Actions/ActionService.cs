using System.Diagnostics;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Actions;

/// <summary>
/// Führt Admin-Befehle auf dem PC aus: Programme über winget installieren/deinstallieren, aus einer Adresse
/// herunterladen und still installieren, Nachricht anzeigen, neu starten/herunterfahren. Läuft nur ein Befehl
/// gleichzeitig; das Ergebnis geht ans Panel zurück.
/// </summary>
/// <param name="runProcess">Startet ein Programm und liefert (Exitcode, Ausgabe) – injizierbar für Tests.</param>
/// <param name="download">Lädt eine Datei und gibt den lokalen Pfad zurück – injizierbar.</param>
internal sealed class ActionService(
    ILogger<ActionService> logger,
    Func<string, string, TimeSpan, (int Code, string Output)>? runProcess = null,
    Func<string, string, CancellationToken, Task<string>>? download = null,
    Action<AdminMessage>? showMessage = null)
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);

    private readonly Func<string, string, TimeSpan, (int Code, string Output)> _run = runProcess ?? RunProcess;
    private readonly Func<string, string, CancellationToken, Task<string>> _download = download ?? DownloadAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Neueste Admin-Nachricht für den Launcher (null, bis eine kommt).</summary>
    public AdminMessage? CurrentMessage { get; private set; }

    public event Action<AdminMessage>? MessageChanged;

    public async Task<CommandResult> RunAsync(AdminCommand command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("Admin-Befehl: {Describe}", command.Describe());
            return command switch
            {
                InstallPackageCommand install => Winget(command, "install", install.WingetId),
                UninstallPackageCommand uninstall => Winget(command, "uninstall", uninstall.WingetId),
                InstallFromUrlCommand url => await InstallFromUrlAsync(url, cancellationToken),
                ShowMessageCommand message => Message(message),
                RestartCommand restart => Restart(restart),
                _ => new CommandResult(command.CommandId, false, command.Describe(), "Unbekannter Befehl."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin-Befehl fehlgeschlagen");
            return new CommandResult(command.CommandId, false, command.Describe(), ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CommandResult Winget(AdminCommand command, string verb, string id)
    {
        if (!IsValidPackageId(id))
            return new CommandResult(command.CommandId, false, command.Describe(), "Ungültige Paket-ID.");
        var arguments = $"{verb} --id {id} --exact --silent --accept-source-agreements" +
                        (verb == "install" ? " --accept-package-agreements" : "");
        var (code, output) = _run("winget", arguments, InstallTimeout);
        return new CommandResult(command.CommandId, code == 0, command.Describe(), code == 0 ? null : Tail(output));
    }

    private async Task<CommandResult> InstallFromUrlAsync(InstallFromUrlCommand command, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(command.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new CommandResult(command.CommandId, false, command.Describe(), "Nur sichere https-Adressen sind erlaubt.");
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".exe" or ".msi"))
            return new CommandResult(command.CommandId, false, command.Describe(), "Die Adresse muss auf eine .exe oder .msi zeigen.");

        var file = await _download(command.Url, extension, cancellationToken);
        try
        {
            var (code, output) = extension == ".msi"
                ? _run("msiexec", $"/i \"{file}\" /qn /norestart", InstallTimeout)
                : _run(file, "/S /silent /quiet /norestart", InstallTimeout);
            return new CommandResult(command.CommandId, code == 0, command.Describe(),
                code == 0 ? null : $"Installer endete mit Code {code}. {Tail(output)}");
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private CommandResult Message(ShowMessageCommand command)
    {
        var title = command.Title.Trim();
        var text = command.Text.Trim();
        if (title.Length == 0 && text.Length == 0)
            return new CommandResult(command.CommandId, false, command.Describe(), "Leere Nachricht.");
        var message = new AdminMessage(command.CommandId, Clip(title, 80), Clip(text, 500), DateTimeOffset.UtcNow);
        CurrentMessage = message;
        if (showMessage is not null)
            showMessage(message);
        else
            MessageChanged?.Invoke(message);
        return new CommandResult(command.CommandId, true, command.Describe());
    }

    private CommandResult Restart(RestartCommand command)
    {
        var delay = Math.Clamp(command.DelaySeconds, 5, 3600);
        var comment = command.Shutdown ? "MorniLAN: Der PC wird heruntergefahren." : "MorniLAN: Der PC wird neu gestartet.";
        var (code, output) = _run("shutdown", $"{(command.Shutdown ? "/s" : "/r")} /t {delay} /c \"{comment}\"", TimeSpan.FromSeconds(15));
        return new CommandResult(command.CommandId, code == 0, command.Describe(), code == 0 ? null : Tail(output));
    }

    internal static bool IsValidPackageId(string id) =>
        id.Length is > 0 and <= 128 && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '+');

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
    private static string Tail(string output) => Clip(output.Trim().Replace("\r\n", " ").Replace('\n', ' '), 300);

    private static (int, string) RunProcess(string file, string arguments, TimeSpan timeout)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "Zeitüberschreitung.");
        }
        return (process.ExitCode, output);
    }

    private static async Task<string> DownloadAsync(string url, string extension, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var file = Path.Combine(Path.GetTempPath(), $"mornilan-{Guid.NewGuid():N}{extension}");
        await using var response = await http.GetStreamAsync(url, cancellationToken);
        await using var target = File.Create(file);
        await response.CopyToAsync(target, cancellationToken);
        return file;
    }
}
