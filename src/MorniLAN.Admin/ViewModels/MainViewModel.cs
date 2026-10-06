using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Platform;
using MorniLAN.Admin.Server;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Security;
using Serilog;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Eine eigene Adresse des Admin-PCs zum Abschreiben ins Geräte-Setup.</summary>
public sealed record AddressItem(string Kind, string Address);

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int AppsPage = 1;
    private const int RemotePage = 2;
    private const int DiagnosticsPage = 4;
    private const int SettingsPage = 5;

    private static readonly string[] Pages = ["Übersicht", "Freigaben", "Fernzugriff", "Aktionen", "Diagnose", "Einstellungen"];
    private static readonly string[] Placeholders =
    [
        "",
        "",
        "Fernzugriff mit Sunshine und Moonlight folgt in Meilenstein 7.",
        "Aktionen (Installieren, Nachricht, Neustart) folgen in Meilenstein 8.",
        "",
        "",
    ];

    private readonly DispatcherTimer _presenceTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private AdminSettings _settings = AdminSettings.Load();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly AdminUpdater _updater = new();
    private AdminServer? _server;

    public MainViewModel()
    {
        _presenceTimer.Tick += (_, _) => Refresh();
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        Diagnostics = new DiagnosticsViewModel(() => _server);
        Apps = new AppsViewModel(() => _server);
        Remote = new RemoteViewModel(() => _server);
        StartWithWindows = Autostart.IsEnabled();
        KeepRunningInTray = _settings.KeepRunningInTray;
    }

    public string Version => VersionInfo.Display;

    /// <summary>Von der App gesetzt: das Panel beenden (nach dem Start eines Updates).</summary>
    public Action? RequestExit { get; set; }

    /// <summary>Vom Fenster gesetzt: Text in die Zwischenablage legen.</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public ObservableCollection<PendingPairingViewModel> PendingPairings { get; } = [];
    public ObservableCollection<AddressItem> Addresses { get; } = [];
    public DiagnosticsViewModel Diagnostics { get; }
    public AppsViewModel Apps { get; }
    public RemoteViewModel Remote { get; }

    // --- Navigation ---------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsApps), nameof(IsRemote), nameof(IsDiagnostics), nameof(IsSettings), nameof(IsPlaceholder),
        nameof(PageTitle), nameof(PagePlaceholder))]
    public partial int SelectedNavIndex { get; set; }

    public bool IsOverview => SelectedNavIndex <= 0;
    public bool IsApps => SelectedNavIndex == AppsPage;
    public bool IsRemote => SelectedNavIndex == RemotePage;
    public bool IsDiagnostics => SelectedNavIndex == DiagnosticsPage;
    public bool IsSettings => SelectedNavIndex == SettingsPage;
    public bool IsPlaceholder => !IsOverview && !IsApps && !IsRemote && !IsDiagnostics && !IsSettings;
    public string PageTitle => Pages[Math.Clamp(SelectedNavIndex, 0, Pages.Length - 1)];
    public string PagePlaceholder => Placeholders[Math.Clamp(SelectedNavIndex, 0, Placeholders.Length - 1)];

    partial void OnSelectedNavIndexChanged(int value)
    {
        if (value == DiagnosticsPage)
            _ = Diagnostics.RefreshAsync();
        if (value == AppsPage)
            Apps.Reload();
    }

    // --- Server ---------------------------------------------------------------

    [ObservableProperty] public partial string ServerStatus { get; set; } = "Server startet …";
    [ObservableProperty] public partial string ServerDetails { get; set; } = "";
    [ObservableProperty] public partial IBrush ServerBrush { get; set; } = Brushes.Gray;
    [ObservableProperty] public partial bool HasNoDevices { get; set; } = true;

    public async Task StartServerAsync()
    {
        try
        {
            var server = new AdminServer(new AdminServerOptions());
            await server.StartAsync();
            _server = server;
            server.Registry.Changed += OnServerChanged;
            server.Pairing.Changed += OnServerChanged;
            server.Inventory.Changed += OnInventoryChanged;
            server.Policies.Changed += OnPolicyChanged;
            server.Profiles.Changed += OnProfilesChanged;
            server.Accounts.Changed += OnAccountsChanged;
            server.Remote.Changed += OnRemoteChanged;
            server.Help.Changed += OnHelpChanged;
            ServerStatus =$"Bereit – wartet auf PCs (Port {server.Port})";
            ServerDetails = $"Name im Netzwerk {server.Identity.Name} · Zertifikat {CertificateFingerprint.Short(server.Identity.Fingerprint)}";
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
        RefreshAddresses();
        await CheckFirewallAsync();
        _updateTimer.Start();
        await Task.Delay(TimeSpan.FromSeconds(10));
        await CheckForUpdatesAsync();
    }

    // --- Firewall ---------------------------------------------------------------

    [ObservableProperty] public partial bool FirewallMissing { get; set; }
    [ObservableProperty] public partial string FirewallText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetupFirewallCommand))]
    public partial bool FirewallBusy { get; set; }

    private async Task CheckFirewallAsync()
    {
        var rules = await Task.Run(Firewall.Check);
        var missing = rules.Count(r => !r.Present);
        FirewallMissing = missing > 0;
        FirewallText = missing == 0
            ? "Firewall ist eingerichtet."
            : $"Firewall noch nicht eingerichtet ({missing} von {rules.Count} Regeln fehlen). Andere PCs erreichen das Panel sonst nicht.";
    }

    private bool CanSetupFirewall() => !FirewallBusy;

    [RelayCommand(CanExecute = nameof(CanSetupFirewall))]
    private async Task SetupFirewallAsync()
    {
        FirewallBusy = true;
        try
        {
            var ok = await Firewall.SetupElevatedAsync();
            await CheckFirewallAsync();
            if (!ok && FirewallMissing)
                FirewallText = "Firewall wurde nicht eingerichtet. Bitte die Windows-Abfrage mit „Ja“ bestätigen.";
            Log.Information("Firewall-Einrichtung: {Result}", FirewallMissing ? "unvollständig" : "erfolgreich");
        }
        finally
        {
            FirewallBusy = false;
        }
    }

    // --- Adressen (Gerät manuell hinzufügen) ----------------------------------------

    private void RefreshAddresses()
    {
        Addresses.Clear();
        foreach (var address in NetworkInfo.LocalIPv4())
            Addresses.Add(new AddressItem(address.Kind, address.Address.ToString()));
        Addresses.Add(new AddressItem("Rechnername", Environment.MachineName));
    }

    [RelayCommand]
    private async Task CopyAsync(string text)
    {
        if (CopyToClipboard is { } copy)
            await copy(text);
    }

    // --- Updates -------------------------------------------------------------------

    [ObservableProperty] public partial bool AdminUpdateAvailable { get; set; }
    [ObservableProperty] public partial string AdminUpdateText { get; set; } = "";
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "Noch nicht nach Updates gesucht.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallAdminUpdateCommand), nameof(CheckForUpdatesCommand))]
    public partial bool UpdateBusy { get; set; }

    private bool CanUseUpdates() => !UpdateBusy;

    [RelayCommand(CanExecute = nameof(CanUseUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        UpdateBusy = true;
        try
        {
            UpdateStatus = "Suche nach Updates …";
            await _updater.CheckAsync();
            AdminUpdateAvailable = _updater.AdminUpdate is not null;
            AdminUpdateText = _updater.AdminUpdate is { } update
                ? $"MorniLAN Admin v{update.Version} ist verfügbar (installiert: v{AdminUpdater.CurrentVersion})."
                : "";
            UpdateStatus = (_updater.AdminUpdate is { } u ? $"Update auf v{u.Version} verfügbar." : $"Das Panel ist aktuell (v{AdminUpdater.CurrentVersion}).")
                           + $" Geprüft {DateTime.Now:HH:mm} Uhr. Betas werden mit angeboten.";
            Refresh();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            UpdateStatus = $"GitHub nicht erreichbar ({ex.Message}). Nächster Versuch automatisch.";
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseUpdates))]
    private async Task InstallAdminUpdateAsync()
    {
        if (_updater.AdminUpdate is not { } update)
            return;
        UpdateBusy = true;
        try
        {
            AdminUpdateText = $"Lade v{update.Version} und prüfe die Prüfsumme …";
            if (await _updater.PrepareAndLaunchAsync(update))
            {
                AdminUpdateText = "Update wird installiert, das Panel startet gleich neu …";
                RequestExit?.Invoke();
            }
        }
        catch (Exception ex)
        {
            AdminUpdateText = $"Update fehlgeschlagen: {ex.Message}";
            Log.Warning(ex, "Admin-Update fehlgeschlagen");
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    // --- Einstellungen ------------------------------------------------------------

    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool KeepRunningInTray { get; set; }
    [ObservableProperty] public partial string SettingsMessage { get; set; } = "";

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (value == Autostart.IsEnabled())
            return;
        try
        {
            Autostart.Set(value);
            SettingsMessage = value ? "Das Panel startet jetzt mit Windows (im Infobereich)." : "Autostart ist aus.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            SettingsMessage = $"Autostart konnte nicht geändert werden: {ex.Message}";
        }
    }

    partial void OnKeepRunningInTrayChanged(bool value)
    {
        if (_settings.KeepRunningInTray == value)
            return;
        _settings = _settings with { KeepRunningInTray = value };
        _settings.Save();
    }

    // --- Laufende Aktualisierung ----------------------------------------------------

    private void OnServerChanged() => Dispatcher.UIThread.Post(Refresh);

    private void OnInventoryChanged(Guid deviceId) => Dispatcher.UIThread.Post(() => Apps.Reload(deviceId));

    private void OnPolicyChanged(Guid deviceId) => Dispatcher.UIThread.Post(() => Apps.PolicyChanged(deviceId));

    private void OnProfilesChanged(Guid deviceId) => Dispatcher.UIThread.Post(() => Apps.ProfilesChanged(deviceId));

    private void OnAccountsChanged(Guid deviceId) => Dispatcher.UIThread.Post(() => Apps.AccountsChanged(deviceId));

    private void OnRemoteChanged(Guid deviceId) => Dispatcher.UIThread.Post(() => Remote.RemoteChanged(deviceId));

    /// <summary>Offene Hilfe-Anfragen (Banner auf der Übersicht). Eine neue holt das Fenster nach vorne.</summary>
    public ObservableCollection<HelpNoticeViewModel> HelpNotices { get; } = [];

    [ObservableProperty] public partial bool HasHelpNotices { get; set; }

    /// <summary>Fenster zeigen (gesetzt von App), z. B. bei einer Hilfe-Anfrage aus dem Infobereich.</summary>
    public Action? RequestShow { get; set; }

    private void OnHelpChanged(HelpNotice? added) => Dispatcher.UIThread.Post(() =>
    {
        if (_server is not { } server)
            return;
        HelpNotices.Clear();
        foreach (var notice in server.Help.Open.OrderByDescending(n => n.ReceivedAt))
            HelpNotices.Add(new HelpNoticeViewModel(notice));
        HasHelpNotices = HelpNotices.Count > 0;
        if (added is not null)
        {
            SelectedNavIndex = 0; // Übersicht
            RequestShow?.Invoke();
        }
    });

    [RelayCommand]
    private void DismissHelp(HelpNoticeViewModel notice) => _server?.Help.Dismiss(notice.Notice.Request.Id);

    private void Refresh()
    {
        if (_server is not { } server)
            return;

        var now = DateTimeOffset.UtcNow;
        var devices = server.Registry.Snapshot();
        Sync(Devices, devices, d => d.Device.DeviceId, vm => vm.DeviceId,
            d => new DeviceViewModel(d.Device.DeviceId, server.UnpairAsync, server.RequestDeviceUpdateAsync),
            (vm, d) => vm.Update(d, now, _updater.DeviceUpdateFor(d.Info?.AgentVersion)));

        var pending = server.Pairing.Snapshot();
        Sync(PendingPairings, pending, p => p.RequestId, vm => vm.RequestId,
            p => new PendingPairingViewModel(p, server.Pairing), (vm, p) => vm.Update(p));

        HasNoDevices = Devices.Count == 0;
        Apps.SyncDevices();
        Remote.SyncDevices();
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
        _updateTimer.Stop();
        if (_server is { } server)
        {
            server.Registry.Changed -= OnServerChanged;
            server.Pairing.Changed -= OnServerChanged;
            server.Inventory.Changed -= OnInventoryChanged;
            server.Policies.Changed -= OnPolicyChanged;
            server.Profiles.Changed -= OnProfilesChanged;
            server.Accounts.Changed -= OnAccountsChanged;
            server.Remote.Changed -= OnRemoteChanged;
            server.Help.Changed -= OnHelpChanged;
            await server.DisposeAsync();
        }
    }
}
