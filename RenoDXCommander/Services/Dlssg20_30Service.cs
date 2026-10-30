using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the 20/30 FG Unlock component (dlssg_for_sm86, GitHub: sdli1995/dlssg_for_sm86).
///
/// Since 0.4.0 the mod is a package, not a single DLL. A game needs, next to its rendering EXE:
///   • exactly one proxy DLL — version.dll (first choice) or one of alternatives\*.dll.
///     Each name is its own build that forwards to the system DLL of the same name,
///     so a proxy must never be renamed to another name;
///   • dlssg_sm86.ini;
///   • the dlssg_sm86\ folder (backend, kernel packs, bundled DLSS-G runtime, XeSS-FG, SR).
///
/// The release zip is staged under %LocalAppData%\RHI\dlssg2030\pkg (docs and readmes skipped).
/// Every file deployed into dlssg_sm86\ is listed in dlssg_sm86\rhi_install.txt so updates can
/// remove files a new release dropped; uninstall removes the whole dlssg_sm86\ folder (the mod's
/// own logs live there too) plus the ini, and restores the proxy slot via the sentinel pattern.
/// </summary>
public class Dlssg20_30Service
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string RepoOwner     = "sdli1995";
    private const string RepoName      = "dlssg_for_sm86";
    private const string LatestApiUrl  = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public const string IniFileName    = "dlssg_sm86.ini";
    public const string ModFolderName  = "dlssg_sm86";
    public const string LogRelPath     = @"dlssg_sm86\logs\dlssg_sm86.log";
    private const string TrackingFile  = "rhi_install.txt";

    /// <summary>
    /// Proxy names in the order the mod's README says to try them. version.dll ships at the
    /// package root; the rest come from alternatives\. dxgi/d3d12 are render-path proxies
    /// (last resort, never both, not loaded by Vulkan games).
    /// </summary>
    public static readonly string[] ProxyNames =
    {
        "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll",
    };

    /// <summary>Files that must be staged for an install to work.</summary>
    private static readonly string[] RequiredModFiles =
    {
        @"backend\sm86_backend.dll", @"kernels\fg_kernels.dll", @"runtime\nvngx_dlssg.dll",
    };

    // ── Instance state ─────────────────────────────────────────────────────────

    private readonly HttpClient      _http;
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter  _crashReporter;
    private readonly string          _stagingDir;
    private readonly string          _pkgDir;
    private readonly string          _versionFile;
    private string?                  _latestAssetUrl;

    public Dlssg20_30Service(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http          = http;
        _etagCache     = etagCache;
        _crashReporter = crashReporter;
        _stagingDir    = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "dlssg2030");
        _pkgDir        = Path.Combine(_stagingDir, "pkg");
        _versionFile   = Path.Combine(_stagingDir, "version.txt");
    }

    public bool IsStagingReady =>
        File.Exists(Path.Combine(_pkgDir, "version.dll")) &&
        File.Exists(Path.Combine(_pkgDir, IniFileName)) &&
        RequiredModFiles.All(f => File.Exists(Path.Combine(_pkgDir, ModFolderName, f)));

    /// <summary>Release tag of the staged package (e.g. "0.4.1"), or null if not staged.</summary>
    public string? StagedVersion => IsStagingReady && File.Exists(_versionFile)
        ? File.ReadAllText(_versionFile).Trim() : null;

    public bool     HasUpdate     { get; private set; }
    public string?  LatestVersion { get; private set; }

    // ── Detection ─────────────────────────────────────────────────────────────

    public bool IsInstalledIn(string installPath, string? installedAs)
        => !string.IsNullOrEmpty(installPath) && !string.IsNullOrEmpty(installedAs)
        && File.Exists(Path.Combine(installPath, installedAs));

    public static bool IsSupportedProxyName(string? name)
        => !string.IsNullOrEmpty(name) && ProxyNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task CheckForUpdateAsync()
    {
        var tag = await FetchLatestReleaseAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(tag)) return;

        LatestVersion = tag;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, tag, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[Dlssg20_30Service.CheckForUpdate] Staged={current ?? "(none)"}, Latest={tag}, HasUpdate={HasUpdate}");
    }

    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[Dlssg20_30Service.EnsureStaging] Already up to date");
            return;
        }

        Directory.CreateDirectory(_stagingDir);

        if (_latestAssetUrl == null || LatestVersion == null)
            await FetchLatestReleaseAsync().ConfigureAwait(false);
        var tag = LatestVersion;
        var url = _latestAssetUrl;
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url))
        {
            _crashReporter.Log("[Dlssg20_30Service.EnsureStaging] Could not resolve the latest release package");
            return;
        }

        var previous = File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;
        var zipPath  = Path.Combine(_stagingDir, "package.zip.tmp");
        var newPkg   = Path.Combine(_stagingDir, "pkg.new");
        try
        {
            progress?.Report(("Downloading 20/30 FG Unlock...", 10));
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] HTTP {(int)response.StatusCode} for {url}");
                    return;
                }
                await using var net  = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 1024 * 1024, useAsync: true);
                await net.CopyToAsync(file).ConfigureAwait(false);
            }

            progress?.Report(("Extracting 20/30 FG Unlock...", 70));
            if (Directory.Exists(newPkg)) Directory.Delete(newPkg, recursive: true);
            Directory.CreateDirectory(newPkg);
            int extracted = 0;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                    if (!IsRuntimeFile(entry.FullName)) continue;
                    var dest = FileHelper.SafeCombine(newPkg, entry.FullName);
                    if (dest == null)
                    {
                        _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Skipped unsafe entry '{entry.FullName}'");
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                    extracted++;
                }
            }

            // Validate before swapping in, so a bad download never replaces a working package
            bool valid = File.Exists(Path.Combine(newPkg, "version.dll"))
                && File.Exists(Path.Combine(newPkg, IniFileName))
                && RequiredModFiles.All(f => File.Exists(Path.Combine(newPkg, ModFolderName, f)));
            if (!valid)
            {
                _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Package {tag} is missing required files — keeping the current one");
                Directory.Delete(newPkg, recursive: true);
                return;
            }

            if (Directory.Exists(_pkgDir)) Directory.Delete(_pkgDir, recursive: true);
            Directory.Move(newPkg, _pkgDir);
            File.WriteAllText(_versionFile, tag);
            CleanupLegacyStaging();
            LatestVersion = tag;
            HasUpdate = false;
            _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Staged {tag} ({extracted} files)");
            progress?.Report(("20/30 FG Unlock ready", 100));

            try
            {
                App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
                {
                    Timestamp     = DateTime.UtcNow,
                    Category      = "20/30 FG Unlock",
                    ComponentName = "20/30 FG Unlock",
                    OldVersion    = previous,
                    NewVersion    = tag,
                    SizeBytes     = new FileInfo(zipPath).Length,
                });
            }
            catch (Exception ex) { _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Update log failed — {ex.Message}"); }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Failed — {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); }
            catch (IOException ex) { _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Could not delete temp zip — {ex.Message}"); }
            try { if (Directory.Exists(newPkg)) Directory.Delete(newPkg, recursive: true); }
            catch (IOException ex) { _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Could not delete temp folder — {ex.Message}"); }
        }
    }

    /// <summary>
    /// True for the files a game needs: the root proxy, the ini, the alternative proxies and
    /// everything under dlssg_sm86\ (except its logs). Docs, readmes and build metadata are skipped.
    /// </summary>
    private static bool IsRuntimeFile(string entryPath)
    {
        var p = entryPath.Replace('\\', '/');
        if (p.Equals("version.dll", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Equals(IniFileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.StartsWith("alternatives/", StringComparison.OrdinalIgnoreCase))
            return p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        if (p.StartsWith(ModFolderName + "/", StringComparison.OrdinalIgnoreCase))
            return !p.StartsWith(ModFolderName + "/logs/", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>Removes the pre-0.4.0 staging layout (loose version.dll + ini in the staging root).</summary>
    private void CleanupLegacyStaging()
    {
        foreach (var name in new[] { "version.dll", IniFileName })
        {
            var p = Path.Combine(_stagingDir, name);
            try { if (File.Exists(p)) File.Delete(p); }
            catch (IOException ex) { _crashReporter.Log($"[Dlssg20_30Service.CleanupLegacyStaging] {name} — {ex.Message}"); }
        }
    }

    private string? StagedProxyPath(string dllName)
        => dllName.Equals("version.dll", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_pkgDir, "version.dll")
            : Path.Combine(_pkgDir, "alternatives", dllName);

    // ── Install / Update ──────────────────────────────────────────────────────

    /// <summary>
    /// Deploys the proxy for <paramref name="dllName"/>, the ini and the dlssg_sm86\ folder.
    /// An existing dlssg_sm86.ini keeps the settings RHI manages (see <see cref="ManagedKeys"/>).
    /// </summary>
    public bool Install(string installPath, string dllName)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return false;
        if (!IsSupportedProxyName(dllName))
        {
            _crashReporter.Log($"[Dlssg20_30Service.Install] '{dllName}' is not a supported proxy name");
            return false;
        }
        try
        {
            var source = StagedProxyPath(dllName);
            if (source == null || !File.Exists(source))
            {
                _crashReporter.Log($"[Dlssg20_30Service.Install] Staged proxy '{dllName}' not found");
                return false;
            }

            var dllDest = Path.Combine(installPath, dllName);
            AuxInstallService.SentinelBackup(dllDest);
            File.Copy(source, dllDest, overwrite: true);

            DeployIni(installPath);
            DeployModFolder(installPath);

            _crashReporter.Log($"[Dlssg20_30Service.Install] Deployed {StagedVersion} as '{dllName}' to '{installPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.Install] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Brings an installed game up to the staged package. Returns the proxy name now in use
    /// (it changes only when the old name is no longer supported, e.g. pre-0.4.0 "winhttp.dll"),
    /// or null on failure.
    /// </summary>
    public string? Update(string installPath, string installedAs)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return null;
        try
        {
            var name = installedAs;
            if (!IsSupportedProxyName(name))
            {
                // Old installs renamed version.dll to any name; that name has no matching build now
                AuxInstallService.SentinelRestore(Path.Combine(installPath, installedAs));
                name = "version.dll";
                _crashReporter.Log($"[Dlssg20_30Service.Update] '{installedAs}' is no longer supported — switching to {name}");
                return Install(installPath, name) ? name : null;
            }

            var dllDest = Path.Combine(installPath, name);
            File.Copy(StagedProxyPath(name)!, dllDest, overwrite: true);
            DeployIni(installPath);
            DeployModFolder(installPath);
            _crashReporter.Log($"[Dlssg20_30Service.Update] Updated '{name}' in '{installPath}' to {StagedVersion}");
            return name;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.Update] Failed for '{installPath}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// True when the game's copy differs from the staged package: proxy, mod folder files or
    /// tracking list. Catches installs from before 0.4.0 (no dlssg_sm86\ folder) and games that
    /// missed an update (e.g. running at the time).
    /// </summary>
    public bool NeedsUpdate(string installPath, string installedAs)
    {
        if (!IsStagingReady || string.IsNullOrEmpty(installPath)) return false;
        if (!IsSupportedProxyName(installedAs)) return true;
        try
        {
            if (!SameFile(StagedProxyPath(installedAs)!, Path.Combine(installPath, installedAs))) return true;
            var stagedMod = Path.Combine(_pkgDir, ModFolderName);
            var gameMod   = Path.Combine(installPath, ModFolderName);
            foreach (var src in Directory.EnumerateFiles(stagedMod, "*", SearchOption.AllDirectories))
                if (!SameFile(src, Path.Combine(gameMod, Path.GetRelativePath(stagedMod, src)))) return true;
            return !File.Exists(Path.Combine(gameMod, TrackingFile));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.NeedsUpdate] {installPath} — {ex.Message}");
            return false;
        }
    }

    private static bool SameFile(string a, string b)
    {
        if (!File.Exists(a) || !File.Exists(b)) return false;
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        return fa.Length == fb.Length && fa.LastWriteTimeUtc == fb.LastWriteTimeUtc;
    }

    /// <summary>
    /// Copies dlssg_sm86\ into the game (only changed files), removes files an earlier RHI
    /// deploy placed that the new package no longer has, and rewrites the tracking list.
    /// </summary>
    private void DeployModFolder(string installPath)
    {
        var stagedMod = Path.Combine(_pkgDir, ModFolderName);
        var gameMod   = Path.Combine(installPath, ModFolderName);
        Directory.CreateDirectory(gameMod);

        var trackingPath = Path.Combine(gameMod, TrackingFile);
        var previous = File.Exists(trackingPath)
            ? File.ReadAllLines(trackingPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
            : new List<string>();

        var deployed = new List<string>();
        foreach (var src in Directory.EnumerateFiles(stagedMod, "*", SearchOption.AllDirectories))
        {
            var rel  = Path.GetRelativePath(stagedMod, src);
            var dest = Path.Combine(gameMod, rel);
            if (!SameFile(src, dest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
            }
            deployed.Add(rel);
        }

        foreach (var rel in previous.Except(deployed, StringComparer.OrdinalIgnoreCase))
        {
            var stale = FileHelper.SafeCombine(gameMod, rel);
            if (stale == null) continue;
            try { if (File.Exists(stale)) File.Delete(stale); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[Dlssg20_30Service.DeployModFolder] Could not remove stale '{rel}' — {ex.Message}"); }
        }

        File.WriteAllLines(trackingPath, deployed);
    }

    /// <summary>
    /// Copies the release's dlssg_sm86.ini, then writes back the user's values for the keys RHI
    /// manages (only valid values), so updates pick up new defaults without losing choices.
    /// </summary>
    private void DeployIni(string installPath)
    {
        var dest = Path.Combine(installPath, IniFileName);
        var kept = new List<(string Section, string Key, string Value)>();
        if (File.Exists(dest))
        {
            foreach (var k in ManagedKeys)
            {
                var v = ReadIniValue(dest, k.Section, k.Key);
                if (v != null && k.Allowed.Contains(v, StringComparer.OrdinalIgnoreCase))
                    kept.Add((k.Section, k.Key, v));
            }
        }
        File.Copy(Path.Combine(_pkgDir, IniFileName), dest, overwrite: true);
        foreach (var (section, key, value) in kept)
            WriteIniValue(dest, section, key, value);
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public void Uninstall(string installPath, string? installedAs)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        try
        {
            if (!string.IsNullOrEmpty(installedAs))
                AuxInstallService.SentinelRestore(Path.Combine(installPath, installedAs));

            var iniPath = Path.Combine(installPath, IniFileName);
            try { if (File.Exists(iniPath)) File.Delete(iniPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[Dlssg20_30Service.Uninstall] Failed to delete '{IniFileName}' — {ex.Message}"); }

            // The whole folder belongs to the mod (its logs too), as the mod's README describes
            var modDir = Path.Combine(installPath, ModFolderName);
            try { if (Directory.Exists(modDir)) Directory.Delete(modDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[Dlssg20_30Service.Uninstall] Failed to delete '{ModFolderName}' — {ex.Message}"); }

            _crashReporter.Log($"[Dlssg20_30Service.Uninstall] Removed from '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.Uninstall] Failed — {ex.Message}");
        }
    }

    // ── Settings (dlssg_sm86.ini) ─────────────────────────────────────────────

    public sealed record IniKey(string Section, string Key, string[] Allowed);

    /// <summary>Keys exposed in the ⚙ dialog and preserved across updates, with their valid values.</summary>
    public static readonly IniKey[] ManagedKeys =
    {
        new("General",          "Enabled",            new[] { "0", "1" }),
        new("FrameGeneration",  "Engine",             new[] { "DLSS", "XeSS" }),
        new("FrameGeneration",  "MaxGeneratedFrames", new[] { "0", "1", "2", "3", "4", "5" }),
        new("DLSSG",            "Preset",             new[] { "A", "B", "C" }),
        new("DLSSG",            "Optimized",          new[] { "0", "1", "2", "3" }),
        new("SuperResolution",  "Mode",               new[] { "Off", "M-INT8", "L-INT8", "M-FP16", "L-FP16" }),
    };

    /// <summary>Reads a value from <c>[section] key=value</c>, or null if absent.</summary>
    public static string? ReadIniValue(string iniPath, string section, string key)
    {
        if (!File.Exists(iniPath)) return null;
        bool inSection = false;
        foreach (var raw in File.ReadAllLines(iniPath))
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || line.StartsWith(";", StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(eq + 1)..];
                var comment = value.IndexOf(';');
                if (comment >= 0) value = value[..comment];
                return value.Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Sets <c>[section] key=value</c>, replacing the existing line, adding it to the section,
    /// or appending the section. Comments and other lines are left untouched.
    /// </summary>
    public static void WriteIniValue(string iniPath, string section, string key, string value)
    {
        var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : new List<string>();
        int sectionStart = -1, sectionEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith("[", StringComparison.Ordinal)) continue;
            if (sectionStart >= 0) { sectionEnd = i; break; }
            if (t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase)) sectionStart = i;
        }

        if (sectionStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            int found = -1, lastKeyLine = sectionStart;
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith(";", StringComparison.Ordinal)) continue;
                lastKeyLine = i;
                var eq = t.IndexOf('=');
                if (eq > 0 && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
            }
            if (found >= 0) lines[found] = $"{key}={value}";
            else lines.Insert(lastKeyLine + 1, $"{key}={value}");
        }
        File.WriteAllLines(iniPath, lines);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads the latest release: tag and the main package asset ("dlssg-release-X-x64.zip";
    /// the "-310.9" alternative-runtime build is skipped). Returns the tag or null.
    /// </summary>
    private async Task<string?> FetchLatestReleaseAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log("[Dlssg20_30Service.FetchLatestRelease] request failed");
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.StartsWith("dlssg-release-", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith("-x64.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(assetUrl))
            {
                _crashReporter.Log($"[Dlssg20_30Service.FetchLatestRelease] No usable package (tag={tag ?? "null"}, asset={assetUrl ?? "null"})");
                return null;
            }
            LatestVersion   = tag;
            _latestAssetUrl = assetUrl;
            return tag;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.FetchLatestRelease] {ex.Message}");
            return null;
        }
    }
}
