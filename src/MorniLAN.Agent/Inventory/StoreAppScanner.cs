using System.Xml.Linq;
using Windows.Management.Deployment;

namespace MorniLAN.Agent.Inventory;

/// <summary>Eine startbare App aus einem Store-Paket (ein Paket kann mehrere Apps enthalten).</summary>
/// <param name="LogoPath">Bestes gefundenes Logo (PNG) für das Icon.</param>
internal sealed record StoreApp(string AppUserModelId, string Name, string Publisher, string Version, string? LogoPath,
    long? SizeBytes);

/// <summary>
/// Findet installierte Store-Apps über den Windows-PackageManager. Als Dienst (SYSTEM) für alle Benutzer,
/// im Konsolenmodus nur für den eigenen Benutzer. Frameworks, Ressourcen-Pakete und Systempakete
/// (nicht aus dem Store signiert) werden übergangen, ebenso Apps ohne Eintrag im Startmenü.
/// </summary>
internal static class StoreAppScanner
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    public static IReadOnlyList<StoreApp> Scan()
    {
        var manager = new PackageManager();
        IEnumerable<Windows.ApplicationModel.Package> packages;
        try
        {
            packages = manager.FindPackages(); // alle Benutzer: braucht Admin-Rechte bzw. SYSTEM
        }
        catch (UnauthorizedAccessException)
        {
            packages = manager.FindPackagesForUser(string.Empty);
        }

        var apps = new List<StoreApp>();
        foreach (var package in packages)
        {
            try
            {
                if (package.IsFramework || package.IsResourcePackage || package.IsBundle
                    || package.SignatureKind != Windows.ApplicationModel.PackageSignatureKind.Store)
                    continue;
                var location = package.InstalledLocation?.Path;
                if (location is null)
                    continue;
                var version = package.Id.Version;
                var fullName = package.Id.FullName;
                var packageName = package.Id.Name;
                apps.AddRange(ReadManifest(location, package.Id.FamilyName, package.DisplayName, package.PublisherDisplayName,
                    $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                    resource => ResolveResource(fullName, packageName, resource)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
                                           or System.Xml.XmlException)
            {
                // ein kaputtes Paket darf die übrigen nicht verhindern
            }
        }
        return [.. apps.DistinctBy(a => a.AppUserModelId.ToUpperInvariant())];
    }

    /// <summary>Liest die Apps aus AppxManifest.xml. Öffentlich für Tests mit Beispiel-Manifesten.</summary>
    internal static IEnumerable<StoreApp> ReadManifest(string location, string familyName, string packageDisplayName,
        string publisher, string version, Func<string, string?>? resolveResource = null)
    {
        var manifestPath = Path.Combine(location, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            yield break;
        var manifest = XDocument.Load(manifestPath);
        var applications = manifest.Descendants(Foundation + "Application")
            .Where(a => a.Element(Uap + "VisualElements") is { } v
                        && !string.Equals((string?)v.Attribute("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var application in applications)
        {
            var visual = application.Element(Uap + "VisualElements")!;
            var id = (string?)application.Attribute("Id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            // DisplayName im Manifest ist oft ein Verweis ("ms-resource:…"): auflösen, sonst der Paketname.
            // Hat ein Paket mehrere Apps (iCloud: Fotos, Passwörter), bekommt jede ihren eigenen Namen.
            var name = (string?)visual.Attribute("DisplayName");
            if (name is not null && name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                name = resolveResource?.Invoke(name);
            if (string.IsNullOrWhiteSpace(name))
                name = applications.Count > 1 && applications[0] != application ? $"{packageDisplayName} ({id})" : packageDisplayName;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var logo = (string?)visual.Attribute("Square44x44Logo") ?? (string?)visual.Attribute("Square150x150Logo");
            yield return new StoreApp($"{familyName}!{id}", name.Trim(), publisher, version, ResolveLogo(location, logo),
                null);
        }
    }

    /// <summary>
    /// Löst "ms-resource:AppName" aus der resources.pri des Pakets auf. Probiert die üblichen Schreibweisen
    /// (mit und ohne "Resources/"-Pfad), null wenn nichts passt.
    /// </summary>
    private static string? ResolveResource(string packageFullName, string packageName, string resource)
    {
        var key = resource["ms-resource:".Length..];
        string[] uris = key.StartsWith("//", StringComparison.Ordinal)
            ? ["ms-resource:" + key]
            : key.StartsWith('/')
                ? [$"ms-resource://{packageName}{key}"]
                : [$"ms-resource://{packageName}/Resources/{key}", $"ms-resource://{packageName}/{key}"];
        foreach (var uri in uris)
        {
            var buffer = new System.Text.StringBuilder(512);
            if (SHLoadIndirectString($"@{{{packageFullName}?{uri}}}", buffer, buffer.Capacity, IntPtr.Zero) == 0
                && buffer.Length > 0 && !buffer.ToString().StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return buffer.ToString();
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, System.Text.StringBuilder output, int outputLength, IntPtr reserved);

    /// <summary>
    /// Logos gibt es in Varianten (Logo.scale-200.png, Logo.targetsize-48.png …). Bevorzugt 48 px, dann
    /// scale-200, sonst die größte Datei.
    /// </summary>
    internal static string? ResolveLogo(string location, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var path = Path.Combine(location, relative.Replace('/', '\\'));
        if (File.Exists(path))
            return path;
        var directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
            return null;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var candidates = Directory.EnumerateFiles(directory, baseName + ".*.png")
            .Select(f => new FileInfo(f)).ToList();
        if (candidates.Count == 0)
            return null;
        return (candidates.FirstOrDefault(f => f.Name.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase)
                                               && !f.Name.Contains("contrast", StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(f => f.Name.Contains("scale-200", StringComparison.OrdinalIgnoreCase)
                                                  && !f.Name.Contains("contrast", StringComparison.OrdinalIgnoreCase))
                ?? candidates.OrderByDescending(f => f.Length).First()).FullName;
    }
}
