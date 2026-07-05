// MainViewModel.Install.Components.cs -- ReLimiter and Display Commander install/uninstall commands.

using System.Net.Http.Headers;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{

    /// <summary>
    /// Runs background detection for Display Commander, OptiScaler, DXVK, and DLSS/Streamline
    /// on a manually added game, then updates the card on the UI thread.
    /// </summary>
    private async Task ScanManualGameComponentsAsync(GameCardViewModel card)
    {
        try
        {
            if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
                return;

            var installPath = card.InstallPath;
            var gameName = card.GameName;
            var is32Bit = card.Is32Bit;
            var auxRecords = _auxInstaller.LoadAll();

            // ── Display Commander ─────────────────────────────────────────────────
            GameStatus dcStatus = GameStatus.NotInstalled;
            string? dcFile = null;
            string? dcVersion = null;

            var dcFileName = is32Bit ? "DisplayCommander.addon32" : "DisplayCommander.addon64";
            var addonDeployPath = ModInstallService.GetAddonDeployPath(installPath);
            if (File.Exists(Path.Combine(addonDeployPath, dcFileName))
                || File.Exists(Path.Combine(installPath, dcFileName)))
            {
                dcStatus = GameStatus.Installed;
                dcFile = dcFileName;
                dcVersion = AuxInstallService.ReadInstalledVersion(installPath, dcFileName)
                         ?? AuxInstallService.ReadInstalledVersion(addonDeployPath, dcFileName);
            }
            else
            {
                var dcRec = auxRecords.FirstOrDefault(r =>
                    r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                    r.AddonType == "DisplayCommander");
                if (dcRec != null && File.Exists(Path.Combine(dcRec.InstallPath, dcRec.InstalledAs)))
                {
                    dcStatus = GameStatus.Installed;
                    dcFile = dcRec.InstalledAs;
                    dcVersion = AuxInstallService.ReadInstalledVersion(dcRec.InstallPath, dcRec.InstalledAs);
                }
            }

            // ── OptiScaler ────────────────────────────────────────────────────────
            GameStatus osStatus = GameStatus.NotInstalled;
            string? osFile = null;
            string? osVersion = null;

            var osRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == OptiScalerService.AddonType);
            if (osRec != null && File.Exists(Path.Combine(osRec.InstallPath, osRec.InstalledAs)))
            {
                osStatus = GameStatus.Installed;
                osFile = osRec.InstalledAs;
                osVersion = AuxInstallService.ReadInstalledVersion(osRec.InstallPath, osRec.InstalledAs);
            }

            // ── DXVK ──────────────────────────────────────────────────────────────
            string? dxvkVersion = null;
            var dxvkRecords = _dxvkService.LoadAllRecords();
            var dxvkRec = dxvkRecords.FirstOrDefault(r =>
                r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            if (dxvkRec != null)
            {
                dxvkVersion = dxvkRec.DxvkVersion;
            }
            else
            {
                dxvkVersion = _dxvkService.DetectInstallation(installPath, card.GraphicsApi);
            }

            // ── DLSS/Streamline ───────────────────────────────────────────────────
            var dlssDetection = _dlssStreamlineService.Detect(installPath);

            // ── Update card on UI thread ──────────────────────────────────────────
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (dcStatus == GameStatus.Installed)
                {
                    card.DcStatus = dcStatus;
                    card.DcInstalledFile = dcFile;
                    card.DcInstalledVersion = dcVersion;
                }

                if (osStatus == GameStatus.Installed)
                {
                    card.OsStatus = osStatus;
                    card.OsInstalledFile = osFile;
                    card.OsInstalledVersion = osVersion;
                }

                if (dxvkVersion != null)
                {
                    card.DxvkStatus = GameStatus.Installed;
                    card.DxvkInstalledVersion = dxvkVersion;
                    if (dxvkRec != null)
                        card.DxvkRecord = dxvkRec;
                }

                if (dlssDetection.HasAny)
                    card.ApplyDlssDetection(dlssDetection);

                card.NotifyAll();
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.ScanManualGameComponentsAsync] Failed for '{card.GameName}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Installs all emulator addons (bundle download) for a Ryubing card.
    /// Resolves snapshot URLs from the wiki-scraped mod list, downloads each addon,
    /// and deploys to the emulator's install path.
    /// </summary>
    private async Task InstallEmulatorAddonsAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath) || card.EmulatorAddonNames == null)
            return;

        card.IsInstalling = true;
        card.ActionMessage = "Installing emulator addons...";
        _crashReporter.Log($"[MainViewModel.InstallEmulatorAddonsAsync] Starting bundle install for {card.GameName}");

        int installed = 0;
        int failed = 0;
        var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);

        try
        {
            foreach (var wikiName in card.EmulatorAddonNames)
            {
                // Resolve URL from wiki-scraped mod list
                var mod = _allMods.FirstOrDefault(m =>
                    m.Name.Equals(wikiName, StringComparison.OrdinalIgnoreCase));
                if (mod?.SnapshotUrl == null)
                {
                    _crashReporter.Log($"[InstallEmulatorAddonsAsync] Skipping '{wikiName}' — not found in wiki or no snapshot URL");
                    failed++;
                    continue;
                }

                var fileName = Path.GetFileName(mod.SnapshotUrl);
                card.ActionMessage = $"Downloading {wikiName}... ({installed + 1}/{card.EmulatorAddonNames.Count})";

                try
                {
                    var cachePath = Path.Combine(DownloadPaths.RenoDX, fileName);
                    var destPath = Path.Combine(deployPath, fileName);

                    // Download to cache
                    using var response = await _http.GetAsync(mod.SnapshotUrl);
                    response.EnsureSuccessStatusCode();

                    var tempPath = cachePath + ".tmp";
                    using (var netStream = await response.Content.ReadAsStreamAsync())
                    using (var cacheFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await netStream.CopyToAsync(cacheFile);
                    }
                    if (File.Exists(cachePath)) File.Delete(cachePath);
                    File.Move(tempPath, cachePath);

                    // Deploy to game folder
                    File.Copy(cachePath, destPath, overwrite: true);
                    installed++;
                    _crashReporter.Log($"[InstallEmulatorAddonsAsync] Deployed '{fileName}' for '{wikiName}'");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[InstallEmulatorAddonsAsync] Failed to install '{wikiName}' — {ex.Message}");
                    failed++;
                }
            }

            DispatcherQueue?.TryEnqueue(() =>
            {
                card.Status = installed > 0 ? GameStatus.Installed : GameStatus.Available;
                card.InstalledAddonFileName = $"{installed} addons";
                card.ActionMessage = failed == 0
                    ? $"✅ {installed} addons installed!"
                    : $"✅ {installed} installed, {failed} failed.";
                card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
                card.NotifyAll();
                SaveLibrary();
                _filterViewModel.UpdateCounts();
            });
        }
        catch (Exception ex)
        {
            card.ActionMessage = $"❌ Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallEmulatorAddonsAsync", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.IsInstalling = false;
        }
    }

    /// <summary>
    /// Uninstalls all emulator addons by removing addon files from the deploy path.
    /// </summary>
    private void UninstallEmulatorAddons(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath) || card.EmulatorAddonNames == null) return;

        _crashReporter.Log($"[MainViewModel.UninstallEmulatorAddons] Removing emulator addons for {card.GameName}");
        var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
        int removed = 0;

        foreach (var wikiName in card.EmulatorAddonNames)
        {
            var mod = _allMods.FirstOrDefault(m =>
                m.Name.Equals(wikiName, StringComparison.OrdinalIgnoreCase));
            if (mod?.SnapshotUrl == null) continue;

            var fileName = Path.GetFileName(mod.SnapshotUrl);
            var filePath = Path.Combine(deployPath, fileName);
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); removed++; }
                catch (Exception ex) { _crashReporter.Log($"[UninstallEmulatorAddons] Failed to delete '{fileName}' — {ex.Message}"); }
            }

            // Also check emulator root (in case AddonPath wasn't set)
            var rootPath = Path.Combine(card.InstallPath, fileName);
            if (rootPath != filePath && File.Exists(rootPath))
            {
                try { File.Delete(rootPath); removed++; }
                catch (Exception ex) { _crashReporter.Log($"[UninstallEmulatorAddons] Failed to delete '{fileName}' from root — {ex.Message}"); }
            }
        }

        card.Status = GameStatus.Available;
        card.InstalledAddonFileName = null;
        card.ActionMessage = $"✖ {removed} addons removed.";
        card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
        card.NotifyAll();
        SaveLibrary();
        _filterViewModel.UpdateCounts();
    }


    private const string UltraLimiterFileName64 = "relimiter.addon64";
    private const string UltraLimiterFileName32 = "relimiter.addon32";
    private const string LegacyUltraLimiterFileName = "ultra_limiter.addon64";
    private const string LegacyUltraLimiterFileName32 = "ultra_limiter.addon32";
    private const string UltraLimiterReleasesApiUrl =
        "https://api.github.com/repos/RankFTW/ReLimiter/releases/latest";
    private const string UlChangelogUrl =
        "https://raw.githubusercontent.com/RankFTW/ReLimiter/main/CHANGELOG.md";

    internal static string GetUlFileName(bool is32Bit) =>
        is32Bit ? UltraLimiterFileName32 : UltraLimiterFileName64;

    internal static string GetUlCachePath(bool is32Bit) =>
        Path.Combine(DownloadPaths.FrameLimiter, GetUlFileName(is32Bit));

    private static readonly string UlMetaPath = Path.Combine(
        DownloadPaths.FrameLimiter, "ul_meta.json");

    /// <summary>
    /// Downloads ReLimiter from GitHub (or uses cache) and deploys to the game folder.
    /// Stores file size + SHA-256 hash for update detection.
    /// </summary>
    public async Task InstallUlAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Mutual exclusion guard: ReLimiter cannot be installed when DC is installed
        if (card.IsDcInstalled) return;

        card.UlIsInstalling = true;
        card.UlActionMessage = "Downloading ReLimiter...";
        card.UlProgress = 0;
        try
        {
            // Force fresh download on reinstall (but not update — the check already cached the new file)
            if (card.UlStatus == GameStatus.Installed)
            {
                if (File.Exists(GetUlCachePath(card.Is32Bit))) File.Delete(GetUlCachePath(card.Is32Bit));
            }

            // Download to cache if not already cached
            await EnsureUlCachedAsync(card.Is32Bit, new Progress<(string msg, double pct)>(p =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    card.UlActionMessage = p.msg;
                    card.UlProgress = p.pct;
                });
            }));

            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var destPath = Path.Combine(deployPath, GetUlFileName(card.Is32Bit));
            File.Copy(GetUlCachePath(card.Is32Bit), destPath, overwrite: true);

            // Remove legacy ultra_limiter.addon64 / ultra_limiter.addon32 if present
            var legacyPath = Path.Combine(deployPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            var legacyDirect = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyDirect)) File.Delete(legacyDirect);
            var legacyPath32 = Path.Combine(deployPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyPath32)) File.Delete(legacyPath32);
            var legacyDirect32 = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyDirect32)) File.Delete(legacyDirect32);

            // Save version metadata after successful install
            if (!string.IsNullOrEmpty(_latestUlVersion))
                SaveUlMeta(_latestUlVersion, card.Is32Bit);

            // Deploy relimiter.ini from AppData if not already present in game folder
            AuxInstallService.DeployUlIniIfAbsent(card.InstallPath);

            // Apply shared presets setting to the newly deployed INI if enabled
            if (_settingsViewModel.UlSharedPresets)
            {
                var deployPath2 = ModInstallService.GetAddonDeployPath(card.InstallPath);
                var iniFile = Path.Combine(deployPath2, "relimiter.ini");
                if (File.Exists(iniFile))
                {
                    AuxInstallService.ApplyUlSharedPresets(iniFile, true);
                    if (_settingsViewModel.UlDlssHooks)
                        AuxInstallService.ApplyUlDlssHooks(iniFile, true);
                }
            }

            DispatcherQueue?.TryEnqueue(() =>
            {
                card.UlInstalledFile = GetUlFileName(card.Is32Bit);
                card.UlInstalledVersion = _latestUlVersion?.TrimStart('v', 'V')
                    ?? ReadUlInstalledVersion(card.Is32Bit);
                card.UlStatus = GameStatus.Installed;
                card.UlActionMessage = "✅ ReLimiter installed!";
                card.UlIsInstalling = false;
                card.NotifyAll();
                card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.UlActionMessage = $"❌ Install failed: {ex.Message}";
                card.UlIsInstalling = false;
                card.NotifyAll();
            });
            _crashReporter.WriteCrashReport("InstallUl", ex, note: $"Game: {card.GameName}");
        }
    }

    /// <summary>
    /// Downloads ReLimiter to the cache directory if not already present.
    /// Fetches the latest release info from GitHub if not already cached from an update check.
    /// </summary>
    private async Task EnsureUlCachedAsync(bool is32Bit, IProgress<(string msg, double pct)>? progress = null)
    {
        if (File.Exists(GetUlCachePath(is32Bit)))
        {
            progress?.Report(("Installing from cache...", 50));
            return;
        }

        // If we don't have a download URL yet (fresh install, not from update check), fetch it
        var currentUrl = is32Bit ? _latestUlDownloadUrl32 : _latestUlDownloadUrl;
        if (string.IsNullOrEmpty(currentUrl))
        {
            await FetchLatestUlReleaseInfoAsync(is32Bit);
            currentUrl = is32Bit ? _latestUlDownloadUrl32 : _latestUlDownloadUrl;
        }

        var url = currentUrl;
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("Could not determine ReLimiter download URL from GitHub releases.");
        }

        Directory.CreateDirectory(DownloadPaths.FrameLimiter);
        var tempPath = GetUlCachePath(is32Bit) + ".tmp";

        progress?.Report(("Downloading...", 0));
        var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;
        var buffer = new byte[1024 * 1024];

        using (var net = await resp.Content.ReadAsStreamAsync())
        using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            int read;
            while ((read = await net.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report(($"Downloading... {downloaded / 1024} KB", (double)downloaded / total * 100));
            }
        }

        if (File.Exists(GetUlCachePath(is32Bit))) File.Delete(GetUlCachePath(is32Bit));
        File.Move(tempPath, GetUlCachePath(is32Bit));

        // Save version metadata for update detection
        if (!string.IsNullOrEmpty(_latestUlVersion))
            SaveUlMeta(_latestUlVersion, is32Bit);
        progress?.Report(("Downloaded!", 100));
    }

    private async Task FetchLatestUlReleaseInfoAsync(bool is32Bit)
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, UltraLimiterReleasesApiUrl).ConfigureAwait(false);
            if (json == null) return;

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                _latestUlVersion = tagEl.GetString();

            if (doc.RootElement.TryGetProperty("body", out var bodyEl))
                _latestUlReleaseBody = bodyEl.GetString();

            var targetFileName = GetUlFileName(is32Bit);
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        name.GetString()?.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) == true &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        if (is32Bit)
                            _latestUlDownloadUrl32 = urlEl.GetString();
                        else
                            _latestUlDownloadUrl = urlEl.GetString();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[FetchLatestUlReleaseInfoAsync] Failed — {ex.Message}");
        }
    }

    public void UninstallUl(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var filePath = Path.Combine(deployPath, GetUlFileName(card.Is32Bit));
            if (File.Exists(filePath))
                File.Delete(filePath);

            // Also check the game folder directly if AddonPath was different
            var directPath = Path.Combine(card.InstallPath, GetUlFileName(card.Is32Bit));
            if (File.Exists(directPath))
                File.Delete(directPath);

            // Remove legacy ultra_limiter.addon64 if present
            var legacyPath = Path.Combine(deployPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            var legacyDirect = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName);
            if (File.Exists(legacyDirect)) File.Delete(legacyDirect);

            // Remove legacy ultra_limiter.addon32 if present
            var legacyPath32 = Path.Combine(deployPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyPath32)) File.Delete(legacyPath32);
            var legacyDirect32 = Path.Combine(card.InstallPath, LegacyUltraLimiterFileName32);
            if (File.Exists(legacyDirect32)) File.Delete(legacyDirect32);

            // Remove relimiter.ini from both deploy path and game folder
            var ulIniPath = Path.Combine(deployPath, "relimiter.ini");
            if (File.Exists(ulIniPath)) File.Delete(ulIniPath);
            var ulIniDirect = Path.Combine(card.InstallPath, "relimiter.ini");
            if (File.Exists(ulIniDirect)) File.Delete(ulIniDirect);

            // Remove relimiter log and csv files (relimiter_*.log, relimiter_*.csv)
            try
            {
                foreach (var f in Directory.GetFiles(card.InstallPath, "relimiter*.log")) File.Delete(f);
                foreach (var f in Directory.GetFiles(card.InstallPath, "relimiter*.csv")) File.Delete(f);
            }
            catch { /* best effort */ }

            card.UlInstalledFile = null;
            card.UlInstalledVersion = null;
            card.UlStatus = GameStatus.NotInstalled;
            card.UlActionMessage = "✖ ReLimiter removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
        }
        catch (Exception ex)
        {
            card.UlActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallUl", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── Display Commander commands ────────────────────────────────────────────────

    private const string DcFileName64 = "zzz_display_commander.addon64";
    private const string DcFileName32 = "zzz_display_commander.addon32";
    // Legacy filenames for migration from DC Lite
    private const string LegacyDcFileName64 = "zzz_display_commander_lite.addon64";
    private const string LegacyDcFileName32 = "zzz_display_commander_lite.addon32";
    private const string DcReleasesUrl =
        "https://github.com/pmnoxx/display-commander/releases/tag/latest_build";
    private const string DcReleasesApiUrl =
        "https://api.github.com/repos/pmnoxx/display-commander/releases/tags/latest_build";

    internal static string GetDcFileName(bool is32Bit) =>
        is32Bit ? DcFileName32 : DcFileName64;

    internal static string GetLegacyDcFileName(bool is32Bit) =>
        is32Bit ? LegacyDcFileName32 : LegacyDcFileName64;

    internal static string GetDcCachePath(bool is32Bit) =>
        Path.Combine(DownloadPaths.FrameLimiter, GetDcFileName(is32Bit));

    /// <summary>
    /// Resolves the effective DC filename for a game using the priority chain:
    /// user DLL override > manifest override > default variant filename.
    /// </summary>
    internal string ResolveDcFileName(GameCardViewModel card)
    {
        // Priority 1: user DLL override
        if (card.DllOverrideEnabled)
        {
            var overrideCfg = GetDllOverride(card.GameName);
            if (overrideCfg != null && !string.IsNullOrEmpty(overrideCfg.DcFileName))
                return overrideCfg.DcFileName;
        }

        // Priority 2: manifest override
        var manifestNames = GetManifestDllNames(card.GameName);
        if (manifestNames?.Dc is { Length: > 0 } mDc)
            return mDc;

        // Priority 3: default
        return GetDcFileName(card.Is32Bit);
    }

    private static readonly string DcMetaPath = Path.Combine(
        DownloadPaths.FrameLimiter, "dc_meta.json");

    /// <summary>
    /// Downloads Display Commander to the cache directory if not already present.
    /// Fetches the latest release info from GitHub if not already cached from an update check.
    /// </summary>
    private async Task EnsureDcCachedAsync(bool is32Bit, IProgress<(string msg, double pct)>? progress = null)
    {
        // Clean up legacy DC Lite cache files
        var legacyCachePath64 = Path.Combine(DownloadPaths.FrameLimiter, LegacyDcFileName64);
        var legacyCachePath32 = Path.Combine(DownloadPaths.FrameLimiter, LegacyDcFileName32);
        if (File.Exists(legacyCachePath64)) { File.Delete(legacyCachePath64); _crashReporter.Log("[DC] Cleaned up legacy lite cache (64-bit)"); }
        if (File.Exists(legacyCachePath32)) { File.Delete(legacyCachePath32); _crashReporter.Log("[DC] Cleaned up legacy lite cache (32-bit)"); }

        if (File.Exists(GetDcCachePath(is32Bit)))
        {
            progress?.Report(("Installing from cache...", 50));
            return;
        }

        // If we don't have a download URL yet (fresh install, not from update check), fetch it
        var currentUrl = is32Bit ? _latestDcDownloadUrl32 : _latestDcDownloadUrl;
        if (string.IsNullOrEmpty(currentUrl))
        {
            await FetchLatestDcReleaseInfoAsync(is32Bit);
            currentUrl = is32Bit ? _latestDcDownloadUrl32 : _latestDcDownloadUrl;
        }

        var url = currentUrl;
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("Could not determine Display Commander download URL from GitHub releases.");
        }

        Directory.CreateDirectory(DownloadPaths.FrameLimiter);
        var tempPath = GetDcCachePath(is32Bit) + ".tmp";

        progress?.Report(("Downloading...", 0));
        var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;
        var buffer = new byte[1024 * 1024];

        using (var net = await resp.Content.ReadAsStreamAsync())
        using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            int read;
            while ((read = await net.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report(($"Downloading... {downloaded / 1024} KB", (double)downloaded / total * 100));
            }
        }

        if (File.Exists(GetDcCachePath(is32Bit))) File.Delete(GetDcCachePath(is32Bit));
        File.Move(tempPath, GetDcCachePath(is32Bit));

        // Save version metadata for update detection
        if (!string.IsNullOrEmpty(_latestDcVersion))
            SaveDcMeta(_latestDcVersion, is32Bit);
        progress?.Report(("Downloaded!", 100));
    }

    private async Task FetchLatestDcReleaseInfoAsync(bool is32Bit)
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, DcReleasesApiUrl).ConfigureAwait(false);
            if (json == null) return;

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
            {
                var tag = tagEl.GetString();
                // DC uses a fixed "latest_build" tag — extract real version from release body
                if (tag == "latest_build" && doc.RootElement.TryGetProperty("body", out var bodyEl))
                {
                    var body = bodyEl.GetString() ?? "";
                    _latestDcReleaseBody = body;
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(
                        body, @"\*{0,2}Version in binaries\*{0,2}:\s*([\d.]+)");
                    if (versionMatch.Success)
                        _latestDcVersion = versionMatch.Groups[1].Value;
                    else if (doc.RootElement.TryGetProperty("name", out var nameEl))
                        _latestDcVersion = nameEl.GetString();
                    else
                        _latestDcVersion = tag;
                }
                else
                {
                    _latestDcVersion = tag;
                    if (doc.RootElement.TryGetProperty("body", out var bodyEl2))
                        _latestDcReleaseBody = bodyEl2.GetString();
                }
            }

            var targetFileName = GetDcFileName(is32Bit);
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        name.GetString()?.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) == true &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        if (is32Bit)
                            _latestDcDownloadUrl32 = urlEl.GetString();
                        else
                            _latestDcDownloadUrl = urlEl.GetString();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[FetchLatestDcReleaseInfoAsync] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads Display Commander from GitHub (or uses cache) and deploys to the game folder.
    /// Creates an AuxInstalledRecord with AddonType "DisplayCommander".
    /// Mutual exclusion: returns early if ReLimiter is already installed.
    /// </summary>
    public async Task InstallDcAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Mutual exclusion guard: DC cannot be installed when ReLimiter is installed
        if (card.IsUlInstalled) return;

        card.DcIsInstalling = true;
        card.DcActionMessage = "Downloading Display Commander...";
        card.DcProgress = 0;
        try
        {
            // Force fresh download on reinstall (but not update — the check already cached the new file)
            if (card.DcStatus == GameStatus.Installed)
            {
                if (File.Exists(GetDcCachePath(card.Is32Bit))) File.Delete(GetDcCachePath(card.Is32Bit));
            }

            // Download to cache if not already cached
            await EnsureDcCachedAsync(card.Is32Bit, new Progress<(string msg, double pct)>(p =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    card.DcActionMessage = p.msg;
                    card.DcProgress = p.pct;
                });
            }));

            // Resolve the target filename using the priority chain
            var targetFileName = ResolveDcFileName(card);

            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var destPath = Path.Combine(deployPath, targetFileName);

            // Migration: remove legacy DC Lite file if present
            var legacyName = GetLegacyDcFileName(card.Is32Bit);
            var legacyPath = Path.Combine(deployPath, legacyName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
                _crashReporter.Log($"[InstallDcAsync] Removed legacy DC Lite file '{legacyName}' from '{card.GameName}'");
            }
            // Also check game root if deploy path differs
            if (!deployPath.Equals(card.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                var legacyRootPath = Path.Combine(card.InstallPath, legacyName);
                if (File.Exists(legacyRootPath))
                {
                    File.Delete(legacyRootPath);
                    _crashReporter.Log($"[InstallDcAsync] Removed legacy DC Lite file '{legacyName}' from game root '{card.GameName}'");
                }
            }

            File.Copy(GetDcCachePath(card.Is32Bit), destPath, overwrite: true);

            // Save version metadata after successful install
            if (!string.IsNullOrEmpty(_latestDcVersion))
                SaveDcMeta(_latestDcVersion, card.Is32Bit);

            // Deploy DisplayCommander.ini from AppData if not already present in game folder
            AuxInstallService.DeployDcIniIfAbsent(card.InstallPath);

            // Create and persist AuxInstalledRecord for DC tracking
            var dcRecord = new AuxInstalledRecord
            {
                GameName    = card.GameName,
                InstallPath = card.InstallPath,
                AddonType   = "DisplayCommander",
                InstalledAs = targetFileName,
                InstalledAt = DateTime.UtcNow,
            };
            _auxInstaller.SaveAuxRecord(dcRecord);

            DispatcherQueue?.TryEnqueue(() =>
            {
                card.DcInstalledFile = targetFileName;
                // Try PE file version first, then cached version, then meta
                var peVersion = Services.AuxInstallService.ReadInstalledVersion(
                    ModInstallService.GetAddonDeployPath(card.InstallPath), targetFileName);
                var cachedVersion = _latestDcVersion?.TrimStart('v', 'V');
                // Don't use the tag name "latest_build" as a version
                if (cachedVersion == "latest_build") cachedVersion = null;
                card.DcInstalledVersion = peVersion ?? cachedVersion ?? ReadDcInstalledVersion(card.Is32Bit);
                card.DcStatus = GameStatus.Installed;
                card.DcActionMessage = "✅ Display Commander installed!";
                card.DcIsInstalling = false;
                card.NotifyAll();
                card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.DcActionMessage = $"❌ Install failed: {ex.Message}";
                card.DcIsInstalling = false;
                card.NotifyAll();
            });
            _crashReporter.WriteCrashReport("InstallDc", ex, note: $"Game: {card.GameName}");
        }
    }

    public void UninstallDc(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            // Remove the DC file using the filename it was installed as
            if (!string.IsNullOrEmpty(card.DcInstalledFile))
            {
                var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
                var filePath = Path.Combine(deployPath, card.DcInstalledFile);
                if (File.Exists(filePath))
                    File.Delete(filePath);

                // Also check the game folder directly if AddonPath was different
                var directPath = Path.Combine(card.InstallPath, card.DcInstalledFile);
                if (File.Exists(directPath))
                    File.Delete(directPath);
            }

            // Remove the AuxInstalledRecord for DisplayCommander
            var dcRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, "DisplayCommander");
            if (dcRecord != null)
                _auxInstaller.RemoveRecord(dcRecord);

            card.DcInstalledFile = null;
            card.DcInstalledVersion = null;
            card.DcStatus = GameStatus.NotInstalled;
            card.DcActionMessage = "✖ Display Commander removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallDc", ex, note: $"Game: {card.GameName}");
        }
    }

    // Cached latest DC release info from the update check
    private string? _latestDcVersion;
    private string? _latestDcDownloadUrl;
    private string? _latestDcDownloadUrl32;
    private string? _latestDcReleaseBody;

    /// <summary>Markdown body of the latest Display Commander GitHub release, or null if not yet fetched.</summary>
    public string? LatestDcReleaseBody => _latestDcReleaseBody;

    /// <summary>
    /// Fetches the ReLimiter CHANGELOG.md from GitHub and extracts the
    /// entries for the installed version and the 2 previous versions (3 total).
    /// </summary>
    public async Task EnsureUlReleaseBodyAsync(string? installedVersion = null)
    {
        if (_latestUlReleaseBody != null) return;
        try
        {
            var response = await _http.GetAsync(UlChangelogUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[EnsureUlReleaseBodyAsync] HTTP {(int)response.StatusCode} fetching CHANGELOG.md");
                return;
            }

            var changelog = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _latestUlReleaseBody = ExtractDcChangelogEntries(changelog, installedVersion, 3);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[EnsureUlReleaseBodyAsync] Failed — {ex.Message}");
        }
    }

    private const string DcChangelogUrl =
        "https://raw.githubusercontent.com/pmnoxx/display-commander/main/CHANGELOG.md";

    /// <summary>
    /// Fetches the Display Commander CHANGELOG.md from GitHub and extracts the
    /// entries for the installed version and the 2 previous versions (3 total).
    /// The installed version is matched by the first 4 version numbers.
    /// </summary>
    public async Task EnsureDcReleaseBodyAsync(string? installedVersion = null)
    {
        try
        {
            var response = await _http.GetAsync(DcChangelogUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[EnsureDcReleaseBodyAsync] HTTP {(int)response.StatusCode} fetching CHANGELOG.md");
                return;
            }

            var changelog = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _latestDcReleaseBody = ExtractDcChangelogEntries(changelog, installedVersion, 3);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[EnsureDcReleaseBodyAsync] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts <paramref name="count"/> changelog entries starting from the entry
    /// matching <paramref name="installedVersion"/> (by first 4 version numbers).
    /// Falls back to the first <paramref name="count"/> entries if no match is found.
    /// </summary>
    internal static string ExtractDcChangelogEntries(string changelog, string? installedVersion, int count)
    {
        // Split into version sections by "## " headers that start with a version number
        // Handles both "## v0.14.17" and "## 3.2.0" formats
        var sections = new List<(string header, string body)>();
        var lines = changelog.Split('\n');
        string? currentHeader = null;
        var currentBody = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (IsVersionHeader(line))
            {
                if (currentHeader != null)
                    sections.Add((currentHeader, currentBody.ToString().TrimEnd()));
                currentHeader = line.TrimEnd();
                currentBody.Clear();
            }
            else if (currentHeader != null)
            {
                currentBody.AppendLine(line);
            }
        }
        if (currentHeader != null)
            sections.Add((currentHeader, currentBody.ToString().TrimEnd()));

        if (sections.Count == 0)
            return "(No changelog entries found)";

        // Find the section matching the installed version
        int startIndex = 0;
        if (!string.IsNullOrEmpty(installedVersion))
        {
            // Normalize: strip leading 'v', trim trailing ".0" segments for flexible matching
            var normalized = installedVersion.TrimStart('v', 'V').Trim();

            for (int i = 0; i < sections.Count; i++)
            {
                var headerVersion = ExtractVersionFromHeader(sections[i].header);

                // Compare: installed "3.2.0" should match header "3.2.0"
                // installed "0.14.17.0" should match header "0.14.17"
                if (string.Equals(headerVersion, normalized, StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith(headerVersion + ".", StringComparison.OrdinalIgnoreCase)
                    || headerVersion.StartsWith(normalized + ".", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerVersion, normalized.Split('.').Length > 3
                        ? string.Join(".", normalized.Split('.')[..3]) : normalized, StringComparison.OrdinalIgnoreCase))
                {
                    startIndex = i;
                    break;
                }
            }
        }

        // Take 'count' entries starting from the matched index
        var result = new System.Text.StringBuilder();
        for (int i = startIndex; i < Math.Min(startIndex + count, sections.Count); i++)
        {
            if (result.Length > 0)
                result.AppendLine();
            result.AppendLine(sections[i].header);
            result.AppendLine(sections[i].body);
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns true if the line is a changelog version header.
    /// Matches "## v0.14.17", "## 3.2.0", "## v0.13.189 (2026-04-14)", etc.
    /// </summary>
    private static bool IsVersionHeader(string line)
    {
        if (!line.StartsWith("## ", StringComparison.Ordinal))
            return false;
        var rest = line.AsSpan(3).TrimStart();
        // Skip optional 'v' prefix
        if (rest.Length > 0 && (rest[0] == 'v' || rest[0] == 'V'))
            rest = rest[1..];
        // Must start with a digit (version number)
        return rest.Length > 0 && char.IsDigit(rest[0]);
    }

    /// <summary>
    /// Extracts the version string from a changelog header line.
    /// "## v0.14.17 (2026-04-14)" → "0.14.17"
    /// "## 3.2.0" → "3.2.0"
    /// </summary>
    private static string ExtractVersionFromHeader(string header)
    {
        var version = header;
        // Strip "## " prefix
        if (version.StartsWith("## ", StringComparison.Ordinal))
            version = version[3..];
        version = version.TrimStart();
        // Strip optional 'v' prefix
        if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V'))
            version = version[1..];
        version = version.Trim();
        // Take only the version part (before any space/parenthesis)
        var spaceIdx = version.IndexOfAny(new[] { ' ', '(' });
        if (spaceIdx >= 0)
            version = version[..spaceIdx].Trim();
        return version;
    }

}
