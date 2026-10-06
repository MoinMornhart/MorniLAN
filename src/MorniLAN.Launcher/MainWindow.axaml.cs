using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Launcher;

public partial class MainWindow : Window
{
    private static readonly IBrush Online = new SolidColorBrush(Color.Parse("#3DDC84"));
    private static readonly IBrush Waiting = new SolidColorBrush(Color.Parse("#4C8DFF"));
    private static readonly IBrush Offline = new SolidColorBrush(Color.Parse("#5C6575"));
    private static readonly IBrush Problem = new SolidColorBrush(Color.Parse("#FF6B6B"));

    /// <summary>Zum Testen gegen einen eigenen Agent (sonst die Pipes des Dienstes).</summary>
    private static readonly string StatusPipe =
        Environment.GetEnvironmentVariable("MORNILAN_PIPE") ?? MorniLanConstants.LauncherPipeName;
    private static readonly string AppsPipe =
        Environment.GetEnvironmentVariable("MORNILAN_APPS_PIPE") ?? MorniLanConstants.LauncherAppsPipeName;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _querying;
    private string? _appsHash;
    private int _tileCount;
    private string? _launchMessage;
    private DateTimeOffset _launchMessageUntil;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"MorniLAN Launcher {VersionInfo.Display}";
        _timer.Tick += async (_, _) => await RefreshAsync();
        Opened += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (_querying)
            return;
        _querying = true;
        try
        {
            var status = await Task.Run(() => LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(1.5), pipeName: StatusPipe));
            // Neue Kacheln nur holen, wenn sich die Freigaben geändert haben (Bilder sind groß)
            if (status?.AppsHash is { } hash && hash != _appsHash)
            {
                var list = await Task.Run(() => LauncherAppsPipe.QueryAsync(TimeSpan.FromSeconds(10), pipeName: AppsPipe));
                if (list is not null)
                {
                    ShowTiles(list);
                    _appsHash = hash;
                }
            }
            Show(status);
        }
        finally
        {
            _querying = false;
        }
    }

    private void ShowTiles(LauncherAppList list)
    {
        var tiles = list.Apps.Select(a => new LauncherTile(a)).ToList();
        GamesList.ItemsSource = tiles.Where(t => t.App.IsGame).ToList();
        AppsList.ItemsSource = tiles.Where(t => !t.App.IsGame).ToList();
        GamesSection.IsVisible = tiles.Any(t => t.App.IsGame);
        AppsSection.IsVisible = tiles.Any(t => !t.App.IsGame);
        _tileCount = tiles.Count;
    }

    private void Show(AgentLocalStatus? status)
    {
        if (status is null)
        {
            StatusDot.Fill = Problem;
            StatusText.Text = "Der MorniLAN-Dienst läuft nicht. Bitte den Admin fragen.";
            PairingCard.IsVisible = false;
            ShowWelcome("Launcher – deine freigegebenen Apps und Spiele erscheinen hier.");
            return;
        }

        var admin = string.IsNullOrEmpty(status.AdminName) ? "dem Admin-PC" : $"„{status.AdminName}“";
        var waitingForPairing = status.State == AgentLinkState.WaitingForPairing && status.PairingCode is not null;
        switch (status.State)
        {
            case AgentLinkState.Online:
                StatusDot.Fill = Online;
                StatusText.Text = $"Verbunden mit {admin}";
                break;
            case AgentLinkState.WaitingForPairing when status.PairingCode is { } code:
                StatusDot.Fill = Waiting;
                StatusText.Text = $"Wartet auf Kopplung mit {admin}";
                PairingHint.Text = $"Gib diesen Code im Admin-Panel auf {admin} ein:";
                PairingCode.Text = code;
                break;
            default:
                // Gekoppelt, aber das Panel ist gerade aus: Die Kacheln gelten trotzdem (lokal gespeichert)
                StatusDot.Fill = Offline;
                StatusText.Text = "Suche das Admin-Panel im Netzwerk …";
                break;
        }
        if (_launchMessage is not null && DateTimeOffset.UtcNow < _launchMessageUntil)
            StatusText.Text = _launchMessage;

        PairingCard.IsVisible = waitingForPairing;
        if (waitingForPairing)
            ShowWelcome("Launcher – deine freigegebenen Apps und Spiele erscheinen hier.");
        else if (_tileCount == 0)
            ShowWelcome(status.AppsHash is null
                ? "Launcher – deine freigegebenen Apps und Spiele erscheinen hier."
                : "Noch nichts freigegeben. Der Admin kann im Panel festlegen, was hier erscheint.");
        else
        {
            WelcomeView.IsVisible = false;
            TilesView.IsVisible = true;
        }
    }

    private void ShowWelcome(string text)
    {
        Subtitle.Text = text;
        WelcomeView.IsVisible = true;
        TilesView.IsVisible = false;
    }

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherTile tile)
            return;
        var error = AppStarter.Start(tile.App.LaunchTarget, tile.App.Arguments);
        _launchMessage = error is null ? $"{tile.Name} wird gestartet …" : $"{tile.Name} ließ sich nicht starten: {error}";
        _launchMessageUntil = DateTimeOffset.UtcNow.AddSeconds(error is null ? 6 : 12);
        StatusText.Text = _launchMessage;
    }
}
