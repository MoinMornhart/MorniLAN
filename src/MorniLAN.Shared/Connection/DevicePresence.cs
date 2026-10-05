namespace MorniLAN.Shared.Connection;

public enum DevicePresence
{
    Offline,
    Online,
}

public static class PresenceRules
{
    /// <summary>
    /// Online nur bei offener Verbindung und letztem Lebenszeichen innerhalb von
    /// <see cref="ConnectionDefaults.OfflineAfter"/>. Eine hängende Verbindung zählt also als offline.
    /// </summary>
    public static DevicePresence Evaluate(bool connected, DateTimeOffset? lastSeen, DateTimeOffset now) =>
        connected && lastSeen is { } seen && now - seen <= ConnectionDefaults.OfflineAfter
            ? DevicePresence.Online
            : DevicePresence.Offline;
}
