// RtxEncoreConfig.cs -- reads and edits single values in RTX Encore's rtx-encore.jsonc without
// rewriting the file. The file is JSON with a // comment after almost every key, written by the mod
// itself, so a normal JSON round-trip would lose every comment. Edits replace only the value token on
// the key's line; a missing key is inserted at the top of its section, a missing section at the top of
// the file. When the file doesn't exist yet (the mod creates it on first launch), a small file holding
// only the keys set here is created; the mod uses its defaults for everything else.

using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public static class RtxEncoreConfig
{
    public const string FileName = "rtx-encore.jsonc";

    /// <summary>configVersion written into a file RHI creates (the version RTX Encore 1.5.3 writes).</summary>
    private const int CreatedConfigVersion = 4;

    /// <summary>Reads the raw value of <c>section.key</c> (strings without quotes), or null when absent.</summary>
    public static string? Get(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        int i = FindKeyLine(lines, section, key);
        if (i < 0) return null;
        var m = KeyLine(key).Match(lines[i]);
        if (!m.Success) return null;
        var token = m.Groups[2].Value;
        return token.StartsWith('"') ? Unquote(token) : token;
    }

    public static void SetString(string path, string section, string key, string value) => Set(path, section, key, Quote(value));
    public static void SetBool(string path, string section, string key, bool value) => Set(path, section, key, value ? "true" : "false");
    public static void SetInt(string path, string section, string key, int value) => Set(path, section, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Sets <c>section.key</c> to a JSON token (already quoted for strings), keeping everything else.</summary>
    public static void Set(string path, string section, string key, string token)
    {
        if (!File.Exists(path))
        {
            var created = new List<string>
            {
                "// Created by RHI with only the settings chosen in RHI. RTX Encore fills in the rest with its defaults when the game starts.",
                "{",
                $"  \"configVersion\": {CreatedConfigVersion},",
                $"  \"{section}\": {{",
                $"    \"{key}\": {token}",
                "  }",
                "}",
            };
            WriteLines(path, created, "\r\n");
            return;
        }

        var text = File.ReadAllText(path);
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        bool trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);

        int keyLine = FindKeyLine(lines, section, key);
        if (keyLine >= 0)
        {
            lines[keyLine] = KeyLine(key).Replace(lines[keyLine], m => m.Groups[1].Value + token + m.Groups[3].Value, 1);
        }
        else
        {
            int sectionLine = FindSectionLine(lines, section);
            if (sectionLine >= 0)
            {
                // Top of the section, with a comma: the section already holds at least one key after it
                var indent = Regex.Match(lines[sectionLine], @"^\s*").Value + "  ";
                lines.Insert(sectionLine + 1, $"{indent}\"{key}\": {token},");
            }
            else
            {
                // New section at the top of the file, with a comma: the file already holds other entries after it
                int root = lines.FindIndex(l => StripComment(l).Trim().StartsWith("{", StringComparison.Ordinal));
                if (root < 0) throw new InvalidDataException("rtx-encore.jsonc has no opening brace.");
                lines.InsertRange(root + 1, new[]
                {
                    $"  \"{section}\": {{",
                    $"    \"{key}\": {token}",
                    "  },",
                });
            }
        }
        if (trailingNewline) lines.Add("");
        WriteLines(path, lines, nl);
    }

    // ── Parsing ─────────────────────────────────────────────────────────────────

    private static Regex KeyLine(string key) =>
        new($@"^(\s*""{Regex.Escape(key)}""\s*:\s*)(""(?:[^""\\]|\\.)*""|[^,\s/}}]+)(.*)$");

    /// <summary>Index of the line holding <c>"key": value</c> directly inside top-level <paramref name="section"/>.</summary>
    private static int FindKeyLine(IList<string> lines, string section, string key)
    {
        var path = new List<string>();
        var keyRx = KeyLine(key);
        for (int i = 0; i < lines.Count; i++)
        {
            var code = StripComment(lines[i]);
            // A key opening an object: "name": {
            var open = Regex.Match(code, @"^\s*""([^""]+)""\s*:\s*\{");
            if (open.Success)
            {
                path.Add(open.Groups[1].Value);
                if (code.IndexOf('}', open.Length) >= 0) path.RemoveAt(path.Count - 1); // one-line section
                continue;
            }
            if (path.Count == 1 && path[0] == section && keyRx.IsMatch(lines[i])) return i;
            int closes = code.Count(c => c == '}');
            for (int c = 0; c < closes && path.Count > 0; c++) path.RemoveAt(path.Count - 1);
        }
        return -1;
    }

    private static int FindSectionLine(IList<string> lines, string section)
    {
        for (int i = 0; i < lines.Count; i++)
            if (Regex.IsMatch(StripComment(lines[i]), $@"^\s*""{Regex.Escape(section)}""\s*:\s*\{{\s*$"))
                return i;
        return -1;
    }

    /// <summary>The line without a trailing // comment (ignoring // inside strings).</summary>
    private static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && inString) { i++; continue; }
            if (c == '"') inString = !inString;
            else if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string Unquote(string token) => token[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static void WriteLines(string path, IEnumerable<string> lines, string nl)
    {
        var tmp = path + ".rhi-tmp";
        File.WriteAllText(tmp, string.Join(nl, lines), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
