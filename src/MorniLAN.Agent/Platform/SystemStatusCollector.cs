using System.Runtime.InteropServices;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Platform;

/// <summary>CPU, RAM und Systemlaufwerk für den Heartbeat.</summary>
internal sealed class SystemStatusCollector
{
    private readonly Lock _lock = new();
    private long _lastIdle, _lastTotal;

    public DeviceStatus Collect(Guid deviceId)
    {
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var hasMemory = GlobalMemoryStatusEx(ref memory);

        long driveFree = 0, driveTotal = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            driveFree = drive.AvailableFreeSpace;
            driveTotal = drive.TotalSize;
        }
        catch (IOException) { }

        return new DeviceStatus(
            deviceId,
            DateTimeOffset.UtcNow,
            CpuPercent(),
            hasMemory ? (long)(memory.TotalPhys - memory.AvailPhys) : 0,
            hasMemory ? (long)memory.TotalPhys : 0,
            driveFree,
            driveTotal,
            ForegroundApp: null,
            LoggedOnUser: null,
            LastUserActivity: null,
            RemoteSessionActive: false);
    }

    /// <summary>Auslastung seit dem letzten Aufruf (beim ersten Aufruf seit Systemstart).</summary>
    private double CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;
        lock (_lock)
        {
            var total = kernel + user; // Kernel-Zeit enthält die Leerlaufzeit
            var idleDelta = idle - _lastIdle;
            var totalDelta = total - _lastTotal;
            _lastIdle = idle;
            _lastTotal = total;
            return totalDelta <= 0 ? 0 : Math.Round(Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100), 1);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}
