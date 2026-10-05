using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Kachel eines gekoppelten PCs in der Übersicht.</summary>
public sealed partial class DeviceViewModel(Guid deviceId, Func<Guid, Task> unpair) : ObservableObject
{
    public static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#3DDC84"));
    public static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#5C6575"));
    public static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public Guid DeviceId { get; } = deviceId;

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = OfflineBrush;
    [ObservableProperty] public partial string Details { get; set; } = "";
    [ObservableProperty] public partial string Cpu { get; set; } = "–";
    [ObservableProperty] public partial string Ram { get; set; } = "–";
    [ObservableProperty] public partial string Disk { get; set; } = "–";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnpairText))]
    public partial bool ConfirmingUnpair { get; set; }

    public string UnpairText => ConfirmingUnpair ? "Wirklich entfernen?" : "Entkoppeln";

    public void Update(DeviceSnapshot snapshot, DateTimeOffset now)
    {
        Name = snapshot.Info?.MachineName ?? snapshot.Device.MachineName;
        var online = snapshot.PresenceAt(now) == DevicePresence.Online;
        StatusBrush = online ? OnlineBrush : OfflineBrush;
        StatusText = online
            ? "Online"
            : snapshot.LastSeen is { } seen
                ? $"Offline · zuletzt gesehen {FormatTime(seen, now)}"
                : "Offline · noch nie verbunden";

        Details = string.Join(" · ", new[]
        {
            snapshot.Info?.Windows.FriendlyName,
            snapshot.Info is { } info ? $"Agent v{info.AgentVersion}" : null,
            snapshot.RemoteAddress,
        }.Where(s => !string.IsNullOrEmpty(s)));

        if (online && snapshot.Status is { } status)
        {
            Cpu = string.Create(German, $"{status.CpuPercent:0} %");
            Ram = $"{Bytes(status.RamUsedBytes)} / {Bytes(status.RamTotalBytes)}";
            Disk = $"{Bytes(status.SystemDriveFreeBytes)} frei von {Bytes(status.SystemDriveTotalBytes)}";
        }
        else
        {
            Cpu = Ram = Disk = "–";
        }
    }

    [RelayCommand]
    private async Task UnpairAsync()
    {
        // Zweistufig statt Dialog: erster Klick fragt nach, zweiter entfernt.
        if (!ConfirmingUnpair)
        {
            ConfirmingUnpair = true;
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => ConfirmingUnpair = false,
                TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        await unpair(DeviceId);
    }

    private static string Bytes(long bytes) =>
        bytes <= 0 ? "–" : string.Create(German, $"{bytes / 1024d / 1024 / 1024:0.0} GB");

    private static string FormatTime(DateTimeOffset seen, DateTimeOffset now)
    {
        var local = seen.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? $"um {local:HH:mm} Uhr"
            : $"am {local.ToString("dd.MM.yyyy HH:mm", German)}";
    }
}
