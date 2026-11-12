// UltraPlusConfig.cs — reading and writing Ultra+ (UE4SS "UltraPlusExtensions") settings files. Pure text functions,
// unit tested in UltraPlusConfigTests. The file rules follow Ultra+ Manager (gitlab.com/ultra-plus/ultraplus-manager,
// GPL-3.0, src/Services/ConfigService.cs and friends), so RHI and UPM read and write the same files the same way.
// Plan: docs/ultraplus-overlay-plan.md.

using System.Globalization;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services.UltraPlus;

/// <summary>One setting from parser_friendly_settings.ini (Ultra+'s own schema).</summary>
public sealed record UltraPlusSetting
{
    public string Key { get; init; } = "";
    /// <summary>"enum" or "numeric".</summary>
    public string Type { get; init; } = "enum";
    public string? Default { get; init; }
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    /// <summary>"int" or "float" for numeric settings.</summary>
    public string? ValueType { get; init; }
    /// <summary>Schema Category=Advanced.</summary>
    public bool IsAdvanced { get; init; }
    /// <summary>In-game key that toggles the setting (Shortcut=), e.g. "F2".</summary>
    public string? Shortcut { get; init; }
    /// <summary>The mod's own description (the comment after its option list).</summary>
    public string? ModDescription { get; init; }
    /// <summary>The comment starts with '!': the mod's description wins over UPM's description data.</summary>
    public bool PreferModDescription { get; init; }

    public bool IsNumeric => Type.Equals("numeric", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A preset file (preset_*.ini) in the game's Ultra+ config folder.</summary>
public sealed record UltraPlusPreset(string StorageName, string DisplayName, string? Description, bool IsCustom,
    IReadOnlyDictionary<string, string> Values);

public static class UltraPlusConfig
{
    public const string ConfigFileName   = "UltraPlusConfig.ini";
    public const string SchemaFileName   = "parser_friendly_settings.ini";
    public const string DefaultsFileName = "UltraPlusConfig.default";
    public const string VersionFileName  = "UltraPlus_version.txt";
    /// <summary>UPM writes this empty file after every save; Ultra+'s AutoApplyConfig reload follows it.</summary>
    public const string ModifiedFlagFileName = "config_modified";
    public const string AutoApplyKey     = "AutoApplyConfig";
    public const string CustomPresetMark = ";custom_preset";
    public const string DescriptionMark  = ";description=";
    /// <summary>The mod ships its recommended settings as preset_uplus_defaults.ini.</summary>
    public const string RecommendedStorageName = "uplus_defaults";
    public const string RecommendedDisplayName = "U+ Recommended";

    /// <summary>Config folder below a UE4SS folder.</summary>
    public static readonly string[] ConfigRelativeSegments = { "Mods", "UltraPlusExtensions", "scripts", "config" };

    private static readonly Regex ValueLine = new(@"^(\w+)=(.*)$", RegexOptions.CultureInvariant);

    // ── Schema ───────────────────────────────────────────────────────────────

    /// <summary>Settings from parser_friendly_settings.ini, in file order. Unknown properties are ignored.</summary>
    public static List<UltraPlusSetting> ParseSchema(string? text)
    {
        var result = new List<UltraPlusSetting>();
        if (string.IsNullOrEmpty(text)) return result;

        var order = new List<string>();
        var props = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var left = line[..eq].Trim();
            var dot = left.IndexOf('.');
            if (dot <= 0 || dot == left.Length - 1 || left.IndexOf('.', dot + 1) >= 0) continue;
            var key = left[..dot];
            var prop = left[(dot + 1)..];
            var value = Unquote(line[(eq + 1)..].Trim());
            if (!props.TryGetValue(key, out var p)) { p = new(StringComparer.OrdinalIgnoreCase); props[key] = p; order.Add(key); }
            p[prop] = value;
        }

        foreach (var key in order)
        {
            var p = props[key];
            string? Get(string name) => p.TryGetValue(name, out var v) ? v : null;
            var comment = Get("Comment");
            string? description = null;
            if (comment != null)
            {
                var semi = comment.IndexOf(';');
                description = (semi >= 0 ? comment[(semi + 1)..] : comment).Trim();
                if (description.Length == 0) description = null;
            }
            var values = Get("Values") ?? Get("UserSettings");
            result.Add(new UltraPlusSetting
            {
                Key = key,
                Type = Get("Type") ?? "enum",
                Default = Get("Default"),
                Options = values?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                Min = ParseDouble(Get("Min")),
                Max = ParseDouble(Get("Max")),
                Step = ParseDouble(Get("Step")),
                ValueType = Get("ValueType"),
                IsAdvanced = string.Equals(Get("Category"), "Advanced", StringComparison.OrdinalIgnoreCase),
                Shortcut = Get("Shortcut"),
                ModDescription = description,
                PreferModDescription = comment?.StartsWith('!') == true,
            });
        }
        return result;
    }

    // ── Values ───────────────────────────────────────────────────────────────

    /// <summary>Key → value from UltraPlusConfig.ini, a .default or a preset. Comment lines and inline "; comment" are dropped.</summary>
    public static Dictionary<string, string> ParseValues(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return values;
        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('[')) continue;
            var m = ValueLine.Match(line);
            if (m.Success && m.Groups[2].Value.Length > 0) values[m.Groups[1].Value] = StripInlineComment(m.Groups[2].Value).Trim();
        }
        return values;
    }

    /// <summary>
    /// The text with each key's value replaced. Only the matching Key=value line changes (an inline "; comment" on it
    /// is kept); keys not in the file are appended. Line endings and every other line stay as they were.
    /// </summary>
    public static string SetValues(string? text, IReadOnlyDictionary<string, string> changes)
    {
        text ??= "";
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        bool endsWithNewline = text.EndsWith('\n');
        var lines = text.Length == 0 ? new List<string>() : text.Replace("\r\n", "\n").Split('\n').ToList();
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);
        var pending = new Dictionary<string, string>(changes, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Count && pending.Count > 0; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith(';')) continue;
            var m = ValueLine.Match(trimmed);
            if (!m.Success || !pending.TryGetValue(m.Groups[1].Value, out var value)) continue;
            var raw = m.Groups[2].Value;
            // Keep an inline "; comment" with the spacing before it exactly as it was
            var idx = raw.IndexOf("; ", StringComparison.Ordinal);
            var comment = idx > 0 ? raw[raw[..idx].TrimEnd().Length..] : "";
            var indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
            lines[i] = $"{indent}{m.Groups[1].Value}={value}{comment}";
            pending.Remove(m.Groups[1].Value);
        }
        foreach (var kv in changes)
            if (pending.ContainsKey(kv.Key)) lines.Add($"{kv.Key}={kv.Value}");

        return string.Join(nl, lines) + (endsWithNewline ? nl : "");
    }

    /// <summary>"value; comment" → "value". A value that starts with ';' is kept as it is.</summary>
    public static string StripInlineComment(string value)
    {
        var idx = value.IndexOf("; ", StringComparison.Ordinal);
        return idx > 0 ? value[..idx].TrimEnd() : value;
    }

    // ── Numbers ──────────────────────────────────────────────────────────────

    /// <summary>A number rounded to the step's precision with trailing zeros trimmed ("0.60" → "0.6"); other text unchanged.</summary>
    public static string NormalizeNumeric(string? value, double? step)
    {
        if (value == null) return "";
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return value;
        var places = step is > 0 ? Math.Max(0, -(int)Math.Floor(Math.Log10(step.Value))) + 1 : 6;
        var s = Math.Round(n, Math.Min(places, 15)).ToString(CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s == "-0" ? "0" : s;
    }

    /// <summary>The value as shown and compared: numeric settings normalised.</summary>
    public static string Normalize(UltraPlusSetting setting, string? value) =>
        setting.IsNumeric ? NormalizeNumeric(value, setting.Step) : value ?? "";

    /// <summary>
    /// The choices for a numeric setting, as UPM lists them: "-1" when the setting uses it, "game" when that's the default,
    /// then Min to Max by Step. Empty when the range is incomplete.
    /// </summary>
    public static List<string> NumericChoices(UltraPlusSetting s)
    {
        var list = new List<string>();
        if (!s.IsNumeric || s.Min is not double min || s.Max is not double max || s.Step is not double step || step <= 0 || max < min)
            return list;
        if (min == -1 || s.Default == "-1" || s.ModDescription?.Contains("-1") == true) list.Add("-1");
        if (string.Equals(s.Default, "game", StringComparison.OrdinalIgnoreCase)) list.Add("game");
        int count = (int)Math.Round((max - min) / step) + 1;
        for (int i = 0; i < count && i < 10_000; i++)
        {
            var v = min + i * step;
            var text = s.ValueType == "int"
                ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                : NormalizeNumeric(v.ToString("R", CultureInfo.InvariantCulture), step);
            if (!list.Contains(text)) list.Add(text);
        }
        return list;
    }

    /// <summary>Every choice for a setting: enum options, or the numeric list.</summary>
    public static IReadOnlyList<string> Choices(UltraPlusSetting s) => s.IsNumeric ? NumericChoices(s) : s.Options;

    /// <summary>True when the value may be written for this setting (an enum option, or a number in range / a listed word).</summary>
    public static bool IsAllowed(UltraPlusSetting s, string value)
    {
        if (!s.IsNumeric) return s.Options.Count == 0 || s.Options.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return n == -1 || ((s.Min is not double min || n >= min - 1e-9) && (s.Max is not double max || n <= max + 1e-9));
        return NumericChoices(s).Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    // ── Presets ──────────────────────────────────────────────────────────────

    /// <summary>"preset_high.ini" → "high"; null for other names.</summary>
    public static string? PresetStorageName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (!fileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
            || !name.StartsWith("preset_", StringComparison.OrdinalIgnoreCase) || name.Length <= 7) return null;
        return name[7..];
    }

    /// <summary>Display name: uplus_defaults → "U+ Recommended", known qualities capitalised ("veryhigh" → "Very High").</summary>
    public static string PresetDisplayName(string storageName) => storageName.ToLowerInvariant() switch
    {
        RecommendedStorageName => RecommendedDisplayName,
        "low" => "Low",
        "medium" => "Medium",
        "high" => "High",
        "veryhigh" => "Very High",
        "epic" => "Epic",
        "cinematic" => "Cinematic",
        _ => storageName,
    };

    /// <summary>Sort order: U+ Recommended, then quality presets low → cinematic, then the rest by name; custom ones last.</summary>
    public static int PresetRank(UltraPlusPreset p)
    {
        if (p.IsCustom) return 100;
        int q = Array.IndexOf(new[] { RecommendedStorageName, "low", "medium", "high", "veryhigh", "epic", "cinematic" },
            p.StorageName.ToLowerInvariant());
        return q >= 0 ? q : 50;
    }

    public static UltraPlusPreset ParsePreset(string storageName, string? text)
    {
        var lines = SplitLines(text ?? "").ToList();
        bool custom = lines.Count > 0 && lines[0].Trim().Equals(CustomPresetMark, StringComparison.OrdinalIgnoreCase);
        var descLine = lines.FirstOrDefault(l => l.StartsWith(DescriptionMark, StringComparison.OrdinalIgnoreCase));
        var desc = descLine?[DescriptionMark.Length..].Trim();
        return new UltraPlusPreset(storageName, PresetDisplayName(storageName), string.IsNullOrEmpty(desc) ? null : desc,
            custom, ParseValues(text));
    }

    /// <summary>
    /// A custom preset file (the format UPM writes, so both tools see it): ";custom_preset", optional ";description=",
    /// then Key=value lines in the given order.
    /// </summary>
    public static string BuildCustomPreset(string? description, IEnumerable<KeyValuePair<string, string>> values)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CustomPresetMark).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(description))
            sb.Append(DescriptionMark).Append(description.Replace("\r", " ").Replace("\n", " ").Trim()).Append("\r\n");
        foreach (var kv in values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// A preset name the user typed → a safe file part ("My RT" → "My RT"), or null when it's empty, too long, uses
    /// characters a file name can't have, or is a name the mod reserves.
    /// </summary>
    public static string? SafePresetName(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n) || n.Length > 60) return null;
        if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n.Contains("..")) return null;
        if (n.Equals(RecommendedStorageName, StringComparison.OrdinalIgnoreCase)
            || n.Equals(RecommendedDisplayName, StringComparison.OrdinalIgnoreCase)) return null;
        return n;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static string Unquote(string v) =>
        v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;

    private static double? ParseDouble(string? v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
