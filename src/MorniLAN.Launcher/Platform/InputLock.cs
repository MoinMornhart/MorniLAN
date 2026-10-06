using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace MorniLAN.Launcher.Platform;

/// <summary>
/// Sperrt Maus und Tastatur des Freundes während des Fernzugriffs, damit er dem Admin nicht dazwischenfunkt.
/// Umgesetzt mit systemweiten Low-Level-Hooks (keine Admin-Rechte nötig): Eingaben werden verschluckt.
/// Läuft im Launcher, weil der in der Sitzung des Freundes läuft – der Dienst (Sitzung 0) könnte das nicht.
/// <para>Sicherheit: hebt sich nach <see cref="MaxDuration"/> von selbst auf, falls die Verbindung abreißt.</para>
/// </summary>
internal sealed class InputLock : IDisposable
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private DispatcherTimer? _safety;

    public InputLock()
    {
        _keyboardProc = Swallow;
        _mouseProc = Swallow;
    }

    public bool IsLocked => _keyboardHook != IntPtr.Zero;

    public void Set(bool locked)
    {
        if (locked)
            Enable();
        else
            Disable();
    }

    private void Enable()
    {
        if (IsLocked)
            return;
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        _safety = new DispatcherTimer { Interval = MaxDuration };
        _safety.Tick += (_, _) => Disable();
        _safety.Start();
    }

    private void Disable()
    {
        _safety?.Stop();
        _safety = null;
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    public void Dispose() => Disable();

    /// <summary>Hook-Rückruf: nicht an die nächste Stelle weitergeben, also verschlucken.</summary>
    private IntPtr Swallow(int code, IntPtr wParam, IntPtr lParam) =>
        code >= 0 ? 1 : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;

    private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
