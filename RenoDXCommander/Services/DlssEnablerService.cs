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
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter _crashReporter;
    private readonly IGameNameService _gameNameService;
    private readonly IOptiScalerService _optiScalerService;

    private readonly string _stagingDir;
    private readonly string _versionFile;

    public DlssEnablerService(
        HttpClient http,
        ICrashReporter crashReporter,
        IGameNameService gameNameService,
        IOptiScalerService optiScalerService, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
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
        progress?.Report(("Downloading DLSS Enabler...", 10));

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[DlssEnablerService.EnsureStagingAsync] Could not resolve latest release");
            return;
        }

        progress?.Report(("Downloading DLSS Enabler...", 30));

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

            var previousVersion = StagedVersion;
            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Staged v{version}");
            UpdateHistory.Record("Component", "DLSS Enabler", previousVersion, version);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssEnablerService.EnsureStagingAsync] Download failed ({downloadUrl}) — {ex.Message}");
            progress?.Report(($"DLSS Enabler download failed: {ex.Message}", 0));
            return;
        }

        progress?.Report(("DLSS Enabler ready", 90));

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

        progress?.Report(("DLSS Enabler ready", 100));
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

        var src  = Path.Combine(_stagingDir, StagedFileName);
        var dest = Path.Combine(installPath, dllName);
        var backup = dest + ".original";

        // A file of that name that isn't DLSS Enabler (the game's own, or another mod's) is kept as .original and
        // put back on uninstall. A real .original is never replaced: then the name is refused.
        bool destIsOurs = IsOurDll(dest);
        try
        {
            if (File.Exists(dest) && !destIsOurs && File.Exists(backup))
            {
                // Either backup kind blocks it: a real one can't be replaced, and an empty one says the game had no
                // file of that name, so this one came from something else since (a game update, another mod)
                _crashReporter.Log(new FileInfo(backup).Length > 0
                    ? $"[DlssEnablerService.InstallStandaloneAsync] '{dllName}' in '{installPath}' isn't DLSS Enabler and '{dllName}.original' already holds a backup — not installed"
                    : $"[DlssEnablerService.InstallStandaloneAsync] '{dllName}' in '{installPath}' isn't DLSS Enabler and was put there after RHI's empty '{dllName}.original' marker (another program's file) — not installed");
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Couldn't check '{backup}' — {ex.Message}");
            return false;
        }

        // Remove old install if renaming
        if (!string.IsNullOrEmpty(existingInstalledAs) &&
            !existingInstalledAs.Equals(dllName, StringComparison.OrdinalIgnoreCase))
            RemoveStandaloneFile(installPath, existingInstalledAs, "InstallStandaloneAsync");

        bool hadBackup = File.Exists(backup);
        try
        {
            AuxInstallService.SentinelBackupReplacingOwn(dest, destIsOurs);
            if (File.Exists(dest) && !destIsOurs)
            {
                // The other file couldn't be moved aside: don't overwrite it
                _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Couldn't back up '{dest}' — not installed");
                return false;
            }
            File.Copy(src, dest, overwrite: true);
            _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Installed as '{dllName}' in '{installPath}'");
            EnsureStandaloneConfig(installPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[DlssEnablerService.InstallStandaloneAsync] Copy failed — {ex.Message}");
            if (!hadBackup) UndoNewBackup(dest, backup);
            return false;
        }
    }

    /// <summary>
    /// Writes DLSS Enabler's default dlss-enabler.ini beside the standalone DLL when the game has none, so its
    /// settings can be changed in RHI before the first launch. An existing file (the user's settings) is never replaced.
    /// Not removed on uninstall: it holds the user's settings, like RTX Encore's rtx-encore.jsonc.
    /// </summary>
    public void EnsureStandaloneConfig(string installPath)
    {
        try
        {
            if (DlssEnablerConfig.CreateIfMissing(installPath))
                _crashReporter.Log($"[DlssEnablerService.EnsureStandaloneConfig] Created {DlssEnablerConfig.FileName} in '{installPath}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[DlssEnablerService.EnsureStandaloneConfig] Couldn't create {DlssEnablerConfig.FileName} in '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> is RHI's DLSS Enabler: its version info says DLSS Enabler, or it is the
    /// staged copy (in case a build's ProductName is ever different).
    /// </summary>
    internal bool IsOurDll(string path)
        => IsDlssEnablerDll(path) || MatchesFile(path, Path.Combine(_stagingDir, StagedFileName));

    /// <summary>
    /// True when both files exist and are the same build: same size and same non-empty FileVersion, or, without a
    /// FileVersion, the same bytes.
    /// </summary>
    internal static bool MatchesFile(string path, string referencePath)
    {
        try
        {
            if (!File.Exists(path) || !File.Exists(referencePath)) return false;
            if (new FileInfo(path).Length != new FileInfo(referencePath).Length) return false;
            var a = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion?.Trim();
            var b = System.Diagnostics.FileVersionInfo.GetVersionInfo(referencePath).FileVersion?.Trim();
            if (!string.IsNullOrEmpty(a) || !string.IsNullOrEmpty(b))
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(referencePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DlssEnablerService.MatchesFile] Couldn't compare '{path}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>True when <paramref name="path"/> is a DLSS Enabler DLL (version-info ProductName).</summary>
    internal static bool IsDlssEnablerDll(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var product = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName ?? "";
            return product.Trim().Equals("DLSS Enabler", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DlssEnablerService.IsDlssEnablerDll] Couldn't read '{path}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a standalone DLSS Enabler at <paramref name="dllName"/>: restores the file it replaced (sentinel
    /// pattern), or deletes it (installs from before RHI wrote sentinels). A file there that isn't DLSS Enabler
    /// (something replaced it since) is left alone, with its backup.
    /// False when the file was kept (not DLSS Enabler, or it couldn't be removed).
    /// </summary>
    internal bool RemoveStandaloneFile(string installPath, string dllName, string caller)
    {
        var path = Path.Combine(installPath, dllName);
        try
        {
            if (File.Exists(path) && !IsOurDll(path))
            {
                _crashReporter.Log($"[DlssEnablerService.{caller}] Kept '{path}' — it isn't DLSS Enabler");
                return false;
            }
            if (File.Exists(path + ".original")) AuxInstallService.SentinelRestore(path);
            else if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path) && IsOurDll(path))
            {
                _crashReporter.Log($"[DlssEnablerService.{caller}] '{path}' is still there after removal");
                return false;
            }
            _crashReporter.Log($"[DlssEnablerService.{caller}] Removed '{dllName}' from '{installPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[DlssEnablerService.{caller}] Couldn't remove '{path}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Reverses a backup made by this install after the copy failed.</summary>
    private void UndoNewBackup(string dest, string backup)
    {
        try
        {
            if (!File.Exists(backup) || File.Exists(dest)) return;
            if (new FileInfo(backup).Length > 0) File.Move(backup, dest);
            else File.Delete(backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[DlssEnablerService.UndoNewBackup] Couldn't restore '{dest}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the standalone DLSS Enabler DLL from the game folder. False when the file was kept (it isn't
    /// DLSS Enabler, or it couldn't be removed): the caller keeps its record then.
    /// </summary>
    public bool UninstallStandalone(string installPath, string dllName)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(dllName)) return false;
        return RemoveStandaloneFile(installPath, dllName, "UninstallStandalone");
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
            if (card.IsHidden) continue; // hidden games are left alone
            var installedAs = viewModel.GetDeInstalledAs(card.GameName, card.Source ?? "");
            if (string.IsNullOrEmpty(installedAs)) continue;
            var dest = Path.Combine(card.InstallPath, installedAs);
            if (!File.Exists(dest)) continue; // only update if already deployed
            if (!IsOurDll(dest))
            {
                // Another file took the name since (a game update, another mod): never overwrite it
                _crashReporter.Log($"[DlssEnablerService.AutoUpdateStandaloneInstalls] Skipped '{card.GameName}' — '{installedAs}' isn't DLSS Enabler");
                continue;
            }
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
            var json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[DlssEnablerService] GitHub API request failed");
                return (null, null);
            }
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
