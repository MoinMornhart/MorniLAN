using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Platform;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Models;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Seite „Fernzugriff“: Sitzung starten/beenden, Eingabesperre, „ohne Rückfrage erlauben“.</summary>
public sealed partial class RemoteViewModel : ObservableObject
{
    private readonly Func<AdminServer?> _server;
    private bool _syncing;

    public RemoteViewModel(Func<AdminServer?> server) => _server = server;

    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    [ObservableProperty] public partial DeviceChoice? SelectedDevice { get; set; }
    [ObservableProperty] public partial bool HasDevice { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = DeviceViewModel.OfflineBrush;
    [ObservableProperty] public partial bool AllowWithoutConsent { get; set; }
    [ObservableProperty] public partial bool CanConnect { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool InputLocked { get; set; }
    [ObservableProperty] public partial string MoonlightHint { get; set; } = "";
    [ObservableProperty] public partial bool MoonlightMissing { get; set; }
    private string? _moonlightStartedFor;

    partial void OnSelectedDeviceChanged(DeviceChoice? value)
    {
        HasDevice = value is not null;
        Load();
    }

    partial void OnAllowWithoutConsentChanged(bool value)
    {
        if (_syncing)
            return;
        if (_server() is { } server && SelectedDevice is { } device)
            server.Remote.SetAllowWithoutConsent(device.Id, value);
    }

    /// <summary>Geräteliste abgleichen (läuft regelmäßig).</summary>
    public void SyncDevices()
    {
        if (_server() is not { } server)
            return;
        var devices = server.Registry.Snapshot().Select(d => new DeviceChoice(d.Device.DeviceId,
            d.Info?.MachineName ?? d.Device.MachineName)).ToList();
        if (!devices.SequenceEqual(Devices))
        {
            var selected = SelectedDevice?.Id;
            Devices.Clear();
            foreach (var device in devices)
                Devices.Add(device);
            SelectedDevice = Devices.FirstOrDefault(d => d.Id == selected) ?? Devices.FirstOrDefault();
        }
        Load();
    }

    public void RemoteChanged(Guid deviceId)
    {
        if (deviceId == SelectedDevice?.Id)
            Load();
    }

    private void Load()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
        {
            StatusText = "Noch kein PC gekoppelt.";
            CanConnect = IsActive = false;
            MoonlightHint = "";
            return;
        }
        var online = server.Registry.ConnectionIdOf(device.Id) is not null;
        var state = server.Remote.State(device.Id);
        _syncing = true;
        AllowWithoutConsent = server.Remote.Settings(device.Id).AllowWithoutConsent;
        _syncing = false;

        IsActive = state.Phase == RemoteSessionPhase.Active;
        InputLocked = state.InputLocked;
        CanConnect = online && state.Phase is RemoteSessionPhase.Idle or RemoteSessionPhase.Denied or RemoteSessionPhase.Unavailable;
        (StatusText, StatusBrush) = state.Phase switch
        {
            RemoteSessionPhase.Active => (state.Message, DeviceViewModel.OnlineBrush),
            RemoteSessionPhase.WaitingForConsent => ("Warte auf Bestätigung am PC …", DeviceViewModel.WarnBrush),
            RemoteSessionPhase.Denied => (state.Message, DeviceViewModel.ErrorBrush),
            RemoteSessionPhase.Unavailable => (state.Message, DeviceViewModel.ErrorBrush),
            _ => (online ? "Bereit. „Verbinden“ startet den Fernzugriff." : "Der PC ist gerade nicht online.", DeviceViewModel.OfflineBrush),
        };
        if (state is { Phase: RemoteSessionPhase.Active, Host: { } host })
        {
            if (MoonlightLauncher.IsInstalled())
            {
                MoonlightMissing = false;
                // Moonlight automatisch öffnen – aber nur einmal je Sitzung (Load läuft regelmäßig)
                if (_moonlightStartedFor != host)
                {
                    _moonlightStartedFor = host;
                    MoonlightLauncher.TryStream(host);
                }
                MoonlightHint = $"Moonlight geöffnet ({host}). Beim allerersten Mal die angezeigte PIN in Sunshine bestätigen.";
            }
            else
            {
                MoonlightMissing = true;
                MoonlightHint = "Moonlight ist auf diesem PC noch nicht installiert – einmal installieren, dann öffnet sich der Fernzugriff automatisch.";
            }
        }
        else
        {
            _moonlightStartedFor = null;
            MoonlightMissing = false;
            MoonlightHint = "";
        }
    }

    /// <summary>Moonlight einmalig per winget installieren (läuft im Benutzerkontext des Panels).</summary>
    [RelayCommand]
    private void InstallMoonlight()
    {
        try
        {
            Process.Start(new ProcessStartInfo("winget",
                $"install --id {MoonlightLauncher.WingetId} --silent --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = true,
            });
            MoonlightHint = "Moonlight wird installiert … danach erneut „Verbinden“ drücken.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MoonlightHint = "Moonlight-Installer ließ sich nicht starten. Bitte Moonlight von moonlight-stream.org installieren.";
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_server() is { } server && SelectedDevice is { } device)
        {
            if (!await server.StartRemoteAsync(device.Id))
                StatusText = "Der PC ist gerade nicht online.";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_server() is { } server && SelectedDevice is { } device)
            await server.StopRemoteAsync(device.Id);
    }

    [RelayCommand]
    private async Task ToggleInputLockAsync()
    {
        if (_server() is { } server && SelectedDevice is { } device)
            await server.SetInputLockAsync(device.Id, !InputLocked);
    }
}
