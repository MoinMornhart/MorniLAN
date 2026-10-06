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

/// <summary>Für wen die Schalter gelten: alle Profile (Regeln des PCs) oder ein einzelnes Profil.</summary>
public sealed record ScopeChoice(string? ProfileId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Ein Windows-Konto mit Schalter „einschränken“.</summary>
public sealed partial class AccountRowViewModel : ObservableObject
{
    private readonly Action<AccountRowViewModel, bool> _setRestricted;
    private bool _syncing;

    public AccountRowViewModel(LocalAccount account, bool restricted, Action<AccountRowViewModel, bool> setRestricted)
    {
        Account = account;
        _setRestricted = setRestricted;
        _syncing = true;
        IsRestricted = restricted && !account.IsAdministrator;
        _syncing = false;
    }

    public LocalAccount Account { get; }
    public string Name => Account.Name;
    public bool CanRestrict => !Account.IsAdministrator;
    public string Role => Account.IsAdministrator ? "Administrator – wird nie eingeschränkt" : "Standardkonto";

    [ObservableProperty] public partial bool IsRestricted { get; set; }

    partial void OnIsRestrictedChanged(bool value)
    {
        if (!_syncing)
            _setRestricted(this, value);
    }

    internal void Sync(bool restricted)
    {
        _syncing = true;
        IsRestricted = restricted && !Account.IsAdministrator;
        _syncing = false;
    }
}

/// <summary>Ein Windows-Bereich (z. B. Eingabeaufforderung) mit Schalter „sperren“.</summary>
public sealed partial class AreaRowViewModel : ObservableObject
{
    private readonly Action<string, bool> _setBlocked;
    private bool _syncing;

    public AreaRowViewModel(string area, bool blocked, Action<string, bool> setBlocked)
    {
        Area = area;
        Name = WindowsAreas.Describe(area);
        _setBlocked = setBlocked;
        _syncing = true;
        IsBlocked = blocked;
        _syncing = false;
    }

    public string Area { get; }
    public string Name { get; }

    [ObservableProperty] public partial bool IsBlocked { get; set; }

    partial void OnIsBlockedChanged(bool value)
    {
        if (!_syncing)
            _setBlocked(Area, value);
    }

    internal void Sync(bool blocked)
    {
        _syncing = true;
        IsBlocked = blocked;
        _syncing = false;
    }
}

/// <summary>Ein Profil in der Verwaltung.</summary>
public sealed class ProfileRowViewModel(LauncherProfile profile, int ownRules)
{
    public LauncherProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Initial => Profile.Name.Length > 0 ? char.ToUpperInvariant(Profile.Name[0]).ToString() : "?";
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(ProfileColors.Normalize(profile.Color)));
    public bool HasPassword => Profile.HasPassword;
    public string Details => (Profile.HasPassword ? "🔒 mit Passwort · " : "") + ownRules switch
    {
        0 => "folgt den Freigaben des PCs",
        1 => "1 eigene Abweichung",
        _ => $"{ownRules} eigene Abweichungen",
    };
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
    public ObservableCollection<ScopeChoice> Scopes { get; } = [];
    public ObservableCollection<ProfileRowViewModel> ProfileRows { get; } = [];
    public ObservableCollection<AccountRowViewModel> Accounts { get; } = [];
    public ObservableCollection<AreaRowViewModel> Areas { get; } = [];

    [ObservableProperty] public partial bool HasAccounts { get; set; }
    [ObservableProperty] public partial string RestrictionStatus { get; set; } = "";
    [ObservableProperty] public partial IBrush RestrictionBrush { get; set; } = DeviceViewModel.OfflineBrush;
    private bool _syncingRestrictions;

    /// <summary>Konten oder Sperren-Stand eines PCs haben sich geändert.</summary>
    public void AccountsChanged(Guid deviceId)
    {
        if (deviceId == SelectedDevice?.Id)
            LoadRestrictions();
    }

    private void LoadRestrictions()
    {
        _syncingRestrictions = true;
        try
        {
            Accounts.Clear();
            Areas.Clear();
            if (_server() is not { } server || SelectedDevice is not { } device)
            {
                HasAccounts = false;
                RestrictionStatus = "";
                return;
            }
            var policy = server.Policies.Get(device.Id);
            foreach (var account in server.Accounts.Get(device.Id))
                Accounts.Add(new AccountRowViewModel(account, policy.IsRestricted(account.Sid), SetRestricted));
            foreach (var area in WindowsAreas.All)
                Areas.Add(new AreaRowViewModel(area, (policy.BlockedAreas ?? []).Contains(area), SetAreaBlocked));
            HasAccounts = Accounts.Count > 0;
            UpdateRestrictionStatus();
        }
        finally
        {
            _syncingRestrictions = false;
        }
    }

    private void UpdateRestrictionStatus()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
        {
            RestrictionStatus = "";
            return;
        }
        var state = server.Accounts.State(device.Id);
        var restricted = Accounts.Count(a => a.IsRestricted);
        if (!string.IsNullOrEmpty(state.Message))
        {
            RestrictionStatus = state.Message;
            RestrictionBrush = state.Applied ? DeviceViewModel.WarnBrush : DeviceViewModel.ErrorBrush;
        }
        else if (restricted == 0)
        {
            RestrictionStatus = "Kein Konto eingeschränkt. Alle nutzen Windows ganz normal.";
            RestrictionBrush = DeviceViewModel.OfflineBrush;
        }
        else
        {
            RestrictionStatus = $"{restricted} Konto(s) eingeschränkt. Wirkt, sobald der PC online ist.";
            RestrictionBrush = DeviceViewModel.OnlineBrush;
        }
    }

    private void SetRestricted(AccountRowViewModel row, bool restricted)
    {
        if (!_syncingRestrictions)
            ChangePolicy((p, now) => p.WithRestrictedAccount(row.Account.Sid, restricted, now));
    }

    private void SetAreaBlocked(string area, bool blocked)
    {
        if (!_syncingRestrictions)
            ChangePolicy((p, now) => p.WithBlockedArea(area, blocked, now));
    }

    [RelayCommand]
    private async Task ApplyRestrictionsAsync()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
            return;
        RestrictionStatus = await server.ApplyRestrictionsAsync(device.Id)
            ? "Sperren werden auf dem PC angewandt …"
            : "Der PC ist gerade nicht online.";
    }

    /// <summary>Für wen die Schalter gerade gelten (null-Profil = alle Profile, also der PC).</summary>
    [ObservableProperty] public partial ScopeChoice? SelectedScope { get; set; }
    [ObservableProperty] public partial bool HasProfiles { get; set; }
    [ObservableProperty] public partial string NewProfileName { get; set; } = "";
    [ObservableProperty] public partial string ProfileMessage { get; set; } = "";
    [ObservableProperty] public partial bool ProfileCreationAllowed { get; set; } = true;
    [ObservableProperty] public partial string ScopeHint { get; set; } = "";
    private bool _syncingProfiles;

    private string? ScopeProfileId => SelectedScope?.ProfileId;

    partial void OnSelectedScopeChanged(ScopeChoice? value)
    {
        if (_syncingProfiles)
            return;
        ScopeHint = value?.ProfileId is null
            ? "Die Schalter gelten für alle Profile, solange ein Profil nichts Eigenes hat."
            : $"Die Schalter gelten nur für „{value.Name}“. Was hier gleich wie beim PC ist, folgt automatisch dem PC.";
        PolicyChanged(SelectedDevice?.Id ?? Guid.Empty);
    }

    partial void OnProfileCreationAllowedChanged(bool value)
    {
        if (!_syncingProfiles)
            ChangePolicy((p, now) => p.WithProfileCreationBlocked(!value, now));
    }

    /// <summary>Profile eines PCs haben sich geändert (vom PC gemeldet).</summary>
    public void ProfilesChanged(Guid deviceId)
    {
        if (deviceId == SelectedDevice?.Id)
            LoadProfiles();
    }

    private void LoadProfiles()
    {
        _syncingProfiles = true;
        try
        {
            var selected = SelectedScope?.ProfileId;
            Scopes.Clear();
            ProfileRows.Clear();
            if (_server() is not { } server || SelectedDevice is not { } device)
            {
                HasProfiles = false;
                return;
            }
            var policy = server.Policies.Get(device.Id);
            var profiles = server.Profiles.Get(device.Id);
            Scopes.Add(new ScopeChoice(null, "Alle Profile"));
            foreach (var profile in profiles)
            {
                Scopes.Add(new ScopeChoice(profile.Id, profile.Name));
                ProfileRows.Add(new ProfileRowViewModel(profile,
                    policy.ProfileRules?.FirstOrDefault(r => r.ProfileId == profile.Id)?.Rules.Length ?? 0));
            }
            HasProfiles = profiles.Length > 0;
            ProfileCreationAllowed = !policy.BlockProfileCreation;
            SelectedScope = Scopes.FirstOrDefault(s => s.ProfileId == selected) ?? Scopes[0];
        }
        finally
        {
            _syncingProfiles = false;
        }
        OnSelectedScopeChanged(SelectedScope);
    }

    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
            return;
        if (LauncherProfile.ValidateName(NewProfileName) is { } error)
        {
            ProfileMessage = error;
            return;
        }
        var color = ProfileColors.All[server.Profiles.Get(device.Id).Length % ProfileColors.All.Length];
        ProfileMessage = await server.CreateProfileAsync(device.Id, NewProfileName, color)
            ? $"Profil „{NewProfileName.Trim()}“ wird auf dem PC angelegt …"
            : "Profile lassen sich nur anlegen, während der PC online ist.";
        NewProfileName = "";
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(ProfileRowViewModel row)
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
            return;
        ProfileMessage = await server.DeleteProfileAsync(device.Id, row.Profile.Id)
            ? $"Profil „{row.Name}“ wird gelöscht …"
            : "Profile lassen sich nur löschen, während der PC online ist.";
    }

    [RelayCommand]
    private async Task ResetPasswordAsync(ProfileRowViewModel row)
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
            return;
        ProfileMessage = await server.ResetProfilePasswordAsync(device.Id, row.Profile.Id)
            ? $"Passwort von „{row.Name}“ wird zurückgesetzt …"
            : "Das geht nur, während der PC online ist.";
    }

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
        ProfileMessage = "";
        LoadProfiles();
        LoadRestrictions();
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
            item.Sync(policy.IsAllowed(item.App.Id, ScopeProfileId));
        // Konten- und Bereich-Schalter an den neuen Stand angleichen
        _syncingRestrictions = true;
        foreach (var account in Accounts)
            account.Sync(policy.IsRestricted(account.Account.Sid));
        foreach (var area in Areas)
            area.Sync((policy.BlockedAreas ?? []).Contains(area.Area));
        _syncingRestrictions = false;
        UpdateRestrictionStatus();
        if (!_syncingProfiles)
        {
            ProfileCreationAllowedSync(!policy.BlockProfileCreation);
            // Anzahl eigener Abweichungen je Profil aktualisieren
            var rows = ProfileRows.Select(r => new ProfileRowViewModel(r.Profile,
                policy.ProfileRules?.FirstOrDefault(p => p.ProfileId == r.Profile.Id)?.Rules.Length ?? 0)).ToList();
            ProfileRows.Clear();
            foreach (var row in rows)
                ProfileRows.Add(row);
        }
        UpdatePolicyStatus();
        ApplyFilter();
    }

    private void ProfileCreationAllowedSync(bool allowed)
    {
        _syncingProfiles = true;
        ProfileCreationAllowed = allowed;
        _syncingProfiles = false;
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
        _all = apps.Select(a => new AppItemViewModel(a, server.Inventory, policy.IsAllowed(a.Id, ScopeProfileId), SetAllowed)).ToList();
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

    private void SetAllowed(AppItemViewModel item, bool allowed) => SetAllowed([item.App.Id], allowed);

    /// <summary>Für den PC (alle Profile) oder nur für das gewählte Profil.</summary>
    private void SetAllowed(IReadOnlyList<string> ids, bool allowed)
    {
        var profileId = ScopeProfileId;
        ChangePolicy(profileId is null
            ? (p, now) => p.WithAllowed(ids, allowed, now)
            : (p, now) => p.WithAllowedForProfile(profileId, ids, allowed, now));
    }

    /// <summary>Alle gerade sichtbaren Einträge (Suche und Filter) auf einmal.</summary>
    [RelayCommand]
    private void AllowVisible() => SetVisible(true);

    [RelayCommand]
    private void BlockVisible() => SetVisible(false);

    private void SetVisible(bool allowed)
    {
        var ids = _visible.Select(i => i.App.Id).ToList();
        if (ids.Count > 0)
            SetAllowed(ids, allowed);
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
