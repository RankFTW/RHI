// OsIniEditor.cs — reads OptiScaler.ini as a list of documented settings and writes edits back
// without touching anything else in the file. Used by the "All Settings" view of the OptiScaler cog.
//
// OptiScaler documents each setting in the comment lines above it, ending with an options line:
//   ; nofg, fsrfg, xefg, nukems - Default (auto) is nofg
//   FGOutput=auto
// Every build (Stable, Nightly, forks, custom builds) ships its own copy, so the options come from the
// ini of the build that was installed (a "reference" copy saved at install / update time), not from
// lists written into RHI. Edits the user applies are saved per game and written back after a
// reinstall or update.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public enum OsIniKind { Choice, Bool, Text }

/// <summary>One choice of a setting. <see cref="Label"/> includes the build's note, e.g. "xess (native DX11, Arc only)".</summary>
public sealed record OsIniOption(string Value, string Label);

/// <summary>A setting found in OptiScaler.ini, with the help text and options from its comments.</summary>
public sealed record OsIniSetting(
    string Section,
    string Key,
    string Value,
    string Help,
    OsIniKind Kind,
    IReadOnlyList<OsIniOption> Options,
    string? Default,
    bool InFile = true)
{
    /// <summary>Key used for saved edits: "Section|Key".</summary>
    public string Id => OsIniEditor.MakeId(Section, Key);
}

public static class OsIniEditor
{
    public const string Auto = "auto";

    public static string MakeId(string section, string key) => $"{section}|{key}";

    public static (string Section, string Key) SplitId(string id)
    {
        var i = id.IndexOf('|');
        return i < 0 ? ("", id) : (id[..i], id[(i + 1)..]);
    }

    // "<options> - Default (auto) is <default>". Lazy so "0.0 - 1.0 - Default ..." keeps "0.0 - 1.0".
    private static readonly Regex OptionsLine = new(@"^(?<opts>.+?)\s+-\s+Default\b[^\n]*?\bis\s+(?<def>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RangeOpts = new(@"^(?<a>\d+)\s+to\s+(?<b>\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex NumberedLine = new(@"^\s*\d+\s*:", RegexOptions.CultureInvariant);
    private static readonly Regex Token = new(@"^[A-Za-z0-9_.+\-]+$", RegexOptions.CultureInvariant);

    // ── Reading ──────────────────────────────────────────────────────────────────

    /// <summary>Reads every key=value line with the comment block above it. Keys before any section are ignored.</summary>
    public static List<OsIniSetting> Parse(IEnumerable<string> lines)
    {
        var result = new List<OsIniSetting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        var comments = new List<string>();
        List<string> lastBlock = new();
        bool lastWasKey = false;

        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.Length == 0) { comments.Clear(); lastWasKey = false; continue; }
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                section = t[1..^1].Trim();
                comments.Clear();
                lastWasKey = false;
                continue;
            }
            if (t.StartsWith(';') || t.StartsWith('#'))
            {
                var c = t.TrimStart(';', '#').Trim();
                if (c.Length > 0 && c.All(ch => ch == '-' || ch == '=' || ch == '*')) continue; // separator line
                comments.Add(c);
                lastWasKey = false;
                continue;
            }
            int eq = t.IndexOf('=');
            if (eq <= 0 || section == null) continue;
            var key = t[..eq].Trim();
            var value = t[(eq + 1)..].Trim();

            // Keys listed straight after one another share its comments (RectLeft, RectTop, ...)
            var block = comments.Count > 0 ? new List<string>(comments) : lastWasKey ? lastBlock : new List<string>();
            comments.Clear();
            lastBlock = block;
            lastWasKey = true;

            if (!seen.Add(MakeId(section, key))) continue; // OptiScaler reads the first one
            result.Add(Describe(section, key, value, block));
        }
        return result;
    }

    public static List<OsIniSetting> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadAllLines(path)) : new List<OsIniSetting>();

    /// <summary>Works out the kind of control and the options from a setting's comment lines.</summary>
    internal static OsIniSetting Describe(string section, string key, string value, IReadOnlyList<string> comments)
    {
        var help = string.Join("\n", comments).Trim();
        OsIniKind kind = OsIniKind.Text;
        var options = new List<OsIniOption>();
        string? def = null;

        // "0: Input | 1: Time between Present calls | 2: Zero" wins over a wrong "true or false" line
        var numbered = comments.LastOrDefault(c => NumberedLine.IsMatch(c) && c.Contains('|'));
        Match? m = null;
        for (int i = comments.Count - 1; i >= 0 && m == null; i--)
        {
            var mm = OptionsLine.Match(comments[i]);
            if (mm.Success) m = mm;
        }
        if (m != null) def = m.Groups["def"].Value.Trim();

        if (numbered != null)
        {
            foreach (var part in numbered.Split('|'))
            {
                var p = part.Trim();
                int colon = p.IndexOf(':');
                if (colon <= 0) continue;
                var v = p[..colon].Trim();
                if (Token.IsMatch(v)) options.Add(new OsIniOption(v, p));
            }
            if (options.Count > 0) kind = OsIniKind.Choice;
        }
        else if (m != null)
        {
            var opts = m.Groups["opts"].Value.Trim();
            var range = RangeOpts.Match(opts);
            if (opts.Equals("true or false", StringComparison.OrdinalIgnoreCase))
            {
                kind = OsIniKind.Bool;
                options.Add(new OsIniOption("true", "true"));
                options.Add(new OsIniOption("false", "false"));
            }
            else if (range.Success && int.TryParse(range.Groups["a"].Value, out var a)
                     && int.TryParse(range.Groups["b"].Value, out var b) && b >= a && b - a <= 16)
            {
                kind = OsIniKind.Choice;
                for (int n = a; n <= b; n++) options.Add(new OsIniOption(n.ToString(), n.ToString()));
            }
            else if (opts.Contains(','))
            {
                var parsed = new List<OsIniOption>();
                bool ok = true;
                foreach (var item in SplitTopLevel(opts))
                {
                    var label = item.Trim();
                    if (label.Length == 0) continue;
                    int cut = label.IndexOfAny(new[] { ' ', '(' });
                    var v = (cut > 0 ? label[..cut] : label).Trim();
                    if (!Token.IsMatch(v)) { ok = false; break; }
                    if (!parsed.Any(o => o.Value.Equals(v, StringComparison.OrdinalIgnoreCase)))
                        parsed.Add(new OsIniOption(v, label));
                }
                if (ok && parsed.Count > 1) { kind = OsIniKind.Choice; options = parsed; }
            }
        }

        // "auto" is always allowed (it means: use the default)
        if (kind != OsIniKind.Text && !options.Any(o => o.Value.Equals(Auto, StringComparison.OrdinalIgnoreCase)))
            options.Insert(0, new OsIniOption(Auto, def != null ? $"auto (default: {def})" : "auto (default)"));

        return new OsIniSetting(section, key, value, help, kind, options, def);
    }

    /// <summary>Splits on commas that aren't inside brackets.</summary>
    private static IEnumerable<string> SplitTopLevel(string s)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth = Math.Max(0, depth - 1);
            else if (s[i] == ',' && depth == 0) { yield return s[start..i]; start = i + 1; }
        }
        yield return s[start..];
    }

    /// <summary>
    /// The game's settings (values from the game's ini), described by the installed build's own ini
    /// when there is one. Settings the build has but the game's ini lacks are added with "auto".
    /// </summary>
    public static List<OsIniSetting> Merge(List<OsIniSetting> game, List<OsIniSetting>? reference)
    {
        if (reference == null || reference.Count == 0) return game;
        var refById = new Dictionary<string, OsIniSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in reference) refById.TryAdd(r.Id, r);

        var result = new List<OsIniSetting>(game.Count + 16);
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in game)
        {
            have.Add(g.Id);
            result.Add(refById.TryGetValue(g.Id, out var r)
                ? r with { Value = g.Value, InFile = true }
                : g);
        }
        foreach (var r in reference)
        {
            if (!have.Add(r.Id)) continue;
            var missing = r with { Value = Auto, InFile = false };
            int at = result.FindLastIndex(s => s.Section.Equals(r.Section, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) result.Insert(at + 1, missing);
            else result.Add(missing);
        }
        return result;
    }

    /// <summary>Settings for a game folder: its OptiScaler.ini, described by the saved reference copy.</summary>
    public static List<OsIniSetting> Load(string gameDir)
    {
        var game = ParseFile(Path.Combine(gameDir, OptiScalerService.IniFileName));
        var refPath = ReferenceIniPath(gameDir);
        List<OsIniSetting>? reference = null;
        try { if (File.Exists(refPath)) reference = ParseFile(refPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsIniEditor.Load] Couldn't read the reference ini — {ex.Message}");
        }
        return Merge(game, reference);
    }

    // ── Writing ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <paramref name="lines"/> with each value set. Existing keys are changed in place (comments,
    /// order and spacing kept); missing keys go after the last key of their section, or in a new section.
    /// </summary>
    public static List<string> ApplyValues(IEnumerable<string> lines, IEnumerable<(string Section, string Key, string Value)> values)
    {
        var result = lines.ToList();
        foreach (var (section, key, value) in values)
        {
            int sectionStart = -1, lastKeyLine = -1, keyLine = -1;
            bool inSection = false;
            for (int i = 0; i < result.Count; i++)
            {
                var t = result[i].Trim();
                if (t.StartsWith('[') && t.EndsWith(']'))
                {
                    if (inSection) break;
                    inSection = t[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                    if (inSection) sectionStart = i;
                    continue;
                }
                if (!inSection || t.Length == 0 || t.StartsWith(';') || t.StartsWith('#')) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                lastKeyLine = i;
                if (t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { keyLine = i; break; }
            }

            if (keyLine >= 0)
            {
                var line = result[keyLine];
                int eq = line.IndexOf('=');
                result[keyLine] = line[..(eq + 1)] + value; // keeps the file's spelling of the key
            }
            else if (sectionStart >= 0)
                result.Insert((lastKeyLine >= 0 ? lastKeyLine : sectionStart) + 1, $"{key}={value}");
            else
            {
                if (result.Count > 0 && result[^1].Trim().Length > 0) result.Add("");
                result.Add($"[{section}]");
                result.Add($"{key}={value}");
            }
        }
        return result;
    }

    /// <summary>Backs up the game's OptiScaler.ini, then writes the values. Returns an error message, or null.</summary>
    public static string? WriteValues(string gameDir, IReadOnlyCollection<(string Section, string Key, string Value)> values, bool backup = true)
    {
        var iniPath = Path.Combine(gameDir, OptiScalerService.IniFileName);
        if (values.Count == 0) return null;
        if (!File.Exists(iniPath)) return "OptiScaler.ini wasn't found in the game folder.";
        try
        {
            if (backup) Backup(gameDir, iniPath);
            var lines = File.ReadAllLines(iniPath);
            var updated = ApplyValues(lines, values);
            var attrs = File.GetAttributes(iniPath);
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(iniPath, attrs & ~FileAttributes.ReadOnly);
            File.WriteAllLines(iniPath, updated);
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(iniPath, attrs);
            foreach (var (s, k, v) in values)
                CrashReporter.Log($"[OsIniEditor.WriteValues] [{s}] {k}={v} in '{gameDir}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsIniEditor.WriteValues] Failed for '{gameDir}' — {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>Writes a game's saved edits ("Section|Key" → value) into its OptiScaler.ini. Returns how many were written.</summary>
    public static int ApplySaved(string gameDir, IReadOnlyDictionary<string, string>? edits)
    {
        if (edits == null || edits.Count == 0) return 0;
        var values = edits.Select(kv => { var (s, k) = SplitId(kv.Key); return (s, k, kv.Value); })
                          .Where(v => v.s.Length > 0 && v.k.Length > 0).ToList();
        var err = WriteValues(gameDir, values, backup: false);
        if (err != null) return 0;
        CrashReporter.Log($"[OsIniEditor.ApplySaved] Re-applied {values.Count} saved OptiScaler.ini edit(s) in '{gameDir}'");
        return values.Count;
    }

    // ── Files in RHI's own folder ────────────────────────────────────────────────

    private static readonly string RhiDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

    /// <summary>Short stable name for a game folder: "<folder>-<hash>".</summary>
    private static string FolderTag(string gameDir)
    {
        var norm = Path.GetFullPath(gameDir).Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(norm)))[..10];
        var name = Path.GetFileName(norm);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length > 40) name = name[..40];
        return $"{name}-{hash}";
    }

    /// <summary>Where the installed build's own OptiScaler.ini is kept for a game folder.</summary>
    public static string ReferenceIniPath(string gameDir) =>
        Path.Combine(RhiDir, "optiscaler", "ini-reference", FolderTag(gameDir) + ".ini");

    /// <summary>
    /// Keeps a copy of the installed build's OptiScaler.ini (its comments list that build's options).
    /// A build without one removes the old copy, so it can't describe a different build.
    /// </summary>
    public static void SaveReferenceIni(string gameDir, string? stagedIniPath)
    {
        try
        {
            var dest = ReferenceIniPath(gameDir);
            if (!string.IsNullOrEmpty(stagedIniPath) && File.Exists(stagedIniPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(stagedIniPath, dest, overwrite: true);
                CrashReporter.Log($"[OsIniEditor.SaveReferenceIni] Saved the build's OptiScaler.ini for '{gameDir}'");
            }
            else if (File.Exists(dest))
                File.Delete(dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsIniEditor.SaveReferenceIni] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>Copies the game's OptiScaler.ini to RHI's backups folder, keeping the newest 5 per game.</summary>
    internal static void Backup(string gameDir, string iniPath)
    {
        try
        {
            var dir = Path.Combine(RhiDir, "backups", "OptiScalerIni", FolderTag(gameDir));
            Directory.CreateDirectory(dir);
            File.Copy(iniPath, Path.Combine(dir, $"OptiScaler-{DateTime.Now:yyyyMMdd-HHmmss}.ini"), overwrite: true);
            foreach (var old in new DirectoryInfo(dir).GetFiles("OptiScaler-*.ini")
                         .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(5))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsIniEditor.Backup] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>The backups folder for a game, for "Open backups".</summary>
    public static string BackupDir(string gameDir) =>
        Path.Combine(RhiDir, "backups", "OptiScalerIni", FolderTag(gameDir));
}
