using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Models;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Ein PC zur Auswahl auf der Seite „Freigaben“.</summary>
public sealed record DeviceChoice(Guid Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Ein Programm, Spiel oder eine Store-App in der Liste.</summary>
public sealed class AppItemViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly IBrush SteamBrush = new SolidColorBrush(Color.Parse("#4C8DFF"));
    private static readonly IBrush StoreBrush = new SolidColorBrush(Color.Parse("#B07CFF"));
    private static readonly IBrush ProgramBrush = new SolidColorBrush(Color.Parse("#5C6575"));

    public AppItemViewModel(AppEntry app, InventoryStore store)
    {
        App = app;
        Icon = Load(store.ImagePath(app.IconHash));
        Cover = Load(store.ImagePath(app.CoverHash));
    }

    public AppEntry App { get; }
    public string Name => App.Name;
    public Bitmap? Icon { get; }
    public Bitmap? Cover { get; }
    public bool HasIcon => Icon is not null;
    public bool HasCover => Cover is not null;
    public double Opacity => App.IsSystemComponent ? 0.55 : 1.0;

    public string Badge => App.Source switch
    {
        AppSource.Steam => "Steam",
        AppSource.StoreApp => "Store",
        AppSource.Custom => "Eigener Eintrag",
        _ => App.IsSystemComponent ? "System" : "Programm",
    };

    public IBrush BadgeBrush => App.Source switch
    {
        AppSource.Steam => SteamBrush,
        AppSource.StoreApp => StoreBrush,
        _ => ProgramBrush,
    };

    public string Details => string.Join(" · ", new[]
    {
        App.Publisher,
        App.Version is { Length: > 0 } v && App.Source != AppSource.StoreApp ? $"Version {v}" : null,
        App.SizeBytes is { } size and > 0 ? FormatSize(size) : null,
        App.Source == AppSource.InstalledProgram ? App.ExecutablePath : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    private static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Create(German, $"{bytes / 1024d / 1024 / 1024:0.0} GB")
        : string.Create(German, $"{bytes / 1024d / 1024:0} MB");

    private static Bitmap? Load(string? path)
    {
        if (path is null)
            return null;
        try { return new Bitmap(path); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { return null; }
    }
}

public enum AppFilter
{
    All,
    Games,
    Programs,
    Store,
}

/// <summary>Seite „Freigaben“: was auf dem PC installiert ist. Die Schalter zum Freigeben kommen in M4.</summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private readonly Func<AdminServer?> _server;
    private List<AppItemViewModel> _all = [];
    private DateTimeOffset? _collectedAt;

    public AppsViewModel(Func<AdminServer?> server)
    {
        _server = server;
    }

    public ObservableCollection<DeviceChoice> Devices { get; } = [];
    public ObservableCollection<AppItemViewModel> Games { get; } = [];
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    [ObservableProperty] public partial DeviceChoice? SelectedDevice { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int FilterIndex { get; set; }
    [ObservableProperty] public partial bool ShowSystem { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial string EmptyText { get; set; } = "";
    [ObservableProperty] public partial bool HasGames { get; set; }
    [ObservableProperty] public partial bool HasApps { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;
    [ObservableProperty] public partial string RefreshMessage { get; set; } = "";

    partial void OnSelectedDeviceChanged(DeviceChoice? value) => LoadReport();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterIndexChanged(int value) => ApplyFilter();
    partial void OnShowSystemChanged(bool value) => ApplyFilter();

    /// <summary>Geräteliste und Inhalt neu laden (Seite geöffnet, oder für ein Gerät kam eine neue Liste).</summary>
    public void Reload(Guid? changedDevice = null)
    {
        SyncDevices();
        if (changedDevice is null || changedDevice == SelectedDevice?.Id)
            LoadReport();
    }

    /// <summary>Nur die Geräteauswahl abgleichen (billig, läuft regelmäßig). Lädt nur bei geänderter Auswahl.</summary>
    public void SyncDevices()
    {
        if (_server() is not { } server)
            return;
        var devices = server.Registry.Snapshot().Select(d => new DeviceChoice(d.Device.DeviceId,
            d.Info?.MachineName ?? d.Device.MachineName)).ToList();
        if (devices.SequenceEqual(Devices))
            return;
        var selected = SelectedDevice?.Id;
        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selected) ?? Devices.FirstOrDefault();
        if (SelectedDevice is null)
            LoadReport();
    }

    private void LoadReport()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
        {
            _all = [];
            _collectedAt = null;
            EmptyText = "Noch kein PC gekoppelt. Sobald einer gekoppelt ist, erscheinen hier seine Programme und Spiele.";
            ApplyFilter();
            return;
        }
        var report = server.Inventory.Get(device.Id);
        _all = report?.Apps.Select(a => new AppItemViewModel(a, server.Inventory)).ToList() ?? [];
        _collectedAt = report?.CollectedAt;
        EmptyText = report is null
            ? $"„{device.Name}“ hat noch keine Programmliste geschickt. Ist der PC online? Sonst auf „Neu einlesen“ klicken."
            : "Nichts gefunden. Suche oder Filter anpassen.";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = (AppFilter)Math.Clamp(FilterIndex, 0, 3);
        var search = SearchText.Trim();
        var visible = _all.Where(i =>
                (ShowSystem || !i.App.IsSystemComponent)
                && (search.Length == 0 || i.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                       || (i.App.Publisher?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false))
                && filter switch
                {
                    AppFilter.Games => i.App.Source == AppSource.Steam,
                    AppFilter.Programs => i.App.Source is AppSource.InstalledProgram or AppSource.Custom,
                    AppFilter.Store => i.App.Source == AppSource.StoreApp,
                    _ => true,
                })
            .ToList();

        Replace(Games, visible.Where(i => i.App.Source == AppSource.Steam));
        Replace(Apps, visible.Where(i => i.App.Source != AppSource.Steam));
        HasGames = Games.Count > 0;
        HasApps = Apps.Count > 0;
        IsEmpty = visible.Count == 0;

        var hiddenSystem = _all.Count(i => i.App.IsSystemComponent);
        Summary = _all.Count == 0
            ? ""
            : $"{visible.Count} von {_all.Count} Einträgen" +
              (ShowSystem || hiddenSystem == 0 ? "" : $" · {hiddenSystem} Systemkomponenten ausgeblendet") +
              (_collectedAt is { } at ? $" · Stand {at.ToLocalTime():dd.MM. HH:mm} Uhr" : "");
    }

    private static void Replace(ObservableCollection<AppItemViewModel> target, IEnumerable<AppItemViewModel> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
            return;
        RefreshMessage = await server.RequestInventoryRefreshAsync(device.Id)
            ? "Wird auf dem PC neu eingelesen …"
            : "Der PC ist gerade nicht online.";
        _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Dispatcher.UIThread.Post(() => RefreshMessage = ""),
            TaskScheduler.Default);
    }
}
