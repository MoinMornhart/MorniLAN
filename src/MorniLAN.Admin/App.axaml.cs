using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MorniLAN.Admin.Platform;
using MorniLAN.Admin.ViewModels;

namespace MorniLAN.Admin;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            // Das Fenster zu schließen beendet nicht automatisch: Das entscheidet OnWindowClosing.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _viewModel = new MainViewModel();
            var viewModel = _viewModel;
            viewModel.RequestExit = () => Dispatcher.UIThread.Post(Exit);
            viewModel.RequestShow = () => Dispatcher.UIThread.Post(ShowWindow);
            // Server außerhalb des UI-Threads stoppen, sonst blockieren sich die Fortsetzungen gegenseitig.
            desktop.Exit += (_, _) => Task.Run(() => viewModel.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));

            // Beim Autostart unsichtbar im Infobereich starten.
            if (desktop.Args?.Contains(Autostart.MinimizedArgument) != true)
                ShowWindow();

            ListenForShowSignal();
            _ = viewModel.StartServerAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _viewModel };
            _viewModel!.CopyToClipboard = async text =>
            {
                if (_window?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(text);
            };
            _window.Closing += OnWindowClosing;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Beim Beenden schließt Shutdown() selbst das Fenster: nicht erneut Exit() aufrufen (Endlosschleife,
        // Stapelüberlauf beim Update, gefunden 2026-10-05).
        if (_exiting)
            return;
        if (_viewModel?.KeepRunningInTray == true)
        {
            e.Cancel = true;
            _window?.Hide();
            return;
        }
        Exit();
    }

    private void Exit()
    {
        if (_exiting)
            return;
        _exiting = true;
        _desktop?.Shutdown();
    }

    /// <summary>Ein zweiter Start (Startmenü, Desktop) holt das laufende Fenster nach vorne.</summary>
    private void ListenForShowSignal()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignalName);
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.UIThread.Post(ShowWindow);
        }) { IsBackground = true, Name = "MorniLAN.ShowSignal" };
        thread.Start();
    }

    private void OnTrayClicked(object? sender, EventArgs e) => ShowWindow();
    private void OnTrayOpen(object? sender, EventArgs e) => ShowWindow();
    private void OnTrayExit(object? sender, EventArgs e) => Exit();
}
