using Avalonia.Controls;
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

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _querying;

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
            var status = await Task.Run(() => LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(1.5)));
            Show(status);
        }
        finally
        {
            _querying = false;
        }
    }

    private void Show(AgentLocalStatus? status)
    {
        if (status is null)
        {
            StatusDot.Fill = Problem;
            StatusText.Text = "Der MorniLAN-Dienst läuft nicht. Bitte den Admin fragen.";
            PairingCard.IsVisible = false;
            return;
        }

        var admin = string.IsNullOrEmpty(status.AdminName) ? "dem Admin-PC" : $"„{status.AdminName}“";
        switch (status.State)
        {
            case AgentLinkState.Online:
                StatusDot.Fill = Online;
                StatusText.Text = $"Verbunden mit {admin}";
                PairingCard.IsVisible = false;
                break;
            case AgentLinkState.WaitingForPairing when status.PairingCode is { } code:
                StatusDot.Fill = Waiting;
                StatusText.Text = $"Wartet auf Kopplung mit {admin}";
                PairingHint.Text = $"Gib diesen Code im Admin-Panel auf {admin} ein:";
                PairingCode.Text = code;
                PairingCard.IsVisible = true;
                break;
            default:
                StatusDot.Fill = Offline;
                StatusText.Text = "Suche das Admin-Panel im Netzwerk …";
                PairingCard.IsVisible = false;
                break;
        }
    }
}
