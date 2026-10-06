using System.Runtime.InteropServices;
using Avalonia;
using MorniLAN.Launcher.Platform;

namespace MorniLAN.Launcher;

internal static class Program
{
    /// <summary>So startet ihn Windows bei der Anmeldung (Eintrag des Geräte-Setups).</summary>
    public const string AutostartArgument = "--autostart";

    /// <summary>Vollbild oder Fenster, unabhängig vom Konto (zum Testen).</summary>
    public const string FullscreenArgument = "--fullscreen";
    public const string WindowedArgument = "--windowed";

    internal static bool Fullscreen { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var admin = UserAccount.IsAdministrator();
        // Das eigene Admin-Konto bleibt ein normaler Windows-Desktop: dort startet der Launcher nicht von selbst
        if (args.Contains(AutostartArgument) && admin)
            return 0;

        // Einmal pro Anmeldung (Local\ = je Sitzung)
        using var single = new Mutex(initiallyOwned: true, @"Local\MorniLAN.Launcher", out var first);
        if (!first)
            return 0;

        Fullscreen = args.Contains(FullscreenArgument) || (!admin && !args.Contains(WindowedArgument));

        // Wenn das Geräte-Setup für ein Update den Launcher schließt, öffnet Windows ihn danach wieder.
        RegisterApplicationRestart(Fullscreen ? FullscreenArgument : null, 0);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Wird auch vom Avalonia-Designer verwendet.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);
}
