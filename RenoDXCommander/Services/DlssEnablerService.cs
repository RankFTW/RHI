using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the DLSS Enabler DLL — download, staging, install, uninstall, and update detection.
/// Hosted on RankFTW/rhi-repo GitHub releases under the DLSS-Enabler- tag prefix.
/// </summary>
public class DlssEnablerService
{
    private const string StagedFileName = "version.dll";
    private const string DeployFileName = "dlss-enabler-headless.dll";
    private const string TagPrefix = "DLSS-Enabler-";
    private static readonly string GitHubApiUrl = "https://api.github.com/repos/RankFTW/rhi-repo/releases?per_page=100";

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly IGameNameService _gameNameService;
    private readonly IOptiScalerService _optiScalerService;

    private readonly string _stagingDir;
    private readonly string _versionFile;

    public DlssEnablerService(
        HttpClient http,
        ICrashReporter crashReporter,
        IGameNameService gameNameService,
        IOptiScalerService optiScalerService)
    {
        _http = http;
        _crashReporter = crashReporter;
        _gameNameService = gameNameService;
        _optiScalerService = optiScalerService;

        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "optiDE");
        _versionFile = Path.Combine(_stagingDir, "version.txt");
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>Whether the staging directory has version.dll ready for deployment.</summary>
    public bool IsStagingReady => File.Exists(Path.Combine(_stagingDir, StagedFileName));

    /// <summary>The currently staged version string (tag suffix), or null if not staged.</summary>
    public string? StagedVersion => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    /// <summary>Whether an update is available (set after CheckForUpdateAsync).</summary>
    public bool HasUpdate { get; private set; }

    /// <summary>The latest remote version string (set after CheckForUpdateAsync).</summary>
    public string? LatestVersion { get; private set; }

    // ── Staging ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks GitHub for a newer version than what's currently staged.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync()
    {
        var (version, _) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
        {
            _crashReporter.Log("[DlssEnablerService.CheckForUpdateAsync] Could not resolve latest version");
            return false;
        }

        LatestVersion = version;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[DlssEnablerService.CheckForUpdateAsync] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
        return HasUpdate;
    }

    /// <summary>
    /// Ensures version.dll is staged. Downloads if not present or if an update is available.
    /// After a successful download, auto-deploys to all games where DLSS Enabler is enabled.
    /// </summary>
    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[DlssEnablerService.EnsureStagingAsync] Staging already valid — skipping download");
            return;
        }

        Directory.CreateDirectory(_stagingDir);
        progress?.Report(("Загрузка DLSS Enabler...", 10));

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[DlssEnablerService.EnsureStagingAsync] Could not resolve latest release");
            return;
        }

        progress?.Report(("Загрузка DLSS Enabler...", 30));

        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            var destPath = Path.Combine(_stagingDir, StagedFileName);

            // If the download URL points to a zip, extract version.dll from it
            if (downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tempZip = Path.Combine(_stagingDir, "_download.zip.tmp");
                await File.WriteAllBytesAsync(tempZip, bytes).ConfigureAwait(false);
                using (var zip = System.IO.Compression.ZipFile.OpenRead(tempZip))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, StagedFileName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] '{StagedFileName}' not found in zip");
                        File.Delete(tempZip);
                        return;
                    }
                    using var entryStream = entry.Open();
                    using var outStream = File.Create(destPath);
                    await entryStream.CopyToAsync(outStream).ConfigureAwait(false);
                }
                File.Delete(tempZip);
                _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Extracted {StagedFileName} from zip ({new FileInfo(destPath).Length} bytes)");
            }
            else
            {
                await File.WriteAllBytesAsync(destPath, bytes).ConfigureAwait(false);
            }

            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Staged v{version}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Download failed ({downloadUrl}) — {ex.Message}");
            progress?.Report(($"Не удалось скачать DLSS Enabler: {ex.Message}", 0));
            return;
        }

        progress?.Report(("DLSS Enabler готов", 90));

        // ── Auto-deploy to all games where DLSS Enabler is enabled ────────────
        try
        {
            var stagedDll = Path.Combine(_stagingDir, StagedFileName);
            var records = _optiScalerService.LoadAllRecords()
                .Where(r => !string.IsNullOrEmpty(r.InstallPath))
                .ToList();

            foreach (var rec in records)
            {
                var compositeKey = GameKey.From(rec.GameName, rec.Store ?? "").ToKey();
                bool enabled = _gameNameService.OsDeployDlssEnabler.Contains(compositeKey)
                            || _gameNameService.OsDeployDlssEnabler.Contains(rec.GameName);
                if (!enabled) continue;

                var optiScalerDir = Path.Combine(rec.InstallPath, "OptiScaler");
                var dest = Path.Combine(optiScalerDir, DeployFileName);
                if (!File.Exists(dest)) continue; // only update if already deployed

                try
                {
                    File.Copy(stagedDll, dest, overwrite: true);
                    _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Auto-deployed to '{rec.GameName}' at {optiScalerDir}");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Auto-deploy failed for '{rec.GameName}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Auto-deploy loop failed — {ex.Message}");
        }

        progress?.Report(("DLSS Enabler готов", 100));
    }

    // ── Install / Uninstall / Detection ───────────────────────────────────────

    /// <summary>
    /// Installs DLSS Enabler into &lt;optiScalerPath&gt;\dlss-enabler-headless.dll.
    /// </summary>
    public async Task InstallAsync(string optiScalerPath)
    {
        if (string.IsNullOrEmpty(optiScalerPath)) return;

        await EnsureStagingAsync().ConfigureAwait(false);
        if (!IsStagingReady)
        {
            _crashReporter.Log("[DlssEnablerService.InstallAsync] Staging not ready — cannot install");
            return;
        }

        try
        {
            Directory.CreateDirectory(optiScalerPath);
            var src = Path.Combine(_stagingDir, StagedFileName);
            var dest = Path.Combine(optiScalerPath, DeployFileName);
            File.Copy(src, dest, overwrite: true);
            _crashReporter.Log($"[DlssEnablerService.InstallAsync] Deployed to {optiScalerPath}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.InstallAsync] Deploy failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Uninstalls DLSS Enabler by deleting dlss-enabler-headless.dll from the OptiScaler folder.
    /// </summary>
    public void Uninstall(string optiScalerPath)
    {
        if (string.IsNullOrEmpty(optiScalerPath)) return;
        var filePath = Path.Combine(optiScalerPath, DeployFileName);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _crashReporter.Log($"[DlssEnablerService.Uninstall] Removed from {optiScalerPath}");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.Uninstall] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if dlss-enabler-headless.dll exists in the given OptiScaler folder.
    /// </summary>
    public bool IsInstalledIn(string optiScalerPath)
        => !string.IsNullOrEmpty(optiScalerPath)
        && File.Exists(Path.Combine(optiScalerPath, DeployFileName));

    // ── Standalone install (game root, user-chosen DLL name) ─────────────────

    /// <summary>
    /// Installs DLSS Enabler as a standalone DLL in the game folder root.
    /// If reinstalling under a different name, removes the old file first.
    /// </summary>
    public async Task<bool> InstallStandaloneAsync(
        string gameName, string installPath, string store,
        string dllName, string? existingInstalledAs,
        IProgress<(string message, double percent)>? progress = null)
    {
        if (string.IsNullOrEmpty(installPath)) return false;

        await EnsureStagingAsync(progress).ConfigureAwait(false);
        if (!IsStagingReady)
        {
            _crashReporter.Log("[DlssEnablerService.InstallStandaloneAsync] Staging not ready");
            return false;
        }

        // Remove old install if renaming
        if (!string.IsNullOrEmpty(existingInstalledAs) &&
            !existingInstalledAs.Equals(dllName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(installPath, existingInstalledAs);
            try { if (File.Exists(oldPath)) File.Delete(oldPath); }
            catch (Exception ex) { _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Old file cleanup failed — {ex.Message}"); }
        }

        var src  = Path.Combine(_stagingDir, StagedFileName);
        var dest = Path.Combine(installPath, dllName);

        try
        {
            File.Copy(src, dest, overwrite: true);
            _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Installed as '{dllName}' in '{installPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Copy failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Removes the standalone DLSS Enabler DLL from the game folder.</summary>
    public bool UninstallStandalone(string installPath, string dllName)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(dllName)) return false;
        var filePath = Path.Combine(installPath, dllName);
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            _crashReporter.Log($"[DlssEnablerService.UninstallStandalone] Removed '{dllName}' from '{installPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.UninstallStandalone] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Returns true if the standalone DLL is installed (the tracked DLL name exists on disk).</summary>
    public bool IsStandaloneInstalledIn(string installPath, string? dllName)
        => !string.IsNullOrEmpty(installPath) && !string.IsNullOrEmpty(dllName)
        && File.Exists(Path.Combine(installPath, dllName));

    /// <summary>
    /// After EnsureStagingAsync downloads a new version, auto-redeploy to all games
    /// with standalone DLSS Enabler installed (tracked by DeInstalledAs dict).
    /// </summary>
    public void AutoUpdateStandaloneInstalls(IReadOnlyList<GameCardViewModel> cards, MainViewModel viewModel)
    {
        if (!IsStagingReady) return;
        var src = Path.Combine(_stagingDir, StagedFileName);
        foreach (var card in cards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            var installedAs = viewModel.GetDeInstalledAs(card.GameName, card.Source ?? "");
            if (string.IsNullOrEmpty(installedAs)) continue;
            var dest = Path.Combine(card.InstallPath, installedAs);
            if (!File.Exists(dest)) continue; // only update if already deployed
            try
            {
                File.Copy(src, dest, overwrite: true);
                _crashReporter.Log($"[DlssEnablerService.AutoUpdateStandaloneInstalls] Updated '{card.GameName}' → '{installedAs}'");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[DlssEnablerService.AutoUpdateStandaloneInstalls] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.Add("User-Agent", "RHI");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[DlssEnablerService] GitHub API returned {response.StatusCode}");
                return (null, null);
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            // Find the latest release with the DLSS-Enabler- tag prefix
            // Collect all matching DLSS-Enabler releases, then pick the highest version
            var candidates = new List<(string version, string downloadUrl, Version parsed)>();

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString();
                if (tag == null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                var version = tag.Substring(TagPrefix.Length);

                // Find the zip asset download URL (asset named DLSS-Enabler-*.zip or version.dll)
                string? downloadUrl = null;
                if (release.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                        if (assetName == null) continue;
                        // Accept zip archives named with the tag prefix OR direct version.dll
                        bool isZip = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                  && assetName.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase);
                        bool isDirect = string.Equals(assetName, StagedFileName, StringComparison.OrdinalIgnoreCase);
                        if ((isZip || isDirect) && asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            downloadUrl = urlEl.GetString();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl)) continue;

                if (Version.TryParse(version, out var parsed))
                    candidates.Add((version, downloadUrl!, parsed));
                else
                    candidates.Add((version, downloadUrl!, new Version(0, 0)));
            }

            if (candidates.Count == 0)
            {
                _crashReporter.Log("[DlssEnablerService] No release found with DLSS-Enabler- tag");
                return (null, null);
            }

            // Pick the release with the highest version number
            var best = candidates.OrderByDescending(c => c.parsed).First();
            return (best.version, best.downloadUrl);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService] FetchLatestReleaseInfo failed — {ex.Message}");
            return (null, null);
        }
    }
}
