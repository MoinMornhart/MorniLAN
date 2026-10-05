namespace MorniLAN.Shared.Models;

/// <summary>Statische Infos über einen verwalteten PC.</summary>
public sealed record DeviceInfo(
    Guid DeviceId,
    string MachineName,
    WindowsEditionInfo Windows,
    string AgentVersion);

/// <summary>Laufender Status, wird per Heartbeat gemeldet.</summary>
public sealed record DeviceStatus(
    Guid DeviceId,
    DateTimeOffset Timestamp,
    double CpuPercent,
    long RamUsedBytes,
    long RamTotalBytes,
    long SystemDriveFreeBytes,
    long SystemDriveTotalBytes,
    string? ForegroundApp,
    string? LoggedOnUser,
    DateTimeOffset? LastUserActivity,
    bool RemoteSessionActive);

/// <summary>Update-Kanal für Auto-Updates.</summary>
public enum UpdateChannel
{
    Stable,
    Beta,
}
