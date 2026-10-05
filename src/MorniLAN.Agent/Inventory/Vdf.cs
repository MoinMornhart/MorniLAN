using System.Text;

namespace MorniLAN.Agent.Inventory;

/// <summary>Ein Knoten im Valve-KeyValues-Format (VDF/ACF): Schlüssel → Text oder Unterknoten.</summary>
internal sealed class VdfNode
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, object>> Entries => _values;

    public string? this[string key] => _values.TryGetValue(key, out var value) ? value as string : null;

    public VdfNode? Child(string key) => _values.TryGetValue(key, out var value) ? value as VdfNode : null;

    internal void Set(string key, object value) => _values[key] = value;
}

/// <summary>
/// Liest Steams Textformat (libraryfolders.vdf, appmanifest_*.acf): "Schlüssel" "Wert" oder "Schlüssel" { … },
/// mit Escapes (\\, \", \n, \t), //-Kommentaren und Bedingungen wie [$WIN32], die ignoriert werden.
/// Fehlerhafte Dateien führen zu einer FormatException, nie zu einer Endlosschleife.
/// </summary>
internal static class Vdf
{
    public static VdfNode Parse(string text)
    {
        var position = 0;
        var root = new VdfNode();
        ParseInto(root, text, ref position, isRoot: true);
        return root;
    }

    public static VdfNode ParseFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    private static void ParseInto(VdfNode node, string text, ref int position, bool isRoot)
    {
        while (true)
        {
            var key = NextToken(text, ref position);
            if (key is null)
            {
                if (isRoot)
                    return;
                throw new FormatException("VDF: schließende Klammer fehlt");
            }
            if (key.Value.IsBrace && key.Value.Text == "}")
            {
                if (isRoot)
                    throw new FormatException("VDF: unerwartete schließende Klammer");
                return;
            }
            if (key.Value.IsBrace)
                throw new FormatException("VDF: Schlüssel erwartet");

            var value = NextToken(text, ref position) ?? throw new FormatException($"VDF: Wert für \"{key.Value.Text}\" fehlt");
            if (value.IsBrace && value.Text == "{")
            {
                var child = new VdfNode();
                ParseInto(child, text, ref position, isRoot: false);
                node.Set(key.Value.Text, child);
            }
            else if (value.IsBrace)
            {
                throw new FormatException($"VDF: Wert für \"{key.Value.Text}\" fehlt");
            }
            else
            {
                node.Set(key.Value.Text, value.Text);
            }
        }
    }

    private readonly record struct Token(string Text, bool IsBrace);

    private static Token? NextToken(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                    position++;
            }
            else if (c == '[')
            {
                // Plattform-Bedingung wie [$WIN32]: überspringen
                while (position < text.Length && text[position] != ']')
                    position++;
                position++;
            }
            else if (c is '{' or '}')
            {
                position++;
                return new Token(c.ToString(), true);
            }
            else if (c == '"')
            {
                return new Token(ReadQuoted(text, ref position), false);
            }
            else
            {
                var start = position;
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
                    position++;
                return new Token(text[start..position], false);
            }
        }
        return null;
    }

    private static string ReadQuoted(string text, ref int position)
    {
        position++; // öffnendes "
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position++];
            if (c == '"')
                return builder.ToString();
            if (c == '\\' && position < text.Length)
            {
                var next = text[position++];
                builder.Append(next switch { 'n' => '\n', 't' => '\t', _ => next });
            }
            else
            {
                builder.Append(c);
            }
        }
        throw new FormatException("VDF: Anführungszeichen nicht geschlossen");
    }
}
