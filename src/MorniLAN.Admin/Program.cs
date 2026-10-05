using Avalonia;
using MorniLAN.Admin.Platform;
using MorniLAN.Admin.Server;
using MorniLAN.Shared;
using Serilog;
using Serilog.Events;

namespace MorniLAN.Admin;

internal static class Program
{
    /// <summary>Signal an eine laufende Instanz: „Fenster zeigen“ (zweiter Start, z. B. aus dem Startmenü).</summary>
    internal const string ShowSignalName = @"Local\MorniLAN.Admin.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        // Hilfsmodus: per UAC mit Admin-Rechten gestartet, nur um die Firewall einzurichten.
        if (args.Contains(Firewall.SetupArgument))
            return Firewall.Apply();

        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\MorniLAN.Admin", out var isFirst);
        if (!isFirst)
        {
            // Läuft schon (vielleicht unsichtbar im Infobereich): Fenster nach vorne holen und beenden.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
                using (signal)
                    signal.Set();
            return 0;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(AdminPaths.Logs, "admin-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        try
        {
            Log.Information("{Product} Admin {Version} gestartet", MorniLanConstants.ProductName, VersionInfo.Display);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Wird auch vom Avalonia-Designer verwendet.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
