using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MorniLAN.Agent.Inventory;

/// <summary>Eine Verknüpfung im Startmenü, die auf eine EXE zeigt.</summary>
internal sealed record StartMenuShortcut(string Name, string TargetPath, string? Arguments, string ShortcutPath);

/// <summary>
/// Liest die Startmenü-Verknüpfungen für alle Benutzer und jedes Benutzerprofil.
/// Nur .lnk auf .exe-Dateien; Uninstaller, Hilfe-Dateien und Internet-Links werden übergangen.
/// </summary>
internal static class StartMenuScanner
{
    public static IReadOnlyList<StartMenuShortcut> Scan()
    {
        var shortcuts = new List<StartMenuShortcut>();
        foreach (var folder in StartMenuFolders())
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                if (TryResolve(file) is { } shortcut)
                    shortcuts.Add(shortcut);
            }
        }
        return [.. shortcuts.DistinctBy(s => (s.TargetPath.ToUpperInvariant(), s.Arguments ?? ""))];
    }

    private static IEnumerable<string> StartMenuFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

        // Jedes Benutzerprofil (als Dienst sieht der Agent sonst nur das Startmenü von SYSTEM)
        var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)) is { } publicDir
            ? Path.GetDirectoryName(publicDir)
            : null;
        if (users is null || !Directory.Exists(users))
            yield break;
        foreach (var profile in Directory.EnumerateDirectories(users))
        {
            var name = Path.GetFileName(profile);
            if (name is "Public" or "Default" or "Default User" or "All Users")
                continue;
            var programs = Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs");
            if (Directory.Exists(programs))
                yield return programs;
        }
    }

    private static StartMenuShortcut? TryResolve(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(shortcutPath, 0);
                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var arguments = new StringBuilder(2048);
                link.GetArguments(arguments, arguments.Capacity);

                var path = Environment.ExpandEnvironmentVariables(target.ToString());
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || PathText.IsUninstaller(path) || !File.Exists(path))
                    return null;
                var args = arguments.ToString().Trim();
                return new StartMenuShortcut(Path.GetFileNameWithoutExtension(shortcutPath), path,
                    args.Length == 0 ? null : args, shortcutPath);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            return null;
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
