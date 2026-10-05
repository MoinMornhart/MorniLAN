using System.Text.Json.Serialization;

namespace MorniLAN.Shared.Models;

/// <summary>
/// Befehl vom Admin-Panel an den Agent. Polymorph serialisiert über das Feld "type".
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(InstallPackageCommand), "install")]
[JsonDerivedType(typeof(RestartCommand), "restart")]
[JsonDerivedType(typeof(ShowMessageCommand), "message")]
[JsonDerivedType(typeof(LockNowCommand), "lock")]
public abstract record AdminCommand
{
    public Guid CommandId { get; init; } = Guid.NewGuid();
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Programm per winget installieren.</summary>
public sealed record InstallPackageCommand(string WingetId) : AdminCommand;

/// <summary>PC neu starten (bzw. herunterfahren).</summary>
public sealed record RestartCommand(int DelaySeconds = 60, bool Shutdown = false) : AdminCommand;

/// <summary>Nachricht im Launcher anzeigen.</summary>
public sealed record ShowMessageCommand(string Title, string Text) : AdminCommand;

/// <summary>Sperren sofort (neu) anwenden.</summary>
public sealed record LockNowCommand : AdminCommand;

/// <summary>Ergebnis eines Befehls.</summary>
public sealed record CommandResult(Guid CommandId, bool Success, string? Message = null);
