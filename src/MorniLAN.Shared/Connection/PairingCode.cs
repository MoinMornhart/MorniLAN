using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace MorniLAN.Shared.Connection;

/// <summary>
/// Pairing-Code, den der Agent anzeigt und der Admin im Panel eintippt.
/// 8 Zeichen Crockford-Base32 (40 Bit), angezeigt als "XXXX-XXXX".
/// Verwechselbare Zeichen werden beim Eintippen toleriert (O → 0, I/L → 1).
/// </summary>
public static class PairingCode
{
    internal const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 8;

    /// <summary>Neuer zufälliger Code in Normalform (ohne Bindestrich).</summary>
    public static string Generate() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));

    /// <summary>Anzeigeform, z. B. "K7Q2-M9XD".</summary>
    public static string Format(string normalized) =>
        normalized.Length == Length ? $"{normalized[..4]}-{normalized[4..]}" : normalized;

    /// <summary>Eingabe bereinigen. Liefert false, wenn es kein gültiger Code sein kann.</summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        Span<char> buffer = stackalloc char[Length];
        var count = 0;
        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '_' or '\t')
                continue;
            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            if (count == Length || !Alphabet.Contains(c))
                return false;
            buffer[count++] = c;
        }

        if (count != Length)
            return false;
        normalized = new string(buffer);
        return true;
    }
}
