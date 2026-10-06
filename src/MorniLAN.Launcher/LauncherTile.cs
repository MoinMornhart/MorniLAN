using Avalonia.Media.Imaging;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Launcher;

/// <summary>Eine Kachel: Spiele mit Cover (Hochformat), Apps mit Icon.</summary>
public sealed class LauncherTile
{
    public LauncherTile(LauncherApp app)
    {
        App = app;
        Icon = Load(app.Icon);
        Cover = Load(app.Cover);
    }

    public LauncherApp App { get; }
    public string Name => App.Name;
    public Bitmap? Icon { get; }
    public Bitmap? Cover { get; }
    public bool HasIcon => Icon is not null;
    public bool HasCover => Cover is not null;
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    private static Bitmap? Load(byte[]? data)
    {
        if (data is not { Length: > 0 })
            return null;
        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
