using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MorniLAN.Agent.Restrictions;

/// <summary>Zu welchem Windows-Konto gehört ein laufender Prozess? Nur als SYSTEM zuverlässig lesbar.</summary>
internal static class ProcessOwnership
{
    public sealed record RunningProcess(int Id, string? ExecutablePath, string? OwnerSid);

    /// <summary>Alle Prozesse mit Konto und EXE-Pfad, soweit lesbar (Systemprozesse bleiben ohne Angaben).</summary>
    public static IEnumerable<RunningProcess> Enumerate()
    {
        foreach (var process in Process.GetProcesses())
        {
            yield return new RunningProcess(process.Id, SafePath(process), OwnerSid(process));
            process.Dispose();
        }
    }

    private static string? SafePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    private static string? OwnerSid(Process process)
    {
        var token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process.Handle, TokenQuery, out token))
                return null;
            GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var length);
            if (length == 0)
                return null;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenUser, buffer, length, out _))
                    return null;
                var user = Marshal.PtrToStructure<TokenUserStruct>(buffer);
                return ConvertSidToStringSid(user.User.Sid, out var sid) ? sid : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (token != IntPtr.Zero)
                CloseHandle(token);
        }
    }

    private const int TokenQuery = 0x0008;
    private const int TokenUser = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenUserStruct
    {
        public SidAndAttributes User;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr buffer,
        int bufferLength, out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out string sidString);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
