// MainViewModel.Install.Nexus.cs — Nexus Mods direct download and install logic.
// Gated behind DevUnlockService.IsUnlocked. Premium users get silent CDN downloads.
// Free users with a registered nxm:// handler get one-click download via the NXM key path.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>
    /// Downloads and installs a specific Nexus file for a card (premium path).
    /// Resolves the latest MAIN file automatically.
    /// </summary>
    public async Task InstallNexusModAsync(GameCardViewModel card)
    {
        if (!FeatureFlags.NexusMods) return;

        var nexusDl = App.Services.GetRequiredService<NexusDownloadService>();
        if (!nexusDl.IsApiKeyConfigured || !nexusDl.IsPremium) return;

        var parsed = NexusUpdateService.ParseNexusUrl(card.NexusUrl);
        if (parsed == null)
        {
            _crashReporter.Log($"[MainViewModel.InstallNexusModAsync] Cannot parse NexusUrl for '{card.GameName}'");
            return;
        }

        if (!await CheckInstallWarningAsync(card.GameName, "renodx")) return;

        card.IsInstalling = true;
        card.ActionMessage = "Загрузка информации о модах...";

        try
        {
            var latestFile = await nexusDl.GetLatestMainFileAsync(parsed.Value.Domain, parsed.Value.ModId).ConfigureAwait(false);
            if (latestFile == null)
            {
                _crashReporter.Log($"[MainViewModel.InstallNexusModAsync] No MAIN file found for '{card.GameName}'");
                DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Файлы для скачивания не найдены."; card.IsInstalling = false; });
                return;
            }

            await InstallNexusFileAsync(card, parsed.Value.Domain, parsed.Value.ModId, latestFile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.InstallNexusModAsync] '{card.GameName}' — {ex.Message}");
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = $"Не удалось установить: {ex.Message}"; card.IsInstalling = false; });
        }
    }

    /// <summary>
    /// Handles an incoming NXM link — finds the matching card by domain+modId and installs.
    /// Works for both premium (CDN) and free (NXM key) users.
    /// </summary>
    public async Task HandleNxmLinkAsync(NxmLink link)
    {
        if (!FeatureFlags.NexusMods) return;

        _crashReporter.Log($"[MainViewModel.HandleNxmLinkAsync] NXM: {link.Domain}/mods/{link.ModId}/files/{link.FileId}");

        // Find the card whose NexusUrl matches this domain+modId
        var card = AllCards.FirstOrDefault(c =>
        {
            var parsed = NexusUpdateService.ParseNexusUrl(c.NexusUrl);
            return parsed.HasValue
                && string.Equals(parsed.Value.Domain, link.Domain, StringComparison.OrdinalIgnoreCase)
                && parsed.Value.ModId == link.ModId;
        });

        if (card == null)
        {
            _crashReporter.Log($"[MainViewModel.HandleNxmLinkAsync] No card found for {link.Domain}/mods/{link.ModId}");
            return;
        }

        if (!await CheckInstallWarningAsync(card.GameName, "renodx")) return;

        card.IsInstalling = true;
        card.ActionMessage = "Определение ссылки на скачивание...";

        try
        {
            var nexusDl = App.Services.GetRequiredService<NexusDownloadService>();

            // Resolve the download URI — try NXM key path first (works for both premium and free)
            string? uri = null;
            if (!string.IsNullOrEmpty(link.Key) && !string.IsNullOrEmpty(link.Expires))
            {
                uri = await nexusDl.GetDownloadUriWithNxmKeyAsync(
                    link.Domain, link.ModId, link.FileId,
                    link.Key, link.Expires, link.UserId).ConfigureAwait(false);
            }

            // Fall back to premium direct download if NXM key failed/absent
            if (uri == null && nexusDl.IsPremium)
            {
                uri = await nexusDl.GetDownloadUriAsync(
                    link.Domain, link.ModId, link.FileId).ConfigureAwait(false);
            }

            if (uri == null)
            {
                _crashReporter.Log($"[MainViewModel.HandleNxmLinkAsync] Could not resolve download URI for '{card.GameName}'");
                DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Не удалось определить ссылку на скачивание."; card.IsInstalling = false; });
                return;
            }

            // Build a synthetic NexusModFile from what we know (NXM doesn't include filename/version)
            var fileInfo = new NexusModFile
            {
                FileId    = link.FileId,
                FileName  = $"nexus_{link.Domain}_{link.ModId}_{link.FileId}.zip",
                CategoryName = "MAIN",
            };

            // Try to enrich with actual file info (best-effort, won't fail the install)
            try
            {
                var files = await nexusDl.GetModFilesAsync(link.Domain, link.ModId).ConfigureAwait(false);
                var match = files.FirstOrDefault(f => f.FileId == link.FileId);
                if (match != null) fileInfo = match;
            }
            catch { /* non-fatal */ }

            await DownloadAndDeployNexusFileAsync(card, uri, fileInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.HandleNxmLinkAsync] '{card.GameName}' — {ex.Message}");
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = $"Не удалось установить: {ex.Message}"; card.IsInstalling = false; });
        }
    }

    /// <summary>
    /// Checks for an update to a Nexus mod and installs the latest MAIN file (premium path).
    /// Called by AutoUpdateService for silent background updates.
    /// </summary>
    public async Task UpdateNexusModAsync(GameCardViewModel card)
    {
        if (!FeatureFlags.NexusMods) return;
        var nexusDl = App.Services.GetRequiredService<NexusDownloadService>();
        if (!nexusDl.IsApiKeyConfigured || !nexusDl.IsPremium) return;

        // Re-use install path — it fetches latest MAIN file
        await InstallNexusModAsync(card).ConfigureAwait(false);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a CDN URI for a specific file and deploys it. Premium only.
    /// </summary>
    private async Task InstallNexusFileAsync(GameCardViewModel card, string domain, int modId, NexusModFile file)
    {
        var nexusDl = App.Services.GetRequiredService<NexusDownloadService>();

        DispatcherQueue?.TryEnqueue(() => card.ActionMessage = "Определение ссылки на скачивание...");

        var uri = await nexusDl.GetDownloadUriAsync(domain, modId, file.FileId).ConfigureAwait(false);
        if (uri == null)
        {
            _crashReporter.Log($"[MainViewModel.InstallNexusFileAsync] Could not get CDN URI for '{card.GameName}' file {file.FileId}");
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Ссылка на скачивание недоступна."; card.IsInstalling = false; });
            return;
        }

        await DownloadAndDeployNexusFileAsync(card, uri, file).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a file from <paramref name="uri"/>, extracts the addon, deploys it,
    /// saves the tracking record, and updates the card state. Shared by all download paths.
    /// </summary>
    private async Task DownloadAndDeployNexusFileAsync(GameCardViewModel card, string uri, NexusModFile file)
    {
        if (string.IsNullOrEmpty(card.InstallPath))
        {
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Путь установки не задан."; card.IsInstalling = false; });
            return;
        }

        var nexusDl = App.Services.GetRequiredService<NexusDownloadService>();

        var progress = new Progress<(string msg, double pct)>(p =>
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = p.msg; card.InstallProgress = p.pct; }));

        // ── Download to temp ──────────────────────────────────────────────────
        var tempPath = await nexusDl.DownloadToTempAsync(uri, progress).ConfigureAwait(false);
        if (tempPath == null)
        {
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Не удалось скачать."; card.IsInstalling = false; });
            return;
        }

        try
        {
            DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Extracting..."; card.InstallProgress = 50; });

            // ── Extract to a temp dir ─────────────────────────────────────────
            var tempExtractDir = Path.Combine(Path.GetTempPath(), $"nexus_extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                // Use 7-Zip for extraction (same as the rest of RHI)
                var sevenZip = App.Services.GetRequiredService<ISevenZipExtractor>();
                var sevenZipExe = sevenZip.Find7ZipExe();
                if (!string.IsNullOrEmpty(sevenZipExe))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(sevenZipExe,
                        $"x \"{tempPath}\" -o\"{tempExtractDir}\" -y")
                    {
                        UseShellExecute = false,
                        CreateNoWindow  = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                    };
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    proc.WaitForExit(60_000);
                }
                else
                {
                    // Fall back to built-in ZipFile
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, tempExtractDir, overwriteFiles: true);
                }

                // ── Find the addon file ───────────────────────────────────────
                var addonFiles = Directory.GetFiles(tempExtractDir, "*.addon64", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(tempExtractDir, "*.addon32", SearchOption.AllDirectories))
                    .Where(f => Path.GetFileName(f).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (addonFiles.Count == 0)
                {
                    _crashReporter.Log($"[MainViewModel.DownloadAndDeployNexusFileAsync] No renodx-*.addon64/32 found in archive for '{card.GameName}'");
                    DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "В архиве не найден файл аддона."; card.IsInstalling = false; });
                    return;
                }

                var addonPath    = addonFiles[0];
                var addonFileName = Path.GetFileName(addonPath);

                DispatcherQueue?.TryEnqueue(() => { card.ActionMessage = "Deploying..."; card.InstallProgress = 75; });

                // ── Deploy to game folder ─────────────────────────────────────
                var deployDir = ModInstallService.GetAddonDeployPath(card.InstallPath);
                Directory.CreateDirectory(deployDir);
                File.Copy(addonPath, Path.Combine(deployDir, addonFileName), overwrite: true);

                _crashReporter.Log($"[MainViewModel.DownloadAndDeployNexusFileAsync] Deployed '{addonFileName}' to '{deployDir}' for '{card.GameName}'");

                // ── Post-install steps (mirror InstallModAsync) ───────────────
                var record = new InstalledModRecord
                {
                    GameName      = card.GameName,
                    Store         = card.Source ?? "",
                    InstallPath   = card.InstallPath,
                    AddonFileName = addonFileName,
                    InstalledAt   = DateTime.UtcNow,
                    NexusFileId   = file.FileId,
                };

                // Preserve Engine.ini toggle state from existing record
                if (card.InstalledRecord != null)
                {
                    record.EngineIniHdr = card.InstalledRecord.EngineIniHdr;
                    record.EngineIniLut = card.InstalledRecord.EngineIniLut;
                }

                _installer.SaveRecordPublic(record);

                // Apply INI overrides and Engine.ini settings (same as InstallModAsync)
                if (card.UseUeExtended)
                {
                    bool isUe4 = card.EngineHint?.Contains("Unreal Engine 4") == true;
                    AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath, usesSdrPath: isUe4);
                }

                if (_manifest?.RenodxIniOverrides != null
                    && _manifest.RenodxIniOverrides.TryGetValue(card.GameName, out var iniOverrides))
                    AuxInstallService.ApplyRenodxIniOverrides(card.InstallPath, iniOverrides, forceOverwrite: true);

                bool isUe4Game  = card.EngineHint?.Contains("Unreal Engine 4") == true;
                var compatEntry = _manifestUeExtendedCompat.TryGetValue(card.GameName, out var ce) ? ce : null;
                bool deployHdr  = compatEntry?.Hdr ?? !isUe4Game;
                bool deployLut  = compatEntry?.Lut ?? true;

                string? engineIniFilename = null;
                _manifest?.EngineIniFiles?.TryGetValue(card.GameName, out engineIniFilename);

                if (card.UseUeExtended && !string.IsNullOrEmpty(engineIniFilename))
                {
                    await AuxInstallService.ApplyEngineIniFromFileAsync(_http, engineIniFilename, card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source).ConfigureAwait(false);
                    record.EngineIniHdr = false;
                    record.EngineIniLut = false;
                    App.Services.GetRequiredService<IModInstallService>().SaveRecordPublic(record);
                }
                else if (card.UseUeExtended && record.EngineIniHdr != false && deployHdr)
                    AuxInstallService.ApplyEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);

                if (card.EngineHint?.Contains("Unreal") == true && card.InstalledRecord?.EngineIniLut != false && deployLut
                    && string.IsNullOrEmpty(engineIniFilename))
                    AuxInstallService.ApplyEngineIniLutSetting(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);

                // Update Nexus baseline so update indicator clears
                _nexusUpdateService.ResetBaseline(card.GameName);

                // Update baseline FileId so future update checks can compare file_id
                // (ResetBaseline only clears hasUpdate — we also need to store the new fileId)
                // Access the baselines via SaveBaselines (baselines are internal to NexusUpdateService)
                // We rely on the next CheckForUpdatesAsync call to update the InstalledVersion.

                // ── Update card state on UI thread ────────────────────────────
                DispatcherQueue?.TryEnqueue(() =>
                {
                    card.InstalledRecord        = record;
                    card.InstalledAddonFileName = addonFileName;
                    card.RdxInstalledVersion    = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName);
                    card.Status                 = GameStatus.Installed;
                    card.IsInstalling           = false;
                    card.InstallProgress        = 0;
                    card.FadeMessage(m => card.ActionMessage = m, "✅ Установлено!");
                    _crashReporter.Log($"[MainViewModel.DownloadAndDeployNexusFileAsync] Complete: '{card.GameName}' — {addonFileName}");

                    if (!string.IsNullOrEmpty(card.InstallPath))
                        _addonFileCache[card.InstallPath.ToLowerInvariant()] = addonFileName;

                    card.NotifyAll();
                    SaveLibrary();
                });
            }
            finally
            {
                // Clean up temp extract dir
                try { Directory.Delete(tempExtractDir, recursive: true); } catch { }
            }
        }
        finally
        {
            // Clean up downloaded temp file
            try { File.Delete(tempPath); } catch { }
        }
    }
}
