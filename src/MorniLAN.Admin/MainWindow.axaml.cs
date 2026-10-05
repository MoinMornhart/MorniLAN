using Avalonia.Controls;
using MorniLAN.Shared;

namespace MorniLAN.Admin;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = VersionInfo.Display;
    }
}
