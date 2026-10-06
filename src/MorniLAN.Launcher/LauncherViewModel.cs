using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Launcher.Platform;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;

namespace MorniLAN.Launcher;

/// <summary>Was der Launcher gerade zeigt.</summary>
public enum LauncherView
{
    /// <summary>Begrüßung oder Hinweis (Dienst läuft nicht, noch nichts freigegeben).</summary>
    Welcome,

    /// <summary>Der PC ist noch nicht gekoppelt: Pairing-Code groß.</summary>
    Pairing,

    /// <summary>Wer spielt? Profilauswahl beim Start.</summary>
    Profiles,

    /// <summary>Kacheln.</summary>
    Home,
}

/// <summary>Eine Profilkachel in der Auswahl.</summary>
public sealed class ProfileTile(LauncherProfile profile)
{
    public LauncherProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Initial => Profile.Name.Length > 0 ? char.ToUpperInvariant(Profile.Name[0]).ToString() : "?";
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(ProfileColors.Normalize(profile.Color)));
}

/// <summary>Eine Farbe zur Auswahl beim Anlegen eines Profils.</summary>
public sealed partial class ColorChoice(string hex) : ObservableObject
{
    public string Hex { get; } = hex;
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(hex));
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>Zustand und Befehle des Launchers. Daten kommen nur lesend vom Dienst (Pipes).</summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#3DDC84"));
    private static readonly IBrush WaitingBrush = new SolidColorBrush(Color.Parse("#4C8DFF"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#5C6575"));
    private static readonly IBrush ProblemBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));

    private static readonly string StatusPipe =
        Environment.GetEnvironmentVariable("MORNILAN_PIPE") ?? MorniLanConstants.LauncherPipeName;
    private static readonly string AppsPipe =
        Environment.GetEnvironmentVariable("MORNILAN_APPS_PIPE") ?? MorniLanConstants.LauncherAppsPipeName;
    private static readonly string InboxFolder =
        Environment.GetEnvironmentVariable("MORNILAN_INBOX") ?? LauncherInbox.CurrentUserFolder();

    private readonly LauncherStore _store = new();
    private List<LauncherTile> _all = [];
    private string? _appsHash;
    private AgentLocalStatus? _status;
    private string? _pendingProfileName;
    private bool _chosenThisSession;
    private string? _toast;
    private DateTimeOffset _toastUntil;

    public LauncherViewModel()
    {
        Colors = [.. ProfileColors.All.Select(c => new ColorChoice(c))];
        Colors[0].IsSelected = true;
        VersionText = $"MorniLAN {VersionInfo.Display}";
        UpdateClock();
        LauncherStore.CleanInbox(InboxFolder, TimeSpan.FromMinutes(2));
    }

    public ObservableCollection<ProfileTile> Profiles { get; } = [];
    public ObservableCollection<LauncherTile> Recent { get; } = [];
    public ObservableCollection<LauncherTile> Games { get; } = [];
    public ObservableCollection<LauncherTile> Apps { get; } = [];
    public ColorChoice[] Colors { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsPairing), nameof(IsProfiles), nameof(IsHome))]
    public partial LauncherView View { get; set; } = LauncherView.Welcome;

    public bool IsWelcome => View == LauncherView.Welcome;
    public bool IsPairing => View == LauncherView.Pairing;
    public bool IsProfiles => View == LauncherView.Profiles;
    public bool IsHome => View == LauncherView.Home;

    [ObservableProperty] public partial string WelcomeText { get; set; } = "Verbinde mit dem MorniLAN-Dienst …";
    [ObservableProperty] public partial string PairingHint { get; set; } = "";
    [ObservableProperty] public partial string PairingCode { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "Verbinde mit dem MorniLAN-Dienst …";
    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = OfflineBrush;
    [ObservableProperty] public partial string VersionText { get; set; }
    [ObservableProperty] public partial string Clock { get; set; } = "";
    [ObservableProperty] public partial string DateText { get; set; } = "";
    [ObservableProperty] public partial string NetworkText { get; set; } = "";
    [ObservableProperty] public partial int Volume { get; set; }
    [ObservableProperty] public partial bool Muted { get; set; }
    [ObservableProperty] public partial bool HasAudio { get; set; }
    [ObservableProperty] public partial string HelpText { get; set; } = "Hilfe anfordern";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileName), nameof(ProfileInitial), nameof(ProfileBrush), nameof(HasProfile))]
    public partial ProfileTile? CurrentProfile { get; set; }

    public bool HasProfile => CurrentProfile is not null;
    public string ProfileName => CurrentProfile?.Name ?? "Ohne Profil";
    public string ProfileInitial => CurrentProfile?.Initial ?? "M";
    public IBrush ProfileBrush => CurrentProfile?.Brush ?? WaitingBrush;

    [ObservableProperty] public partial bool CanCreateProfile { get; set; } = true;
    [ObservableProperty] public partial bool CanContinueWithoutProfile { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool HasRecent { get; set; }
    [ObservableProperty] public partial bool HasGames { get; set; }
    [ObservableProperty] public partial bool HasApps { get; set; }
    [ObservableProperty] public partial bool NothingFound { get; set; }

    // Dialoge
    [ObservableProperty] public partial bool ShowNewProfile { get; set; }
    [ObservableProperty] public partial string NewProfileName { get; set; } = "";
    [ObservableProperty] public partial string NewProfileError { get; set; } = "";
    [ObservableProperty] public partial bool ShowPower { get; set; }

    public bool HasOverlay => ShowNewProfile || ShowPower;

    partial void OnShowNewProfileChanged(bool value) => OnPropertyChanged(nameof(HasOverlay));
    partial void OnShowPowerChanged(bool value) => OnPropertyChanged(nameof(HasOverlay));
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Wird gesetzt, wenn eine Ansicht den Fokus neu setzen soll (Fenster kümmert sich darum).</summary>
    public event Action? FocusRequested;

    // ───── Daten vom Dienst ─────

    private bool _querying;

    /// <summary>Alle 2 s: Status holen, bei geändertem Hash die Kacheln.</summary>
    public async Task RefreshAsync()
    {
        if (_querying)
            return;
        _querying = true;
        try
        {
            var status = await Task.Run(() => LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(1.5), pipeName: StatusPipe));
            if (status?.AppsHash is { } hash && hash != _appsHash)
            {
                var list = await Task.Run(() => LauncherAppsPipe.QueryAsync(TimeSpan.FromSeconds(10), pipeName: AppsPipe));
                if (list is not null)
                {
                    Apply(list);
                    _appsHash = hash;
                }
            }
            _status = status;
            ShowStatus(status);
        }
        finally
        {
            _querying = false;
        }
    }

    /// <summary>Uhr jede Sekunde, Netzwerk und Ton seltener.</summary>
    public void UpdateClock()
    {
        var now = DateTime.Now;
        Clock = now.ToString("HH:mm", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        DateText = now.ToString("dddd, d. MMMM", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }

    public async Task UpdateSystemAsync()
    {
        NetworkText = await Task.Run(NetworkStatus.Describe);
        LauncherStore.CleanInbox(InboxFolder, TimeSpan.FromMinutes(2));
        if (AudioVolume.Get() is { } audio)
        {
            HasAudio = true;
            _syncingVolume = true;
            Volume = audio.Level;
            Muted = audio.Muted;
            _syncingVolume = false;
        }
        else
        {
            HasAudio = false;
        }
    }

    private bool _syncingVolume;

    partial void OnVolumeChanged(int value)
    {
        if (!_syncingVolume)
            AudioVolume.Set(value);
    }

    private void Apply(LauncherAppList list)
    {
        _all = [.. list.Apps.Select(a => new LauncherTile(a))];
        var profiles = list.Profiles ?? [];
        var selected = CurrentProfile?.Profile.Id;
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(new ProfileTile(profile));
        CanCreateProfile = !list.ProfileCreationBlocked && profiles.Length < LauncherProfile.MaxProfiles;
        CanContinueWithoutProfile = profiles.Length == 0;

        // Gerade angelegtes Profil gleich auswählen
        if (_pendingProfileName is { } pending && Profiles.FirstOrDefault(p => p.Name == pending) is { } created)
        {
            _pendingProfileName = null;
            SelectProfile(created);
            return;
        }
        CurrentProfile = Profiles.FirstOrDefault(p => p.Profile.Id == selected);
        if (CurrentProfile is null && Profiles.Count > 0 && _chosenThisSession)
            _chosenThisSession = false; // ausgewähltes Profil wurde gelöscht: neu wählen
        ApplyFilter();
    }

    private void ShowStatus(AgentLocalStatus? status)
    {
        if (status is null)
        {
            StatusBrush = ProblemBrush;
            StatusText = "Der MorniLAN-Dienst läuft nicht. Bitte den Admin fragen.";
            WelcomeText = "Der MorniLAN-Dienst läuft nicht. Bitte den Admin fragen.";
            if (_all.Count == 0)
                View = LauncherView.Welcome;
            return;
        }

        var admin = string.IsNullOrEmpty(status.AdminName) ? "dem Admin-PC" : $"„{status.AdminName}“";
        switch (status.State)
        {
            case AgentLinkState.Online:
                StatusBrush = OnlineBrush;
                StatusText = $"Verbunden mit {admin}";
                break;
            case AgentLinkState.WaitingForPairing when status.PairingCode is { } code:
                StatusBrush = WaitingBrush;
                StatusText = $"Wartet auf Kopplung mit {admin}";
                PairingHint = $"Gib diesen Code im Admin-Panel auf {admin} ein:";
                PairingCode = code;
                View = LauncherView.Pairing;
                return;
            default:
                // Gekoppelt, aber das Panel ist gerade aus: Kacheln und Profile gelten trotzdem (lokal gespeichert)
                StatusBrush = OfflineBrush;
                StatusText = "Admin-PC gerade nicht erreichbar";
                break;
        }
        if (_toast is not null && DateTimeOffset.UtcNow < _toastUntil)
            StatusText = _toast;

        HelpText = status.Help switch
        {
            { Delivered: true } h when DateTimeOffset.UtcNow - h.RequestedAt < TimeSpan.FromMinutes(10) => "✓ Admin benachrichtigt",
            { Delivered: false } => "Hilfe wird gesendet …",
            _ => "Hilfe anfordern",
        };

        var previous = View;
        if (_all.Count == 0 && Profiles.Count == 0)
        {
            WelcomeText = status.AppsHash is null
                ? "Launcher – deine freigegebenen Apps und Spiele erscheinen hier."
                : "Noch nichts freigegeben. Der Admin kann im Panel festlegen, was hier erscheint.";
            View = LauncherView.Welcome;
        }
        else if (!_chosenThisSession && (Profiles.Count > 0 || CanCreateProfile))
        {
            View = LauncherView.Profiles;
        }
        else
        {
            View = LauncherView.Home;
        }
        if (View != previous)
            FocusRequested?.Invoke();
    }

    private void ApplyFilter()
    {
        var profileId = CurrentProfile?.Profile.Id;
        var visible = _all.Where(t => t.App.IsVisibleFor(profileId)).ToList();
        var search = SearchText.Trim();
        var filtered = search.Length == 0
            ? visible
            : visible.Where(t => t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var recent = search.Length > 0
            ? []
            : _store.RecentFor(profileId).Select(id => visible.FirstOrDefault(t => t.App.Id == id)).OfType<LauncherTile>().Take(6).ToList();
        Replace(Recent, recent);
        Replace(Games, filtered.Where(t => t.App.IsGame));
        Replace(Apps, filtered.Where(t => !t.App.IsGame));
        HasRecent = Recent.Count > 0;
        HasGames = Games.Count > 0;
        HasApps = Apps.Count > 0;
        NothingFound = filtered.Count == 0;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    // ───── Befehle ─────

    [RelayCommand]
    private void SelectProfile(ProfileTile? profile)
    {
        CurrentProfile = profile;
        _chosenThisSession = true;
        _store.RememberProfile(profile?.Profile.Id);
        SearchText = "";
        ApplyFilter();
        View = _all.Count == 0 && Profiles.Count == 0 ? LauncherView.Welcome : LauncherView.Home;
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private void ContinueWithoutProfile() => SelectProfile(null);

    [RelayCommand]
    private void SwitchProfile()
    {
        _chosenThisSession = false;
        View = LauncherView.Profiles;
        FocusRequested?.Invoke();
    }

    /// <summary>Zuletzt benutztes Profil zuerst zeigen (Fokus).</summary>
    public ProfileTile? LastProfile => Profiles.FirstOrDefault(p => p.Profile.Id == _store.Memory.LastProfileId);

    [RelayCommand]
    private void OpenNewProfile()
    {
        NewProfileName = "";
        NewProfileError = "";
        ShowNewProfile = true;
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private void PickColor(ColorChoice choice)
    {
        foreach (var color in Colors)
            color.IsSelected = color == choice;
    }

    [RelayCommand]
    private void CreateProfile()
    {
        if (LauncherProfile.ValidateName(NewProfileName) is { } error)
        {
            NewProfileError = error;
            return;
        }
        if (Profiles.Any(p => string.Equals(p.Name, NewProfileName.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            NewProfileError = "Diesen Namen gibt es schon.";
            return;
        }
        try
        {
            LauncherInbox.WriteProfileRequest(InboxFolder,
                new LauncherInbox.ProfileRequest(NewProfileName.Trim(), Colors.First(c => c.IsSelected).Hex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NewProfileError = $"Konnte nicht gespeichert werden: {ex.Message}";
            return;
        }
        _pendingProfileName = NewProfileName.Trim();
        ShowNewProfile = false;
        Toast($"Profil „{_pendingProfileName}“ wird angelegt …", 6);
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private void Launch(LauncherTile tile)
    {
        var error = AppStarter.Start(tile.App.LaunchTarget, tile.App.Arguments);
        if (error is null)
        {
            _store.RememberLaunch(CurrentProfile?.Profile.Id, tile.App.Id);
            Toast($"{tile.Name} wird gestartet …", 6);
            if (SearchText.Length == 0)
                Dispatcher.UIThread.Post(ApplyFilter, DispatcherPriority.Background);
        }
        else
        {
            Toast($"{tile.Name} ließ sich nicht starten: {error}", 12);
        }
    }

    [RelayCommand]
    private void RequestHelp()
    {
        if (_status?.Help is { } last && DateTimeOffset.UtcNow - last.RequestedAt < TimeSpan.FromMinutes(1))
        {
            Toast("Hilfe ist schon angefordert.", 5);
            return;
        }
        try
        {
            LauncherInbox.WriteHelpRequest(InboxFolder, new LauncherInbox.HelpMessage(CurrentProfile?.Name));
            HelpText = "Hilfe wird gesendet …";
            Toast("Der Admin wird benachrichtigt.", 6);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast($"Hilfe-Anfrage ging nicht: {ex.Message}", 10);
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        Muted = !Muted;
        AudioVolume.SetMuted(Muted);
    }

    [RelayCommand] private void OpenPower() { ShowPower = true; FocusRequested?.Invoke(); }
    [RelayCommand] private void LogOff() => Power.LogOff();
    [RelayCommand] private void Restart() => Power.Restart();
    [RelayCommand] private void ShutDown() => Power.ShutDown();

    /// <summary>„Zurück“ (Esc, B): Dialog zu, Suche leeren, sonst zur Profilauswahl.</summary>
    [RelayCommand]
    private void Back()
    {
        if (ShowNewProfile || ShowPower)
        {
            ShowNewProfile = ShowPower = false;
        }
        else if (SearchText.Length > 0)
        {
            SearchText = "";
        }
        else if (View == LauncherView.Home && (Profiles.Count > 0 || CanCreateProfile))
        {
            SwitchProfile();
            return;
        }
        FocusRequested?.Invoke();
    }

    private void Toast(string text, int seconds)
    {
        _toast = text;
        _toastUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
        StatusText = text;
    }
}
