using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RHI.ManifestEditor.Json;

/// <summary>
/// Writes new JSON values in the manifest's style: 2-space indent, objects and arrays
/// one entry per line, non-ASCII characters kept as-is (™, ✅), only required escapes.
/// </summary>
public static class JsonOut
{
    public static string Write(JsonNode? node, int indent, string nl)
    {
        var sb = new StringBuilder();
        WriteTo(sb, node, indent, nl);
        return sb.ToString();
    }

    private static void WriteTo(StringBuilder sb, JsonNode? node, int indent, string nl)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                if (obj.Count == 0) { sb.Append("{}"); break; }
                sb.Append('{').Append(nl);
                int i = 0;
                foreach (var kv in obj)
                {
                    sb.Append(' ', indent + 2).Append(Quote(kv.Key)).Append(": ");
                    WriteTo(sb, kv.Value, indent + 2, nl);
                    if (++i < obj.Count) sb.Append(',');
                    sb.Append(nl);
                }
                sb.Append(' ', indent).Append('}');
                break;
            case JsonArray arr:
                if (arr.Count == 0) { sb.Append("[]"); break; }
                sb.Append('[').Append(nl);
                for (int j = 0; j < arr.Count; j++)
                {
                    sb.Append(' ', indent + 2);
                    WriteTo(sb, arr[j], indent + 2, nl);
                    if (j < arr.Count - 1) sb.Append(',');
                    sb.Append(nl);
                }
                sb.Append(' ', indent).Append(']');
                break;
            case JsonValue v:
                using (var d = JsonDocument.Parse(v.ToJsonString()))
                {
                var el = d.RootElement;
                switch (el.ValueKind)
                {
                    case JsonValueKind.String: sb.Append(Quote(el.GetString() ?? "")); break;
                    case JsonValueKind.True: sb.Append("true"); break;
                    case JsonValueKind.False: sb.Append("false"); break;
                    case JsonValueKind.Null: sb.Append("null"); break;
                    default: sb.Append(el.GetRawText()); break;
                }
                }
                break;
        }
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case (char)0x2028: sb.Append("\\u2028"); break;
                case (char)0x2029: sb.Append("\\u2029"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public static JsonNode Str(string s) => JsonValue.Create(s);
    public static JsonNode Bool(bool b) => JsonValue.Create(b);
    public static JsonNode Num(long n) => JsonValue.Create(n);
}
