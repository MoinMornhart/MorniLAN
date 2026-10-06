using System.Text.Json;
using Microsoft.Win32;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Agent.Policy;

/// <summary>
/// Holt alle 2 s ab, was der Launcher in die Briefkästen der Benutzer gelegt hat (siehe <see cref="LauncherInbox"/>):
/// neue Profile und Hilfe-Anfragen. Alles darin ist Benutzereingabe: Größe, Inhalt und Häufigkeit werden begrenzt.
/// <para>
/// Der Dienst (SYSTEM) löscht dort bewusst nichts und folgt keinen Umleitungen (Junctions, Symlinks): Sonst könnte
/// ein Benutzer den Ordner auf einen Systemordner umbiegen und SYSTEM Dateien löschen lassen. Verarbeitete Dateien
/// merkt er sich, aufräumen tut der Launcher. Es zählen nur Dateien, die höchstens <see cref="MaxAge"/> alt sind.
/// </para>
/// </summary>
/// <param name="userFolders">Benutzerprofile, die durchsucht werden (für Tests; sonst alle aus der Registry).</param>
internal sealed class LauncherInboxService(
    AgentProfileStore profiles,
    AgentPolicyStore policy,
    ILogger<LauncherInboxService> logger,
    Func<IEnumerable<string>>? userFolders = null,
    TimeProvider? time = null,
    Remote.RemoteAccessService? remote = null) : BackgroundService
{
    internal static readonly TimeSpan HelpCooldown = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<IEnumerable<string>> _userFolders = userFolders ?? UserProfileFolders;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private HelpRequest? _pendingHelp;
    private HelpState? _help;

    /// <summary>Neue Hilfe-Anfrage, die noch zum Panel muss.</summary>
    public event Action<HelpRequest>? HelpRequested;

    /// <summary>Für die Statuszeile des Launchers.</summary>
    public HelpState? Help
    {
        get
        {
            lock (_lock)
                return _help;
        }
    }

    /// <summary>Wartende Hilfe-Anfrage (wird nach dem Verbinden nachgeschickt).</summary>
    public HelpRequest? PendingHelp
    {
        get
        {
            lock (_lock)
                return _pendingHelp;
        }
    }

    public void MarkDelivered(string requestId)
    {
        lock (_lock)
        {
            if (_pendingHelp?.Id == requestId)
                _pendingHelp = null;
            if (_help?.RequestId == requestId)
                _help = _help with { Delivered = true };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), _time);
        try
        {
            do
            {
                try { ProcessOnce(); remote?.Tick(); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Briefkasten des Launchers: {Error}", ex.Message);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Dienst wird beendet
        }
    }

    /// <summary>Einmal alle Briefkästen leeren.</summary>
    internal void ProcessOnce()
    {
        var now = _time.GetUtcNow();
        foreach (var stale in _seen.Where(s => now - s.Value > MaxAge * 2).Select(s => s.Key).ToList())
            _seen.Remove(stale);

        foreach (var user in _userFolders())
        {
            var folder = LauncherInbox.FolderFor(user);
            if (!IsPlainFolderChain(user, folder))
                continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(20).ToList())
            {
                if (_seen.ContainsKey(file))
                    continue;
                _seen[file] = now;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > LauncherInbox.MaxFileBytes
                        || now - info.LastWriteTimeUtc > MaxAge)
                        continue;
                    Handle(info.Name, File.ReadAllBytes(file));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // kaputt oder halb geschrieben: ignorieren, der Launcher räumt auf
                }
            }
        }
    }

    /// <summary>Weder der Briefkasten noch ein Ordner darüber (bis zum Benutzerprofil) ist eine Umleitung.</summary>
    internal static bool IsPlainFolderChain(string userFolder, string folder)
    {
        var root = Path.GetFullPath(userFolder).TrimEnd('\\');
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
        {
            if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;
            if (string.Equals(current.FullName.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void Handle(string name, byte[] data)
    {
        if (name.StartsWith(LauncherInbox.ProfilePrefix, StringComparison.Ordinal))
        {
            var request = JsonSerializer.Deserialize(data, MorniLanJsonContext.Default.ProfileRequest);
            if (request is null)
                return;
            if (policy.Current.BlockProfileCreation)
            {
                logger.LogInformation("Neues Profil abgelehnt: Der Admin hat das Anlegen am PC gesperrt");
                return;
            }
            if (profiles.Add(request.Name, request.Color) is { } profile)
                logger.LogInformation("Profil „{Name}“ im Launcher angelegt", profile.Name);
        }
        else if (name.StartsWith(LauncherInbox.EditPrefix, StringComparison.Ordinal))
        {
            var edit = JsonSerializer.Deserialize(data, MorniLanJsonContext.Default.ProfileEdit);
            if (edit is not null && profiles.Edit(edit))
                logger.LogInformation("Profil {Id} im Launcher angepasst", edit.ProfileId);
        }
        else if (name.StartsWith(LauncherInbox.RemotePrefix, StringComparison.Ordinal))
        {
            var consent = JsonSerializer.Deserialize(data, MorniLanJsonContext.Default.RemoteConsent);
            if (consent is not null)
                remote?.Consent(consent.Allow);
        }
        else if (name.StartsWith(LauncherInbox.HelpPrefix, StringComparison.Ordinal))
        {
            var message = JsonSerializer.Deserialize(data, MorniLanJsonContext.Default.HelpMessage);
            var now = _time.GetUtcNow();
            HelpRequest request;
            lock (_lock)
            {
                if (_help is { } last && now - last.RequestedAt < HelpCooldown)
                    return; // Mehrfachdrücken: eine Anfrage pro Minute reicht
                var profileName = message?.ProfileName?.Trim();
                if (profileName is { Length: > LauncherProfile.MaxNameLength })
                    profileName = profileName[..LauncherProfile.MaxNameLength];
                request = new HelpRequest(Guid.NewGuid().ToString("N"), profileName, now);
                _pendingHelp = request;
                _help = new HelpState(request.Id, now, Delivered: false);
            }
            logger.LogInformation("Hilfe angefordert ({Profile})", request.ProfileName ?? "ohne Profil");
            HelpRequested?.Invoke(request);
        }
    }

    /// <summary>Ordner aller echten Benutzerprofile (S-1-5-21-…) aus der Registry.</summary>
    private static IEnumerable<string> UserProfileFolders()
    {
        using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (list is null)
            yield break;
        foreach (var sid in list.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal)))
        {
            using var key = list.OpenSubKey(sid);
            if (key?.GetValue("ProfileImagePath") is string path && Directory.Exists(path))
                yield return path;
        }
    }
}
