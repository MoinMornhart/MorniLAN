namespace MorniLAN.Shared.Models;

/// <summary>
/// Erkennt „unnötige“ Einträge, die der Admin vermutlich nicht in der Liste sehen will (Laufzeiten, Treiber und
/// Hersteller-Tools, Windows-/Store-Zubehör, Hintergrund-Programme). Nur ein <b>Vorschlag</b>: Ausgeblendet wird
/// erst, wenn der Admin es bestätigt – darum dürfen ein paar Fehltreffer dabei sein, er hakt sie ab.
/// Läuft auf dem fertigen <see cref="AppEntry"/>, nicht auf den Roh-Registry-Daten, und ergänzt damit die harte
/// Ausblendung über <see cref="AppEntry.IsSystemComponent"/> (die bleibt die Standard-Ausblendung).
/// </summary>
public static class AppNoise
{
    public const string Runtime = "Laufzeit/Redistributable";
    public const string Driver = "Treiber/Hersteller-Tool";
    public const string WindowsStore = "Windows-/Store-Zubehör";
    public const string Background = "Hintergrund-Programm";

    private static readonly string[] RuntimeNames =
    [
        "Redistributable", "Visual C++", "Microsoft .NET", ".NET Runtime", ".NET SDK", ".NET Desktop Runtime",
        ".NET Host", "ASP.NET Core", "Windows Desktop Runtime", "Windows SDK", "DirectX", "Vulkan Run", "PhysX",
        "Microsoft Edge WebView2", "Windows App Runtime", "GameInput", "OpenAL", "Java Runtime", "Java SE Development",
    ];

    private static readonly string[] VendorPublishers =
    [
        "NVIDIA", "Realtek", "Intel", "Advanced Micro Devices", "AMD", "Synaptics", "ELAN", "Killer", "Rivet",
        "Dolby", "DTS", "Waves", "Nahimic", "MICRO-STAR", "MSI", "ASUSTeK", "ASUS", "Gigabyte", "Logitech", "Razer",
        "Corsair", "SteelSeries", "Lenovo", "Dell", "Hewlett", "HP Inc",
    ];

    private static readonly string[] VendorToolNames =
    [
        "Driver", "Treiber", "Chipset", "Control Panel", "Audio Console", "Audio Driver", "Graphics Driver",
        "Management Engine", "Serial IO", "FrameView", "HD Audio", "GeForce Experience", "Armoury Crate",
        "MyASUS", "Vantage", "Command Center", "Dragon Center", "Gaming Lobby", "Firmware", "LED", "RGB", "iCUE",
        "Synapse", "G HUB", "Sonic Studio", "Smart Gesture", "Precision", "Optane",
    ];

    private static readonly string[] BackgroundNames =
    [
        "Updater", "Update Service", "AutoUpdate", "Crash Handler", "Crash Reporter", "Helper", "Background",
        "Notification", "Tray", "Web Helper", "Service Host", "Telemetry", "Reporting", "Diagnostics",
    ];

    /// <summary>
    /// Grund, warum der Eintrag als unnötig vorgeschlagen wird, oder null, wenn er normal angezeigt bleiben soll.
    /// Spiele und eigene Einträge des Admins werden nie vorgeschlagen.
    /// </summary>
    public static string? Classify(AppEntry app)
    {
        if (app.IsGame || app.Source == AppSource.Custom)
            return null;

        var name = app.Name ?? "";
        var publisher = app.Publisher ?? "";

        if (RuntimeNames.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return Runtime;

        if (VendorToolNames.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))
            || (VendorPublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase))
                && app.Source == AppSource.InstalledProgram))
            return Driver;

        if (app.Source == AppSource.StoreApp
            && (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
                || VendorPublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase))))
            return WindowsStore;

        if (BackgroundNames.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return Background;

        return null;
    }
}
