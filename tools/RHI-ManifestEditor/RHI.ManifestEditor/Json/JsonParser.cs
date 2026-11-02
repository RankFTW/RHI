using System.Text;

namespace RHI.ManifestEditor.Json;

public enum JKind { Object, Array, String, Number, True, False, Null }

/// <summary>A parsed JSON value that remembers where it sits in the source text.</summary>
public sealed class JNode
{
    public JKind Kind;
    /// <summary>Start (inclusive) and end (exclusive) of the value in the source text.</summary>
    public int Start, End;
    /// <summary>Decoded string value (String nodes only).</summary>
    public string? Str;
    public List<JMember>? Members;
    public List<JNode>? Items;

    public bool IsObject => Kind == JKind.Object;
    public bool IsArray => Kind == JKind.Array;
    public bool IsString => Kind == JKind.String;

    /// <summary>Member value by exact (decoded) key. With duplicate keys the last one wins, like the app.</summary>
    public JNode? Get(string key)
    {
        if (Members == null) return null;
        for (int i = Members.Count - 1; i >= 0; i--)
            if (Members[i].Key == key) return Members[i].Value;
        return null;
    }

    public int IndexOf(string key)
    {
        if (Members == null) return -1;
        for (int i = Members.Count - 1; i >= 0; i--)
            if (Members[i].Key == key) return i;
        return -1;
    }

    public string Raw(string text) => text[Start..End];

    public bool? AsBool => Kind switch { JKind.True => true, JKind.False => false, _ => null };
}

public sealed class JMember
{
    public string Key = "";
    public int KeyStart, KeyEnd;
    public JNode Value = null!;
}

public sealed class JsonParseException : Exception
{
    public int Position { get; }
    public JsonParseException(string message, int position) : base(message) => Position = position;
}

/// <summary>
/// Small JSON parser that keeps source positions, so edits can change only the text
/// they touch. Also reports duplicate keys (the app silently keeps the last one).
/// </summary>
public sealed class JsonParser
{
    private readonly string _t;
    private int _p;
    private readonly List<string>? _duplicates;
    private readonly Stack<string> _path = new();

    private JsonParser(string text, List<string>? duplicates) { _t = text; _duplicates = duplicates; }

    public static JNode Parse(string text, List<string>? duplicates = null)
    {
        var p = new JsonParser(text, duplicates);
        p.SkipWs();
        var root = p.Value();
        p.SkipWs();
        if (p._p != text.Length) throw new JsonParseException("Unexpected text after the end of the JSON", p._p);
        return root;
    }

    private void SkipWs()
    {
        while (_p < _t.Length && (_t[_p] == ' ' || _t[_p] == '\t' || _t[_p] == '\r' || _t[_p] == '\n')) _p++;
    }

    private JsonParseException Err(string msg) => new(msg, _p);

    private JNode Value()
    {
        if (_p >= _t.Length) throw Err("Unexpected end of file");
        char c = _t[_p];
        switch (c)
        {
            case '{': return Obj();
            case '[': return Arr();
            case '"':
            {
                int s = _p;
                var str = Str();
                return new JNode { Kind = JKind.String, Start = s, End = _p, Str = str };
            }
            case 't': return Lit("true", JKind.True);
            case 'f': return Lit("false", JKind.False);
            case 'n': return Lit("null", JKind.Null);
            default:
                if (c == '-' || char.IsAsciiDigit(c)) return Num();
                throw Err($"Unexpected character '{c}'");
        }
    }

    private JNode Lit(string word, JKind kind)
    {
        if (string.CompareOrdinal(_t, _p, word, 0, word.Length) != 0) throw Err("Unexpected value");
        var n = new JNode { Kind = kind, Start = _p, End = _p + word.Length };
        _p += word.Length;
        return n;
    }

    private JNode Num()
    {
        int s = _p;
        if (_t[_p] == '-') _p++;
        while (_p < _t.Length && (char.IsAsciiDigit(_t[_p]) || _t[_p] is '.' or 'e' or 'E' or '+' or '-')) _p++;
        if (_p == s || (_p == s + 1 && _t[s] == '-')) throw Err("Bad number");
        return new JNode { Kind = JKind.Number, Start = s, End = _p };
    }

    private string Str()
    {
        _p++; // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (_p >= _t.Length) throw Err("Unterminated string");
            char c = _t[_p++];
            if (c == '"') break;
            if (c < 0x20) throw new JsonParseException("Control character inside a string (use \\n for line breaks)", _p - 1);
            if (c != '\\') { sb.Append(c); continue; }
            if (_p >= _t.Length) throw Err("Unterminated string");
            char e = _t[_p++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (_p + 4 > _t.Length || !int.TryParse(_t.AsSpan(_p, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
                        throw Err("Bad \\u escape");
                    sb.Append((char)code);
                    _p += 4;
                    break;
                default: throw new JsonParseException($"Bad escape \\{e}", _p - 1);
            }
        }
        return sb.ToString();
    }

    private JNode Obj()
    {
        var n = new JNode { Kind = JKind.Object, Start = _p, Members = new() };
        _p++;
        SkipWs();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (_p < _t.Length && _t[_p] == '}') { _p++; n.End = _p; return n; }
        while (true)
        {
            SkipWs();
            if (_p >= _t.Length || _t[_p] != '"') throw Err("Expected a \"key\"");
            int ks = _p;
            var key = Str();
            int ke = _p;
            if (!seen.Add(key) && _duplicates != null)
                _duplicates.Add(string.Join(" > ", _path.Reverse().Append(key)));
            SkipWs();
            if (_p >= _t.Length || _t[_p] != ':') throw Err("Expected ':' after a key");
            _p++;
            SkipWs();
            _path.Push(key);
            var v = Value();
            _path.Pop();
            n.Members.Add(new JMember { Key = key, KeyStart = ks, KeyEnd = ke, Value = v });
            SkipWs();
            if (_p >= _t.Length) throw Err("Unexpected end of file (missing '}')");
            if (_t[_p] == ',') { _p++; continue; }
            if (_t[_p] == '}') { _p++; n.End = _p; return n; }
            throw Err("Expected ',' or '}' (missing comma?)");
        }
    }

    private JNode Arr()
    {
        var n = new JNode { Kind = JKind.Array, Start = _p, Items = new() };
        _p++;
        SkipWs();
        if (_p < _t.Length && _t[_p] == ']') { _p++; n.End = _p; return n; }
        int i = 0;
        while (true)
        {
            SkipWs();
            _path.Push($"[{i++}]");
            n.Items.Add(Value());
            _path.Pop();
            SkipWs();
            if (_p >= _t.Length) throw Err("Unexpected end of file (missing ']')");
            if (_t[_p] == ',') { _p++; continue; }
            if (_t[_p] == ']') { _p++; n.End = _p; return n; }
            throw Err("Expected ',' or ']' (missing comma?)");
        }
    }

    /// <summary>1-based line and column of a position, for error messages.</summary>
    public static (int line, int col) LineCol(string text, int pos)
    {
        int line = 1, col = 1;
        for (int i = 0; i < pos && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; col = 1; } else col++;
        }
        return (line, col);
    }
}
