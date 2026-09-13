using System.Text;

namespace RHI.Linux.Core;

// Valve KeyValues: tokenization handles comments, escapes, duplicate keys and nested objects.
// Source offsets allow launch-option edits without rewriting unrelated Steam settings.
public sealed class VdfNode
{
    public string Key { get; init; } = "";
    public string? Value { get; init; }
    public int ValueStart { get; init; }
    public int ValueEnd { get; init; }
    public int CloseOffset { get; init; }
    public List<VdfNode> Children { get; init; } = [];
    public VdfNode? Child(string key) => Children.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    public string? Text(string key) => Child(key)?.Value;
    public VdfNode? At(params string[] keys) => keys.Aggregate((VdfNode?)this, (node, key) => node?.Child(key));
}

public static class Vdf
{
    private record Token(string Text, int Start, int End, bool Quoted);
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    public static VdfNode Parse(string text)
    {
        var tokens = new List<Token>();
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == '\uFEFF') { i++; continue; }
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            { while (i < text.Length && text[i] != '\n') i++; continue; }
            var start = i;
            if (text[i] is '{' or '}') { tokens.Add(new(text[i++].ToString(), start, i, false)); continue; }
            var s = new StringBuilder();
            bool quoted = text[i] == '"';
            if (quoted)
            {
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '\\' or '"') i++;
                    s.Append(text[i++]);
                }
                if (i == text.Length) throw new FormatException("Unterminated VDF string.");
                i++;
            }
            else while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not '{' and not '}') s.Append(text[i++]);
            tokens.Add(new(s.ToString(), start, i, quoted));
        }
        int pos = 0;
        VdfNode Read(string key, bool nested)
        {
            var children = new List<VdfNode>();
            while (pos < tokens.Count)
            {
                var k = tokens[pos++];
                if (!k.Quoted && k.Text == "}")
                {
                    if (!nested) throw new FormatException("Unexpected VDF closing brace.");
                    return new() { Key = key, Children = children, CloseOffset = k.Start };
                }
                if (pos >= tokens.Count) throw new FormatException("Missing VDF value.");
                var v = tokens[pos++];
                if (!v.Quoted && v.Text == "{") children.Add(Read(k.Text, true));
                else if (!v.Quoted && v.Text == "}") throw new FormatException("Missing VDF value.");
                else children.Add(new() { Key = k.Text, Value = v.Text, ValueStart = v.Start, ValueEnd = v.End });
            }
            if (nested) throw new FormatException("Unclosed VDF object.");
            return new() { Key = key, Children = children, CloseOffset = text.Length };
        }
        return Read("", false);
    }
}
