using System.Text.Json.Serialization;

namespace MorniLAN.Shared.Models;

/// <summary>
/// Befehl vom Admin-Panel an den Agent. Polymorph serialisiert über das Feld "type".
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(InstallPackageCommand), "install")]
[JsonDerivedType(typeof(UninstallPackageCommand), "uninstall")]
[JsonDerivedType(typeof(InstallFromUrlCommand), "installUrl")]
[JsonDerivedType(typeof(RestartCommand), "restart")]
[JsonDerivedType(typeof(ShowMessageCommand), "message")]
[JsonDerivedType(typeof(LockNowCommand), "lock")]
public abstract record AdminCommand
{
    public Guid CommandId { get; init; } = Guid.NewGuid();
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Kurzer Text fürs Panel („Discord wird installiert …“).</summary>
    public abstract string Describe();
}

/// <summary>Programm per winget installieren.</summary>
/// <param name="WingetId">Paket-ID, z. B. "Valve.Steam".</param>
public sealed record InstallPackageCommand(string WingetId, string? Name = null) : AdminCommand
{
    public override string Describe() => $"{Name ?? WingetId} wird installiert";
}

/// <summary>Programm per winget deinstallieren.</summary>
public sealed record UninstallPackageCommand(string WingetId, string? Name = null) : AdminCommand
{
    public override string Describe() => $"{Name ?? WingetId} wird deinstalliert";
}

/// <summary>
/// Programm aus einer Download-Adresse holen und still installieren (Nutzer-Wunsch 2026-10-06). Nur https.
/// </summary>
/// <param name="Url">Direkter Link auf eine .exe oder .msi.</param>
/// <param name="Name">Anzeigename.</param>
public sealed record InstallFromUrlCommand(string Url, string Name) : AdminCommand
{
    public override string Describe() => $"{Name} wird heruntergeladen und installiert";
}

/// <summary>PC neu starten (bzw. herunterfahren).</summary>
public sealed record RestartCommand(int DelaySeconds = 60, bool Shutdown = false) : AdminCommand
{
    public override string Describe() => Shutdown ? "PC wird heruntergefahren" : "PC wird neu gestartet";
}

/// <summary>Nachricht im Launcher anzeigen.</summary>
public sealed record ShowMessageCommand(string Title, string Text) : AdminCommand
{
    public override string Describe() => "Nachricht wird angezeigt";
}

/// <summary>Sperren sofort (neu) anwenden.</summary>
public sealed record LockNowCommand : AdminCommand
{
    public override string Describe() => "Sperren werden angewandt";
}

/// <summary>Ergebnis eines Befehls (vom PC ans Panel).</summary>
public sealed record CommandResult(Guid CommandId, bool Success, string Description, string? Message = null);

/// <summary>Nachricht des Admins, die der Launcher anzeigt.</summary>
public sealed record AdminMessage(Guid Id, string Title, string Text, DateTimeOffset At);
