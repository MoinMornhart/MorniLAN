using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MorniLAN.Admin.Server;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Security;
using Serilog;

namespace MorniLAN.Admin.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly string[] Pages = ["Übersicht", "Freigaben", "Fernzugriff", "Aktionen", "Einstellungen"];
    private static readonly string[] Placeholders =
    [
        "",
        "Programme und Spiele freigeben folgt in Meilenstein 3 und 4.",
        "Fernzugriff mit Sunshine und Moonlight folgt in Meilenstein 7.",
        "Aktionen (Installieren, Nachricht, Neustart) folgen in Meilenstein 8.",
        "Einstellungen folgen mit dem Feinschliff in Meilenstein 11.",
    ];

    private readonly DispatcherTimer _presenceTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private AdminServer? _server;

    public MainViewModel()
    {
        _presenceTimer.Tick += (_, _) => Refresh();
    }

    public string Version => VersionInfo.Display;

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public ObservableCollection<PendingPairingViewModel> PendingPairings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(PageTitle), nameof(PagePlaceholder))]
    public partial int SelectedNavIndex { get; set; }

    public bool IsOverview => SelectedNavIndex <= 0;
    public string PageTitle => Pages[Math.Clamp(SelectedNavIndex, 0, Pages.Length - 1)];
    public string PagePlaceholder => Placeholders[Math.Clamp(SelectedNavIndex, 0, Placeholders.Length - 1)];

    [ObservableProperty]
    public partial string ServerStatus { get; set; } = "Server startet …";

    [ObservableProperty]
    public partial string ServerDetails { get; set; } = "";

    [ObservableProperty]
    public partial IBrush ServerBrush { get; set; } = Brushes.Gray;

    [ObservableProperty]
    public partial bool HasNoDevices { get; set; } = true;

    public async Task StartServerAsync()
    {
        try
        {
            var server = new AdminServer(new AdminServerOptions());
            await server.StartAsync();
            _server = server;
            server.Registry.Changed += OnServerChanged;
            server.Pairing.Changed += OnServerChanged;
            ServerStatus = $"Bereit – wartet auf Agents (Port {server.Port})";
            ServerDetails = $"Zertifikat {CertificateFingerprint.Short(server.Identity.Fingerprint)} · " +
                            $"erreichbar unter {string.Join(", ", NetworkInfo.AdminEndpoints())}";
            ServerBrush = DeviceViewModel.OnlineBrush;
            _presenceTimer.Start();
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Admin-Server konnte nicht starten");
            ServerStatus = "Server konnte nicht starten";
            ServerDetails = ex is IOException
                ? $"Port {MorniLanConstants.AdminPort} ist belegt. Läuft das Admin-Panel schon?"
                : ex.Message;
            ServerBrush = DeviceViewModel.ErrorBrush;
        }
    }

    private void OnServerChanged() => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (_server is not { } server)
            return;

        var now = DateTimeOffset.UtcNow;
        var devices = server.Registry.Snapshot();
        Sync(Devices, devices, d => d.Device.DeviceId, vm => vm.DeviceId,
            d => new DeviceViewModel(d.Device.DeviceId, server.UnpairAsync), (vm, d) => vm.Update(d, now));

        var pending = server.Pairing.Snapshot();
        Sync(PendingPairings, pending, p => p.RequestId, vm => vm.RequestId,
            p => new PendingPairingViewModel(p, server.Pairing), (vm, p) => vm.Update(p));

        HasNoDevices = Devices.Count == 0;
    }

    /// <summary>Liste abgleichen statt neu aufbauen, damit Eingaben (Code-Feld) erhalten bleiben.</summary>
    private static void Sync<TItem, TViewModel>(ObservableCollection<TViewModel> target, IReadOnlyList<TItem> source,
        Func<TItem, Guid> sourceKey, Func<TViewModel, Guid> targetKey, Func<TItem, TViewModel> create,
        Action<TViewModel, TItem> update)
    {
        var keys = source.Select(sourceKey).ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!keys.Contains(targetKey(target[i])))
                target.RemoveAt(i);

        foreach (var item in source)
        {
            var vm = target.FirstOrDefault(t => targetKey(t) == sourceKey(item));
            if (vm is null)
            {
                vm = create(item);
                target.Add(vm);
            }
            update(vm, item);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _presenceTimer.Stop();
        if (_server is { } server)
        {
            server.Registry.Changed -= OnServerChanged;
            server.Pairing.Changed -= OnServerChanged;
            await server.DisposeAsync();
        }
    }
}
