using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MorniLAN.Admin.ViewModels;

namespace MorniLAN.Admin;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            // Server außerhalb des UI-Threads stoppen, sonst blockieren sich die Fortsetzungen gegenseitig.
            desktop.Exit += (_, _) => Task.Run(() => viewModel.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
            _ = viewModel.StartServerAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
