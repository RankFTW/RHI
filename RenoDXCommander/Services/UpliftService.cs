// UpliftService.cs — GITC Uplift (ghostinthecamera/GITC-Uplift): a ReShade addon that runs NVIDIA
// DLSS Neural Rendering on the image ReShade sees. One of the Neural Rendering section's methods.
//
// Release zip layout:
//   Addons/gitc-uplift.addon64 | .addon32   → ReShade addon folder (GetAddonDeployPath)
//   Addons/gitc-uplift-helper64.exe         → same folder (32-bit games, Vulkan "Helper" mode)
//   Addons/GITC-Uplift/                     → nvngx_dlssnr.dll goes in this subfolder, not beside the exe
//   Shaders/Uplift.fx, UpliftMask.fx        → game reshade-shaders\Shaders (via the shader sync hook)
//
// Versions: "Latest" is staged in %LocalAppData%\RHI\uplift\ and auto-updates; a pinned version is
// staged in uplift-versions\{ver}\ and never changes. Staged only while a game uses it (installing
// downloads it). Files RHI placed are recorded in rhi_install.txt under "Uplift", relative to the
// addon folder; GITC-Uplift\rhi-uplift.txt (also recorded) holds the installed + pinned version.

using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class UpliftService
{
    private const string LatestApiUrl   = "https://api.github.com/repos/ghostinthecamera/GITC-Uplift/releases/latest";
    private const string ReleasesApiUrl = "https://api.github.com/repos/ghostinthecamera/GITC-Uplift/releases?per_page=30";
    public const string ReleasesPage = "https://github.com/ghostinthecamera/GITC-Uplift";

    public const string Component    = "Uplift";
    public const string Addon64      = "gitc-uplift.addon64";
    public const string Addon32      = "gitc-uplift.addon32";
    public const string HelperExe    = "gitc-uplift-helper64.exe";
    public const string DllFolder    = "GITC-Uplift";
    public const string NrDllName    = "nvngx_dlssnr.dll";
    private const string MarkerName  = "rhi-uplift.txt"; // in GITC-Uplift\: installed + pinned version
    public static readonly string[] ShaderFiles = { "Uplift.fx", "UpliftMask.fx" };
    // Names used before 1.x; the README says to delete them when updating
    private static readonly string[] OldNames = { "uplift.addon64", "uplift.addon32", "uplift-helper64.exe" };
    private static readonly string[] StagedFiles = { Addon64, Addon32, HelperExe, "Uplift.fx", "UpliftMask.fx" };

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly GitHubETagCache _etagCache;
    private readonly SemaphoreSlim _stagingLock = new(1, 1);

    private static readonly string RhiDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
    public static readonly string StagingDir = Path.Combine(RhiDir, "uplift");
    private static readonly string VersionsDir = Path.Combine(RhiDir, "uplift-versions");
    private static readonly string ReleasesCacheFile = Path.Combine(RhiDir, "uplift_releases.json");

    internal sealed record Release(string Version, string? ZipUrl, string? SumsUrl);
    private List<Release> _releases = new();

    public UpliftService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http = http;
        _crashReporter = crashReporter;
        _etagCache = etagCache;
        LoadReleasesCache();
    }

    private static string? ReadVersion(string dir)
    {
        var f = Path.Combine(dir, "version.txt");
        try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch (IOException) { return null; }
    }
    private static bool IsReady(string dir) => StagedFiles.All(f => File.Exists(Path.Combine(dir, f))) && ReadVersion(dir) != null;
    private static string VersionDir(string version) => Path.Combine(VersionsDir, version);

    public string? StagedVersion => ReadVersion(StagingDir);
    public bool IsStagingReady => IsReady(StagingDir);
    public string? LatestVersion { get; private set; }
    public bool HasUpdate { get; private set; }

    /// <summary>Released versions, newest first (for the version picker).</summary>
    public IReadOnlyList<string> AvailableVersions => _releases.Select(r => r.Version).ToList();

    // ── Where things go ───────────────────────────────────────────────────────

    public static string AddonDir(string installPath) => ModInstallService.GetAddonDeployPath(installPath);
    public static string AddonFileFor(bool is32Bit) => is32Bit ? Addon32 : Addon64;

    /// <summary>Uplift's addon is in this game's addon folder.</summary>
    public static bool IsInstalledIn(string installPath, bool is32Bit) =>
        !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(AddonDir(installPath), AddonFileFor(is32Bit)));

    /// <summary>RHI installed Uplift here (it has a record), as opposed to a manual install.</summary>
    public static bool IsRhiInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) && RhiInstallManifest.GetComponentFiles(installPath, Component).Count > 0;

    /// <summary>The NR DLL Uplift loads: in the GITC-Uplift subfolder of the addon folder.</summary>
    public static string NrDllPath(string installPath) => Path.Combine(AddonDir(installPath), DllFolder, NrDllName);

    private static string MarkerPath(string installPath) => Path.Combine(AddonDir(installPath), DllFolder, MarkerName);

    /// <summary>The version RHI installed here and the pinned version (null = Latest).</summary>
    public static (string? Installed, string? Pinned) ReadMarker(string installPath)
    {
        var m = ReadMarkerAll(installPath);
        return (m.Installed, m.Pinned);
    }

    // technique= in the marker: "" not checked yet, "1" RHI enabled it (removed again on uninstall),
    // "user" it was already on (left alone). Checked once, so a user who turns it off keeps it off.
    // launchpad= (iMMERSE Launchpad, see below): "" not decided yet, "1" RHI enabled its technique,
    // "user" it was already on, "off" switched off. Its shader files are deployed for "1" / "user".
    private static (string? Installed, string? Pinned, string Technique, string Launchpad) ReadMarkerAll(string installPath)
    {
        string? installed = null, pinned = null;
        string technique = "", launchpad = "";
        try
        {
            var path = MarkerPath(installPath);
            if (!File.Exists(path)) return (null, null, "", "");
            foreach (var line in File.ReadAllLines(path))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var key = line[..i].Trim();
                var val = line[(i + 1)..].Trim();
                if (key == "version") installed = val.Length > 0 ? val : null;
                else if (key == "pinned") pinned = val.Length > 0 ? val : null;
                else if (key == "technique") technique = val;
                else if (key == "launchpad") launchpad = val;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (installed, pinned, technique, launchpad);
    }

    private static void WriteMarker(string installPath, string? version, string? pinned, string technique, string launchpad) =>
        File.WriteAllLines(MarkerPath(installPath), new[] { $"version={version}", $"pinned={pinned}", $"technique={technique}", $"launchpad={launchpad}" });

    // ── The Uplift technique in the ReShade preset ────────────────────────────
    // Uplift.fx's "Uplift" technique marks where NR runs among the effects. Without it NR runs
    // before every effect at Present, which on OpenGL can't read a multisampled back buffer
    // (src/gl/format.cpp BackBufferProblem): OpenMW and other MSAA games then get no NR at all.
    // With it enabled NR runs on ReShade's resolved single-sampled copy. RHI enables it first in
    // the list (NR before the user's effects, as without it) on OpenGL games.

    public const string TechniqueToken = "Uplift@Uplift.fx";

    /// <summary>The game's ReShade preset: [GENERAL] PresetPath in reshade.ini, else ReShadePreset.ini.</summary>
    public static string PresetPath(string installPath)
    {
        string? value = null;
        var ini = Path.Combine(installPath, "reshade.ini");
        try
        {
            if (File.Exists(ini))
            {
                string section = "";
                foreach (var raw in File.ReadAllLines(ini))
                {
                    var line = raw.Trim();
                    if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
                    if (section.Equals("GENERAL", StringComparison.OrdinalIgnoreCase)
                        && line.StartsWith("PresetPath=", StringComparison.OrdinalIgnoreCase))
                        value = line["PresetPath=".Length..].Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (string.IsNullOrWhiteSpace(value)) value = "ReShadePreset.ini";
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(installPath, value));
    }

    /// <summary>
    /// Puts a technique (Uplift by default) in the preset's Techniques and TechniqueSorting, first or
    /// just before <paramref name="before"/> (creating the preset or the lines when missing). False if
    /// it was already enabled (so RHI didn't add it).
    /// </summary>
    public bool EnableTechnique(string installPath, string token = TechniqueToken, string? before = null)
    {
        var preset = PresetPath(installPath);
        string text = File.Exists(preset) ? File.ReadAllText(preset) : "";
        var (newText, alreadyOn, changed) = AddTechniqueToPreset(text, token, before);
        if (changed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preset)!);
            File.WriteAllText(preset, newText);
            _crashReporter.Log($"[UpliftService.EnableTechnique] Enabled '{token}'{(before != null ? $" before '{before}'" : " first")} in '{preset}'");
        }
        return !alreadyOn;
    }

    /// <summary>Takes a technique (Uplift by default) back out of the preset's Techniques and TechniqueSorting.</summary>
    public void DisableTechnique(string installPath, string token = TechniqueToken)
    {
        var preset = PresetPath(installPath);
        if (!File.Exists(preset)) return;
        try
        {
            var (newText, changed) = RemoveTechniqueFromPreset(File.ReadAllText(preset), token);
            if (changed)
            {
                File.WriteAllText(preset, newText);
                _crashReporter.Log($"[UpliftService.DisableTechnique] Removed '{token}' from '{preset}'");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[UpliftService.DisableTechnique] '{preset}' not updated — {ex.Message}"); }
    }

    /// <summary>Preset text with Uplift first in every Techniques / TechniqueSorting line.</summary>
    public static (string Text, bool AlreadyOn, bool Changed) AddTechniqueToPreset(string text) =>
        AddTechniqueToPreset(text, TechniqueToken, null);

    /// <summary>
    /// Preset text with <paramref name="token"/> in every Techniques / TechniqueSorting line, first or
    /// just before <paramref name="before"/> when that's in the line (lines added at the top when
    /// missing). A token already in TechniqueSorting but after <paramref name="before"/> is moved in
    /// front of it. Line endings and everything else are kept. AlreadyOn: Techniques had it.
    /// </summary>
    public static (string Text, bool AlreadyOn, bool Changed) AddTechniqueToPreset(string text, string token, string? before)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Length == 0 ? new List<string>() : text.Split(nl).ToList();
        bool hadTechniques = false, hadSorting = false, alreadyOn = false, changed = false;
        for (int i = 0; i < lines.Count; i++)
        {
            foreach (var key in new[] { "Techniques", "TechniqueSorting" })
            {
                if (!lines[i].StartsWith(key + "=", StringComparison.Ordinal)) continue;
                var items = SplitList(lines[i][(key.Length + 1)..]);
                int at = items.FindIndex(t => t.Equals(token, StringComparison.OrdinalIgnoreCase));
                int beforeAt = before == null ? -1 : items.FindIndex(t => t.Equals(before, StringComparison.OrdinalIgnoreCase));
                if (key == "Techniques") { hadTechniques = true; alreadyOn |= at >= 0; }
                else hadSorting = true;
                if (at >= 0)
                {
                    // Order matters only in TechniqueSorting: keep it in front of `before`
                    if (key != "TechniqueSorting" || beforeAt < 0 || at < beforeAt) continue;
                    items.RemoveAt(at);
                    items.Insert(beforeAt, token);
                }
                else items.Insert(Math.Max(0, beforeAt), token);
                lines[i] = key + "=" + string.Join(",", items);
                changed = true;
            }
        }
        // Top-level lines go before the first section, as ReShade writes them
        if (!hadSorting) { lines.Insert(0, "TechniqueSorting=" + token); changed = true; }
        if (!hadTechniques) { lines.Insert(0, "Techniques=" + token); changed = true; }
        if (text.Length == 0) lines.Add(""); // end a new file with a line break
        return (string.Join(nl, lines), alreadyOn, changed);
    }

    /// <summary>Preset text with Uplift removed from every Techniques / TechniqueSorting line.</summary>
    public static (string Text, bool Changed) RemoveTechniqueFromPreset(string text) =>
        RemoveTechniqueFromPreset(text, TechniqueToken);

    /// <summary>Preset text with <paramref name="token"/> removed from every Techniques / TechniqueSorting line.</summary>
    public static (string Text, bool Changed) RemoveTechniqueFromPreset(string text, string token)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(nl);
        bool changed = false;
        for (int i = 0; i < lines.Length; i++)
            foreach (var key in new[] { "Techniques", "TechniqueSorting" })
            {
                if (!lines[i].StartsWith(key + "=", StringComparison.Ordinal)) continue;
                var items = SplitList(lines[i][(key.Length + 1)..]);
                if (items.RemoveAll(t => t.Equals(token, StringComparison.OrdinalIgnoreCase)) == 0) continue;
                lines[i] = key + "=" + string.Join(",", items);
                changed = true;
            }
        return (string.Join(nl, lines), changed);
    }

    private static List<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// OpenGL games need the Uplift technique (see above). Enables it where RHI installed Uplift and
    /// it isn't on yet, and records that RHI added it. True if the preset was changed.
    /// </summary>
    public bool EnsureTechniqueFor(string installPath, bool isOpenGl)
    {
        if (!isOpenGl || !IsRhiInstalled(installPath)) return false;
        var m = ReadMarkerAll(installPath);
        if (m.Technique.Length > 0) return false; // already checked for this install
        try
        {
            bool added = EnableTechnique(installPath);
            WriteMarker(installPath, m.Installed, m.Pinned, added ? "1" : "user", m.Launchpad);
            return added;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[UpliftService.EnsureTechniqueFor] '{installPath}' — {ex.Message}");
            return false;
        }
    }

    // ── iMMERSE Launchpad (motion vectors) ────────────────────────────────────
    // Uplift needs motion vectors. DLSS games give it theirs; in other games they come from a ReShade
    // effect: iMMERSE Launchpad (Marty McFly, free iMMERSE repo). Its technique must be on and above
    // the Uplift technique, which Uplift.fx uses to hand the motion to the add-on (README / wiki Guide,
    // "Motion sources and effect order", 1.8.0). RHI deploys only Launchpad and its includes from the
    // iMMERSE shader pack (through the shader sync hook, like Uplift.fx) and edits the preset.
    // On by default in games without DLSS; a per-game toggle in the Neural Rendering section.

    public const string LaunchpadToken = "MartysMods_Launchpad@MartysMods_LAUNCHPAD.fx";
    /// <summary>The shader pack Launchpad comes from (ShaderPackService).</summary>
    public const string LaunchpadPackId = "iMMERSE";
    private static string LaunchpadFx => Path.Combine(ShaderPackService.ShadersDir, "iMMERSE", "MartysMods_LAUNCHPAD.fx");

    /// <summary>True when the iMMERSE pack (with Launchpad) has been downloaded.</summary>
    public static bool LaunchpadStaged => File.Exists(LaunchpadFx);

    /// <summary>True when RHI deploys Launchpad in this game (switched on, or it was already on).</summary>
    public static bool LaunchpadOn(string installPath) => ReadMarkerAll(installPath).Launchpad is "1" or "user";

    /// <summary>True once Launchpad has been switched on or off for this install.</summary>
    public static bool LaunchpadDecided(string installPath) => ReadMarkerAll(installPath).Launchpad.Length > 0;

    /// <summary>Launchpad's files (reshade-shaders relative path → staged source): the effect, its includes and its texture. HSF uses them too.</summary>
    internal static IEnumerable<(string Rel, string Source)> LaunchpadFiles()
    {
        if (!File.Exists(LaunchpadFx)) yield break;
        var shaders = Path.GetDirectoryName(LaunchpadFx)!;
        yield return (Path.Combine("Shaders", "iMMERSE", "MartysMods_LAUNCHPAD.fx"), LaunchpadFx);
        var includes = Path.Combine(shaders, "MartysMods");
        if (Directory.Exists(includes))
            foreach (var f in Directory.EnumerateFiles(includes))
                yield return (Path.Combine("Shaders", "iMMERSE", "MartysMods", Path.GetFileName(f)), f);
        var noise = Path.Combine(ShaderPackService.TexturesDir, "iMMERSE", "iMMERSE_bluenoise_opt.png");
        if (File.Exists(noise))
            yield return (Path.Combine("Textures", "iMMERSE", "iMMERSE_bluenoise_opt.png"), noise);
    }

    /// <summary>
    /// Switches Launchpad on (its technique, then Uplift's after it, in the preset; the files follow
    /// through the shader sync) or off (only a technique RHI added comes out). The caller syncs the
    /// game's shaders afterwards. False if Uplift isn't installed here or the preset can't be written.
    /// </summary>
    // ── Start with NR on ([Uplift] StartEnabled in reshade.ini, from 2.0.0) ──

    /// <summary>Uplift 2.0.0+ starts every session with NR off; StartEnabled=1 under [Uplift] in reshade.ini turns it on at start.</summary>
    public const string StartEnabledKey = "StartEnabled";

    /// <summary>NR on at game start (StartEnabled=1), false otherwise; null when the game has no reshade.ini.</summary>
    public static bool? ReadStartEnabled(string installPath)
    {
        var ini = Path.Combine(installPath, "reshade.ini");
        if (!File.Exists(ini)) return null;
        return Dlssg20_30Service.ReadIniValue(ini, "Uplift", StartEnabledKey) == "1";
    }

    /// <summary>Writes StartEnabled=1 or 0 under [Uplift] in the game's reshade.ini. False when it couldn't.</summary>
    public bool SetStartEnabled(string installPath, bool on)
    {
        var ini = Path.Combine(installPath, "reshade.ini");
        if (!File.Exists(ini)) return false;
        try
        {
            Dlssg20_30Service.WriteIniValue(ini, "Uplift", StartEnabledKey, on ? "1" : "0");
            _crashReporter.Log($"[UpliftService.SetStartEnabled] {StartEnabledKey}={(on ? 1 : 0)} in '{ini}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[UpliftService.SetStartEnabled] '{ini}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Uplift's NR on/off key: a decimal Win32 virtual-key code under [Uplift] in reshade.ini (0 = none).</summary>
    public const string EnableKeyKey = "EnableKey";

    /// <summary>
    /// Writes RHI's global Uplift key ([Uplift] EnableKey) into a reshade.ini. Nothing is written for 0 (none), so a
    /// key the player set in Uplift's own panel stays until they pick one in RHI. False when it couldn't.
    /// </summary>
    public static bool ApplyEnableKey(string iniPath, int vk)
    {
        if (vk <= 0 || vk > 255 || !File.Exists(iniPath)) return false;
        try
        {
            Dlssg20_30Service.WriteIniValue(iniPath, "Uplift", EnableKeyKey, vk.ToString(System.Globalization.CultureInfo.InvariantCulture));
            CrashReporter.Log($"[UpliftService.ApplyEnableKey] {EnableKeyKey}={vk} in '{iniPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[UpliftService.ApplyEnableKey] '{iniPath}' — {ex.Message}");
            return false;
        }
    }

    public bool SetLaunchpad(string installPath, bool on)
    {
        if (!IsRhiInstalled(installPath)) return false;
        var m = ReadMarkerAll(installPath);
        try
        {
            string technique = m.Technique, launchpad;
            if (on)
            {
                // Launchpad's motion reaches the add-on through the Uplift technique below it
                bool upliftAdded = EnableTechnique(installPath);
                if (technique.Length == 0) technique = upliftAdded ? "1" : "user";
                bool added = EnableTechnique(installPath, LaunchpadToken, before: TechniqueToken);
                launchpad = added || m.Launchpad == "1" ? "1" : "user";
            }
            else
            {
                if (m.Launchpad == "1") DisableTechnique(installPath, LaunchpadToken);
                launchpad = "off";
            }
            WriteMarker(installPath, m.Installed, m.Pinned, technique, launchpad);
            _crashReporter.Log($"[UpliftService.SetLaunchpad] Launchpad {(on ? "on" : "off")} in '{installPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[UpliftService.SetLaunchpad] '{installPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// First time only (install, or installs from before RHI handled Launchpad): applies the game's
    /// Launchpad choice. True if Launchpad was switched on.
    /// </summary>
    public bool EnsureLaunchpadFor(string installPath, bool wanted)
    {
        if (!IsRhiInstalled(installPath) || LaunchpadDecided(installPath)) return false;
        return SetLaunchpad(installPath, wanted) && wanted;
    }

    /// <summary>
    /// Shader files the game's reshade-shaders folder must keep while Uplift is installed there
    /// (relative path inside reshade-shaders → staged source of the installed version). Hooked into
    /// ShaderPackService's sync so the mirror never prunes them.
    /// </summary>
    public IReadOnlyList<(string Rel, string Source)> ShaderFilesFor(string gameDir)
    {
        if (!IsRhiInstalled(gameDir)) return Array.Empty<(string, string)>();
        var pinned = ReadMarker(gameDir).Pinned;
        var dir = pinned != null && IsReady(VersionDir(pinned)) ? VersionDir(pinned) : StagingDir;
        var list = new List<(string, string)>();
        foreach (var f in ShaderFiles)
        {
            var src = Path.Combine(dir, f);
            if (File.Exists(src)) list.Add((Path.Combine("Shaders", f), src));
        }
        if (LaunchpadOn(gameDir)) list.AddRange(LaunchpadFiles());
        return list;
    }

    // ── Release list ──────────────────────────────────────────────────────────

    /// <summary>Refreshes the version list (1-hour cooldown unless forced). ETag-cached.</summary>
    public async Task FetchAvailableVersionsAsync(bool forceRefresh = false)
    {
        try
        {
            if (!forceRefresh && _releases.Count > 0 && File.Exists(ReleasesCacheFile)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(ReleasesCacheFile) < TimeSpan.FromHours(1))
                return;
            var json = await _etagCache.GetWithETagAsync(_http, ReleasesApiUrl).ConfigureAwait(false);
            if (json == null) return;
            var list = new List<Release>();
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    if (rel.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                    if (rel.TryGetProperty("prerelease", out var p) && p.GetBoolean()) continue;
                    if (ParseRelease(rel) is { } r) list.Add(r);
                }
            }
            if (list.Count == 0) return;
            _releases = list;
            File.WriteAllText(ReleasesCacheFile, JsonSerializer.Serialize(list));
            _crashReporter.Log($"[UpliftService.FetchAvailableVersions] {list.Count} versions (latest {list[0].Version})");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException or InvalidOperationException)
        {
            _crashReporter.Log($"[UpliftService.FetchAvailableVersions] Failed — {ex.Message}");
        }
    }

    private void LoadReleasesCache()
    {
        try
        {
            if (File.Exists(ReleasesCacheFile))
                _releases = JsonSerializer.Deserialize<List<Release>>(File.ReadAllText(ReleasesCacheFile)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        { _crashReporter.Log($"[UpliftService] Version cache unreadable — {ex.Message}"); }
    }

    private static Release? ParseRelease(JsonElement rel)
    {
        var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(tag)) return null;
        string? zip = null, sums = null;
        if (rel.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var n = a.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
                var u = a.TryGetProperty("browser_download_url", out var uu) ? uu.GetString() : null;
                if (u == null) continue;
                if (n.StartsWith("GITC-Uplift", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip ??= u;
                else if (n.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = u;
            }
        // Version strings become folder names: keep them to safe characters
        var version = new string(tag.TrimStart('v', 'V').Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return version.Length == 0 ? null : new Release(version, zip, sums);
    }

    private async Task<Release?> FetchLatestAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            return ParseRelease(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            _crashReporter.Log($"[UpliftService.FetchLatest] Failed — {ex.Message}");
            return null;
        }
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    /// <summary>Reads the latest release (ETag-cached) and sets <see cref="LatestVersion"/> / <see cref="HasUpdate"/>.</summary>
    public async Task CheckForUpdateAsync()
    {
        var latest = await FetchLatestAsync().ConfigureAwait(false);
        if (latest == null) return;
        LatestVersion = latest.Version;
        HasUpdate = !string.Equals(StagedVersion, latest.Version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[UpliftService.CheckForUpdate] Staged={StagedVersion ?? "(none)"}, Latest={latest.Version}, HasUpdate={HasUpdate}");
    }

    /// <summary>Downloads the latest release if it isn't staged yet. True when staging is ready afterwards.</summary>
    public async Task<bool> EnsureStagingAsync()
    {
        await _stagingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var latest = await FetchLatestAsync().ConfigureAwait(false);
            if (latest?.ZipUrl == null)
            {
                _crashReporter.Log("[UpliftService.EnsureStaging] Could not read the latest release");
                return IsStagingReady;
            }
            LatestVersion = latest.Version;
            if (IsStagingReady && string.Equals(StagedVersion, latest.Version, StringComparison.OrdinalIgnoreCase))
            {
                HasUpdate = false;
                return true;
            }
            var previousVersion = IsStagingReady ? StagedVersion : null;
            bool ok = await StageAsync(latest, StagingDir).ConfigureAwait(false);
            if (ok)
            {
                HasUpdate = false;
                UpdateHistory.Record("Neural Rendering", "GITC Uplift", previousVersion, latest.Version);
            }
            return ok || IsStagingReady;
        }
        finally { _stagingLock.Release(); }
    }

    /// <summary>Downloads a specific version for pinning (once; it never changes). Its folder, or null.</summary>
    public async Task<string?> EnsureVersionStagedAsync(string version)
    {
        var dir = VersionDir(version);
        if (IsReady(dir)) return dir;
        await _stagingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsReady(dir)) return dir;
            var rel = _releases.FirstOrDefault(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
            if (rel == null)
            {
                await FetchAvailableVersionsAsync(forceRefresh: true).ConfigureAwait(false);
                rel = _releases.FirstOrDefault(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
            }
            if (rel?.ZipUrl == null)
            {
                _crashReporter.Log($"[UpliftService.EnsureVersionStaged] v{version} not found in the release list");
                return null;
            }
            return await StageAsync(rel, dir).ConfigureAwait(false) ? dir : null;
        }
        finally { _stagingLock.Release(); }
    }

    /// <summary>Downloads, checks and extracts a release into <paramref name="targetDir"/> (replacing it).</summary>
    private async Task<bool> StageAsync(Release rel, string targetDir)
    {
        try
        {
            _crashReporter.Log($"[UpliftService.Stage] Downloading v{rel.Version}");
            var bytes = await _http.GetByteArrayAsync(rel.ZipUrl).ConfigureAwait(false);

            // Verify against SHA256SUMS.txt when the release has one
            if (!string.IsNullOrEmpty(rel.SumsUrl))
            {
                var sums = await _http.GetStringAsync(rel.SumsUrl).ConfigureAwait(false);
                var zipName = Path.GetFileName(new Uri(rel.ZipUrl!).LocalPath);
                var expected = sums.Split('\n')
                    .Select(l => l.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(p => p.Length == 2 && p[1].TrimStart('*').Trim().Equals(zipName, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p[0])
                    .FirstOrDefault();
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (expected != null && !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                {
                    _crashReporter.Log($"[UpliftService.Stage] SHA-256 mismatch for {zipName} — expected {expected}, got {actual}; not staged");
                    return false;
                }
                if (expected == null) _crashReporter.Log($"[UpliftService.Stage] {zipName} not listed in SHA256SUMS.txt — continuing without a hash check");
            }

            // Extract only the known files (matched by name, so archive paths can't escape the folder)
            var temp = targetDir + ".tmp";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    var known = StagedFiles.FirstOrDefault(f => f.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                    if (known == null) continue;
                    using var src = entry.Open();
                    using var dst = File.Create(Path.Combine(temp, known));
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }
            }
            if (!StagedFiles.All(f => File.Exists(Path.Combine(temp, f))))
            {
                // Upstream renamed something: never record a version without the files
                _crashReporter.Log($"[UpliftService.Stage] v{rel.Version} is missing expected files — not staged");
                Directory.Delete(temp, true);
                return false;
            }
            File.WriteAllText(Path.Combine(temp, "version.txt"), rel.Version);
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(temp, targetDir);
            _crashReporter.Log($"[UpliftService.Stage] Staged v{rel.Version} in '{targetDir}'");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            _crashReporter.Log($"[UpliftService.Stage] v{rel.Version} failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stages what a game should get: the pinned version, or Latest when <paramref name="pinned"/>
    /// is null (or the pinned one can't be had). The source folder and the version actually used.
    /// </summary>
    public async Task<(string? Dir, string? Pinned)> EnsureSourceAsync(string? pinned)
    {
        if (pinned != null && await EnsureVersionStagedAsync(pinned).ConfigureAwait(false) is { } dir)
            return (dir, pinned);
        if (pinned != null) _crashReporter.Log($"[UpliftService.EnsureSource] v{pinned} unavailable — using Latest");
        return await EnsureStagingAsync().ConfigureAwait(false) ? (StagingDir, null) : (null, null);
    }

    // ── Game folder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the addon (matching the game's bitness) and the helper from <paramref name="sourceDir"/>,
    /// the NR DLL, and the version marker into the game. A nvngx_dlssnr.dll the user put in
    /// GITC-Uplift\ is left alone; pass null <paramref name="nrDllSource"/> to keep the current DLL.
    /// Shaders are deployed by the shader sync (call DeployShadersForCard afterwards).
    /// </summary>
    public void Install(string installPath, bool is32Bit, string? nrDllSource, string sourceDir, string? pinned)
    {
        if (!IsReady(sourceDir)) throw new InvalidOperationException("GITC Uplift isn't downloaded");
        var addonDir = AddonDir(installPath);
        Directory.CreateDirectory(addonDir);
        var recorded = new List<string>(RhiInstallManifest.GetComponentFiles(installPath, Component));
        void Record(string rel) { if (!recorded.Contains(rel, StringComparer.OrdinalIgnoreCase)) recorded.Add(rel); }

        // The other bitness's addon from an earlier install (bitness override changed)
        var otherAddon = AddonFileFor(!is32Bit);
        if (recorded.Contains(otherAddon, StringComparer.OrdinalIgnoreCase))
        {
            TryDelete(Path.Combine(addonDir, otherAddon));
            recorded.RemoveAll(r => r.Equals(otherAddon, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var f in new[] { AddonFileFor(is32Bit), HelperExe })
        {
            File.Copy(Path.Combine(sourceDir, f), Path.Combine(addonDir, f), overwrite: true);
            Record(f);
        }
        foreach (var old in OldNames) TryDelete(Path.Combine(addonDir, old));

        var dllRel = Path.Combine(DllFolder, NrDllName);
        var dllDest = Path.Combine(addonDir, dllRel);
        Directory.CreateDirectory(Path.GetDirectoryName(dllDest)!);
        bool dllOurs = recorded.Contains(dllRel, StringComparer.OrdinalIgnoreCase);
        if (nrDllSource != null && File.Exists(nrDllSource) && (dllOurs || !File.Exists(dllDest)))
        {
            File.Copy(nrDllSource, dllDest, overwrite: true);
            Record(dllRel);
        }
        else if (File.Exists(dllDest) && !dllOurs)
            _crashReporter.Log($"[UpliftService.Install] Keeping the user's own {dllRel} in '{addonDir}'");

        var version = ReadVersion(sourceDir);
        var prev = ReadMarkerAll(installPath);
        WriteMarker(installPath, version, pinned, prev.Technique, prev.Launchpad);
        Record(Path.Combine(DllFolder, MarkerName));

        RhiInstallManifest.SetComponent(installPath, Component, recorded);
        _crashReporter.Log($"[UpliftService.Install] v{version}{(pinned != null ? " (pinned)" : "")} → '{addonDir}'");
    }

    /// <summary>Replaces the NR DLL in GITC-Uplift\ (RHI's copy, or none yet). False if the user's own copy is there.</summary>
    public bool SwapNrDll(string installPath, string nrDllSource)
    {
        var recorded = new List<string>(RhiInstallManifest.GetComponentFiles(installPath, Component));
        var dllRel = Path.Combine(DllFolder, NrDllName);
        var dest = NrDllPath(installPath);
        bool ours = recorded.Contains(dllRel, StringComparer.OrdinalIgnoreCase);
        if (File.Exists(dest) && !ours) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(nrDllSource, dest, overwrite: true);
        if (!ours) { recorded.Add(dllRel); RhiInstallManifest.SetComponent(installPath, Component, recorded); }
        return true;
    }

    /// <summary>
    /// Brings a Latest install up to the staged version (addon + helper; the NR DLL is the user's
    /// choice). Pinned installs are left alone. Files are compared by size and date, so nothing is
    /// written when current; a file the user deleted isn't put back. True if anything was copied.
    /// </summary>
    public bool UpdateInstalled(string installPath)
    {
        if (!IsStagingReady || !IsRhiInstalled(installPath)) return false;
        var (installed, pinned) = ReadMarker(installPath);
        if (pinned != null) return false;
        var addonDir = AddonDir(installPath);
        // Files that differ from the staged release
        var pending = new List<(string Src, string Dest)>();
        foreach (var rel in RhiInstallManifest.GetComponentFiles(installPath, Component))
        {
            var name = Path.GetFileName(rel);
            if (!StagedFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var dest = FileHelper.SafeCombine(addonDir, rel);
            if (dest == null || !File.Exists(dest)) continue;
            var src = new FileInfo(Path.Combine(StagingDir, name));
            var cur = new FileInfo(dest);
            if (cur.Length == src.Length && cur.LastWriteTimeUtc == src.LastWriteTimeUtc) continue;
            pending.Add((src.FullName, dest));
        }

        // All or nothing: the add-ons and the helper must come from the same release (from 2.0.0 they refuse
        // each other otherwise). A running game holds the add-on but not always the helper, so check every
        // file can be replaced before touching any; the next check tries again.
        foreach (var (_, dest) in pending)
        {
            try { using var probe = new FileStream(dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _crashReporter.Log($"[UpliftService.UpdateInstalled] '{dest}' is in use (game running?), nothing updated in '{addonDir}' — {ex.Message}");
                return false;
            }
        }
        bool changed = false, failed = false;
        foreach (var (src, dest) in pending)
        {
            try { File.Copy(src, dest, overwrite: true); changed = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed = true;
                _crashReporter.Log($"[UpliftService.UpdateInstalled] '{dest}' not updated — {ex.Message}");
            }
        }
        foreach (var old in OldNames) TryDelete(Path.Combine(addonDir, old));
        if (failed) return changed; // the marker keeps the old version; the next check copies the rest
        if (changed || installed != StagedVersion)
        {
            try { var m = ReadMarkerAll(installPath); WriteMarker(installPath, StagedVersion, null, m.Technique, m.Launchpad); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[UpliftService.UpdateInstalled] Marker not updated — {ex.Message}"); }
        }
        if (changed)
        {
            _crashReporter.Log($"[UpliftService.UpdateInstalled] Updated to v{StagedVersion} in '{addonDir}'");
            // Same component name as the staging entry, so the log's dedupe keeps one entry per version
            UpdateHistory.Record("Neural Rendering", "GITC Uplift", installed, StagedVersion);
        }
        return changed;
    }

    /// <summary>Deletes the files RHI placed (and the GITC-Uplift folder if that leaves it empty).</summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var addonDir = AddonDir(installPath);
        // Only a technique RHI added comes back out of the preset
        var marker = ReadMarkerAll(installPath);
        if (marker.Technique == "1") DisableTechnique(installPath);
        if (marker.Launchpad == "1") DisableTechnique(installPath, LaunchpadToken);
        foreach (var rel in RhiInstallManifest.GetComponentFiles(installPath, Component))
            if (FileHelper.SafeCombine(addonDir, rel) is { } path) TryDelete(path);
        var folder = Path.Combine(addonDir, DllFolder);
        try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[UpliftService.Uninstall] Couldn't remove '{folder}' — {ex.Message}"); }
        RhiInstallManifest.RemoveComponent(installPath, Component);
        _crashReporter.Log($"[UpliftService.Uninstall] Removed from '{addonDir}'");
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[UpliftService] Couldn't delete '{path}' — {ex.Message}"); }
    }
}
