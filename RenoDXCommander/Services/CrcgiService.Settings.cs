// CrcgiService.Settings.cs -- CRCGI's settings for the ⚙: the effect's values and compile-time switches in the game's
// ReShade preset ([CRCGI_v2.fx] section, CrcgiPreset), the add-on's Level in crcgi_injector.ini next to the game exe
// (the add-on reads it line by line at start and uses its defaults for anything missing), and the author's ready-made
// presets from the repo's presets\ folder (a folder per game; the preset .ini files are per CRCGI version; a shareable
// crcgi_targets_*.ini is copied next to the exe for the add-on's Options › Import). ReShade reads the preset at game
// start (or on reload); saving in the overlay while the game runs writes over these values.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public partial class CrcgiService
{
    private const string PresetsApiUrl = "https://api.github.com/repos/" + Repo + "/contents/presets";
    public  const string InjectorIni   = "crcgi_injector.ini";
    private const string PresetBackupSuffix = ".rhi-crcgi-backup";

    // ── The effect's section in the game's preset ─────────────────────────────

    /// <summary>The effect section RHI edits: the installed effect ("CRCGI_v2.fx"), else the staged one.</summary>
    public static string? EffectSection(string installPath) => MarkerValue(installPath, "effect") ?? StagedEffect;

    public static string PresetFile(string installPath) => UpliftService.PresetPath(installPath);

    public static string? ReadPreset(string installPath)
    {
        try { var p = PresetFile(installPath); return File.Exists(p) ? File.ReadAllText(p) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[CrcgiService.ReadPreset] {ex.Message}");
            return null;
        }
    }

    /// <summary>Applies an edit to the game's preset text and saves it. False when the file can't be written.</summary>
    public bool EditPreset(string installPath, Func<string?, string> edit, string what)
    {
        var path = PresetFile(installPath);
        try
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : null;
            var updated = edit(text);
            if (updated == text) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, updated);
            _crashReporter.Log($"[CrcgiService.EditPreset] {what} in '{path}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[CrcgiService.EditPreset] {what} not written to '{path}' — {ex.Message}");
            return false;
        }
    }

    // ── The add-on's level (crcgi_injector.ini next to the game exe) ──────────

    /// <summary>The folder of the game's exe (where the add-on keeps its ini), else the install folder.</summary>
    public static string ExeDir(string installPath, IPeHeaderService pe)
    {
        try
        {
            if (pe.FindGameExe(installPath) is { } exe && Path.GetDirectoryName(exe) is { } dir) return dir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CrashReporter.Log($"[CrcgiService.ExeDir] {ex.Message}"); }
        return installPath;
    }

    /// <summary>The add-on's level: 0 = A (end of frame), 1 = B (inside the frame), 2 = C (+ the game's buffers). Default A.</summary>
    public static int ReadInjectorLevel(string exeDir)
    {
        try
        {
            var ini = Path.Combine(exeDir, InjectorIni);
            if (!File.Exists(ini)) return 0;
            return int.TryParse(FeederPreset.GetKey(File.ReadAllText(ini), "General", "Level"), out var l) ? Math.Clamp(l, 0, 2) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    public bool WriteInjectorLevel(string exeDir, int level)
    {
        var ini = Path.Combine(exeDir, InjectorIni);
        try
        {
            var text = File.Exists(ini) ? File.ReadAllText(ini) : null;
            File.WriteAllText(ini, FeederPreset.SetKey(text, "General", "Level", Math.Clamp(level, 0, 2).ToString()));
            _crashReporter.Log($"[CrcgiService.WriteInjectorLevel] Level={level} in '{ini}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[CrcgiService.WriteInjectorLevel] '{ini}' — {ex.Message}");
            return false;
        }
    }

    // ── The author's presets (repo presets\<game folder>\*.ini) ───────────────

    public sealed record AuthorPreset(string Folder, string Name, string Url);
    public sealed record AuthorPresetSet(string Folder, IReadOnlyList<AuthorPreset> Presets, AuthorPreset? Targets, string? ReadmeUrl);

    private List<string>? _presetFolders;

    /// <summary>
    /// The author's presets for this game and the installed CRCGI version ("CRCGI v2" in the file name), with a
    /// shareable add-on target listing when the folder has one. Null when there's none or GitHub can't be reached.
    /// </summary>
    public async Task<AuthorPresetSet?> FindAuthorPresetsAsync(string gameName, string? effectFile)
    {
        var major = effectFile == null ? null : Regex.Match(effectFile, @"_v(\d+)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
        if (major == null) return null;
        try
        {
            if (_presetFolders == null)
            {
                var json = await _etagCache.GetWithETagAsync(_http, PresetsApiUrl).ConfigureAwait(false);
                if (json == null) return null;
                using var doc = JsonDocument.Parse(json);
                _presetFolders = doc.RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "dir")
                    .Select(e => e.GetProperty("name").GetString() ?? "")
                    .Where(n => n.Length > 0).ToList();
            }
            foreach (var folder in _presetFolders.Where(f => CrcgiPreset.GameMatchesPresetFolder(gameName, f)))
            {
                var json = await _etagCache.GetWithETagAsync(_http, $"{PresetsApiUrl}/{Uri.EscapeDataString(folder)}").ConfigureAwait(false);
                if (json == null) continue;
                using var doc = JsonDocument.Parse(json);
                var presets = new List<AuthorPreset>();
                AuthorPreset? targets = null;
                string? readme = null;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (!(e.TryGetProperty("type", out var t) && t.GetString() == "file")) continue;
                    var name = e.GetProperty("name").GetString() ?? "";
                    var url = e.TryGetProperty("download_url", out var u) ? u.GetString() : null;
                    if (url == null) continue;
                    if (name.Equals("README.txt", StringComparison.OrdinalIgnoreCase)) readme = e.TryGetProperty("html_url", out var h) ? h.GetString() : url;
                    else if (name.StartsWith("crcgi_targets", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                        targets = new AuthorPreset(folder, name, url);
                    else if (name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                             && !name.Equals(InjectorIni, StringComparison.OrdinalIgnoreCase)
                             && IsPresetForMajor(name, major))
                        presets.Add(new AuthorPreset(folder, name, url));
                }
                if (presets.Count > 0) return new AuthorPresetSet(folder, presets, targets, readme);
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or IOException
                                      or KeyNotFoundException or InvalidOperationException)
        {
            _crashReporter.Log($"[CrcgiService.FindAuthorPresets] '{gameName}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>"… CRCGI v2.ini" / "… CRCGI v2 (Not tested).ini" are v2 presets; "… CRCGI v1.2.ini" and "v20" aren't. Pure.</summary>
    internal static bool IsPresetForMajor(string fileName, string major) =>
        Regex.IsMatch(fileName, $@"CRCGI\s*v{major}(?!\d|\.\d)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Puts the author's values for the effect into the game's preset: the effect's section is replaced with the
    /// preset's (other effects and settings are kept; the preset is backed up once first) and the effect is ticked.
    /// With <paramref name="copyTargets"/> the folder's add-on target listing is copied next to the exe. Returns an error, or null.
    /// </summary>
    public async Task<string?> ApplyAuthorPresetAsync(string installPath, AuthorPreset preset, AuthorPreset? targets, string exeDir, UpliftService uplift)
    {
        var section = EffectSection(installPath);
        if (section == null) return "Install CRCGI first.";
        try
        {
            var text = await _http.GetStringAsync(preset.Url).ConfigureAwait(false);
            var body = CrcgiPreset.GetSection(text, section);
            if (body == null || body.Count == 0)
                return $"The preset has no [{section}] section, so it's for another CRCGI version.";

            var path = PresetFile(installPath);
            if (File.Exists(path) && !File.Exists(path + PresetBackupSuffix)) File.Copy(path, path + PresetBackupSuffix);
            if (!EditPreset(installPath, t => CrcgiPreset.ReplaceSection(t, section, body), $"Author preset '{preset.Name}'"))
                return "Couldn't write the game's ReShade preset. Close the game and try again.";
            if (StagedTechniqueToken is { } token) uplift.EnableTechnique(installPath, token);

            if (targets != null)
            {
                var listing = await _http.GetStringAsync(targets.Url).ConfigureAwait(false);
                if (FileHelper.SafeCombine(exeDir, targets.Name) is { } dest)
                {
                    File.WriteAllText(dest, listing);
                    _crashReporter.Log($"[CrcgiService.ApplyAuthorPreset] Target listing '{targets.Name}' → '{exeDir}'");
                }
            }
            _crashReporter.Log($"[CrcgiService.ApplyAuthorPreset] '{preset.Name}' ({body.Count} values) applied in '{installPath}'");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[CrcgiService.ApplyAuthorPreset] '{preset.Name}' — {ex.Message}");
            return $"Couldn't apply the preset: {ex.Message}";
        }
    }
}
