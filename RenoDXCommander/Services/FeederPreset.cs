// FeederPreset.cs — the ReShade preset settings DLSS5 Feeder needs, done the way Feeder's own installer
// (Install-DLSS5Feeder.ps1, v2.0.0) does them. Pure text edits, unit tested (FeederPresetTests).
//
// - The motion-vector provider is the per-effect preprocessor definition DLSS5_MV_PROVIDER in the
//   preset's [DLSS5_Feed.fx] section. In ReShade.ini it's ignored, so RHI's old global entry is removed.
// - A new install uses 5, the estimator built into DLSS5_Feed.fx since 2.0.0 (nothing else to install),
//   or 3 (LumeniteFX Kernel) with an older shader. An install whose preset already names a provider keeps it.
// - An existing preset is merged (only Techniques / TechniqueSorting and that one definition change), never replaced.

using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public static class FeederPreset
{
    public const string FeedTechnique  = "DLSS5_Feed@DLSS5_Feed.fx";
    public const string DebugTechnique = "DLSS5_Feed_Debug@DLSS5_Feed.fx";
    public const string EffectSection  = "DLSS5_Feed.fx";
    public const int BuiltInProvider   = 5;
    public const int LumeniteProvider  = 3;

    private static readonly Regex ProviderDef = new(@"(?i)(^|,)\s*DLSS5_MV_PROVIDER\s*=\s*([0-5])\s*(,|$)");

    /// <summary>The ReShade technique a provider needs enabled before DLSS5_Feed, or null (built in, or one RHI doesn't install).</summary>
    public static string? ProviderTechnique(int provider) => provider switch
    {
        3 => "Lumenite_Kernel@lumenite_Kernel.fx",
        4 => "Lumenite_QuantMotion@lumenite_QuantMotion.fx",
        _ => null,
    };

    /// <summary>True for providers that need the LumeniteFX shader pack.</summary>
    public static bool NeedsLumenite(int provider) => provider is 3 or 4;

    /// <summary>True when the DLSS5_Feed.fx text has the built-in estimator (2.0.0 and newer), as the installer checks.</summary>
    public static bool HasBuiltInEstimator(string? fxText) =>
        fxText != null && Regex.IsMatch(fxText, @"float4\s+BaseLevel\s*\(");

    /// <summary>The provider an existing preset names in [DLSS5_Feed.fx], or null.</summary>
    public static int? ReadProvider(string? presetText)
    {
        var defs = GetKey(presetText, EffectSection, "PreprocessorDefinitions");
        if (defs == null) return null;
        var m = ProviderDef.Match(defs);
        return m.Success ? int.Parse(m.Groups[2].Value) : null;
    }

    /// <summary>Keep the preset's provider; otherwise built in when the shader has it, else LumeniteFX.</summary>
    public static int ChooseProvider(string? presetText, string? fxText) =>
        ReadProvider(presetText) ?? (HasBuiltInEstimator(fxText) ? BuiltInProvider : LumeniteProvider);

    /// <summary>
    /// The preset with DLSS5_Feed (and its provider's technique before it) enabled and the provider set.
    /// With no existing preset, a minimal new one. Mirrors the installer: the feed's own entries are moved
    /// to the end of both lists, and with the built-in provider a LumeniteFX flow effect that was only
    /// there for the feed is switched off.
    /// </summary>
    public static string Merge(string? existing, int provider)
    {
        var lead = ProviderTechnique(provider);
        if (string.IsNullOrWhiteSpace(existing))
        {
            var list = (lead != null ? lead + "," : "") + FeedTechnique;
            return $"Techniques={list}\r\nTechniqueSorting={list},{DebugTechnique}\r\n\r\n"
                 + $"[{EffectSection}]\r\nDEBUG_VIEW=0\r\nMV_SCALE=1.000000\r\nMV_SIGN=1.000000,1.000000\r\n"
                 + $"PreprocessorDefinitions=DLSS5_MV_PROVIDER={provider}\r\n";
        }

        var text = existing;
        foreach (var key in new[] { "Techniques", "TechniqueSorting" })
        {
            var cur = GetKey(text, "", key);
            var items = (cur ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !t.Equals(lead ?? "", StringComparison.OrdinalIgnoreCase)
                         && !t.Equals(FeedTechnique, StringComparison.OrdinalIgnoreCase)
                         && !t.StartsWith("DLSS5_Feed_Debug@", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (provider == BuiltInProvider && key == "Techniques")
            {
                bool IsFlow(string t) => Regex.IsMatch(t, @"(?i)@lumenite_(Kernel|QuantMotion)\.fx$");
                bool otherLumenite = items.Any(t => Regex.IsMatch(t, "(?i)@lumenite_") && !IsFlow(t));
                if (!otherLumenite) items.RemoveAll(IsFlow);
            }
            if (lead != null) items.Add(lead);
            items.Add(FeedTechnique);
            if (key == "TechniqueSorting") items.Add(DebugTechnique);
            text = SetKey(text, "", key, string.Join(",", items));
        }

        var defs = GetKey(text, EffectSection, "PreprocessorDefinitions");
        var parts = (defs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !Regex.IsMatch(p, @"(?i)^DLSS5_MV_PROVIDER\s*="))
            .Append($"DLSS5_MV_PROVIDER={provider}");
        return SetKey(text, EffectSection, "PreprocessorDefinitions", string.Join(",", parts));
    }

    /// <summary>ReShade.ini text with DLSS5_MV_PROVIDER removed from [GENERAL] PreprocessorDefinitions (it's ignored there).</summary>
    public static string RemoveGlobalProvider(string iniText)
    {
        var defs = GetKey(iniText, "GENERAL", "PreprocessorDefinitions");
        if (defs == null || !Regex.IsMatch(defs, "(?i)DLSS5_MV_PROVIDER")) return iniText;
        var kept = defs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !Regex.IsMatch(p, @"(?i)^DLSS5_MV_PROVIDER\s*="));
        return SetKey(iniText, "GENERAL", "PreprocessorDefinitions", string.Join(",", kept));
    }

    /// <summary>The provider in a game's preset (reshade.ini PresetPath, else ReShadePreset.ini), or null. Reads files.</summary>
    public static int? ReadGameProvider(string installPath)
    {
        try
        {
            var ini = Path.Combine(installPath, "reshade.ini");
            var rel = File.Exists(ini) ? GetKey(File.ReadAllText(ini), "GENERAL", "PresetPath") : null;
            var preset = string.IsNullOrWhiteSpace(rel) ? Path.Combine(installPath, "ReShadePreset.ini")
                : Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(installPath, rel));
            return File.Exists(preset) ? ReadProvider(File.ReadAllText(preset)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            CrashReporter.Log($"[FeederPreset.ReadGameProvider] '{installPath}' — {ex.Message}");
            return null;
        }
    }

    // ── Minimal INI text helpers (section "" = keys before the first [section]) ──

    internal static string? GetKey(string? text, string section, string key)
    {
        if (text == null) return null;
        var cur = "";
        foreach (var raw in text.Split('\n'))
        {
            var t = raw.Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { cur = t[1..^1]; continue; }
            if (!cur.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var eq = t.IndexOf('=');
            if (eq > 0 && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return t[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Sets a key, keeping every other line. Adds the key at the end of its section, or the section at the end.</summary>
    internal static string SetKey(string? text, string section, string key, string value)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var entry = key + "=" + value;

        string cur = "";
        int secStart = section.Length == 0 ? 0 : -1, secEnd = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                if (cur.Equals(section, StringComparison.OrdinalIgnoreCase) && secStart >= 0 && secEnd < 0) secEnd = i;
                cur = t[1..^1];
                if (cur.Equals(section, StringComparison.OrdinalIgnoreCase)) secStart = i + 1;
                continue;
            }
            var eq = t.IndexOf('=');
            if (cur.Equals(section, StringComparison.OrdinalIgnoreCase) && eq > 0
                && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = entry;
                return string.Join("\r\n", lines) + "\r\n";
            }
        }

        if (secStart >= 0)
        {
            if (secEnd < 0) secEnd = lines.Count;
            int at = secEnd;
            while (at > secStart && lines[at - 1].Trim().Length == 0) at--; // after the section's last non-blank line
            lines.Insert(at, entry);
        }
        else
        {
            if (lines.Count > 0 && lines[^1].Trim().Length != 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add(entry);
        }
        return string.Join("\r\n", lines) + "\r\n";
    }
}
