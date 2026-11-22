// ReLimiterPresetKeys.cs: ReLimiter's preset / Move OSD / blackout / OLED Care keys. Settings reads them from the
// shared presets file (%LocalAppData%\RHI\ReLimiter_Presets\presets.ini, [Presets]), which ReLimiter itself reads
// and writes when shared presets is on, and writes them there and into each game's relimiter.ini ([FrameLimiter]).
// Names from ReLimiter's config.cpp: the two files use different key names for the same setting.

namespace RenoDXCommander.Services;

internal static class ReLimiterPresetKeys
{
    /// <summary>One key: its label, its name in presets.ini [Presets], its name in relimiter.ini [FrameLimiter].</summary>
    internal sealed record Bind(string Label, string PresetKey, string GameKey, string Help);

    internal static readonly Bind[] Keys =
    [
        new("Previous OSD preset", "prev_key", "osd_preset_prev_key", "Switches the on-screen display to the previous preset."),
        new("Next OSD preset", "next_key", "osd_preset_next_key", "Switches the on-screen display to the next preset."),
        new("Move OSD", "position_cycle_key", "osd_position_cycle_key", "Moves the on-screen display to the next corner of the screen."),
        new("Blackout other monitors", "blackout_key", "blackout_key", "Covers your other monitors with black, and back."),
        new("OLED Care", "oled_care_key", "oled_care_key", "Covers the game monitor (or every monitor, with All Monitors on in ReLimiter) to protect an OLED screen, and back."),
    ];

    internal const string Section = "Presets";

    internal static string PresetsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "ReLimiter_Presets", "presets.ini");

    /// <summary>Every key's value in presets.ini ("" when missing or unset).</summary>
    internal static Dictionary<string, string> ReadAll()
    {
        var result = Keys.ToDictionary(k => k.PresetKey, _ => "", StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(PresetsPath)) return result;
            string? section = null;
            foreach (var raw in File.ReadAllLines(PresetsPath))
            {
                var line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
                if (!string.Equals(section, Section, StringComparison.OrdinalIgnoreCase)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                if (result.ContainsKey(key)) result[key] = line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[ReLimiterPresetKeys.ReadAll] {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Sets one key in presets.ini [Presets]. Changes only that line (the OSD presets and everything else stay
    /// as they are); adds the line, the section or the file when missing.
    /// </summary>
    internal static bool WritePreset(string presetKey, string value)
    {
        try
        {
            var lines = File.Exists(PresetsPath) ? File.ReadAllLines(PresetsPath).ToList() : new List<string>();
            var updated = SetKey(lines, Section, presetKey, value);
            Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath)!);
            var tmp = PresetsPath + ".tmp";
            File.WriteAllLines(tmp, updated);
            File.Move(tmp, PresetsPath, overwrite: true);
            CrashReporter.Log($"[ReLimiterPresetKeys.WritePreset] {presetKey}={value}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[ReLimiterPresetKeys.WritePreset] {presetKey}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pure: the lines with <paramref name="key"/> set in <paramref name="section"/>: replaced in place, else added
    /// after the section's last setting, else a new section at the top.
    /// </summary>
    internal static List<string> SetKey(List<string> lines, string section, string key, string value)
    {
        var result = new List<string>(lines);
        int start = -1, end = result.Count;
        for (int i = 0; i < result.Count; i++)
        {
            var t = result[i].Trim();
            if (!(t.StartsWith('[') && t.EndsWith(']'))) continue;
            if (start >= 0) { end = i; break; }
            if (string.Equals(t[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase)) start = i;
        }
        var newLine = $"{key}={value}";
        if (start < 0)
        {
            result.InsertRange(0, new[] { $"[{section}]", newLine });
            return result;
        }
        int lastKey = start;
        for (int i = start + 1; i < end; i++)
        {
            var t = result[i].Trim();
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            lastKey = i;
            if (string.Equals(t[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                result[i] = newLine;
                return result;
            }
        }
        result.Insert(lastKey + 1, newLine);
        return result;
    }

    /// <summary>Writes every key (values from presets.ini) into one game's relimiter.ini [FrameLimiter].</summary>
    internal static void WriteToGameIni(string iniPath, IReadOnlyDictionary<string, string> values)
    {
        var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : new List<string>();
        foreach (var k in Keys)
            lines = SetKey(lines, "FrameLimiter", k.GameKey, values.TryGetValue(k.PresetKey, out var v) ? v : "");
        File.WriteAllLines(iniPath, lines);
    }
}
