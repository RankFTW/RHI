// UrtiService.cs — URTI (Universal Ray Tracing Injector, Skynizz/URTI): a ReShade add-on that adds ray-traced
// reflections (DXR 1.1 inline ray queries) to DirectX 12 games that have none. 64-bit only.
//
// Release zip layout (URTI.zip, tags "v108"):
//   URTI\urti.addon64                       → ReShade's add-on folder (GetAddonDeployPath)
//   URTI\urti.ini                           → same folder: URTI reads it next to its own add-on (the release's copy
//                                             is the Red Dead Redemption 2 profile, which the README says to start
//                                             from in other games too). An existing urti.ini is the player's: kept.
//   URTI\reshade-shaders\Shaders\urti.fx, urti_depth.fx, ReShade.fxh, ReShadeUI.fxh
//                                           → game reshade-shaders\Shaders through the shader sync hook (urti.fx is the
//                                             "URTI" checkbox that switches it on; urti_depth.fx asks ReShade for depth).
//                                             The .fxh are only added when no shader pack already gives the game one.
// URTI writes urti_log.txt (and F8 measurement bursts) next to the add-on.
// Files RHI placed are recorded in rhi_install.txt (component "Urti", paths relative to the add-on folder), with
// a marker (rhi-urti.txt: version=) for updates. Staging: %LocalAppData%\RHI\urti\ with version.txt.

using System.IO.Compression;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class UrtiService
{
    private const string LatestApiUrl = "https://api.github.com/repos/Skynizz/URTI/releases/latest";
    public  const string RepoUrl      = "https://github.com/Skynizz/URTI";
    public  const string ReleasesUrl  = RepoUrl + "/releases";
    private const string AssetName    = "URTI.zip";

    public const string Component  = "Urti";
    public const string Addon64    = "urti.addon64";
    public const string IniName    = "urti.ini";
    public const string MarkerName = "rhi-urti.txt";
    public const string LogName    = "urti_log.txt";
    /// <summary>The "URTI" effect in a ReShade preset (Techniques=): ticking it is what draws the reflections.</summary>
    public const string TechniqueToken = "URTI@urti.fx";

    /// <summary>Effects URTI needs in the game's reshade-shaders\Shaders (always deployed while it's installed).</summary>
    public static readonly string[] Effects  = { "urti.fx", "urti_depth.fx" };
    /// <summary>Headers the effects include: deployed only when no shader pack already provides them.</summary>
    public static readonly string[] Headers  = { "ReShade.fxh", "ReShadeUI.fxh" };

    private static readonly string[] StagedFiles = new[] { Addon64, IniName }.Concat(Effects).Concat(Headers).ToArray();

    private readonly HttpClient      _http;
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter  _crashReporter;
    private readonly SemaphoreSlim   _stageLock = new(1, 1);

    private string? _latestTag;
    private string? _assetUrl;

    public static string StagingDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "urti");

    public UrtiService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http          = http;
        _etagCache     = etagCache;
        _crashReporter = crashReporter;
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    private static string VersionFile => Path.Combine(StagingDir, "version.txt");

    public bool IsStagingReady => StagedFiles.All(f => File.Exists(Path.Combine(StagingDir, f)));

    /// <summary>Staged release number ("108"), or null.</summary>
    public string? StagedVersion
    {
        get
        {
            try { return IsStagingReady && File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null; }
            catch (IOException) { return null; }
        }
    }

    public string? LatestVersion => _latestTag == null ? null : VersionFromTag(_latestTag);

    /// <summary>Why the last download failed, for the UI (null after a success).</summary>
    public string? LastError { get; private set; }

    /// <summary>"v108" → "108".</summary>
    public static string VersionFromTag(string tag)
    {
        var t = tag.Trim();
        return t.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? t[1..] : t;
    }

    public bool HasUpdate => LatestVersion != null && IsStagingReady
        && !string.Equals(StagedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the latest release (ETag-cached). True when it has the URTI.zip asset.</summary>
    public async Task<bool> CheckForUpdateAsync(bool forceRefresh = false)
    {
        if (forceRefresh) _etagCache.Invalidate(LatestApiUrl);
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log("[UrtiService.CheckForUpdate] Release request failed");
                return false;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u)) url = u.GetString();
                }
            if (string.IsNullOrEmpty(tag) || url == null)
            {
                _crashReporter.Log($"[UrtiService.CheckForUpdate] No {AssetName} in the latest release (tag={tag ?? "null"})");
                return false;
            }
            _latestTag = tag; _assetUrl = url;
            _crashReporter.Log($"[UrtiService.CheckForUpdate] Latest {VersionFromTag(tag)}; staged {StagedVersion ?? "(none)"}");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or IOException)
        {
            _crashReporter.Log($"[UrtiService.CheckForUpdate] {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads and stages the latest release when it's missing or older. A no-op when staged and current; the staged
    /// copy is only replaced by a complete new one. True when staged afterwards.
    /// </summary>
    public async Task<bool> EnsureStagingAsync()
    {
        await _stageLock.WaitAsync().ConfigureAwait(false);
        try
        {
            LastError = null;
            if (IsStagingReady && !HasUpdate) return true;
            if (_latestTag == null) await CheckForUpdateAsync().ConfigureAwait(false);
            var url = _assetUrl;
            var version = LatestVersion;
            if (url == null || version == null)
            {
                if (!IsStagingReady) LastError = "Couldn't find URTI's latest release on GitHub. Try again later.";
                return IsStagingReady;
            }

            var root    = Path.GetDirectoryName(StagingDir)!;
            Directory.CreateDirectory(root);
            var zipPath = StagingDir + ".zip.tmp";
            var newDir  = StagingDir + ".new";
            var previous = StagedVersion;
            try
            {
                using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _crashReporter.Log($"[UrtiService.EnsureStaging] HTTP {(int)response.StatusCode} for {url}");
                        if (!IsStagingReady) LastError = $"Couldn't download URTI (HTTP {(int)response.StatusCode}).";
                        return IsStagingReady;
                    }
                    await using var net  = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                    await net.CopyToAsync(file).ConfigureAwait(false);
                }

                if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
                Directory.CreateDirectory(newDir);
                var wanted = new HashSet<string>(StagedFiles, StringComparer.OrdinalIgnoreCase);
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    // Everything sits under URTI\ (the effects under reshade-shaders\Shaders): take the files by name
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || !wanted.Contains(entry.Name)) continue;
                        if (FileHelper.SafeCombine(newDir, entry.Name) is { } dest) entry.ExtractToFile(dest, overwrite: true);
                    }
                }
                if (!StagedFiles.All(f => File.Exists(Path.Combine(newDir, f))))
                {
                    _crashReporter.Log($"[UrtiService.EnsureStaging] {version} is missing files — keeping the current copy");
                    if (!IsStagingReady) LastError = "URTI's download is missing files. Try again later.";
                    return IsStagingReady;
                }
                File.WriteAllText(Path.Combine(newDir, "version.txt"), version);
                if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true);
                Directory.Move(newDir, StagingDir);
                _crashReporter.Log($"[UrtiService.EnsureStaging] Staged {version} (was {previous ?? "none"})");
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                _crashReporter.Log($"[UrtiService.EnsureStaging] Failed — {ex.Message}");
                if (!IsStagingReady) LastError = $"Couldn't download URTI: {ex.Message}";
                return IsStagingReady;
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (IOException) { }
                try { if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true); } catch (IOException) { }
            }
        }
        finally { _stageLock.Release(); }
    }

    // ── Game folder ───────────────────────────────────────────────────────────

    public static string AddonDir(string installPath) => ModInstallService.GetAddonDeployPath(installPath);
    public static string IniPath(string installPath) => Path.Combine(AddonDir(installPath), IniName);
    private static string MarkerPath(string installPath) => Path.Combine(AddonDir(installPath), MarkerName);

    /// <summary>URTI's add-on is in this game's add-on folder (by RHI or by hand).</summary>
    public static bool IsInstalledIn(string installPath) =>
        !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(AddonDir(installPath), Addon64));

    /// <summary>RHI installed URTI here (it has a record), as opposed to a manual install.</summary>
    public static bool IsRhiInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) && RhiInstallManifest.GetComponentFiles(installPath, Component).Count > 0;

    /// <summary>Installed release number from the marker, or null.</summary>
    public static string? InstalledVersion(string installPath)
    {
        try
        {
            var path = MarkerPath(installPath);
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadAllLines(path))
                if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    return line[8..].Trim() is { Length: > 0 } v ? v : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>
    /// Copies urti.addon64 into the add-on folder (backing up a file of that name RHI didn't put there) and urti.ini
    /// when the game has none (an existing one is the player's settings). Effects go in through the shader sync
    /// (the caller runs DeployShadersForCardAsync).
    /// </summary>
    public void Install(string installPath)
    {
        if (!IsStagingReady) throw new InvalidOperationException("URTI isn't downloaded");
        var addonDir = AddonDir(installPath);
        Directory.CreateDirectory(addonDir);
        bool ours = IsRhiInstalled(installPath);
        var recorded = new List<string> { Addon64, MarkerName };

        var dest = Path.Combine(addonDir, Addon64);
        if (!ours) AuxInstallService.SentinelBackupReplacingOwn(dest, existingIsRhiCopy: false);
        File.Copy(Path.Combine(StagingDir, Addon64), dest, overwrite: true);

        // urti.ini is recorded only when RHI wrote it, so removing URTI never deletes a player's own file
        var ini = Path.Combine(addonDir, IniName);
        bool iniOurs = RhiInstallManifest.GetComponentFiles(installPath, Component).Contains(IniName, StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(ini)) { File.Copy(Path.Combine(StagingDir, IniName), ini); iniOurs = true; }
        if (iniOurs) recorded.Add(IniName);

        File.WriteAllText(MarkerPath(installPath), $"version={StagedVersion}");
        RhiInstallManifest.SetComponent(installPath, Component, recorded);
        _crashReporter.Log($"[UrtiService.Install] {StagedVersion} → '{addonDir}' (urti.ini {(iniOurs ? "RHI's" : "the player's, kept")})");
    }

    /// <summary>
    /// Brings an RHI install up to the staged release: replaces urti.addon64 when it differs (urti.ini is left as the
    /// player has it). False when nothing changed or the game is running (the file is in use; tried again later).
    /// </summary>
    public bool UpdateInstalled(string installPath)
    {
        if (!IsStagingReady || !IsRhiInstalled(installPath)) return false;
        var dest = Path.Combine(AddonDir(installPath), Addon64);
        var src  = Path.Combine(StagingDir, Addon64);
        bool changed = false;
        try
        {
            if (!File.Exists(dest) || !VulkanLayerService.FilesIdentical(src, dest))
            {
                File.Copy(src, dest, overwrite: true);
                changed = true;
            }
            if (!string.Equals(InstalledVersion(installPath), StagedVersion, StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(MarkerPath(installPath), $"version={StagedVersion}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[UrtiService.UpdateInstalled] '{dest}' not updated (game running?) — {ex.Message}");
            return false;
        }
        if (changed) _crashReporter.Log($"[UrtiService.UpdateInstalled] Updated to {StagedVersion} in '{installPath}'");
        return changed;
    }

    /// <summary>
    /// Removes what RHI placed (the add-on, restoring a file it replaced; urti.ini if RHI wrote it; the marker) and
    /// URTI's log. The effects leave with the next shader sync (the caller runs it).
    /// </summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var addonDir = AddonDir(installPath);
        foreach (var rel in RhiInstallManifest.GetComponentFiles(installPath, Component))
            if (FileHelper.SafeCombine(addonDir, rel) is { } path)
            {
                TryDelete(path);
                if (rel.Equals(Addon64, StringComparison.OrdinalIgnoreCase)) AuxInstallService.SentinelRestore(path);
            }
        TryDelete(Path.Combine(addonDir, LogName));
        RhiInstallManifest.RemoveComponent(installPath, Component);
        _crashReporter.Log($"[UrtiService.Uninstall] Removed from '{addonDir}'");
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[UrtiService] Couldn't delete '{path}' — {ex.Message}"); }
    }

    // ── Shaders (shader sync hook) ────────────────────────────────────────────

    /// <summary>
    /// Shader files the game's reshade-shaders folder must keep while RHI's URTI is installed there. Listed even when not
    /// staged yet: the shader sync then keeps the game's own copy instead of deleting it.
    /// </summary>
    public static IReadOnlyList<(string Rel, string Source)> ShaderFilesFor(string gameDir)
    {
        if (!IsRhiInstalled(gameDir)) return Array.Empty<(string, string)>();
        return Effects.Concat(Headers)
            .Select(f => (Path.Combine("Shaders", f), Path.Combine(StagingDir, f)))
            .ToList();
    }

    // ── urti.ini (key=value lines, no sections) ───────────────────────────────
    // URTI reads urti.ini at game start, and re-reads it every 30 frames while the game runs when the file changes
    // (urti_live.inl LiveHotReload). live, calibration_interval and capture_presents are only read at start.

    /// <summary>The value of <paramref name="key"/> (the first "key=" line, as URTI reads it), or null when absent.</summary>
    public static string? GetValueFromText(string text, string key)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ', '\t');
            if (line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..];
        }
        return null;
    }

    /// <summary>
    /// Text with <paramref name="key"/> set to <paramref name="value"/>: the first "key=" line is replaced (later duplicates
    /// removed), or a line is added at the end. A null value removes the key (URTI then uses its own default). Line
    /// endings and every other line are kept.
    /// </summary>
    public static string SetValueInText(string text, string key, string? value)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Length == 0 ? new List<string>() : text.Split(nl).ToList();
        bool endsWithBreak = lines.Count > 0 && lines[^1].Length == 0;
        if (endsWithBreak) lines.RemoveAt(lines.Count - 1);
        bool done = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith(key + "=", StringComparison.Ordinal)) continue;
            if (done || value == null) { lines.RemoveAt(i--); continue; }
            lines[i] = $"{key}={value}";
            done = true;
        }
        if (!done && value != null) lines.Add($"{key}={value}");
        return string.Join(nl, lines) + (lines.Count > 0 ? nl : "");
    }

    /// <summary>A key's value in the game's urti.ini, or null (missing key or file).</summary>
    public static string? GetSetting(string installPath, string key)
    {
        try
        {
            var ini = IniPath(installPath);
            return File.Exists(ini) ? GetValueFromText(File.ReadAllText(ini), key) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes a key into the game's urti.ini (null removes it). False when the file can't be written.</summary>
    public bool SetSetting(string installPath, string key, string? value)
    {
        var ini = IniPath(installPath);
        try
        {
            var text = File.Exists(ini) ? File.ReadAllText(ini) : "";
            var updated = SetValueInText(text, key, value);
            if (updated == text) return true;
            File.WriteAllText(ini, updated);
            _crashReporter.Log($"[UrtiService.SetSetting] {key}={value ?? "(removed)"} in '{ini}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[UrtiService.SetSetting] {key} not written to '{ini}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>A key's value in the release's urti.ini (the profile RHI installs), or null.</summary>
    public static string? GetProfileValue(string key)
    {
        try
        {
            var ini = Path.Combine(StagingDir, IniName);
            return File.Exists(ini) ? GetValueFromText(File.ReadAllText(ini), key) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
