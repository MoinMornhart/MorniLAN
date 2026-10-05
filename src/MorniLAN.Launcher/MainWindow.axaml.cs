using Avalonia.Controls;
using MorniLAN.Shared;

namespace MorniLAN.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"MorniLAN Launcher {VersionInfo.Display}";
    }
}
