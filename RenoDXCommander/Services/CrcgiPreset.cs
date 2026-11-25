// CrcgiPreset.cs -- pure text edits for CRCGI's settings in a game's ReShade preset. ReShade saves an effect's uniforms in
// a section named after the effect file ([CRCGI_v2.fx] Intensity=1.000000) and its compile-time switches in that
// section's PreprocessorDefinitions (comma-separated NAME=value). Line edits only: every other line and section is kept.
// Tests: CrcgiTests.

namespace RenoDXCommander.Services;

internal static class CrcgiPreset
{
    public const string DefinitionsKey = "PreprocessorDefinitions";

    public static string? GetValue(string? text, string section, string key) => FeederPreset.GetKey(text, section, key);

    public static string SetValue(string? text, string section, string key, string value) => FeederPreset.SetKey(text, section, key, value);

    /// <summary>Removes keys from a section (other lines kept).</summary>
    public static string RemoveKeys(string? text, string section, IEnumerable<string> keys)
    {
        var drop = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var cur = "";
        var result = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) cur = t[1..^1];
            else if (cur.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                var eq = t.IndexOf('=');
                if (eq > 0 && drop.Contains(t[..eq].Trim())) continue;
            }
            result.Add(line);
        }
        return string.Join("\r\n", result) + "\r\n";
    }

    /// <summary>A compile-time switch from the section's PreprocessorDefinitions, or null.</summary>
    public static string? GetDefine(string? text, string section, string name)
    {
        var defs = GetValue(text, section, DefinitionsKey);
        if (string.IsNullOrEmpty(defs)) return null;
        foreach (var part in defs.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Trim().Equals(name, StringComparison.Ordinal)) return part[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Sets (or with null removes) a compile-time switch, keeping the others in their order.</summary>
    public static string SetDefine(string? text, string section, string name, string? value)
    {
        var defs = GetValue(text, section, DefinitionsKey) ?? "";
        var parts = defs.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        int at = parts.FindIndex(p => { var eq = p.IndexOf('='); return (eq > 0 ? p[..eq] : p).Trim().Equals(name, StringComparison.Ordinal); });
        if (value == null) { if (at >= 0) parts.RemoveAt(at); }
        else if (at >= 0) parts[at] = $"{name}={value}";
        else parts.Add($"{name}={value}");
        return parts.Count == 0 && GetValue(text, section, DefinitionsKey) == null
            ? (text ?? "")
            : SetValue(text, section, DefinitionsKey, string.Join(",", parts));
    }

    /// <summary>The body lines of a section (without its [header]), or null when the text has no such section.</summary>
    public static List<string>? GetSection(string? text, string section)
    {
        if (text == null) return null;
        List<string>? body = null;
        var cur = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                cur = t[1..^1];
                if (cur.Equals(section, StringComparison.OrdinalIgnoreCase)) body ??= new List<string>();
                continue;
            }
            if (body != null && cur.Equals(section, StringComparison.OrdinalIgnoreCase) && t.Length > 0) body.Add(raw.TrimEnd('\r'));
        }
        return body;
    }

    /// <summary>Replaces a section's body with <paramref name="body"/> (or adds the section at the end). Other sections are kept.</summary>
    public static string ReplaceSection(string? text, string section, IEnumerable<string> body)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var result = new List<string>();
        bool inSection = false, written = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                if (inSection) { result.Add(""); inSection = false; }
                if (t[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(line);
                    result.AddRange(body);
                    inSection = written = true;
                    continue;
                }
            }
            if (inSection) continue; // the old body
            result.Add(line);
        }
        if (!written)
        {
            if (result.Count > 0 && result[^1].Trim().Length != 0) result.Add("");
            result.Add($"[{section}]");
            result.AddRange(body);
        }
        // No doubled blank lines where the old body was
        var tidy = new List<string>();
        foreach (var l in result)
            if (!(l.Trim().Length == 0 && tidy.Count > 0 && tidy[^1].Trim().Length == 0)) tidy.Add(l);
        return string.Join("\r\n", tidy).TrimEnd('\r', '\n') + "\r\n";
    }

    /// <summary>"Batman: Arkham Knight" matches the preset folder "Batman Arkham Knight - With Add-on" (edition words ignored). Pure.</summary>
    public static bool GameMatchesPresetFolder(string gameName, string folder)
    {
        var prefix = folder.Split(" - ")[0];
        var g = Normalize(gameName);
        var f = Normalize(prefix);
        return f.Length >= 4 && g.Length >= 4 && (g.Contains(f) || f.Contains(g));
    }

    private static readonly string[] EditionWords = { "remastered", "remake", "definitive", "edition", "goty", "complete", "enhanced", "gameoftheyear" };

    internal static string Normalize(string s)
    {
        var letters = new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        foreach (var w in EditionWords) letters = letters.Replace(w, "");
        return letters;
    }
}
