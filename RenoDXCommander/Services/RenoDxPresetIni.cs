// RenoDxPresetIni.cs -- reads and writes keys in reshade.ini's [renodx-preset*] sections line by line.
//
// The key's spelling depends on the mod: Unity / generic RenoDX mods save "toneMapPeakNits", others (e.g. UE-Extended,
// Lies of P) "ToneMapPeakNits". ReShade's ini is case-sensitive, so a mod ignores the other spelling. Older RHI versions
// always wrote the capital-T spelling, so a section can hold both: then the lower-case one came from the mod and is read
// first. Writes go to every spelling present (nothing else in the file is touched); a section with neither gets both.
// Never use ParseIni / WriteIni for these keys: ParseIni is case-insensitive and merges the two spellings into one line.

using System.Globalization;

namespace RenoDXCommander.Services;

internal static class RenoDxPresetIni
{
    private const string PresetPrefix = "[renodx-preset";

    /// <summary>The value from the first [renodx-preset*] section that has it (as a whole number). RenoDX's own spelling wins.</summary>
    public static string? Read(string iniPath, string iniKey)
    {
        if (!File.Exists(iniPath)) return null;
        string? exact = null, other = null;
        bool inPreset = false;
        foreach (var raw in File.ReadAllLines(iniPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inPreset = line.StartsWith(PresetPrefix, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inPreset || line.StartsWith(';')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim();
            var v = line[(eq + 1)..].Trim();
            if (exact == null && k.Equals(iniKey, StringComparison.Ordinal)) exact = v;
            else if (other == null && k.Equals(iniKey, StringComparison.OrdinalIgnoreCase)) other = v;
            if (exact != null) break;
        }
        var value = exact ?? other;
        if (value == null) return null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? ((int)Math.Round(d)).ToString(CultureInfo.InvariantCulture) : value;
    }

    /// <summary>
    /// Writes the value into [renodx-preset*] sections (all spellings of the key; both spellings go into a section that
    /// has neither). <paramref name="presets"/> null = every preset section, and [renodx-preset1] is created when there
    /// is none. Otherwise only those preset numbers, and each one missing from the file is created.
    /// Returns the number of sections written; the file is only rewritten when something changed.
    /// </summary>
    public static int Write(string iniPath, string iniKey, string value, IReadOnlyCollection<int>? presets = null)
    {
        var text = File.ReadAllText(iniPath);
        var eol = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        // Spelling(s) for sections that have neither: the installed mod's own, when it can be told (only looked up if needed)
        var spellingsLazy = new Lazy<IReadOnlyList<string>>(() => SpellingsForMod(iniPath, iniKey));

        int sections = 0;
        bool changed = false, appended = false;
        var written = new HashSet<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            var header = lines[i].Trim();
            if (!header.StartsWith(PresetPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            int end = i + 1;
            while (end < lines.Count && !lines[end].Trim().StartsWith('[')) end++;

            bool hasNumber = TryPresetNumber(header, out var number);
            if (presets != null && (!hasNumber || !presets.Contains(number))) { i = end - 1; continue; }
            sections++;
            if (hasNumber) written.Add(number);

            // The mod's spelling is known: drop the other one (older RHI versions wrote capital-T into Unity mods' inis)
            var only = spellingsLazy.Value.Count == 1 ? spellingsLazy.Value[0] : null;
            if (only != null)
            {
                for (int j = end - 1; j > i; j--)
                {
                    var t = lines[j].Trim();
                    int eq = t.IndexOf('=');
                    if (eq <= 0 || t.StartsWith(';')) continue;
                    var k = t[..eq].Trim();
                    if (k.Equals(iniKey, StringComparison.OrdinalIgnoreCase) && !k.Equals(only, StringComparison.Ordinal))
                    {
                        lines.RemoveAt(j);
                        end--;
                        changed = true;
                    }
                }
            }

            bool found = false;
            int lastKey = i;
            for (int j = i + 1; j < end; j++)
            {
                var t = lines[j].Trim();
                if (t.Length == 0 || t.StartsWith(';')) continue;
                lastKey = j;
                int eq = t.IndexOf('=');
                if (eq > 0 && t[..eq].Trim().Equals(iniKey, StringComparison.OrdinalIgnoreCase))
                {
                    var updated = t[..eq].Trim() + "=" + value;
                    if (lines[j] != updated) { lines[j] = updated; changed = true; }
                    found = true;
                }
            }
            if (!found)
            {
                var spellings = spellingsLazy.Value;
                lines.InsertRange(lastKey + 1, spellings.Select(k => k + "=" + value));
                end += spellings.Count;
                changed = true;
            }
            i = end - 1;
        }

        var missing = presets == null
            ? (sections == 0 ? new[] { 1 } : Array.Empty<int>())
            : presets.Where(n => !written.Contains(n)).OrderBy(n => n).ToArray();
        foreach (var n in missing)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[renodx-preset{n}]");
            lines.AddRange(spellingsLazy.Value.Select(k => k + "=" + value));
            sections++;
            changed = appended = true;
        }

        if (changed)
            File.WriteAllText(iniPath, string.Join(eol, lines) + (endsWithNewline || appended ? eol : ""));
        return sections;
    }

    /// <summary>Both spellings RenoDX mods use for a key: "toneMapPeakNits" and "ToneMapPeakNits".</summary>
    public static IReadOnlyList<string> Spellings(string iniKey)
    {
        var lower = char.ToLowerInvariant(iniKey[0]) + iniKey[1..];
        var upper = char.ToUpperInvariant(iniKey[0]) + iniKey[1..];
        return lower == upper ? new[] { iniKey } : new[] { lower, upper };
    }

    /// <summary>
    /// The spelling the installed RenoDX mod uses for the key: its addon contains the key name as plain text
    /// (renodx-unityengine has only "toneMapPeakNits", renodx-ue-extended only "ToneMapPeakNits"). Looks at the
    /// renodx-*.addon* files beside reshade.ini and in its AddonPath. Both spellings when no addon has the key,
    /// or the addons there disagree.
    /// </summary>
    public static IReadOnlyList<string> SpellingsForMod(string iniPath, string iniKey)
    {
        var both = Spellings(iniKey);
        if (both.Count == 1) return both;
        try
        {
            var iniDir = Path.GetDirectoryName(iniPath);
            if (string.IsNullOrEmpty(iniDir)) return both;
            var dirs = new List<string> { iniDir };
            var addonDir = ModInstallService.GetAddonDeployPath(iniDir);
            if (!string.Equals(Path.GetFullPath(addonDir).TrimEnd('\\'), Path.GetFullPath(iniDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(addonDir))
                dirs.Add(addonDir);

            bool lower = false, upper = false;
            foreach (var file in dirs.SelectMany(d => Directory.EnumerateFiles(d, "renodx-*.addon*")))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)) continue;
                var (l, u) = AddonHasKey(file, both[0], both[1]);
                lower |= l; upper |= u;
            }
            if (lower == upper) return both;
            return new[] { lower ? both[0] : both[1] };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            CrashReporter.Log($"[RenoDxPresetIni.SpellingsForMod] Couldn't read addons for '{iniPath}' — {ex.Message}");
            return both;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Lower, bool Upper)> _addonKeyCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the addon file contains each spelling (case-sensitive bytes). Cached by path, size, date and key.</summary>
    private static (bool Lower, bool Upper) AddonHasKey(string file, string lowerKey, string upperKey)
    {
        var info = new FileInfo(file);
        var cacheKey = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{lowerKey}";
        if (_addonKeyCache.TryGetValue(cacheKey, out var cached)) return cached;

        var lowerBytes = System.Text.Encoding.ASCII.GetBytes(lowerKey);
        var upperBytes = System.Text.Encoding.ASCII.GetBytes(upperKey);
        int overlap = Math.Max(lowerBytes.Length, upperBytes.Length) - 1;
        var buffer = new byte[4 * 1024 * 1024];
        bool lower = false, upper = false;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
        {
            int carried = 0, read;
            while (!(lower && upper) && (read = fs.Read(buffer, carried, buffer.Length - carried)) > 0)
            {
                var span = new ReadOnlySpan<byte>(buffer, 0, carried + read);
                if (!lower && span.IndexOf(lowerBytes) >= 0) lower = true;
                if (!upper && span.IndexOf(upperBytes) >= 0) upper = true;
                carried = Math.Min(overlap, span.Length);
                span[^carried..].CopyTo(buffer);
            }
        }
        _addonKeyCache[cacheKey] = (lower, upper);
        return (lower, upper);
    }

    private static bool TryPresetNumber(string header, out int number)
    {
        var inner = header.TrimEnd(']').Trim();
        return int.TryParse(inner[PresetPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
