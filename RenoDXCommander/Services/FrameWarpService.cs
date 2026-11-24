// FrameWarpService.cs — FrameWarp / XPAR (xPoiler/Reshade-Asynchronous-Reprojection, GPL-3.0): asynchronous camera
// reprojection as a ReShade add-on, so the camera moves at the display's refresh rate whatever the game renders at.
// DirectX 12, DirectX 11 and Vulkan, 64-bit. Mirrors the release's install.ps1 (v2.4.0):
//
//   FrameWarp.addon64                 → ReShade's add-on folder (GetAddonDeployPath). Other copies of it in the game
//                                       folder (a game's staging copy, RE Requiem's _storage_) are updated too, and
//                                       removed with it, as the installer does.
//   FrameWarp\FrameWarpPresenter.exe  → beside the add-on: the add-on looks for <add-on folder>\FrameWarp\ first
//                                       (addon.cpp, g_presenter_path), then <exe folder>\FrameWarp\.
//   FrameWarp\LICENSE, THIRD_PARTY_NOTICES.md (GPL-3.0: kept with the program), FrameWarp\rhi-framewarp.txt (RHI's marker)
//   XPAR.addonfx                      → game reshade-shaders\Shaders through the shader sync hook (it includes
//                                       ReShade.fxh: the sync adds the staged headers when no pack does).
//   Unreal Engine games: r.Streamline.ForceTagging=1 under [ConsoleVariables] in the user Engine.ini
//                                       (%LocalAppData%\<Project>[\<Store>]\Saved\Config\Windows|WindowsNoEditor|WinGDK),
//                                       backup <ini>.framewarp-backup; the line is removed again with FrameWarp.
//   Optional NVIDIA Latewarp: FrameWarp\nvngx_latewarp.dll (NVIDIA's, not in the release). RHI downloads it on demand
//                                       from a pinned link and installs it only when its SHA256 matches.
//
// FrameWarp keeps its settings in the game's ReShade.ini [FrameWarp] and puts most of them back to its defaults the first
// time a new build runs (addon.cpp, SettingsVersion), so RHI doesn't write those. ShowAllSettings isn't reset.
// Files RHI placed are recorded in rhi_install.txt (component "FrameWarp", paths relative to the add-on folder).

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class FrameWarpService
{
    private const string LatestApiUrl = "https://api.github.com/repos/xPoiler/Reshade-Asynchronous-Reprojection/releases/latest";
    public  const string RepoUrl      = "https://github.com/xPoiler/Reshade-Asynchronous-Reprojection";
    public  const string ReleasesUrl  = RepoUrl + "/releases";

    public const string Component     = "FrameWarp";
    public const string Addon64       = "FrameWarp.addon64";
    public const string Folder        = "FrameWarp";
    public const string PresenterExe  = "FrameWarpPresenter.exe";
    public const string ShaderFile    = "XPAR.addonfx";
    public const string MarkerName    = "rhi-framewarp.txt";
    public const string LatewarpDll   = "nvngx_latewarp.dll";
    public const string IniSection    = "FrameWarp";
    public const string EngineCvar    = "r.Streamline.ForceTagging=1";
    public const string UeLaunchOptions = "-slforcetagging -slviewextension";

    /// <summary>NVIDIA Latewarp 310.2.0.0 (the build FrameWarp is tested with): where RHI gets it and the DLL's SHA256.</summary>
    public const string DefaultLatewarpUrl = "https://github.com/SimonMacer/AnWave/releases/download/AnWave-DLSS/nvngx_latewarp_310.2.0.0.zip";
    public const string DefaultLatewarpSha256 = "81E63B4A9DCFB99B2B71C3DDFD3EA4A0E9780B778BD47E9A6F7F010A7A6C0F20";

    /// <summary>Manifest overrides (componentUrls "latewarp" / "latewarpSha256"), set at startup; null = the defaults.</summary>
    public static string? LatewarpUrlOverride { get; set; }
    public static string? LatewarpSha256Override { get; set; }
    public static string LatewarpUrl => string.IsNullOrWhiteSpace(LatewarpUrlOverride) ? DefaultLatewarpUrl : LatewarpUrlOverride!;
    public static string LatewarpSha256 => string.IsNullOrWhiteSpace(LatewarpSha256Override) ? DefaultLatewarpSha256 : LatewarpSha256Override!.Trim().ToUpperInvariant();

    /// <summary>
    /// Manifest componentUrls: "latewarp" (an https link to the zip or DLL) and "latewarpSha256" (the DLL's SHA256). Both
    /// are needed to move to another source; a missing or non-https value keeps the default. Applied in all three init paths.
    /// </summary>
    public static void ApplyManifestUrls(IReadOnlyDictionary<string, string>? urls)
    {
        string? url = null, sha = null;
        if (urls != null)
        {
            urls.TryGetValue("latewarp", out url);
            urls.TryGetValue("latewarpSha256", out sha);
        }
        bool valid = url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                     && sha != null && sha.Trim().Length == 64 && sha.Trim().All(Uri.IsHexDigit);
        LatewarpUrlOverride = valid ? url : null;
        LatewarpSha256Override = valid ? sha : null;
    }

    // Staged: the add-on and shader at the top, the presenter and licences under FrameWarp\ (as the game gets them)
    private static readonly string[] StagedTop    = { Addon64, ShaderFile };
    private static readonly string[] StagedFolder = { PresenterExe, "LICENSE", "THIRD_PARTY_NOTICES.md" };

    private readonly HttpClient      _http;
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter  _crashReporter;
    private readonly SemaphoreSlim   _stageLock = new(1, 1);
    private string? _latestTag;
    private string? _assetUrl;

    public static string StagingDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "framewarp");
    private static string LatewarpStaged => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "framewarp-latewarp", LatewarpDll);

    public FrameWarpService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
        _crashReporter = crashReporter;
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    private static string VersionFile => Path.Combine(StagingDir, "version.txt");

    public bool IsStagingReady =>
        StagedTop.All(f => File.Exists(Path.Combine(StagingDir, f)))
        && StagedFolder.All(f => File.Exists(Path.Combine(StagingDir, Folder, f)));

    /// <summary>Staged release ("2.4.0"), or null.</summary>
    public string? StagedVersion
    {
        get
        {
            try { return IsStagingReady && File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null; }
            catch (IOException) { return null; }
        }
    }

    public string? LatestVersion => _latestTag == null ? null : VersionFromTag(_latestTag);
    public string? LastError { get; private set; }

    /// <summary>"v2.4.0" → "2.4.0".</summary>
    public static string VersionFromTag(string tag)
    {
        var t = tag.Trim();
        return t.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? t[1..] : t;
    }

    public bool HasUpdate => LatestVersion != null && IsStagingReady
        && !string.Equals(StagedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the latest release (ETag-cached): its FrameWarp-*.zip asset.</summary>
    public async Task<bool> CheckForUpdateAsync(bool forceRefresh = false)
    {
        if (forceRefresh) _etagCache.Invalidate(LatestApiUrl);
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null) { _crashReporter.Log("[FrameWarpService.CheckForUpdate] Release request failed"); return false; }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.StartsWith("FrameWarp-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u))
                        url ??= u.GetString();
                }
            if (string.IsNullOrEmpty(tag) || url == null)
            {
                _crashReporter.Log($"[FrameWarpService.CheckForUpdate] No FrameWarp zip in the latest release (tag={tag ?? "null"})");
                return false;
            }
            _latestTag = tag; _assetUrl = url;
            _crashReporter.Log($"[FrameWarpService.CheckForUpdate] Latest {VersionFromTag(tag)}; staged {StagedVersion ?? "(none)"}");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or IOException)
        {
            _crashReporter.Log($"[FrameWarpService.CheckForUpdate] {ex.Message}");
            return false;
        }
    }

    /// <summary>Downloads and stages the latest release when missing or older; the staged copy is only replaced by a complete one.</summary>
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
                if (!IsStagingReady) LastError = "Couldn't find FrameWarp's latest release on GitHub. Try again later.";
                return IsStagingReady;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(StagingDir)!);
            var zipPath = StagingDir + ".zip.tmp";
            var newDir = StagingDir + ".new";
            var previous = StagedVersion;
            try
            {
                using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _crashReporter.Log($"[FrameWarpService.EnsureStaging] HTTP {(int)response.StatusCode} for {url}");
                        if (!IsStagingReady) LastError = $"Couldn't download FrameWarp (HTTP {(int)response.StatusCode}).";
                        return IsStagingReady;
                    }
                    await using var net = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                    await net.CopyToAsync(file).ConfigureAwait(false);
                }
                if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
                Directory.CreateDirectory(Path.Combine(newDir, Folder));
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    // FrameWarp-<version>\FrameWarp.addon64, XPAR.addonfx, LICENSE, THIRD_PARTY_NOTICES.md, FrameWarp\FrameWarpPresenter.exe
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var parts = entry.FullName.Replace('\\', '/').Split('/');
                        string? dest = null;
                        if (parts.Length == 2 && StagedTop.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                            dest = FileHelper.SafeCombine(newDir, entry.Name);
                        else if (parts.Length == 2 && entry.Name is "LICENSE" or "THIRD_PARTY_NOTICES.md")
                            dest = FileHelper.SafeCombine(Path.Combine(newDir, Folder), entry.Name);
                        else if (parts.Length == 3 && parts[1].Equals(Folder, StringComparison.OrdinalIgnoreCase)
                                 && entry.Name.Equals(PresenterExe, StringComparison.OrdinalIgnoreCase))
                            dest = FileHelper.SafeCombine(Path.Combine(newDir, Folder), entry.Name);
                        if (dest != null) entry.ExtractToFile(dest, overwrite: true);
                    }
                }
                bool complete = StagedTop.All(f => File.Exists(Path.Combine(newDir, f)))
                                && StagedFolder.All(f => File.Exists(Path.Combine(newDir, Folder, f)));
                if (!complete)
                {
                    _crashReporter.Log($"[FrameWarpService.EnsureStaging] {version} is missing files — keeping the current copy");
                    if (!IsStagingReady) LastError = "FrameWarp's download is missing files. Try again later.";
                    return IsStagingReady;
                }
                File.WriteAllText(Path.Combine(newDir, "version.txt"), version);
                if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true);
                Directory.Move(newDir, StagingDir);
                _crashReporter.Log($"[FrameWarpService.EnsureStaging] Staged {version} (was {previous ?? "none"})");
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                _crashReporter.Log($"[FrameWarpService.EnsureStaging] Failed — {ex.Message}");
                if (!IsStagingReady) LastError = $"Couldn't download FrameWarp: {ex.Message}";
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
    private static string MarkerPath(string installPath) => Path.Combine(AddonDir(installPath), Folder, MarkerName);
    public static string LogsDir(string installPath) => Path.Combine(AddonDir(installPath), Folder, "logs");

    /// <summary>FrameWarp's add-on is in this game's add-on folder (by RHI or by its own installer).</summary>
    public static bool IsInstalledIn(string installPath) =>
        !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(AddonDir(installPath), Addon64));

    /// <summary>RHI installed FrameWarp here (it has a record).</summary>
    public static bool IsRhiInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) && RhiInstallManifest.GetComponentFiles(installPath, Component).Count > 0;

    public static bool IsLatewarpInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(AddonDir(installPath), Folder, LatewarpDll));

    /// <summary>The marker's key=value lines (version, engine_ini, show_all).</summary>
    public static Dictionary<string, string> ReadMarker(string installPath)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var p = MarkerPath(installPath);
            if (!File.Exists(p)) return d;
            foreach (var line in File.ReadAllLines(p))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return d;
    }

    private static void WriteMarker(string installPath, Dictionary<string, string> values) =>
        File.WriteAllLines(MarkerPath(installPath), values.Where(kv => kv.Value.Length > 0).Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>Sets (or, with null / empty, removes) one marker value. False when FrameWarp isn't installed by RHI here.</summary>
    public static bool SetMarkerValue(string installPath, string key, string? value)
    {
        if (!IsRhiInstalled(installPath)) return false;
        var marker = ReadMarker(installPath);
        if (string.IsNullOrEmpty(value)) marker.Remove(key); else marker[key] = value;
        try { WriteMarker(installPath, marker); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// The game needs FrameWarp's launch options: an Unreal Engine game (its Engine.ini is found the installer's way) that
    /// uses DLSS through Streamline (its README: "Unreal Engine games with DLSS"; the installer prints them for UE +
    /// Streamline). Touches the disk.
    /// </summary>
    public static bool NeedsUeLaunchOptions(string installPath, bool knownStreamline) =>
        !string.IsNullOrEmpty(installPath) && FindEngineIni(installPath) != null
        && (knownStreamline || HsfService.FindGameStreamline(installPath));

    /// <summary>Launch arguments with the options added (only missing ones), and which were added.</summary>
    public static (string Args, string Added) AddLaunchOptionsTo(string current)
    {
        var have = (current ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var added = UeLaunchOptions.Split(' ').Where(o => !have.Contains(o, StringComparer.OrdinalIgnoreCase)).ToList();
        var args = string.Join(" ", have.Concat(added));
        return (args, string.Join(" ", added));
    }

    /// <summary>Launch arguments without the given options (the ones RHI added); everything else is kept in order.</summary>
    public static string RemoveLaunchOptionsFrom(string current, string added)
    {
        var drop = (added ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", (current ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(a => !drop.Contains(a, StringComparer.OrdinalIgnoreCase)));
    }

    public static string? InstalledVersion(string installPath) =>
        ReadMarker(installPath).TryGetValue("version", out var v) && v.Length > 0 ? v : null;

    /// <summary>Stops presenters left running from this game's folder (they lock their exe; they only serve the game).</summary>
    private void StopPresenters(string installPath)
    {
        var dir = Path.Combine(AddonDir(installPath), Folder);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(PresenterExe)))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path == null || !path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill();
                p.WaitForExit(5000);
                _crashReporter.Log($"[FrameWarpService] Stopped a presenter left running: '{path}'");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
    }

    /// <summary>Other FrameWarp.addon64 copies in the game folder (a game's staging copy), as the installer finds them.</summary>
    private static IEnumerable<string> OtherAddonCopies(string installPath)
    {
        var main = Path.Combine(AddonDir(installPath), Addon64);
        IEnumerable<string> all;
        try
        {
            all = Directory.EnumerateFiles(installPath, Addon64, new EnumerationOptions
                { RecurseSubdirectories = true, MaxRecursionDepth = 8, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
        return all.Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(main), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Installs FrameWarp: the add-on (backing up a file of that name RHI didn't put there), the presenter and licences in
    /// FrameWarp\, other add-on copies updated, and the Unreal Engine cvar. The shader goes in through the shader sync
    /// (the caller runs DeployShadersForCardAsync). Throws IOException / UnauthorizedAccessException on write failures.
    /// </summary>
    public void Install(string installPath)
    {
        if (!IsStagingReady) throw new InvalidOperationException("FrameWarp isn't downloaded");
        var addonDir = AddonDir(installPath);
        var fwDir = Path.Combine(addonDir, Folder);
        Directory.CreateDirectory(fwDir);
        StopPresenters(installPath);
        bool ours = IsRhiInstalled(installPath);
        var marker = ReadMarker(installPath);

        var dest = Path.Combine(addonDir, Addon64);
        if (!ours) AuxInstallService.SentinelBackupReplacingOwn(dest, existingIsRhiCopy: false);
        File.Copy(Path.Combine(StagingDir, Addon64), dest, overwrite: true);
        foreach (var copy in OtherAddonCopies(installPath))
        {
            File.Copy(Path.Combine(StagingDir, Addon64), copy, overwrite: true);
            _crashReporter.Log($"[FrameWarpService.Install] Updated another copy: '{copy}'");
        }
        foreach (var f in StagedFolder)
            File.Copy(Path.Combine(StagingDir, Folder, f), Path.Combine(fwDir, f), overwrite: true);

        // Unreal Engine: Streamline resource tagging, as the installer does
        var ini = marker.TryGetValue("engine_ini", out var known) && known.Length > 0 ? known : FindEngineIni(installPath);
        if (ini != null && SetEngineCvar(ini, enable: true)) marker["engine_ini"] = ini;

        marker["version"] = StagedVersion ?? "";
        WriteMarker(installPath, marker);
        var recorded = new List<string> { Addon64, Path.Combine(Folder, MarkerName) };
        recorded.AddRange(StagedFolder.Select(f => Path.Combine(Folder, f)));
        if (IsLatewarpInstalled(installPath)) recorded.Add(Path.Combine(Folder, LatewarpDll));
        RhiInstallManifest.SetComponent(installPath, Component, recorded);
        _crashReporter.Log($"[FrameWarpService.Install] {StagedVersion} → '{addonDir}'{(ini != null ? $" (Engine.ini: {ini})" : "")}");
    }

    /// <summary>Brings an RHI install up to the staged release (add-on, its other copies, presenter). False when nothing changed or files are in use.</summary>
    public bool UpdateInstalled(string installPath)
    {
        if (!IsStagingReady || !IsRhiInstalled(installPath)) return false;
        if (string.Equals(InstalledVersion(installPath), StagedVersion, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(AddonDir(installPath), Addon64))
            && VulkanLayerService.FilesIdentical(Path.Combine(StagingDir, Addon64), Path.Combine(AddonDir(installPath), Addon64)))
            return false;
        try
        {
            Install(installPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[FrameWarpService.UpdateInstalled] '{installPath}' not updated (game running?) — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes FrameWarp as its uninstaller does: the add-on (restoring a file it replaced) and its other copies, the
    /// FrameWarp\ folder (presenter, licences, logs, Latewarp), the Engine.ini line, and RHI's ShowAllSettings. The
    /// shader leaves with the next shader sync (the caller runs it). FrameWarp's [FrameWarp] settings stay in ReShade.ini.
    /// </summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var addonDir = AddonDir(installPath);
        var marker = ReadMarker(installPath);
        StopPresenters(installPath);
        var addon = Path.Combine(addonDir, Addon64);
        TryDelete(addon);
        AuxInstallService.SentinelRestore(addon);
        foreach (var copy in OtherAddonCopies(installPath)) TryDelete(copy);
        if (marker.TryGetValue("engine_ini", out var ini) && ini.Length > 0) SetEngineCvar(ini, enable: false);
        if (marker.TryGetValue("show_all", out var showAll) && showAll == "1") SetShowAllSettings(installPath, null);
        var fwDir = Path.Combine(addonDir, Folder);
        try { if (Directory.Exists(fwDir)) Directory.Delete(fwDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[FrameWarpService.Uninstall] Couldn't remove '{fwDir}' — {ex.Message}"); }
        RhiInstallManifest.RemoveComponent(installPath, Component);
        _crashReporter.Log($"[FrameWarpService.Uninstall] Removed from '{addonDir}'");
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[FrameWarpService] Couldn't delete '{path}' — {ex.Message}"); }
    }

    // ── Unreal Engine (Engine.ini) ────────────────────────────────────────────

    /// <summary>
    /// The user Engine.ini of an Unreal Engine game whose ReShade sits in <Project>\Binaries\Win64 (or WinGDK), as the
    /// installer finds it: %LocalAppData%\<Project>[\<sub>]\Saved\Config\Windows|WindowsNoEditor|WinGDK. Null otherwise.
    /// </summary>
    public static string? FindEngineIni(string binDir, string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var leaf = Path.GetFileName(binDir.TrimEnd('\\', '/'));
        if (!leaf.Equals("Win64", StringComparison.OrdinalIgnoreCase) && !leaf.Equals("WinGDK", StringComparison.OrdinalIgnoreCase)) return null;
        var project = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(binDir.TrimEnd('\\', '/'))) ?? "");
        if (string.IsNullOrEmpty(project)) return null;
        var bases = new List<string> { Path.Combine(localAppData, project) };
        try
        {
            if (Directory.Exists(bases[0])) bases.AddRange(Directory.GetDirectories(bases[0]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var b in bases)
            foreach (var platform in new[] { "Windows", "WindowsNoEditor", "WinGDK" })
            {
                var dir = Path.Combine(b, "Saved", "Config", platform);
                if (Directory.Exists(dir)) return Path.Combine(dir, "Engine.ini");
            }
        return null;
    }

    /// <summary>Engine.ini text with the cvar line under [ConsoleVariables] (enable) or without it (any copies removed).</summary>
    public static string SetEngineCvarInText(string text, bool enable)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Length == 0 ? new List<string>() : text.Split(nl).ToList();
        bool endsWithBreak = lines.Count > 0 && lines[^1].Length == 0;
        if (endsWithBreak) lines.RemoveAt(lines.Count - 1);
        lines.RemoveAll(l => l.Trim() == EngineCvar);
        if (enable)
        {
            int i = lines.FindIndex(l => l.Trim().Equals("[ConsoleVariables]", StringComparison.OrdinalIgnoreCase));
            if (i < 0) { if (lines.Count > 0) lines.Add(""); lines.Add("[ConsoleVariables]"); lines.Add(EngineCvar); }
            else lines.Insert(i + 1, EngineCvar);
        }
        return string.Join(nl, lines) + (lines.Count > 0 ? nl : "");
    }

    /// <summary>Adds or removes the cvar, keeping a read-only flag (performance mods set it) and backing up once before the first change.</summary>
    private bool SetEngineCvar(string ini, bool enable)
    {
        try
        {
            if (!File.Exists(ini))
            {
                if (!enable) return true;
                Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
                File.WriteAllText(ini, "");
            }
            var backup = ini + ".framewarp-backup";
            if (enable && !File.Exists(backup)) File.Copy(ini, backup);
            var text = File.ReadAllText(ini);
            var updated = SetEngineCvarInText(text, enable);
            if (updated == text) return true;
            var info = new FileInfo(ini);
            bool readOnly = info.IsReadOnly;
            if (readOnly) info.IsReadOnly = false;
            try { File.WriteAllText(ini, updated); }
            finally { if (readOnly) new FileInfo(ini).IsReadOnly = true; }
            _crashReporter.Log($"[FrameWarpService.SetEngineCvar] {(enable ? "Added" : "Removed")} {EngineCvar} in '{ini}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[FrameWarpService.SetEngineCvar] '{ini}' — {ex.Message}");
            return false;
        }
    }

    // ── ReShade.ini [FrameWarp] ShowAllSettings ──────────────────────────────

    private static string ReShadeIni(string installPath) => Path.Combine(installPath, "ReShade.ini");

    /// <summary>Sets [FrameWarp] ShowAllSettings in the game's ReShade.ini (null removes the line).</summary>
    private void SetShowAllSettings(string installPath, string? value)
    {
        var ini = ReShadeIni(installPath);
        try
        {
            if (value != null) { Dlssg20_30Service.WriteIniValue(ini, IniSection, "ShowAllSettings", value); return; }
            if (!File.Exists(ini)) return;
            var lines = File.ReadAllLines(ini).ToList();
            bool inSection = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith('[') && t.EndsWith(']')) { inSection = t[1..^1].Equals(IniSection, StringComparison.OrdinalIgnoreCase); continue; }
                if (inSection && t.StartsWith("ShowAllSettings=", StringComparison.OrdinalIgnoreCase)) lines.RemoveAt(i--);
            }
            File.WriteAllLines(ini, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[FrameWarpService.SetShowAllSettings] '{ini}' — {ex.Message}"); }
    }

    // ── NVIDIA Latewarp (optional) ───────────────────────────────────────────

    public static string Sha256Of(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    /// <summary>Downloads the Latewarp zip once and keeps its DLL only when the SHA256 matches. Returns an error, or null.</summary>
    public async Task<string?> EnsureLatewarpStagedAsync()
    {
        var dest = LatewarpStaged;
        try
        {
            if (File.Exists(dest) && Sha256Of(dest) == LatewarpSha256) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".download";
            using (var response = await _http.GetAsync(LatewarpUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return $"Couldn't download NVIDIA Latewarp (HTTP {(int)response.StatusCode}).";
                await using var net = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                await net.CopyToAsync(file).ConfigureAwait(false);
            }
            try
            {
                // A zip with the DLL somewhere inside, or the DLL itself
                bool isZip;
                using (var fs = File.OpenRead(tmp)) isZip = fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
                var candidate = dest + ".new";
                if (isZip)
                {
                    using var zip = ZipFile.OpenRead(tmp);
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(LatewarpDll, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return "The NVIDIA Latewarp download has no nvngx_latewarp.dll.";
                    entry.ExtractToFile(candidate, overwrite: true);
                }
                else File.Copy(tmp, candidate, overwrite: true);
                var sha = Sha256Of(candidate);
                if (sha != LatewarpSha256)
                {
                    File.Delete(candidate);
                    _crashReporter.Log($"[FrameWarpService.EnsureLatewarp] SHA256 {sha} doesn't match {LatewarpSha256} — not used");
                    return "The downloaded nvngx_latewarp.dll isn't the expected NVIDIA build, so RHI didn't use it.";
                }
                File.Move(candidate, dest, overwrite: true);
                _crashReporter.Log("[FrameWarpService.EnsureLatewarp] Staged nvngx_latewarp.dll (hash verified)");
                return null;
            }
            finally { try { File.Delete(tmp); } catch (IOException) { } }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                      or UnauthorizedAccessException or InvalidDataException)
        {
            _crashReporter.Log($"[FrameWarpService.EnsureLatewarp] Failed — {ex.Message}");
            return $"Couldn't download NVIDIA Latewarp: {ex.Message}";
        }
    }

    /// <summary>
    /// Puts the staged Latewarp DLL in FrameWarp\ and turns on [FrameWarp] ShowAllSettings (so its Warp engine choice
    /// shows in the XPAR panel; FrameWarp never resets that key). Remembers whether RHI turned it on.
    /// </summary>
    public void InstallLatewarp(string installPath)
    {
        if (!IsRhiInstalled(installPath)) throw new InvalidOperationException("FrameWarp isn't installed here by RHI");
        var dst = Path.Combine(AddonDir(installPath), Folder, LatewarpDll);
        File.Copy(LatewarpStaged, dst, overwrite: true);
        var marker = ReadMarker(installPath);
        var current = Dlssg20_30Service.ReadIniValue(ReShadeIni(installPath), IniSection, "ShowAllSettings");
        if (current != "1")
        {
            SetShowAllSettings(installPath, "1");
            marker["show_all"] = "1";
        }
        WriteMarker(installPath, marker);
        var files = RhiInstallManifest.GetComponentFiles(installPath, Component).ToList();
        var rel = Path.Combine(Folder, LatewarpDll);
        if (!files.Contains(rel, StringComparer.OrdinalIgnoreCase)) files.Add(rel);
        RhiInstallManifest.SetComponent(installPath, Component, files);
        _crashReporter.Log($"[FrameWarpService.InstallLatewarp] → '{dst}'");
    }

    /// <summary>Removes the Latewarp DLL and, if RHI turned it on, ShowAllSettings.</summary>
    public void RemoveLatewarp(string installPath)
    {
        StopPresenters(installPath);
        File.Delete(Path.Combine(AddonDir(installPath), Folder, LatewarpDll));
        var marker = ReadMarker(installPath);
        if (marker.TryGetValue("show_all", out var s) && s == "1")
        {
            SetShowAllSettings(installPath, null);
            marker.Remove("show_all");
        }
        WriteMarker(installPath, marker);
        var files = RhiInstallManifest.GetComponentFiles(installPath, Component)
            .Where(f => !f.Equals(Path.Combine(Folder, LatewarpDll), StringComparison.OrdinalIgnoreCase)).ToList();
        RhiInstallManifest.SetComponent(installPath, Component, files);
        _crashReporter.Log($"[FrameWarpService.RemoveLatewarp] Removed from '{installPath}'");
    }

    // ── Shaders (shader sync hook) ────────────────────────────────────────────

    /// <summary>XPAR.addonfx while RHI's FrameWarp is installed in the game (listed even before staging: the sync keeps the game's copy).</summary>
    public static IReadOnlyList<(string Rel, string Source)> ShaderFilesFor(string gameDir) =>
        IsRhiInstalled(gameDir)
            ? new[] { (Path.Combine("Shaders", ShaderFile), Path.Combine(StagingDir, ShaderFile)) }
            : Array.Empty<(string, string)>();

    // ── System checks ─────────────────────────────────────────────────────────

    /// <summary>Hardware-accelerated GPU scheduling (HKLM ...\GraphicsDrivers HwSchMode: 2 = on). Null when unknown.</summary>
    public static bool? IsHagsOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return key?.GetValue("HwSchMode") is int v ? v == 2 : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>The VC++ 2015-2022 runtime (x64) the presenter needs, checked as the installer does (System32\msvcp140.dll).</summary>
    public static bool HasVcRuntime() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msvcp140.dll"));
}
