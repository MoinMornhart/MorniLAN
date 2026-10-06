using Avalonia;
using Avalonia.Data.Converters;

namespace MorniLAN.Launcher;

internal static class Converters
{
    /// <summary>Ausgewählte Farbe bekommt einen Rand.</summary>
    public static readonly IValueConverter SelectedBorder =
        new FuncValueConverter<bool, Thickness>(selected => new Thickness(selected ? 4 : 0));

    /// <summary>Symbol aus „Segoe Fluent Icons“: Lautsprecher bzw. durchgestrichen.</summary>
    public static readonly IValueConverter MuteIcon =
        new FuncValueConverter<bool, string>(muted => muted ? "" : "");
}
