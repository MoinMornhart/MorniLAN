using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MorniLAN.Shared.Updates;

/// <summary>
/// SemVer 2.0 ohne Build-Metadaten: 1.2.3 oder 1.2.3-beta.1. Vergleich nach SemVer-Regeln:
/// eine Vorabversion ist kleiner als die fertige Version, Zahlen-Teile werden als Zahlen verglichen.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> Prerelease { get; }
    public bool IsPrerelease => Prerelease.Count > 0;

    /// <summary>Akzeptiert auch ein führendes "v" und hängt Build-Metadaten ("+abc") ab.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value[..plus];

        var dash = value.IndexOf('-');
        var core = dash >= 0 ? value[..dash] : value;
        var pre = dash >= 0 ? value[(dash + 1)..] : null;

        var parts = core.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;

        string[] prerelease = [];
        if (pre is not null)
        {
            prerelease = pre.Split('.');
            if (prerelease.Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
                return false;
        }
        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Keine gültige Version: {text}");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
            return core;
        // Ohne Vorabkennung ist höher als mit
        if (!IsPrerelease || !other.IsPrerelease)
            return other.IsPrerelease.CompareTo(IsPrerelease);
        for (var i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            var a = Prerelease[i];
            var b = other.Prerelease[i];
            var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (result != 0)
                return result;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));
    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{string.Join('.', Prerelease)}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
}
