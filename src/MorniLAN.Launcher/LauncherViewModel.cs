using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    /// <summary>Passwortabfrage vor einem geschützten Profil.</summary>
    Password,

    /// <summary>Kacheln.</summary>
    Home,
}

/// <summary>Ein Hintergrund-Thema zur Auswahl.</summary>
public sealed partial class ThemeChoice(string id, string name, string from, string to) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public IBrush Preview { get; } = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
    };
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>Eine Profilkachel in der Auswahl.</summary>
public sealed class ProfileTile
{
    public ProfileTile(LauncherProfile profile, ProfileMedia media)
    {
        Profile = profile;
        Brush = new SolidColorBrush(Color.Parse(ProfileColors.Normalize(profile.Color)));
        Avatar = profile.HasAvatar ? media.Load(media.AvatarPath(profile.Id)) : null;
    }

    public LauncherProfile Profile { get; }
    public string Name => Profile.Name;
    public string Initial => Profile.Name.Length > 0 ? char.ToUpperInvariant(Profile.Name[0]).ToString() : "?";
    public IBrush Brush { get; }
    public Bitmap? Avatar { get; }
    public bool HasAvatar => Avatar is not null;
    public bool HasPassword => Profile.HasPassword;
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
    private readonly ProfileMedia _media = new();
    private List<LauncherTile> _all = [];
    private string? _appsHash;
    private AgentLocalStatus? _status;
    private string? _pendingProfileName;
    private bool _chosenThisSession;
    private string? _toast;
    private DateTimeOffset _toastUntil;
    private ProfileTile? _awaitingPassword;

    public LauncherViewModel()
    {
        Colors = [.. ProfileColors.All.Select(c => new ColorChoice(c))];
        Colors[0].IsSelected = true;
        Themes = [.. ProfileThemes.Gradients.Select(g => new ThemeChoice(g.Id, g.Name, g.From, g.To))];
        Themes[0].IsSelected = true;
        VersionText = $"MorniLAN {VersionInfo.Display}";
        UpdateClock();
        ApplyBackground(null);
        LauncherStore.CleanInbox(InboxFolder, TimeSpan.FromMinutes(2));
    }

    public ObservableCollection<ProfileTile> Profiles { get; } = [];
    public ObservableCollection<LauncherTile> Recent { get; } = [];
    public ObservableCollection<LauncherTile> Games { get; } = [];
    public ObservableCollection<LauncherTile> Apps { get; } = [];
    public ColorChoice[] Colors { get; }
    public ThemeChoice[] Themes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsPairing), nameof(IsProfiles), nameof(IsPassword), nameof(IsHome))]
    public partial LauncherView View { get; set; } = LauncherView.Welcome;

    public bool IsWelcome => View == LauncherView.Welcome;
    public bool IsPairing => View == LauncherView.Pairing;
    public bool IsProfiles => View == LauncherView.Profiles;
    public bool IsPassword => View == LauncherView.Password;
    public bool IsHome => View == LauncherView.Home;

    /// <summary>Hintergrund des Launchers: Thema-Farbverlauf oder eigenes Bild des aktuellen Profils.</summary>
    [ObservableProperty] public partial IBrush Background { get; set; } = new SolidColorBrush(Color.Parse("#0F1115"));
    [ObservableProperty] public partial Bitmap? BackgroundImage { get; set; }
    public bool HasBackgroundImage => BackgroundImage is not null;

    partial void OnBackgroundImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasBackgroundImage));

    [ObservableProperty] public partial string WelcomeText { get; set; } = "Verbinde mit dem MorniLAN-Dienst …";

    /// <summary>Adressfeld „Panel nicht gefunden?" nur zeigen, wenn der PC gerade KEIN Panel gefunden hat.</summary>
    [ObservableProperty] public partial bool ShowAddressField { get; set; }

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

    // Fernzugriff – immer deutlich sichtbar
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteBanner))]
    public partial bool RemoteActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteBanner))]
    public partial bool RemoteAsking { get; set; }

    [ObservableProperty] public partial bool InputLocked { get; set; }
    [ObservableProperty] public partial string RemoteBanner { get; set; } = "";
    public bool ShowRemoteBanner => RemoteActive || RemoteAsking;

    // Nachricht des Admins
    [ObservableProperty] public partial bool ShowMessage { get; set; }
    [ObservableProperty] public partial string MessageTitle { get; set; } = "";
    [ObservableProperty] public partial string MessageText { get; set; } = "";
    private Guid _dismissedMessage;

    /// <summary>Die Eingabesperre (vom Fenster gesetzt), damit Maus/Tastatur wirklich blockiert werden.</summary>
    public Action<bool>? SetInputLock { get; set; }

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

    // Passwortabfrage
    [ObservableProperty] public partial string PasswordPrompt { get; set; } = "";
    [ObservableProperty] public partial string PasswordInput { get; set; } = "";
    [ObservableProperty] public partial string PasswordError { get; set; } = "";

    // Dialoge
    [ObservableProperty] public partial bool ShowNewProfile { get; set; }
    [ObservableProperty] public partial string NewProfileName { get; set; } = "";
    [ObservableProperty] public partial string NewProfileError { get; set; } = "";
    [ObservableProperty] public partial bool ShowPower { get; set; }

    // Profil bearbeiten
    [ObservableProperty] public partial bool ShowEditProfile { get; set; }
    [ObservableProperty] public partial string EditTitle { get; set; } = "";
    [ObservableProperty] public partial string EditPassword { get; set; } = "";
    [ObservableProperty] public partial bool EditHasPassword { get; set; }
    [ObservableProperty] public partial string EditMessage { get; set; } = "";

    public bool HasOverlay => ShowNewProfile || ShowPower || ShowEditProfile;

    partial void OnShowNewProfileChanged(bool value) => OnPropertyChanged(nameof(HasOverlay));
    partial void OnShowEditProfileChanged(bool value) => OnPropertyChanged(nameof(HasOverlay));
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
            Profiles.Add(new ProfileTile(profile, _media));
        CanCreateProfile = !list.ProfileCreationBlocked && profiles.Length < LauncherProfile.MaxProfiles;
        CanContinueWithoutProfile = profiles.Length == 0;

        // Gerade angelegtes Profil gleich auswählen (frisch angelegt hat nie ein Passwort)
        if (_pendingProfileName is { } pending && Profiles.FirstOrDefault(p => p.Name == pending) is { } created)
        {
            _pendingProfileName = null;
            EnterProfile(created);
            return;
        }
        // Aktuelles Profil aktualisieren (z. B. frisch angepasst), Hintergrund neu anwenden
        CurrentProfile = Profiles.FirstOrDefault(p => p.Profile.Id == selected);
        if (CurrentProfile is not null)
            ApplyBackground(CurrentProfile.Profile);
        else if (Profiles.Count > 0 && _chosenThisSession)
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
            ShowAddressField = false; // Dienst-Problem, kein Panel-Problem
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
                ShowAddressField = false; // verbunden – Adressfeld ausblenden
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
                // Nur wenn noch gar nichts geladen wurde, beim Suchen das Adressfeld anbieten
                ShowAddressField = _all.Count == 0 && Profiles.Count == 0;
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

        ShowRemote(status.Remote ?? RemoteSessionState.Idle);

        // Admin-Nachricht anzeigen, bis sie weggeklickt wird
        if (status.Message is { } message && message.Id != _dismissedMessage)
        {
            MessageTitle = string.IsNullOrEmpty(message.Title) ? "Nachricht vom Admin" : message.Title;
            MessageText = message.Text;
            ShowMessage = true;
        }

        // Während der Passwortabfrage den Bildschirm nicht wegziehen
        if (View == LauncherView.Password)
            return;

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

    private void ShowRemote(RemoteSessionState remote)
    {
        RemoteAsking = remote.Phase == RemoteSessionPhase.WaitingForConsent;
        RemoteActive = remote.Phase == RemoteSessionPhase.Active;
        RemoteBanner = remote.Phase switch
        {
            RemoteSessionPhase.WaitingForConsent => "Der Admin möchte auf diesen PC zugreifen.",
            RemoteSessionPhase.Active when remote.InputLocked => "Fernzugriff läuft – Maus und Tastatur sind gerade gesperrt.",
            RemoteSessionPhase.Active => "Fernzugriff durch den Admin läuft.",
            _ => "",
        };
        if (InputLocked != remote.InputLocked)
        {
            InputLocked = remote.InputLocked;
            SetInputLock?.Invoke(remote.InputLocked && remote.Phase == RemoteSessionPhase.Active);
        }
    }

    [RelayCommand]
    private void DismissMessage()
    {
        ShowMessage = false;
        if (_status?.Message is { } message)
            _dismissedMessage = message.Id;
    }

    [RelayCommand]
    private void AllowRemote() => SendConsent(true);

    [RelayCommand]
    private void DenyRemote() => SendConsent(false);

    private void SendConsent(bool allow)
    {
        try { LauncherInbox.WriteRemoteConsent(InboxFolder, new LauncherInbox.RemoteConsent(allow)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        RemoteAsking = false;
    }

    // --- Panel-Adresse von Hand (falls die automatische Suche scheitert, z. B. Tailscale) ---

    [ObservableProperty] public partial string AdminHostInput { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionMessage))]
    public partial string ConnectionMessage { get; set; } = "";

    public bool HasConnectionMessage => ConnectionMessage.Length > 0;

    [RelayCommand]
    private void SubmitAdminHost()
    {
        var host = AdminHostInput.Trim();
        if (LauncherInbox.ConnectionRequest.Validate(host) is { } error)
        {
            ConnectionMessage = error;
            return;
        }
        try
        {
            LauncherInbox.WriteConnectionRequest(InboxFolder, new LauncherInbox.ConnectionRequest(host));
            ConnectionMessage = $"Adresse {host} übernommen – verbinde …";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConnectionMessage = "Konnte die Adresse nicht speichern.";
        }
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

    /// <summary>Profil anklicken: Hat es ein Passwort, erst danach fragen, sonst gleich öffnen.</summary>
    [RelayCommand]
    private void SelectProfile(ProfileTile? profile)
    {
        if (profile is { HasPassword: true })
        {
            _awaitingPassword = profile;
            PasswordPrompt = $"Passwort für „{profile.Name}“";
            PasswordInput = "";
            PasswordError = "";
            View = LauncherView.Password;
            FocusRequested?.Invoke();
            return;
        }
        EnterProfile(profile);
    }

    /// <summary>Passwort geprüft oder keins nötig: Profil wirklich öffnen.</summary>
    private void EnterProfile(ProfileTile? profile)
    {
        CurrentProfile = profile;
        _awaitingPassword = null;
        _chosenThisSession = true;
        _store.RememberProfile(profile?.Profile.Id);
        ApplyBackground(profile?.Profile);
        SearchText = "";
        ApplyFilter();
        View = _all.Count == 0 && Profiles.Count == 0 ? LauncherView.Welcome : LauncherView.Home;
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private void ConfirmPassword()
    {
        if (_awaitingPassword is not { } profile)
            return;
        if (ProfilePassword.Verify(PasswordInput, profile.Profile.PasswordHash))
        {
            EnterProfile(profile);
        }
        else
        {
            PasswordError = "Falsches Passwort.";
            PasswordInput = "";
            FocusRequested?.Invoke();
        }
    }

    [RelayCommand]
    private void ContinueWithoutProfile() => EnterProfile(null);

    /// <summary>Hintergrund setzen: eigenes Bild des Profils, sonst der Farbverlauf des Themas.</summary>
    private void ApplyBackground(LauncherProfile? profile)
    {
        BackgroundImage = profile is { HasCustomBackground: true } && _media.HasBackground(profile.Id)
            ? _media.Load(_media.BackgroundPath(profile.Id))
            : null;
        var theme = ProfileThemes.Normalize(profile?.Theme);
        var gradient = ProfileThemes.Gradients.FirstOrDefault(g => g.Id == theme, ProfileThemes.Gradients[0]);
        Background = gradient.From == gradient.To
            ? new SolidColorBrush(Color.Parse(gradient.From))
            : new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(gradient.From), 0),
                    new GradientStop(Color.Parse(gradient.To), 1),
                },
            };
    }

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
    private void PickTheme(ThemeChoice choice)
    {
        foreach (var theme in Themes)
            theme.IsSelected = theme == choice;
    }

    /// <summary>Vom Fenster gesetzt: lässt den Nutzer eine Bilddatei wählen (Dateiauswahl braucht das Fenster).</summary>
    public Func<Task<string?>>? PickImageFile { get; set; }

    /// <summary>„Mein Profil“ bearbeiten (nur das aktuell gewählte Profil).</summary>
    [RelayCommand]
    private void OpenEditProfile()
    {
        if (CurrentProfile is not { } profile)
            return;
        foreach (var color in Colors)
            color.IsSelected = string.Equals(color.Hex, profile.Profile.Color, StringComparison.OrdinalIgnoreCase);
        if (!Colors.Any(c => c.IsSelected))
            Colors[0].IsSelected = true;
        var theme = ProfileThemes.Normalize(profile.Profile.Theme);
        foreach (var t in Themes)
            t.IsSelected = t.Id == theme;
        EditTitle = $"„{profile.Name}“ einrichten";
        EditPassword = "";
        EditHasPassword = profile.Profile.HasPassword;
        EditMessage = "";
        ShowEditProfile = true;
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private async Task PickBackgroundAsync()
    {
        if (CurrentProfile is not { } profile || PickImageFile is null)
            return;
        if (await PickImageFile() is { } file && _media.SaveBackground(profile.Profile.Id, file))
        {
            SendEdit(profile.Profile.Id, hasBackground: true);
            EditMessage = "Hintergrundbild gesetzt.";
        }
        else if (PickImageFile is not null)
        {
            EditMessage = "Das Bild konnte nicht gelesen werden.";
        }
    }

    [RelayCommand]
    private void RemoveBackground()
    {
        if (CurrentProfile is not { } profile)
            return;
        try { File.Delete(_media.BackgroundPath(profile.Profile.Id)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        SendEdit(profile.Profile.Id, hasBackground: false);
        BackgroundImage = null;
        ApplyBackground(profile.Profile with { HasCustomBackground = false });
        EditMessage = "Hintergrundbild entfernt.";
    }

    [RelayCommand]
    private async Task PickAvatarAsync()
    {
        if (CurrentProfile is not { } profile || PickImageFile is null)
            return;
        if (await PickImageFile() is { } file && _media.SaveAvatar(profile.Profile.Id, file))
        {
            SendEdit(profile.Profile.Id, hasAvatar: true);
            EditMessage = "Profilbild gesetzt.";
        }
    }

    [RelayCommand]
    private void RemoveAvatar()
    {
        if (CurrentProfile is not { } profile)
            return;
        try { File.Delete(_media.AvatarPath(profile.Profile.Id)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        SendEdit(profile.Profile.Id, hasAvatar: false);
        EditMessage = "Profilbild entfernt.";
    }

    [RelayCommand]
    private void SaveEditProfile()
    {
        if (CurrentProfile is not { } profile)
            return;
        string? passwordHash = null;
        if (!string.IsNullOrEmpty(EditPassword))
        {
            if (ProfilePassword.Validate(EditPassword) is { } error)
            {
                EditMessage = error;
                return;
            }
            passwordHash = ProfilePassword.Hash(EditPassword);
        }
        SendEdit(profile.Profile.Id, color: Colors.First(c => c.IsSelected).Hex,
            theme: Themes.First(t => t.IsSelected).Id, passwordHash: passwordHash);
        ApplyBackground(profile.Profile with { Theme = Themes.First(t => t.IsSelected).Id });
        ShowEditProfile = false;
        Toast("Profil wird angepasst …", 5);
        FocusRequested?.Invoke();
    }

    [RelayCommand]
    private void RemovePassword()
    {
        if (CurrentProfile is { } profile)
        {
            SendEdit(profile.Profile.Id, passwordHash: "");
            EditHasPassword = false;
            EditMessage = "Passwort entfernt.";
        }
    }

    private void SendEdit(string profileId, string? color = null, string? theme = null, string? passwordHash = null,
        bool? hasBackground = null, bool? hasAvatar = null)
    {
        try
        {
            LauncherInbox.WriteProfileEdit(InboxFolder,
                new LauncherInbox.ProfileEdit(profileId, color, theme, passwordHash, hasBackground, hasAvatar));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditMessage = $"Konnte nicht gespeichert werden: {ex.Message}";
        }
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
        if (ShowNewProfile || ShowPower || ShowEditProfile)
        {
            ShowNewProfile = ShowPower = ShowEditProfile = false;
        }
        else if (View == LauncherView.Password)
        {
            _awaitingPassword = null;
            View = LauncherView.Profiles;
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
