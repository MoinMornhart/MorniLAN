using System.Runtime.InteropServices;
using Avalonia;

namespace MorniLAN.Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Wenn das Geräte-Setup für ein Update den Launcher schließt, öffnet Windows ihn danach wieder.
        RegisterApplicationRestart(null, 0);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
