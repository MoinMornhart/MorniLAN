using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace MorniLAN.Launcher.Platform;

/// <summary>Ist das angemeldete Konto ein Administrator? Auch dann, wenn UAC das Token gerade einschränkt.</summary>
internal static class UserAccount
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return true;
        // Mit UAC hat ein Admin ein eingeschränktes Token: Gruppe nur "deny only", erkennbar am Erhöhungstyp
        return GetTokenInformation(identity.Token, TokenElevationType, out var type, sizeof(int), out _)
               && type == TokenElevationTypeLimited;
    }

    private const int TokenElevationType = 18;
    private const int TokenElevationTypeLimited = 3;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
}

/// <summary>Lautstärke des Standard-Ausgabegeräts über die Core-Audio-Schnittstelle.</summary>
internal static class AudioVolume
{
    /// <summary>0 bis 100, oder null, wenn es kein Ausgabegerät gibt.</summary>
    public static (int Level, bool Muted)? Get()
    {
        try
        {
            var volume = Endpoint();
            if (volume is null)
                return null;
            volume.GetMasterVolumeLevelScalar(out var level);
            volume.GetMute(out var muted);
            return ((int)Math.Round(level * 100), muted);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static void Set(int level)
    {
        try
        {
            var context = Guid.Empty;
            var volume = Endpoint();
            volume?.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 100) / 100f, ref context);
            if (level > 0)
                volume?.SetMute(false, ref context);
        }
        catch (COMException) { }
    }

    public static void SetMuted(bool muted)
    {
        try
        {
            var context = Guid.Empty;
            Endpoint()?.SetMute(muted, ref context);
        }
        catch (COMException) { }
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0)
            return null;
        var iid = typeof(IAudioEndpointVolume).GUID;
        return device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var result) == 0
            ? (IAudioEndpointVolume)result
            : null;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

/// <summary>Netzwerkstatus für die Leiste: Kabel, WLAN (mit Name und Signal) oder offline.</summary>
internal static partial class NetworkStatus
{
    public static string Describe()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && n.GetIPProperties().GatewayAddresses.Count > 0)
            .ToList();
        if (active.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
            return "LAN verbunden";
        if (active.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
            return Wlan() is { } wlan ? $"WLAN {wlan.Ssid} · {wlan.Signal} %" : "WLAN verbunden";
        return active.Count > 0 ? "Verbunden" : "Kein Netzwerk";
    }

    /// <summary>Name und Signal des WLANs aus „netsh wlan show interfaces“ (auch als Standardbenutzer).</summary>
    private static (string Ssid, int Signal)? Wlan()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh.exe", "wlan show interfaces")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return ParseWlan(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    internal static (string Ssid, int Signal)? ParseWlan(string output)
    {
        var ssid = SsidLine().Match(output);
        var signal = SignalLine().Match(output);
        return ssid.Success && signal.Success ? (ssid.Groups[1].Value.Trim(), int.Parse(signal.Groups[1].Value)) : null;
    }

    [GeneratedRegex(@"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SsidLine();

    [GeneratedRegex(@"^\s*Signal\s*:\s*(\d+)\s*%", RegexOptions.Multiline)]
    private static partial Regex SignalLine();
}

/// <summary>Abmelden, Neustart, Herunterfahren (darf jeder Benutzer an seinem PC).</summary>
internal static class Power
{
    public static void LogOff() => Run("/l");
    public static void Restart() => Run("/r /t 0");
    public static void ShutDown() => Run("/s /t 0");

    private static void Run(string arguments)
    {
        using var _ = Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true });
    }
}

/// <summary>Was ein Controller gerade will (Knopf gedrückt oder Stick gehalten).</summary>
internal enum PadAction { Up, Down, Left, Right, Accept, Back, Search, Menu }

/// <summary>
/// Xbox-kompatible Controller über XInput (PlayStation-Controller über Steam Input o. Ä.). Wird regelmäßig
/// abgefragt; Steuerkreuz und linker Stick wiederholen beim Halten.
/// </summary>
internal sealed class Gamepad
{
    private static readonly TimeSpan FirstRepeat = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan NextRepeat = TimeSpan.FromMilliseconds(130);

    private readonly ushort[] _lastButtons = new ushort[4];
    private PadAction? _held;
    private DateTime _nextRepeat;
    private bool _available = true;

    /// <summary>Aktionen seit der letzten Abfrage.</summary>
    public IEnumerable<PadAction> Poll()
    {
        if (!_available)
            yield break;
        PadAction? direction = null;
        for (uint i = 0; i < 4; i++)
        {
            XInputState state;
            try
            {
                if (XInputGetState(i, out state) != 0)
                    continue;
            }
            catch (DllNotFoundException)
            {
                _available = false;
                yield break;
            }
            var buttons = state.Gamepad.Buttons;
            var pressed = (ushort)(buttons & ~_lastButtons[i]);
            _lastButtons[i] = buttons;
            if ((pressed & 0x1000) != 0) yield return PadAction.Accept; // A
            if ((pressed & 0x2000) != 0) yield return PadAction.Back; // B
            if ((pressed & 0x8000) != 0) yield return PadAction.Search; // Y
            if ((pressed & 0x0010) != 0) yield return PadAction.Menu; // Start
            direction ??= Direction(buttons, state.Gamepad.ThumbLX, state.Gamepad.ThumbLY);
        }

        var now = DateTime.UtcNow;
        if (direction is null)
        {
            _held = null;
        }
        else if (direction != _held)
        {
            _held = direction;
            _nextRepeat = now + FirstRepeat;
            yield return direction.Value;
        }
        else if (now >= _nextRepeat)
        {
            _nextRepeat = now + NextRepeat;
            yield return direction.Value;
        }
    }

    private static PadAction? Direction(ushort buttons, short x, short y)
    {
        const short dead = 16000;
        if ((buttons & 0x0001) != 0 || y > dead) return PadAction.Up;
        if ((buttons & 0x0002) != 0 || y < -dead) return PadAction.Down;
        if ((buttons & 0x0004) != 0 || x < -dead) return PadAction.Left;
        if ((buttons & 0x0008) != 0 || x > dead) return PadAction.Right;
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);
}
