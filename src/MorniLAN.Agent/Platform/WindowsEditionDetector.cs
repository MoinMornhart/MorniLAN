using Microsoft.Win32;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Platform;

/// <summary>Liest Edition und Build aus der Registry.</summary>
internal static class WindowsEditionDetector
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static WindowsEditionInfo Detect()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(CurrentVersionKey);

        var editionId = key?.GetValue("EditionID") as string ?? "";
        var productName = key?.GetValue("ProductName") as string ?? "Windows";
        var displayVersion = key?.GetValue("DisplayVersion") as string ?? "";
        _ = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build);
        if (build == 0)
            build = Environment.OSVersion.Version.Build;

        return new WindowsEditionInfo(editionId, productName, displayVersion, build);
    }
}
