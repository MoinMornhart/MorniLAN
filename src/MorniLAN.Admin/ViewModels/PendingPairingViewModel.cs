using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Security;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Karte "Neuer PC möchte gekoppelt werden" mit Code-Eingabe.</summary>
public sealed partial class PendingPairingViewModel(PendingPairingSnapshot snapshot, PairingCoordinator pairing)
    : ObservableObject
{
    public Guid RequestId { get; } = snapshot.RequestId;

    public string Title { get; } = $"„{snapshot.Device.MachineName}“ möchte gekoppelt werden";

    public string Details { get; } = string.Join(" · ", new[]
    {
        snapshot.Device.Windows.FriendlyName,
        $"Agent v{snapshot.Device.AgentVersion}",
        snapshot.RemoteAddress,
        $"Zertifikat {CertificateFingerprint.Short(snapshot.AgentFingerprint)}",
    }.Where(s => !string.IsNullOrEmpty(s)));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    public partial string Code { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; } = "";

    public bool HasError => Error.Length > 0;

    public void Update(PendingPairingSnapshot current)
    {
        if (current.FailedAttempts > 0)
            Error = $"Falscher Code ({current.FailedAttempts} von {ConnectionDefaults.MaxPairingAttempts} Versuchen).";
    }

    private bool CanPair() => Code.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanPair))]
    private async Task PairAsync()
    {
        var result = await pairing.SubmitCodeAsync(RequestId, Code);
        switch (result)
        {
            case PairingSubmitResult.InvalidFormat:
                Error = "Der Code hat 8 Zeichen, z. B. K7Q2-M9XD.";
                break;
            case PairingSubmitResult.WrongCode:
                Code = "";
                break;
            case PairingSubmitResult.NotFound:
                Error = "Die Anfrage ist nicht mehr offen (Agent getrennt?).";
                break;
        }
    }

    [RelayCommand]
    private Task RejectAsync() => pairing.RejectAsync(RequestId);
}
