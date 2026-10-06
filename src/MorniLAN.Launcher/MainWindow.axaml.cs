using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MorniLAN.Launcher.Platform;

namespace MorniLAN.Launcher;

/// <summary>
/// Vollbild über dem Desktop (Standardkonten) bzw. normales Fenster (Administratoren). Bedienung mit Maus,
/// Pfeiltasten und Controller: Die Richtungen springen zur nächsten Kachel in dieser Richtung.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel = new();
    private readonly bool _fullscreen;
    private readonly Gamepad _gamepad = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _padTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public MainWindow() : this(fullscreen: false) { }

    public MainWindow(bool fullscreen)
    {
        _fullscreen = fullscreen;
        InitializeComponent();
        DataContext = _viewModel;
        if (fullscreen)
        {
            WindowState = WindowState.FullScreen;
            CanResize = false;
        }

        _viewModel.FocusRequested += () => Dispatcher.UIThread.Post(FocusDefault, DispatcherPriority.Background);
        _refreshTimer.Tick += async (_, _) => await _viewModel.RefreshAsync();
        _clockTimer.Tick += (_, _) => _viewModel.UpdateClock();
        _systemTimer.Tick += async (_, _) => await _viewModel.UpdateSystemAsync();
        _padTimer.Tick += (_, _) => PollGamepad();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Opened += async (_, _) =>
        {
            _refreshTimer.Start();
            _clockTimer.Start();
            _systemTimer.Start();
            _padTimer.Start();
            await _viewModel.UpdateSystemAsync();
            await _viewModel.RefreshAsync();
            FocusDefault();
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _clockTimer.Stop();
            _systemTimer.Stop();
            _padTimer.Stop();
        };
    }

    /// <summary>Im Vollbild lässt sich der Launcher nicht per Alt+F4 schließen, nur Windows selbst beendet ihn.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_fullscreen && e.CloseReason is not (WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown))
            e.Cancel = true;
    }

    // ───── Tastatur und Controller ─────

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.BackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up or Key.Down:
                e.Handled = Move(e.Key == Key.Up ? NavigationDirection.Up : NavigationDirection.Down);
                break;
            case Key.Left or Key.Right when focused is not (TextBox or Slider):
                e.Handled = Move(e.Key == Key.Left ? NavigationDirection.Left : NavigationDirection.Right);
                break;
            case Key.Enter when focused is TextBox && _viewModel.ShowNewProfile:
                _viewModel.CreateProfileCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void PollGamepad()
    {
        // Nur, wenn der Launcher vorne ist: Während eines Spiels gehören die Eingaben dem Spiel
        if (!IsActive)
        {
            foreach (var _ in _gamepad.Poll()) { } // Zustand mitführen, damit nach dem Spiel nichts nachfeuert
            return;
        }
        foreach (var action in _gamepad.Poll())
        {
            switch (action)
            {
                case PadAction.Up: Move(NavigationDirection.Up); break;
                case PadAction.Down: Move(NavigationDirection.Down); break;
                case PadAction.Left: Move(NavigationDirection.Left); break;
                case PadAction.Right: Move(NavigationDirection.Right); break;
                case PadAction.Accept: PressFocused(); break;
                case PadAction.Back: _viewModel.BackCommand.Execute(null); break;
                case PadAction.Search when _viewModel.IsHome && !_viewModel.HasOverlay: SearchBox.Focus(NavigationMethod.Directional); break;
                case PadAction.Menu when !_viewModel.HasOverlay: _viewModel.OpenPowerCommand.Execute(null); break;
            }
        }
    }

    private void PressFocused()
    {
        if (FocusManager?.GetFocusedElement() is Button { Command: { } command } button && command.CanExecute(button.CommandParameter))
            command.Execute(button.CommandParameter);
    }

    private Control? _lastContentFocus;

    /// <summary>
    /// Wie bei Konsolen: hoch/runter zur nächsten Reihe und dort zur waagerecht nächsten Fläche, links/rechts nur
    /// innerhalb der Reihe. Inhalt und Leiste sind getrennt: in die Leiste erst, wenn darunter nichts mehr kommt;
    /// aus der Leiste nach oben zurück zur zuletzt gewählten Kachel.
    /// </summary>
    private bool Move(NavigationDirection direction)
    {
        var current = FocusManager?.GetFocusedElement() as Control;
        var inBar = current is not null && !_viewModel.HasOverlay && IsInside(current, Bar);
        var area = _viewModel.HasOverlay ? Targets(Overlay) : inBar ? Targets(Bar) : Targets(ContentArea);
        if (current is null || !area.Contains(current))
        {
            var start = _lastContentFocus is { } last && Targets(ContentArea).Contains(last) ? last : Targets(ContentArea).FirstOrDefault();
            if (start is not null)
                Focus(start);
            return start is not null;
        }

        var next = Nearest(current, area, direction);
        if (next is null && !_viewModel.HasOverlay)
        {
            if (!inBar && direction == NavigationDirection.Down)
                next = Targets(Bar).FirstOrDefault();
            else if (inBar && direction == NavigationDirection.Up)
                next = _lastContentFocus is { } last && Targets(ContentArea).Contains(last) ? last : Nearest(current, Targets(ContentArea), direction);
        }
        if (next is not null)
        {
            if (!inBar || !IsInside(next, Bar))
                _lastContentFocus = IsInside(next, Bar) ? current : next;
            Focus(next);
        }
        return true; // am Rand stehen bleiben, nicht aus dem Fenster springen
    }

    private Control? Nearest(Control current, List<Control> area, NavigationDirection direction)
    {
        var from = Rect(current);
        var vertical = direction is NavigationDirection.Up or NavigationDirection.Down;
        var candidates = area.Where(c => c != current).Select(c => (Control: c, Rect: Rect(c))).Where(x => direction switch
        {
            NavigationDirection.Up => x.Rect.Bottom <= from.Top + 4,
            NavigationDirection.Down => x.Rect.Top >= from.Bottom - 4,
            // Gleiche Reihe: senkrechte Mitte innerhalb der Fläche
            NavigationDirection.Left => x.Rect.Right <= from.Left + 4 && Math.Abs(x.Rect.Center.Y - from.Center.Y) < from.Height / 2 + 20,
            _ => x.Rect.Left >= from.Right - 4 && Math.Abs(x.Rect.Center.Y - from.Center.Y) < from.Height / 2 + 20,
        }).ToList();
        if (candidates.Count == 0)
            return null;
        if (!vertical)
            return candidates.MinBy(x => Math.Abs(x.Rect.Center.X - from.Center.X)).Control;
        // Nächste Reihe finden, dann darin die waagerecht nächste Fläche
        double Gap((Control Control, Rect Rect) x) =>
            direction == NavigationDirection.Down ? x.Rect.Top - from.Bottom : from.Top - x.Rect.Bottom;
        var rowGap = candidates.Min(Gap);
        return candidates.Where(x => Gap(x) <= rowGap + 24).MinBy(x => Math.Abs(x.Rect.Center.X - from.Center.X)).Control;
    }

    /// <summary>Bedienbare Flächen in einem Bereich (Inhalt, Leiste oder offener Dialog).</summary>
    private static List<Control> Targets(Visual root) =>
        [.. root.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button or TextBox or Slider && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled
                        && c.Bounds.Width > 0)];

    /// <summary>Alle bedienbaren Flächen der aktuellen Ansicht (für den Startfokus).</summary>
    private IEnumerable<Control> Targets() => Targets(_viewModel.HasOverlay ? Overlay : this);

    private static bool IsInside(Control control, Visual area) => control == area || control.GetVisualAncestors().Contains(area);

    private Rect Rect(Control control) =>
        control.TranslatePoint(default, this) is { } topLeft ? new Rect(topLeft, control.Bounds.Size) : default;

    private static void Focus(Control control)
    {
        control.Focus(NavigationMethod.Directional);
        control.BringIntoView();
    }

    /// <summary>Sinnvoller Startpunkt je Ansicht: Dialogfeld, zuletzt gewähltes Profil oder erste Kachel.</summary>
    private void FocusDefault()
    {
        if (_viewModel.ShowNewProfile)
        {
            NewProfileNameBox.Focus(NavigationMethod.Directional);
            return;
        }
        if (_viewModel.ShowPower)
        {
            // Nicht „Herunterfahren“ vorauswählen: ein versehentliches A am Controller wäre sonst das Ende
            PowerDialog.GetVisualDescendants().OfType<Button>().LastOrDefault()?.Focus(NavigationMethod.Directional);
            return;
        }
        if (_viewModel.HasOverlay)
        {
            Targets().FirstOrDefault()?.Focus(NavigationMethod.Directional);
            return;
        }
        if (_viewModel.IsProfiles)
        {
            var buttons = ProfilesView.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            var last = _viewModel.LastProfile;
            (buttons.FirstOrDefault(b => last is not null && b.DataContext == last) ?? buttons.FirstOrDefault())?
                .Focus(NavigationMethod.Directional);
            return;
        }
        if (_viewModel.IsHome)
        {
            var first = HomeView.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Classes.Contains("tile") && b.IsEffectivelyVisible);
            if (first is not null)
                Focus(first);
        }
    }
}
