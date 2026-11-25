// CrcgiService.cs -- CRCGI (Compensated Radiance Cascade GI, FlibbertigibbetLollygagger/CRCGI-Compensated-Radiance-Cascade-
// SSGI): a screen-space global illumination effect for ReShade (radiance cascades) plus its optional add-on, the CRCGI
// Injector, which hands the effect the game's own normals, albedo, motion, depth and field of view and keeps the UI and
// fog out of it. DirectX 10 / 11 / 12, Vulkan, OpenGL (DirectX 9 only through DXVK); 32 and 64-bit.
//
// Release zip (e.g. CRCGI.v2.zip, tags "v2"), flat:
//   crcgi_injector.addon64 / .addon32  → ReShade's add-on folder (GetAddonDeployPath), the game's bitness
//   CRCGI_v2.fx                        → game reshade-shaders\Shaders through the shader sync hook (ShaderFilesFor). The
//                                        file and technique are named after the major version (CRCGI_v2), so they're
//                                        read from the zip, not hard-coded. It includes ReShade.fxh, which the sync adds
//                                        when no shader pack gives the game one.
// The effect reads depth through ReShade's Generic Depth add-on (RHI's reshade.ini turns it off by default), and the
// "CRCGI_v2" technique is ticked in the game's preset at install, as for URTI.
// Files RHI placed are recorded in rhi_install.txt (component "Crcgi", relative to the add-on folder) with a marker
// (rhi-crcgi.txt: version=, effect=, technique=). Staging: %LocalAppData%\RHI\crcgi\ with version.txt + effect.txt.
// The repo has no licence file: RHI only downloads the author's own release.

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class CrcgiService
{
    private const string Repo         = "FlibbertigibbetLollygagger/CRCGI-Compensated-Radiance-Cascade-SSGI";
    private const string LatestApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public  const string RepoUrl      = "https://github.com/" + Repo;
    public  const string ReleasesUrl  = RepoUrl + "/releases";
    public  const string ReadmeUrl    = RepoUrl + "#readme";

    public const string Component  = "Crcgi";
    public const string Addon64    = "crcgi_injector.addon64";
    public const string Addon32    = "crcgi_injector.addon32";
    public const string MarkerName = "rhi-crcgi.txt";
    private const string EffectListName = "effect.txt";

    private readonly HttpClient      _http;
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter  _crashReporter;
    private readonly SemaphoreSlim   _stageLock = new(1, 1);

    private string? _latestTag;
    private string? _assetUrl;

    public static string StagingDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "crcgi");

    public CrcgiService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http          = http;
        _etagCache     = etagCache;
        _crashReporter = crashReporter;
    }

    public static string AddonName(bool is32Bit) => is32Bit ? Addon32 : Addon64;

    // ── Staging ───────────────────────────────────────────────────────────────

    private static string VersionFile => Path.Combine(StagingDir, "version.txt");

    /// <summary>The staged effect file ("CRCGI_v2.fx"), or null when nothing is staged.</summary>
    public static string? StagedEffect
    {
        get
        {
            try
            {
                var list = Path.Combine(StagingDir, EffectListName);
                var name = File.Exists(list) ? File.ReadAllText(list).Trim() : null;
                return !string.IsNullOrEmpty(name) && File.Exists(Path.Combine(StagingDir, name)) ? name : null;
            }
            catch (IOException) { return null; }
        }
    }

    public static bool IsStagingReady =>
        File.Exists(Path.Combine(StagingDir, Addon64)) && File.Exists(Path.Combine(StagingDir, Addon32)) && StagedEffect != null;

    /// <summary>Staged release ("2"), or null.</summary>
    public static string? StagedVersion
    {
        get
        {
            try { return IsStagingReady && File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null; }
            catch (IOException) { return null; }
        }
    }

    /// <summary>The staged effect's technique as a preset token ("CRCGI_v2@CRCGI_v2.fx"), or null.</summary>
    public static string? StagedTechniqueToken
    {
        get
        {
            var fx = StagedEffect;
            if (fx == null) return null;
            try { return TechniqueToken(File.ReadAllText(Path.Combine(StagingDir, fx)), fx); }
            catch (IOException) { return null; }
        }
    }

    /// <summary>The first technique declared in an effect's source as "Name@file.fx" (falls back to the file name). Pure.</summary>
    internal static string TechniqueToken(string fxSource, string fxFile)
    {
        var m = Regex.Match(fxSource, @"^\s*technique\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline);
        var name = m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(fxFile);
        return $"{name}@{fxFile}";
    }

    public string? LatestVersion => _latestTag == null ? null : VersionFromTag(_latestTag);
    public string? LastError { get; private set; }

    /// <summary>"v2" → "2", "v1.2" → "1.2".</summary>
    public static string VersionFromTag(string tag)
    {
        var t = tag.Trim();
        return t.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? t[1..] : t;
    }

    public bool HasUpdate => LatestVersion != null && IsStagingReady
        && !string.Equals(StagedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the latest release (ETag-cached). True when it has a CRCGI .zip asset.</summary>
    public async Task<bool> CheckForUpdateAsync(bool forceRefresh = false)
    {
        if (forceRefresh) _etagCache.Invalidate(LatestApiUrl);
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log("[CrcgiService.CheckForUpdate] Release request failed");
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
                    // By name: "CRCGI.v2.zip" (the name changes with the version)
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && name.Contains("CRCGI", StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u)) { url = u.GetString(); break; }
                }
            if (string.IsNullOrEmpty(tag) || url == null)
            {
                _crashReporter.Log($"[CrcgiService.CheckForUpdate] No CRCGI zip in the latest release (tag={tag ?? "null"})");
                return false;
            }
            _latestTag = tag; _assetUrl = url;
            _crashReporter.Log($"[CrcgiService.CheckForUpdate] Latest {VersionFromTag(tag)}; staged {StagedVersion ?? "(none)"}");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or IOException)
        {
            _crashReporter.Log($"[CrcgiService.CheckForUpdate] {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads and stages the latest release when it's missing or older; the staged copy is only replaced by a
    /// complete new one. True when staged afterwards.
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
                if (!IsStagingReady) LastError = "Couldn't find CRCGI's latest release on GitHub. Try again later.";
                return IsStagingReady;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StagingDir)!);
            var zipPath = StagingDir + ".zip.tmp";
            var newDir  = StagingDir + ".new";
            var previous = StagedVersion;
            try
            {
                using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _crashReporter.Log($"[CrcgiService.EnsureStaging] HTTP {(int)response.StatusCode} for {url}");
                        if (!IsStagingReady) LastError = $"Couldn't download CRCGI (HTTP {(int)response.StatusCode}).";
                        return IsStagingReady;
                    }
                    await using var net  = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                    await net.CopyToAsync(file).ConfigureAwait(false);
                }

                if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
                Directory.CreateDirectory(newDir);
                string? effect = null;
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    // The add-ons by name; the effect = the CRCGI*.fx in the zip (the newest version's, the shortest path wins)
                    foreach (var entry in zip.Entries)
                    {
                        var name = entry.Name;
                        if (string.IsNullOrEmpty(name)) continue;
                        bool addon = name.Equals(Addon64, StringComparison.OrdinalIgnoreCase) || name.Equals(Addon32, StringComparison.OrdinalIgnoreCase);
                        bool fx = name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) && name.StartsWith("CRCGI", StringComparison.OrdinalIgnoreCase);
                        if (!addon && !fx) continue;
                        if (fx && effect != null) continue; // one effect (the release has one)
                        if (FileHelper.SafeCombine(newDir, name) is { } dest)
                        {
                            entry.ExtractToFile(dest, overwrite: true);
                            if (fx) effect = name;
                        }
                    }
                }
                if (effect == null || !File.Exists(Path.Combine(newDir, Addon64)) || !File.Exists(Path.Combine(newDir, Addon32)))
                {
                    _crashReporter.Log($"[CrcgiService.EnsureStaging] {version} is missing files — keeping the current copy");
                    if (!IsStagingReady) LastError = "CRCGI's download is missing files. Try again later.";
                    return IsStagingReady;
                }
                File.WriteAllText(Path.Combine(newDir, EffectListName), effect);
                File.WriteAllText(Path.Combine(newDir, "version.txt"), version);
                if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true);
                Directory.Move(newDir, StagingDir);
                _crashReporter.Log($"[CrcgiService.EnsureStaging] Staged {version} ({effect}) (was {previous ?? "none"})");
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                _crashReporter.Log($"[CrcgiService.EnsureStaging] Failed — {ex.Message}");
                if (!IsStagingReady) LastError = $"Couldn't download CRCGI: {ex.Message}";
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
    private static string MarkerPath(string installPath) => Path.Combine(AddonDir(installPath), MarkerName);

    /// <summary>CRCGI's add-on (either bitness) is in this game's add-on folder (by RHI or by hand).</summary>
    public static bool IsInstalledIn(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return false;
        var dir = AddonDir(installPath);
        return File.Exists(Path.Combine(dir, Addon64)) || File.Exists(Path.Combine(dir, Addon32));
    }

    /// <summary>RHI installed CRCGI here (it has a record), as opposed to a manual install.</summary>
    public static bool IsRhiInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) && RhiInstallManifest.GetComponentFiles(installPath, Component).Count > 0;

    /// <summary>A value from the marker (version / effect / technique), or null.</summary>
    public static string? MarkerValue(string installPath, string key)
    {
        try
        {
            var path = MarkerPath(installPath);
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadAllLines(path))
                if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return line[(key.Length + 1)..].Trim() is { Length: > 0 } v ? v : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    public static string? InstalledVersion(string installPath) => MarkerValue(installPath, "version");

    private static void WriteMarker(string installPath) =>
        File.WriteAllText(MarkerPath(installPath),
            $"version={StagedVersion}\r\neffect={StagedEffect}\r\ntechnique={StagedTechniqueToken}\r\n");

    /// <summary>
    /// Copies the add-on of the game's bitness into the add-on folder (backing up a file of that name RHI didn't put
    /// there). The effect goes in through the shader sync (the caller runs DeployShadersForCardAsync).
    /// </summary>
    public void Install(string installPath, bool is32Bit)
    {
        if (!IsStagingReady) throw new InvalidOperationException("CRCGI isn't downloaded");
        var addonDir = AddonDir(installPath);
        Directory.CreateDirectory(addonDir);
        var addon = AddonName(is32Bit);
        var dest = Path.Combine(addonDir, addon);
        bool ours = RhiInstallManifest.GetComponentFiles(installPath, Component).Contains(addon, StringComparer.OrdinalIgnoreCase);
        if (!ours) AuxInstallService.SentinelBackupReplacingOwn(dest, existingIsRhiCopy: false);
        File.Copy(Path.Combine(StagingDir, addon), dest, overwrite: true);
        WriteMarker(installPath);
        RhiInstallManifest.SetComponent(installPath, Component, new[] { addon, MarkerName });
        _crashReporter.Log($"[CrcgiService.Install] {StagedVersion} ({addon}, {StagedEffect}) → '{addonDir}'");
    }

    /// <summary>
    /// Brings an RHI install up to the staged release (the add-on, when it differs). False when nothing changed or the
    /// game is running (the file is in use; tried again later). <paramref name="oldTechnique"/> = the marker's technique
    /// before the update, when a new major version renamed it (so the caller can tick the new one), else null.
    /// </summary>
    public bool UpdateInstalled(string installPath, out string? oldTechnique)
    {
        oldTechnique = null;
        if (!IsStagingReady || !IsRhiInstalled(installPath)) return false;
        var addon = RhiInstallManifest.GetComponentFiles(installPath, Component)
            .FirstOrDefault(f => f.Equals(Addon64, StringComparison.OrdinalIgnoreCase) || f.Equals(Addon32, StringComparison.OrdinalIgnoreCase));
        if (addon == null) return false;
        var dest = Path.Combine(AddonDir(installPath), addon);
        var src  = Path.Combine(StagingDir, addon);
        bool changed = false;
        try
        {
            if (!File.Exists(dest) || !VulkanLayerService.FilesIdentical(src, dest))
            {
                File.Copy(src, dest, overwrite: true);
                changed = true;
            }
            var before = MarkerValue(installPath, "technique");
            if (!string.Equals(InstalledVersion(installPath), StagedVersion, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(before, StagedTechniqueToken, StringComparison.Ordinal))
            {
                if (before != null && !string.Equals(before, StagedTechniqueToken, StringComparison.Ordinal)) oldTechnique = before;
                WriteMarker(installPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[CrcgiService.UpdateInstalled] '{dest}' not updated (game running?) — {ex.Message}");
            return false;
        }
        if (changed) _crashReporter.Log($"[CrcgiService.UpdateInstalled] Updated to {StagedVersion} in '{installPath}'");
        return changed;
    }

    /// <summary>Removes what RHI placed (the add-on, restoring a file it replaced, and the marker). The effect leaves with the next shader sync.</summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var addonDir = AddonDir(installPath);
        foreach (var rel in RhiInstallManifest.GetComponentFiles(installPath, Component))
            if (FileHelper.SafeCombine(addonDir, rel) is { } path)
            {
                TryDelete(path);
                if (rel.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
                    AuxInstallService.SentinelRestore(path);
            }
        RhiInstallManifest.RemoveComponent(installPath, Component);
        _crashReporter.Log($"[CrcgiService.Uninstall] Removed from '{addonDir}'");
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[CrcgiService] Couldn't delete '{path}' — {ex.Message}"); }
    }

    // ── Shaders (shader sync hook) ────────────────────────────────────────────

    /// <summary>
    /// The effect the game's reshade-shaders folder keeps while RHI's CRCGI is installed there (the staged one, else the
    /// one the marker names, so the sync keeps the game's own copy instead of deleting it).
    /// </summary>
    public static IReadOnlyList<(string Rel, string Source)> ShaderFilesFor(string gameDir)
    {
        if (!IsRhiInstalled(gameDir)) return Array.Empty<(string, string)>();
        var fx = StagedEffect ?? MarkerValue(gameDir, "effect");
        if (string.IsNullOrEmpty(fx)) return Array.Empty<(string, string)>();
        return new[] { (Path.Combine("Shaders", fx), Path.Combine(StagingDir, fx)) };
    }
}
