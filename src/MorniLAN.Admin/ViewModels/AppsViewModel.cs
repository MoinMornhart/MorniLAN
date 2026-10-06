using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Models;
using Serilog;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Ein PC zur Auswahl auf der Seite „Freigaben“.</summary>
public sealed record DeviceChoice(Guid Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Ein Programm, Spiel oder eine Store-App in der Liste, mit Schalter „freigegeben“.</summary>
public sealed partial class AppItemViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly IBrush SteamBrush = new SolidColorBrush(Color.Parse("#4C8DFF"));
    private static readonly IBrush StoreBrush = new SolidColorBrush(Color.Parse("#B07CFF"));
    private static readonly IBrush ProgramBrush = new SolidColorBrush(Color.Parse("#5C6575"));
    private static readonly IBrush LauncherBrush = new SolidColorBrush(Color.Parse("#1F9D57"));
    private static readonly IBrush CustomBrush = new SolidColorBrush(Color.Parse("#B7791F"));

    private readonly Action<AppItemViewModel, bool> _setAllowed;
    private bool _syncing;

    public AppItemViewModel(AppEntry app, InventoryStore store, bool allowed, Action<AppItemViewModel, bool> setAllowed)
    {
        App = app;
        Icon = Load(store.ImagePath(app.IconHash));
        Cover = Load(store.ImagePath(app.CoverHash));
        _setAllowed = setAllowed;
        _syncing = true;
        IsAllowed = allowed;
        _syncing = false;
    }

    public AppEntry App { get; }
    public string Name => App.Name;
    public Bitmap? Icon { get; }
    public Bitmap? Cover { get; }
    public bool HasIcon => Icon is not null;
    public bool HasCover => Cover is not null;
    public bool IsCustom => App.Source == AppSource.Custom;

    /// <summary>Freigegeben (im Launcher sichtbar, ab M6 auch startbar). Ändern speichert sofort.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity), nameof(StateText))]
    public partial bool IsAllowed { get; set; }

    partial void OnIsAllowedChanged(bool value)
    {
        if (!_syncing)
            _setAllowed(this, value);
    }

    /// <summary>Stand von außen übernehmen, ohne erneut zu speichern.</summary>
    internal void Sync(bool allowed)
    {
        _syncing = true;
        IsAllowed = allowed;
        _syncing = false;
    }

    public double Opacity => App.IsSystemComponent || !IsAllowed ? 0.55 : 1.0;
    public string StateText => IsAllowed ? "freigegeben" : "gesperrt";

    public string Badge => App.Launcher ?? App.Source switch
    {
        AppSource.Steam => "Steam",
        AppSource.StoreApp => "Store",
        AppSource.Custom => "Eigener Eintrag",
        _ => App.IsSystemComponent ? "System" : "Programm",
    };

    public IBrush BadgeBrush => App.Launcher is not null
        ? LauncherBrush
        : App.Source switch
        {
            AppSource.Steam => SteamBrush,
            AppSource.StoreApp => StoreBrush,
            AppSource.Custom => CustomBrush,
            _ => ProgramBrush,
        };

    public string Details => string.Join(" · ", new[]
    {
        App.Publisher,
        App.Version is { Length: > 0 } v && App.Source != AppSource.StoreApp ? $"Version {v}" : null,
        App.SizeBytes is { } size and > 0 ? FormatSize(size) : null,
        App.Source is AppSource.InstalledProgram or AppSource.Custom ? App.ExecutablePath : null,
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
    Blocked,
}

/// <summary>Seite „Freigaben“: was auf dem PC installiert ist und was davon freigegeben ist.</summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private readonly Func<AdminServer?> _server;
    private List<AppItemViewModel> _all = [];
    private List<AppItemViewModel> _visible = [];
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

    /// <summary>Kommt der eingestellte Stand auf dem PC an?</summary>
    [ObservableProperty] public partial string PolicyStatus { get; set; } = "";
    [ObservableProperty] public partial IBrush PolicyBrush { get; set; } = DeviceViewModel.OfflineBrush;

    // Eigener Eintrag
    [ObservableProperty] public partial bool ShowCustomForm { get; set; }
    [ObservableProperty] public partial string CustomName { get; set; } = "";
    [ObservableProperty] public partial string CustomPath { get; set; } = "";
    [ObservableProperty] public partial string CustomArguments { get; set; } = "";
    [ObservableProperty] public partial string CustomError { get; set; } = "";

    public bool HasDevice => SelectedDevice is not null;

    partial void OnSelectedDeviceChanged(DeviceChoice? value)
    {
        OnPropertyChanged(nameof(HasDevice));
        LoadReport();
    }

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

    /// <summary>Freigaben geändert (hier oder bestätigt vom PC): Schalter und Status abgleichen.</summary>
    public void PolicyChanged(Guid deviceId)
    {
        if (deviceId != SelectedDevice?.Id || _server() is not { } server)
            return;
        var policy = server.Policies.Get(deviceId);
        foreach (var item in _all)
            item.Sync(policy.IsAllowed(item.App.Id));
        UpdatePolicyStatus();
        ApplyFilter();
    }

    /// <summary>Nur die Geräteauswahl abgleichen (billig, läuft regelmäßig). Lädt nur bei geänderter Auswahl.</summary>
    public void SyncDevices()
    {
        if (_server() is not { } server)
            return;
        var devices = server.Registry.Snapshot().Select(d => new DeviceChoice(d.Device.DeviceId,
            d.Info?.MachineName ?? d.Device.MachineName)).ToList();
        if (devices.SequenceEqual(Devices))
        {
            UpdatePolicyStatus(); // online/offline kann sich geändert haben
            return;
        }
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
            PolicyStatus = "";
            ApplyFilter();
            return;
        }
        var report = server.Inventory.Get(device.Id);
        var policy = server.Policies.Get(device.Id);
        var apps = (report?.Apps ?? []).ToList();
        // Eigene Einträge, die der PC noch nicht zurückgemeldet hat (offline), trotzdem zeigen
        foreach (var custom in policy.CustomApps.Where(c => apps.All(a => a.Id != c.Id)))
            apps.Add(new AppEntry(custom.Id, custom.Name, AppSource.Custom, ExecutablePath: custom.ExecutablePath,
                LaunchArguments: custom.Arguments));
        _all = apps.Select(a => new AppItemViewModel(a, server.Inventory, policy.IsAllowed(a.Id), SetAllowed)).ToList();
        _collectedAt = report?.CollectedAt;
        EmptyText = report is null
            ? $"„{device.Name}“ hat noch keine Programmliste geschickt. Ist der PC online? Sonst auf „Neu einlesen“ klicken."
            : "Nichts gefunden. Suche oder Filter anpassen.";
        UpdatePolicyStatus();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = (AppFilter)Math.Clamp(FilterIndex, 0, 4);
        var search = SearchText.Trim();
        _visible = _all.Where(i =>
                (ShowSystem || !i.App.IsSystemComponent || filter == AppFilter.Blocked)
                && (search.Length == 0 || i.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                       || (i.App.Publisher?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false))
                && filter switch
                {
                    AppFilter.Games => i.App.IsGame,
                    AppFilter.Programs => !i.App.IsGame && i.App.Source is AppSource.InstalledProgram or AppSource.Custom,
                    AppFilter.Store => i.App.Source == AppSource.StoreApp,
                    AppFilter.Blocked => !i.IsAllowed,
                    _ => true,
                })
            .ToList();

        Replace(Games, _visible.Where(i => i.App.IsGame));
        Replace(Apps, _visible.Where(i => !i.App.IsGame));
        HasGames = Games.Count > 0;
        HasApps = Apps.Count > 0;
        IsEmpty = _visible.Count == 0;
        if (IsEmpty && filter == AppFilter.Blocked && _all.Count > 0)
            EmptyText = "Nichts gesperrt: Auf diesem PC ist alles freigegeben.";

        var hiddenSystem = _all.Count(i => i.App.IsSystemComponent);
        var blocked = _all.Count(i => !i.IsAllowed);
        Summary = _all.Count == 0
            ? ""
            : $"{_visible.Count} von {_all.Count} Einträgen" +
              (blocked > 0 ? $" · {blocked} gesperrt" : " · alles freigegeben") +
              (ShowSystem || hiddenSystem == 0 ? "" : $" · {hiddenSystem} Systemkomponenten ausgeblendet") +
              (_collectedAt is { } at ? $" · Stand {at.ToLocalTime():dd.MM. HH:mm} Uhr" : "");
    }

    private void UpdatePolicyStatus()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
        {
            PolicyStatus = "";
            return;
        }
        var online = server.Registry.ConnectionIdOf(device.Id) is not null;
        if (server.Policies.Get(device.Id).Revision == 0)
        {
            PolicyStatus = "Noch nichts eingestellt: Alles ist freigegeben. Sperren per Schalter.";
            PolicyBrush = DeviceViewModel.OfflineBrush;
        }
        else if (server.Policies.IsApplied(device.Id))
        {
            PolicyStatus = "✓ Der PC hat die aktuellen Freigaben.";
            PolicyBrush = DeviceViewModel.OnlineBrush;
        }
        else
        {
            PolicyStatus = online
                ? "Wird an den PC übertragen …"
                : "Gespeichert. Der PC übernimmt die Änderungen, sobald er online ist.";
            PolicyBrush = DeviceViewModel.WarnBrush;
        }
    }

    private static void Replace(ObservableCollection<AppItemViewModel> target, IEnumerable<AppItemViewModel> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    private void SetAllowed(AppItemViewModel item, bool allowed) =>
        ChangePolicy((p, now) => p.WithAllowed([item.App.Id], allowed, now));

    /// <summary>Alle gerade sichtbaren Einträge (Suche und Filter) auf einmal.</summary>
    [RelayCommand]
    private void AllowVisible() => SetVisible(true);

    [RelayCommand]
    private void BlockVisible() => SetVisible(false);

    private void SetVisible(bool allowed)
    {
        var ids = _visible.Select(i => i.App.Id).ToList();
        if (ids.Count > 0)
            ChangePolicy((p, now) => p.WithAllowed(ids, allowed, now));
    }

    [RelayCommand]
    private void ToggleCustomForm()
    {
        ShowCustomForm = !ShowCustomForm;
        CustomError = "";
    }

    [RelayCommand]
    private void AddCustom()
    {
        if (CustomApp.Validate(CustomName, CustomPath) is { } error)
        {
            CustomError = error;
            return;
        }
        var app = new CustomApp(AppId.ForCustom(Guid.NewGuid()), CustomName.Trim(), CustomPath.Trim().Trim('"'),
            string.IsNullOrWhiteSpace(CustomArguments) ? null : CustomArguments.Trim());
        ChangePolicy((p, now) => p.WithCustomApp(app, now), reload: true);
        CustomName = CustomPath = CustomArguments = CustomError = "";
        ShowCustomForm = false;
    }

    [RelayCommand]
    private void RemoveCustom(AppItemViewModel item) =>
        ChangePolicy((p, now) => p.WithoutCustomApp(item.App.Id, now), reload: true);

    private void ChangePolicy(Func<AppPolicy, DateTimeOffset, AppPolicy> change, bool reload = false)
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
            return;
        var task = server.UpdatePolicyAsync(device.Id, change);
        if (reload)
            LoadReport();
        _ = task.ContinueWith(t => Log.Warning(t.Exception?.GetBaseException(), "Freigaben nicht übertragen"),
            TaskContinuationOptions.OnlyOnFaulted);
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
