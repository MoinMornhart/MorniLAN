using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorniLAN.Admin.Platform;
using MorniLAN.Admin.Server;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Security;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Eine Zeile der Diagnose: Bezeichnung, Wert und Ampel.</summary>
public sealed record DiagnosticRow(string Label, string Value, IBrush Brush);

/// <summary>Diagnose-Seite: alles, was man sonst in Logdateien und Befehlen suchen müsste.</summary>
public sealed partial class DiagnosticsViewModel(Func<AdminServer?> server) : ObservableObject
{
    private const int LogLines = 40;

    public ObservableCollection<DiagnosticRow> ServerRows { get; } = [];
    public ObservableCollection<DiagnosticRow> FirewallRows { get; } = [];
    public ObservableCollection<DiagnosticRow> NetworkRows { get; } = [];
    public ObservableCollection<DiagnosticRow> DeviceRows { get; } = [];

    [ObservableProperty] public partial string LogText { get; set; } = "";
    [ObservableProperty] public partial string LastRefresh { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsRefreshing { get; set; }

    private bool CanRefresh() => !IsRefreshing;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var current = server();
            // netsh und Dateizugriffe nicht auf dem UI-Thread
            var firewall = await Task.Run(Firewall.Check);
            var network = await Task.Run(NetworkInfo.LocalIPv4);
            var log = await Task.Run(ReadLogTail);

            Fill(ServerRows, current is null
                ? [new("Server", "läuft nicht (siehe Übersicht)", DeviceViewModel.ErrorBrush)]
                :
                [
                    new("Server", $"läuft auf Port {current.Port}", DeviceViewModel.OnlineBrush),
                    new("Zertifikat", CertificateFingerprint.Short(current.Identity.Fingerprint), DeviceViewModel.OfflineBrush),
                    new("Name im Netzwerk", current.Identity.Name, DeviceViewModel.OfflineBrush),
                    new("LAN-Suche", $"Beacon alle {ConnectionDefaults.BeaconInterval.TotalSeconds:0} s an " +
                                     string.Join(", ", NetworkInfo.BroadcastTargets()), DeviceViewModel.OfflineBrush),
                ]);

            Fill(FirewallRows, [.. firewall.Select(f => new DiagnosticRow(
                $"{f.Rule.Protocol} {f.Rule.Port} · {f.Rule.Purpose}",
                f.Present ? "eingerichtet" : "fehlt",
                f.Present ? DeviceViewModel.OnlineBrush : DeviceViewModel.ErrorBrush))]);

            Fill(NetworkRows, network.Count == 0
                ? [new("Netzwerk", "keine aktive Verbindung gefunden", DeviceViewModel.ErrorBrush)]
                : [.. network.Select(a => new DiagnosticRow(
                    a.Kind,
                    a.Broadcast is { } b ? $"{a.Address} (Broadcast {b})" : a.Address.ToString(),
                    DeviceViewModel.OnlineBrush))]);

            var now = DateTimeOffset.UtcNow;
            var devices = current?.Registry.Snapshot() ?? [];
            Fill(DeviceRows, devices.Count == 0
                ? [new("Geräte", "noch kein PC gekoppelt", DeviceViewModel.OfflineBrush)]
                : [.. devices.Select(d =>
                {
                    var online = d.PresenceAt(now) == DevicePresence.Online;
                    var seen = d.LastSeen is { } s ? s.ToLocalTime().ToString("dd.MM. HH:mm:ss") : "nie";
                    return new DiagnosticRow(d.Device.MachineName,
                        $"{(online ? "online" : "offline")} · {d.RemoteAddress ?? "–"} · zuletzt {seen}",
                        online ? DeviceViewModel.OnlineBrush : DeviceViewModel.OfflineBrush);
                })]);

            LogText = log;
            LastRefresh = $"Stand {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static void Fill(ObservableCollection<DiagnosticRow> target, IEnumerable<DiagnosticRow> rows)
    {
        target.Clear();
        foreach (var row in rows)
            target.Add(row);
    }

    /// <summary>Die letzten Zeilen des aktuellen Logs, auch während Serilog hineinschreibt.</summary>
    private static string ReadLogTail()
    {
        try
        {
            var file = new DirectoryInfo(AdminPaths.Logs).EnumerateFiles("admin-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (file is null)
                return "Noch keine Logdatei vorhanden.";
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new Queue<string>(LogLines);
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == LogLines)
                    lines.Dequeue();
                lines.Enqueue(line);
            }
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Log konnte nicht gelesen werden: {ex.Message}";
        }
    }
}
