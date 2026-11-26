// BfiPresenterService.cs -- BFI Presenter (artzox/BFI-Presenter): presenter-clocked black frame insertion.
// Two routes, released together with one version number:
//   DX     -- BFIPresenter.addon64 / .addon32, a ReShade add-on for Direct3D 12, 11, 9 and OpenGL games. It goes
//             next to ReShade (ReShade's add-on folder) and creates BFIPresenter.ini beside itself on first start.
//   Vulkan -- BFIPresenterVK.dll / BFIPresenterVK32.dll, a Vulkan implicit layer (installed once per user, see
//             BfiPresenterService.VulkanLayer.cs) for Vulkan games and DirectX games run through DXVK. It does
//             nothing in a game unless BFIPresenter.ini sits next to the exe that runs, and it never creates that
//             file itself, so RHI copies the package's ready ini there.
// Both routes read the same BFIPresenter.ini ([BFI] section). Staging: %LocalAppData%\RHI\bfi-presenter\{dx,vulkan}\
// with a version.txt each (the release tag without "v."). The binaries carry no version resource, so installed
// copies are compared with the staged files.

using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

public enum BfiRoute { Dx, Vulkan }

public partial class BfiPresenterService
{
    private const string LatestApiUrl = "https://api.github.com/repos/artzox/BFI-Presenter/releases/latest";
    public  const string RepoUrl      = "https://github.com/artzox/BFI-Presenter";
    public  const string ReleasesUrl  = RepoUrl + "/releases";
    public  const string SettingsGuideUrl = RepoUrl + "/blob/main/docs/settings-guide.md";
    public  const string TestedGamesUrl   = RepoUrl + "/blob/main/docs/tested-games.md";

    public const string IniFileName = "BFIPresenter.ini";
    public const string IniSection  = "BFI";
    public const string Addon64     = "BFIPresenter.addon64";
    public const string Addon32     = "BFIPresenter.addon32";

    /// <summary>Files the DX add-on writes next to its ini (removed with it, carried by folder moves).</summary>
    public static readonly string[] DxGeneratedFiles =
        { IniFileName, "BFIPresenter_shaders.bin", "BFIPresenter_learned.ini" };

    /// <summary>Files the Vulkan layer writes next to the ini (removed with it).</summary>
    public static readonly string[] VulkanGeneratedFiles = { "BFIPresenter_learned.ini" };

    private static readonly string[] DxStagedFiles     = { Addon64, Addon32 };
    private static readonly string[] VulkanStagedFiles =
        { LayerDll64, LayerDll32, LayerJson64, LayerJson32, IniFileName };

    private readonly HttpClient      _http;
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter  _crashReporter;
    private readonly string          _stagingRoot;
    private readonly SemaphoreSlim   _stageLock = new(1, 1);

    private string? _latestTag;
    private string? _dxAssetUrl;
    private string? _vulkanAssetUrl;

    public BfiPresenterService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http          = http;
        _etagCache     = etagCache;
        _crashReporter = crashReporter;
        _stagingRoot   = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "bfi-presenter");
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    private string StageDir(BfiRoute route) => Path.Combine(_stagingRoot, route == BfiRoute.Dx ? "dx" : "vulkan");
    private string VersionFile(BfiRoute route) => Path.Combine(StageDir(route), "version.txt");
    private static string[] StagedFiles(BfiRoute route) => route == BfiRoute.Dx ? DxStagedFiles : VulkanStagedFiles;

    public string StagedPath(BfiRoute route, string file) => Path.Combine(StageDir(route), file);

    public bool IsStagingReady(BfiRoute route) =>
        StagedFiles(route).All(f => File.Exists(Path.Combine(StageDir(route), f)));

    /// <summary>Version of the staged package (e.g. "1.0.41"), or null if not staged.</summary>
    public string? StagedVersion(BfiRoute route)
    {
        try
        {
            return IsStagingReady(route) && File.Exists(VersionFile(route)) ? File.ReadAllText(VersionFile(route)).Trim() : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>Newest staged version of either route, for the row's status.</summary>
    public string? AnyStagedVersion => StagedVersion(BfiRoute.Dx) ?? StagedVersion(BfiRoute.Vulkan);

    public string? LatestVersion => _latestTag == null ? null : VersionFromTag(_latestTag);

    /// <summary>"v.1.0.41" / "v1.0.41" → "1.0.41".</summary>
    public static string VersionFromTag(string tag)
    {
        var t = tag.Trim();
        if (t.StartsWith("v.", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        else if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t[1..];
        return t;
    }

    public bool HasUpdate(BfiRoute route)
    {
        var latest = LatestVersion;
        return latest != null && IsStagingReady(route)
            && !string.Equals(StagedVersion(route), latest, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the latest release (ETag-cached). <paramref name="forceRefresh"/> skips the cache.</summary>
    public async Task<bool> CheckForUpdateAsync(bool forceRefresh = false)
    {
        if (forceRefresh) _etagCache.Invalidate(LatestApiUrl);
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log("[BfiPresenterService.CheckForUpdate] Release request failed");
                return false;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? dx = null, vk = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url  = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (url == null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    // By name: the release also has Tools, Manager and the settings guide
                    if (name.StartsWith("BFIPresenter-DX-", StringComparison.OrdinalIgnoreCase)) dx ??= url;
                    else if (name.StartsWith("BFIPresenter-Vulkan-", StringComparison.OrdinalIgnoreCase)) vk ??= url;
                }
            }
            if (string.IsNullOrEmpty(tag) || (dx == null && vk == null))
            {
                _crashReporter.Log($"[BfiPresenterService.CheckForUpdate] No usable packages (tag={tag ?? "null"})");
                return false;
            }
            _latestTag = tag; _dxAssetUrl = dx; _vulkanAssetUrl = vk;
            _crashReporter.Log($"[BfiPresenterService.CheckForUpdate] Latest {VersionFromTag(tag)}; staged DX={StagedVersion(BfiRoute.Dx) ?? "(none)"}, Vulkan={StagedVersion(BfiRoute.Vulkan) ?? "(none)"}");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or IOException)
        {
            _crashReporter.Log($"[BfiPresenterService.CheckForUpdate] {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads and stages a route's package when it's missing or older than the latest release. A no-op when
    /// staged and current. The current package is only replaced by a complete new one. True when staged afterwards.
    /// </summary>
    public async Task<bool> EnsureStagingAsync(BfiRoute route)
    {
        await _stageLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsStagingReady(route) && !HasUpdate(route)) return true;
            if (_latestTag == null) await CheckForUpdateAsync().ConfigureAwait(false);
            var url = route == BfiRoute.Dx ? _dxAssetUrl : _vulkanAssetUrl;
            var version = LatestVersion;
            if (url == null || version == null)
            {
                _crashReporter.Log($"[BfiPresenterService.EnsureStaging] No {route} package in the latest release");
                return IsStagingReady(route);
            }

            Directory.CreateDirectory(_stagingRoot);
            var dir     = StageDir(route);
            var zipPath = dir + ".zip.tmp";
            var newDir  = dir + ".new";
            var previous = StagedVersion(route);
            try
            {
                using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _crashReporter.Log($"[BfiPresenterService.EnsureStaging] HTTP {(int)response.StatusCode} for {url}");
                        return IsStagingReady(route);
                    }
                    await using var net  = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                    await net.CopyToAsync(file).ConfigureAwait(false);
                }

                if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
                Directory.CreateDirectory(newDir);
                var wanted = new HashSet<string>(StagedFiles(route), StringComparer.OrdinalIgnoreCase);
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    // Files sit in one top folder (BFIPresenter\ / BFIPresenterVK\): take the ones needed by name
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || !wanted.Contains(entry.Name)) continue;
                        var dest = FileHelper.SafeCombine(newDir, entry.Name);
                        if (dest == null) continue;
                        entry.ExtractToFile(dest, overwrite: true);
                    }
                }
                if (!StagedFiles(route).All(f => File.Exists(Path.Combine(newDir, f))))
                {
                    _crashReporter.Log($"[BfiPresenterService.EnsureStaging] {route} {version} is missing files — keeping the current package");
                    return IsStagingReady(route);
                }
                File.WriteAllText(Path.Combine(newDir, "version.txt"), version);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                Directory.Move(newDir, dir);
                _crashReporter.Log($"[BfiPresenterService.EnsureStaging] Staged {route} {version}");
                try
                {
                    App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
                    {
                        Timestamp     = DateTime.UtcNow,
                        Category      = "BFI Presenter",
                        ComponentName = route == BfiRoute.Dx ? "BFI Presenter (DX add-on)" : "BFI Presenter (Vulkan layer)",
                        OldVersion    = previous,
                        NewVersion    = version,
                        SizeBytes     = new FileInfo(zipPath).Length,
                    });
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    _crashReporter.Log($"[BfiPresenterService.EnsureStaging] Update log failed — {ex.Message}");
                }
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                _crashReporter.Log($"[BfiPresenterService.EnsureStaging] {route} failed — {ex.Message}");
                return IsStagingReady(route);
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (IOException) { }
                try { if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true); } catch (IOException) { }
            }
        }
        finally { _stageLock.Release(); }
    }

    // ── Per-game install record ──────────────────────────────────────────────
    // A game's record (GameNameService.BfiPresenterInstalledAs) is the path RHI placed, relative to the game's
    // install folder: the add-on ("BFIPresenter.addon64", or "<AddonPath>\BFIPresenter.addon64") for the DX
    // route, the ini next to the exe ("Binaries\Win64\BFIPresenter.ini") for the Vulkan route.

    public static BfiRoute RouteOf(string installedAs) =>
        installedAs.EndsWith(IniFileName, StringComparison.OrdinalIgnoreCase) ? BfiRoute.Vulkan : BfiRoute.Dx;

    public static string? FullPathOf(string installPath, string? installedAs) =>
        string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(installedAs) ? null : FileHelper.SafeCombine(installPath, installedAs);

    public static bool IsInstalledIn(string installPath, string? installedAs) =>
        FullPathOf(installPath, installedAs) is { } p && File.Exists(p);

    /// <summary>The game's BFIPresenter.ini (it may not exist yet: the DX add-on creates it on first start).</summary>
    public static string? IniPathOf(string installPath, string? installedAs) =>
        FullPathOf(installPath, installedAs) is { } p ? Path.Combine(Path.GetDirectoryName(p)!, IniFileName) : null;

    // ── Install / uninstall ──────────────────────────────────────────────────

    /// <summary>
    /// Copies the 64- or 32-bit add-on into ReShade's add-on folder. A copy RHI's add-on list put there earlier is
    /// taken over; any other file of that name is backed up (sentinel pattern). Returns the record, or null.
    /// </summary>
    public string? InstallDx(string installPath, bool is32Bit)
    {
        var name = is32Bit ? Addon32 : Addon64;
        var src  = StagedPath(BfiRoute.Dx, name);
        if (!File.Exists(src)) { _crashReporter.Log("[BfiPresenterService.InstallDx] Add-on not staged"); return null; }
        try
        {
            var deployDir = ModInstallService.GetAddonDeployPath(installPath);
            Directory.CreateDirectory(deployDir);
            var dest = Path.Combine(deployDir, name);
            bool fromAddonList = AddonPackService.IsAddonTrackedInDeployments(deployDir, name);
            if (fromAddonList) AddonPackService.UntrackAddonDeployment(deployDir, name);
            AuxInstallService.SentinelBackupReplacingOwn(dest, existingIsRhiCopy: fromAddonList);
            File.Copy(src, dest, overwrite: true);
            // The other bitness's copy would only be a stale file next to it
            var other = Path.Combine(deployDir, is32Bit ? Addon64 : Addon32);
            if (AddonPackService.IsAddonTrackedInDeployments(deployDir, Path.GetFileName(other)))
            {
                try { File.Delete(other); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _crashReporter.Log($"[BfiPresenterService.InstallDx] Other-bitness add-on '{Path.GetFileName(other)}' not removed — {ex.Message}"); }
                AddonPackService.UntrackAddonDeployment(deployDir, Path.GetFileName(other));
            }
            var rel = Path.GetRelativePath(installPath, dest);
            _crashReporter.Log($"[BfiPresenterService.InstallDx] {name} {StagedVersion(BfiRoute.Dx)} → '{dest}'");
            return rel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.InstallDx] Failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Vulkan route: copies the package's BFIPresenter.ini next to the game's exe (that file switches the layer on in
    /// this game). An existing ini (the user's settings, or one from a DX install) is kept. Returns the record, or null.
    /// </summary>
    public string? InstallVulkan(string installPath, string exeDir, IDictionary<string, string>? iniValues = null)
    {
        var src = StagedPath(BfiRoute.Vulkan, IniFileName);
        if (!File.Exists(src)) { _crashReporter.Log("[BfiPresenterService.InstallVulkan] ini not staged"); return null; }
        try
        {
            var dest = Path.Combine(exeDir, IniFileName);
            if (!File.Exists(dest)) File.Copy(src, dest);
            if (iniValues != null) foreach (var kv in iniValues) SetIni(dest, kv.Key, kv.Value);
            // The layer stays off where VulkanLayer=0: an ini kept from a DX install could have it
            if (GetIni(dest, "VulkanLayer") == "0") SetIni(dest, "VulkanLayer", "1");
            var rel = Path.GetRelativePath(installPath, dest);
            _crashReporter.Log($"[BfiPresenterService.InstallVulkan] ini → '{dest}'");
            return rel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.InstallVulkan] Failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Removes RHI's install: the add-on (restoring a file it replaced) or, for the Vulkan route, the ini that turns
    /// the layer on. <paramref name="keepSettings"/> keeps BFIPresenter.ini (DX route only; the Vulkan ini is the switch).
    /// </summary>
    public bool Uninstall(string installPath, string installedAs, bool keepSettings = false)
    {
        var full = FullPathOf(installPath, installedAs);
        if (full == null) return false;
        var dir = Path.GetDirectoryName(full)!;
        try
        {
            if (RouteOf(installedAs) == BfiRoute.Dx)
            {
                if (File.Exists(full)) File.Delete(full);
                AuxInstallService.SentinelRestore(full);
                foreach (var f in DxGeneratedFiles)
                {
                    if (keepSettings && f.Equals(IniFileName, StringComparison.OrdinalIgnoreCase)) continue;
                    var p = Path.Combine(dir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
            }
            else
            {
                foreach (var f in VulkanGeneratedFiles.Prepend(IniFileName))
                {
                    var p = Path.Combine(dir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
            }
            _crashReporter.Log($"[BfiPresenterService.Uninstall] Removed '{installedAs}' from '{installPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.Uninstall] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Updates ──────────────────────────────────────────────────────────────

    /// <summary>True when a DX install differs from the staged add-on of its bitness.</summary>
    public bool DxNeedsUpdate(string installPath, string installedAs)
    {
        var full = FullPathOf(installPath, installedAs);
        if (full == null || !File.Exists(full)) return false;
        var src = StagedPath(BfiRoute.Dx, Path.GetFileName(full));
        return File.Exists(src) && !VulkanLayerService.FilesIdentical(src, full);
    }

    /// <summary>Replaces a DX install with the staged add-on. False when the file is in use (game running).</summary>
    public bool UpdateDx(string installPath, string installedAs)
    {
        var full = FullPathOf(installPath, installedAs);
        if (full == null) return false;
        var src = StagedPath(BfiRoute.Dx, Path.GetFileName(full));
        try
        {
            File.Copy(src, full, overwrite: true);
            _crashReporter.Log($"[BfiPresenterService.UpdateDx] '{full}' → {StagedVersion(BfiRoute.Dx)}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.UpdateDx] '{full}' not updated — {ex.Message}");
            return false;
        }
    }

    // ── BFIPresenter.ini ([BFI] section) ─────────────────────────────────────

    public static string? GetIni(string iniPath, string key) => Dlssg20_30Service.ReadIniValue(iniPath, IniSection, key);
    public static void SetIni(string iniPath, string key, string value) => Dlssg20_30Service.WriteIniValue(iniPath, IniSection, key, value);
}
