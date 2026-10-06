using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Platform;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Models;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Ein Eintrag im Aktionsverlauf.</summary>
public sealed class ActionEntryViewModel(ActionEntry entry)
{
    public string Description => entry.Description;
    public string Time => $"{entry.At:HH:mm}";
    public string StateText => entry.Success switch
    {
        null => "läuft …",
        true => "✓ erledigt",
        false => $"✗ Fehler: {entry.Message}",
    };
    public IBrush StateBrush => entry.Success switch
    {
        null => DeviceViewModel.WarnBrush,
        true => DeviceViewModel.OnlineBrush,
        false => DeviceViewModel.ErrorBrush,
    };
}

/// <summary>Seite „Aktionen“: Programme installieren/deinstallieren, Nachricht, Neustart/Herunterfahren.</summary>
public sealed partial class ActionsViewModel : ObservableObject
{
    private readonly Func<AdminServer?> _server;
    private CancellationTokenSource? _searchCts;

    public ActionsViewModel(Func<AdminServer?> server) => _server = server;

    public ObservableCollection<DeviceChoice> Devices { get; } = [];
    public ObservableCollection<WingetHit> SearchResults { get; } = [];
    public ObservableCollection<ActionEntryViewModel> History { get; } = [];

    [ObservableProperty] public partial DeviceChoice? SelectedDevice { get; set; }
    [ObservableProperty] public partial bool HasDevice { get; set; }
    [ObservableProperty] public partial bool IsOnline { get; set; }
    [ObservableProperty] public partial string OfflineHint { get; set; } = "";
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool Searching { get; set; }

    [ObservableProperty] public partial string UrlAddress { get; set; } = "";
    [ObservableProperty] public partial string UrlName { get; set; } = "";
    [ObservableProperty] public partial string MessageTitle { get; set; } = "";
    [ObservableProperty] public partial string MessageText { get; set; } = "";
    [ObservableProperty] public partial string ActionError { get; set; } = "";

    partial void OnSelectedDeviceChanged(DeviceChoice? value)
    {
        HasDevice = value is not null;
        Load();
    }

    partial void OnSearchTextChanged(string value) => _ = SearchAsync(value);

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

    public void HistoryChanged() => Load();

    private void Load()
    {
        if (_server() is not { } server || SelectedDevice is not { } device)
        {
            IsOnline = false;
            OfflineHint = "Noch kein PC gekoppelt.";
            History.Clear();
            return;
        }
        IsOnline = server.Registry.ConnectionIdOf(device.Id) is not null;
        OfflineHint = IsOnline ? "" : "Der PC ist gerade nicht online. Aktionen gehen erst, wenn er wieder da ist.";
        History.Clear();
        foreach (var entry in server.Actions.For(device.Id).Take(20))
            History.Add(new ActionEntryViewModel(entry));
    }

    private async Task SearchAsync(string term)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        if (term.Trim().Length < 2)
        {
            SearchResults.Clear();
            return;
        }
        Searching = true;
        try
        {
            var hits = await WingetSearch.SearchAsync(term, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            SearchResults.Clear();
            foreach (var hit in hits)
                SearchResults.Add(hit);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cts.IsCancellationRequested)
                Searching = false;
        }
    }

    [RelayCommand]
    private Task Install(WingetHit hit) => Send(new InstallPackageCommand(hit.Id, hit.Name));

    [RelayCommand]
    private Task Uninstall(WingetHit hit) => Send(new UninstallPackageCommand(hit.Id, hit.Name));

    [RelayCommand]
    private async Task InstallFromUrlAsync()
    {
        if (!Uri.TryCreate(UrlAddress.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            ActionError = "Bitte eine sichere https-Adresse eingeben.";
            return;
        }
        if (UrlName.Trim().Length == 0)
        {
            ActionError = "Bitte einen Namen eingeben.";
            return;
        }
        if (await Send(new InstallFromUrlCommand(UrlAddress.Trim(), UrlName.Trim())))
        {
            UrlAddress = UrlName = "";
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (MessageText.Trim().Length == 0)
        {
            ActionError = "Bitte einen Text eingeben.";
            return;
        }
        if (await Send(new ShowMessageCommand(MessageTitle.Trim(), MessageText.Trim())))
            MessageTitle = MessageText = "";
    }

    [RelayCommand]
    private Task Restart() => Send(new RestartCommand(DelaySeconds: 60, Shutdown: false));

    [RelayCommand]
    private Task ShutDown() => Send(new RestartCommand(DelaySeconds: 60, Shutdown: true));

    private async Task<bool> Send(AdminCommand command)
    {
        ActionError = "";
        if (_server() is not { } server || SelectedDevice is not { } device)
            return false;
        if (!await server.RunCommandAsync(device.Id, command))
        {
            ActionError = "Der PC ist gerade nicht online.";
            return false;
        }
        Dispatcher.UIThread.Post(Load);
        return true;
    }
}
