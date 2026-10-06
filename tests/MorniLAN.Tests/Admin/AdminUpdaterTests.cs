using System.Diagnostics;
using System.Text;
using MorniLAN.Admin.Platform;

namespace MorniLAN.Tests.Admin;

public class AdminUpdaterTests
{
    [Fact]
    public void LaunchScript_QuotesPathsWithApostrophes()
    {
        var script = AdminUpdater.LaunchScript(1234, @"C:\Users\O'Brien\setup.exe", @"C:\Users\O'Brien\log.txt",
            @"C:\Users\O'Brien\MorniLAN.Admin.exe");

        Assert.StartsWith("Wait-Process -Id 1234 ", script);
        Assert.Contains(@"-FilePath 'C:\Users\O''Brien\setup.exe'", script);
        Assert.Contains(@"/LOG=""C:\Users\O''Brien\log.txt""", script);
    }

    [Fact]
    public void LaunchScript_RestartsPanel_IfNoneRunningAfterSetup()
    {
        var script = AdminUpdater.LaunchScript(1, @"C:\s.exe", @"C:\l.txt", @"C:\App\MorniLAN.Admin.exe");

        // Das Setup wird abgewartet …
        Assert.Contains("-Wait -FilePath 'C:\\s.exe'", script);
        // … und wenn danach kein Panel läuft, wird die vorhandene Panel-Exe selbst gestartet (Selbstheilung)
        Assert.Contains("Get-Process -Name 'MorniLAN.Admin'", script);
        Assert.Contains(@"Start-Process -FilePath 'C:\App\MorniLAN.Admin.exe'", script);
    }

    /// <summary>
    /// Echter Ablauf: Das Skript wartet, bis der „Panel“-Prozess weg ist, und startet erst dann das „Setup“.
    /// Früher lief das Setup nach fester Wartezeit los, während das Panel noch lief, und brach ab.
    /// </summary>
    [Fact]
    public async Task LaunchScript_StartsSetupOnlyAfterPanelExited()
    {
        var dir = Directory.CreateTempSubdirectory("mornilan update '");
        try
        {
            var marker = Path.Combine(dir.FullName, "setup-ran.txt");
            var log = Path.Combine(dir.FullName, "log.txt");
            // Stellvertreter für das Setup: schreibt die Uhrzeit und die Argumente in eine Datei
            var setup = Path.Combine(dir.FullName, "fake setup.cmd");
            await File.WriteAllTextAsync(setup, $"@echo %TIME% %* > \"{marker}\"\r\n", TestContext.Current.CancellationToken);

            // Stellvertreter für das Panel: lebt etwa 4 Sekunden
            using var panel = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep 4")
                { UseShellExecute = false, CreateNoWindow = true })!;

            // Panel-Exe absichtlich nicht vorhanden: Selbstheilung findet nichts und startet nichts nach
            var panelExe = Path.Combine(dir.FullName, "MorniLAN.Admin.exe");
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(AdminUpdater.LaunchScript(panel.Id, setup, log, panelExe)));
            using var launcher = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
                { UseShellExecute = false, CreateNoWindow = true })!;

            await Task.Delay(2000, TestContext.Current.CancellationToken);
            Assert.False(panel.HasExited);
            Assert.False(File.Exists(marker)); // Panel läuft noch: Setup darf nicht gestartet sein

            await launcher.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(panel.HasExited);
            for (var i = 0; i < 50 && !File.Exists(marker); i++)
                await Task.Delay(100, TestContext.Current.CancellationToken);
            var content = await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken);
            Assert.Contains("/VERYSILENT", content);
            Assert.Contains(log, content);
        }
        finally
        {
            for (var i = 0; i < 20; i++)
            {
                try { dir.Delete(recursive: true); break; }
                catch (IOException) { await Task.Delay(100, TestContext.Current.CancellationToken); }
            }
        }
    }
}
