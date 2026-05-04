using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Owns batch update workflows: UpdateAllRenoDxAsync, UpdateAllReShadeAsync,
/// CheckForUpdatesAsync, CheckAuxUpdate.
/// Extracted from MainViewModel per Requirement 1.3.
/// </summary>
public class UpdateOrchestrationService : IUpdateOrchestrationService
{
    private readonly IModInstallService _installer;
    private readonly IAuxInstallService _auxInstaller;
    private readonly ICrashReporter _crashReporter;
    private readonly IAuxFileService _auxFileService;
    private readonly IREFrameworkService _refService;
    private readonly ILumaService _lumaService;

    public UpdateOrchestrationService(
        IModInstallService installer,
        IAuxInstallService auxInstaller,
        ICrashReporter crashReporter,
        IAuxFileService auxFileService,
        IREFrameworkService refService,
        ILumaService lumaService)
    {
        _installer = installer;
        _auxInstaller = auxInstaller;
        _crashReporter = crashReporter;
        _auxFileService = auxFileService;
        _refService = refService;
        _lumaService = lumaService;
    }

    /// <summary>
    /// Returns the set of cards eligible for batch update operations.
    /// Eligibility: card must not be hidden, not excluded from Update All,
    /// not using DLL overrides, and must have a valid install path.
    /// </summary>
    public IEnumerable<GameCardViewModel> UpdateAllEligible(IReadOnlyList<GameCardViewModel> allCards) =>
        allCards.Where(c => !c.IsHidden && !c.DllOverrideEnabled
                          && !string.IsNullOrEmpty(c.InstallPath)
                          && Directory.Exists(c.InstallPath));

    public async Task UpdateAllRenoDxAsync(
        IReadOnlyList<GameCardViewModel> allCards,
        IDllOverrideService dllOverrideService,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action saveLibrary,
        Action notifyUpdateState)
    {
        var targets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllRenoDx)
            .Where(c => c.Status == GameStatus.Installed || c.Status == GameStatus.UpdateAvailable)
            .Where(c => c.Mod?.SnapshotUrl != null)
            .ToList();

        foreach (var card in targets)
        {
            card.IsInstalling  = true;
            card.ActionMessage = "Updating...";

            string? originalSnapshotUrl = card.Mod!.SnapshotUrl;
            bool swappedTo32 = card.Is32Bit && card.Mod.SnapshotUrl32 != null;
            if (swappedTo32)
                card.Mod.SnapshotUrl = card.Mod.SnapshotUrl32;

            try
            {
                var progress = new Progress<(string msg, double pct)>(p =>
                {
                    card.ActionMessage   = p.msg;
                    card.InstallProgress = p.pct;
                });
                var record = await _installer.InstallAsync(card.Mod!, card.InstallPath, progress, card.GameName).ConfigureAwait(false);
                dispatcherQueue?.TryEnqueue(() =>
                {
                    card.InstalledRecord        = record;
                    card.InstalledAddonFileName = record.AddonFileName;
                    card.RdxInstalledVersion    = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName);
                    card.Status                 = GameStatus.Installed;
                    card.ActionMessage          = "✅ Updated!";
                    card.NotifyAll();
                    card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
                });
            }
            catch (Exception ex)
            {
                card.ActionMessage = $"❌ Failed: {ex.Message}";
            }
            finally
            {
                card.IsInstalling = false;
                if (swappedTo32 && card.Mod != null && originalSnapshotUrl != null)
                    card.Mod.SnapshotUrl = originalSnapshotUrl;
            }
        }

        saveLibrary();
        dispatcherQueue?.TryEnqueue(() => notifyUpdateState());
    }

    public async Task UpdateAllReShadeAsync(
        IReadOnlyList<GameCardViewModel> allCards,
        IDllOverrideService dllOverrideService,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState,
        Func<string, string?, IEnumerable<string>?>? shaderResolver = null,
        Func<string, ManifestDllNames?>? manifestDllResolver = null)
    {
        // ── DX proxy games (per-game DLL) ─────────────────────────────────────
        var targets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllReShade)
            .Where(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable)
            .Where(c => !c.RequiresVulkanInstall) // Vulkan games handled separately below
            .Where(c => !c.IsLumaMode) // Luma games use an older ReShade version — skip global updates
            .ToList();

        foreach (var card in targets)
        {
            var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
            if (File.Exists(dxgiPath))
            {
                var fileType = _auxFileService.IdentifyDxgiFile(dxgiPath);
                if (fileType == AuxInstallService.DxgiFileType.Unknown)
                {
                    // Skip the warning if OptiScaler is installed — the dxgi.dll belongs to it
                    if (card.IsOsInstalled)
                    {
                        _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] {card.GameName} — dxgi.dll is OptiScaler, proceeding");
                    }
                    else
                    {
                        _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Skipping {card.GameName} — foreign dxgi.dll detected");
                        dispatcherQueue?.TryEnqueue(() =>
                        {
                            card.RsActionMessage = "⚠ Skipped — unknown dxgi.dll";
                        });
                        continue;
                    }
                }
            }

            card.RsIsInstalling  = true;
            card.RsActionMessage = "Updating...";
            try
            {
                var progress = new Progress<(string msg, double pct)>(p =>
                {
                    card.RsActionMessage = p.msg;
                    card.RsProgress      = p.pct;
                });
                var rsOverride = card.DllOverrideEnabled
                    ? dllOverrideService.GetDllOverride(card.GameName)?.ReShadeFileName
                    : (manifestDllResolver?.Invoke(card.GameName)?.ReShade is { Length: > 0 } mRs
                        ? mRs
                        : MainViewModel.ResolveAutoReShadeFilename(card.DetectedApis));
                var record = await _auxInstaller.InstallReShadeAsync(
                    card.GameName, card.InstallPath,
                    shaderModeOverride: card.ShaderModeOverride,
                    use32Bit:       card.Is32Bit,
                    filenameOverride: rsOverride,
                    selectedPackIds: shaderResolver?.Invoke(card.GameName, card.ShaderModeOverride),
                    progress:       progress).ConfigureAwait(false);
                dispatcherQueue?.TryEnqueue(() =>
                {
                    card.RsRecord           = record;
                    card.RsInstalledFile    = record.InstalledAs;
                    card.RsInstalledVersion = _auxFileService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);
                    card.RsStatus           = GameStatus.Installed;
                    card.RsActionMessage    = "✅ Updated!";
                    card.NotifyAll();
                    card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
                });
            }
            catch (Exception ex)
            {
                card.RsActionMessage = $"❌ Failed: {ex.Message}";
                _crashReporter.WriteCrashReport("UpdateAllReShade", ex, note: $"Game: {card.GameName}");
            }
            finally { card.RsIsInstalling = false; }
        }

        // ── Vulkan games (global layer DLL) ───────────────────────────────────
        var vulkanTargets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllReShade)
            .Where(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable)
            .Where(c => c.RequiresVulkanInstall)
            .Where(c => !c.IsLumaMode)
            .ToList();

        if (vulkanTargets.Count > 0)
        {
            // Update the global Vulkan layer DLLs (copy from staging)
            bool layerUpdated = false;
            var layerDir = VulkanLayerService.LayerDirectory;
            try
            {
                // Update 64-bit layer DLL
                var staged64 = AuxInstallService.RsStagedPath64;
                var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                if (File.Exists(staged64) && new FileInfo(staged64).Length > AuxInstallService.MinReShadeSize)
                {
                    if (File.Exists(layer64))
                    {
                        try
                        {
                            File.Copy(staged64, layer64, overwrite: true);
                            layerUpdated = true;
                            _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Updated Vulkan layer 64-bit DLL ({new FileInfo(layer64).Length} bytes)");
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            // Direct copy denied — try elevated copy via UAC prompt
                            _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Direct copy denied — {uaEx.Message}, attempting elevated copy...");
                            try
                            {
                                ElevatedFileCopy(staged64, layer64);
                                layerUpdated = true;
                                _crashReporter.Log("[UpdateOrchestrationService.UpdateAllReShade] Updated Vulkan layer 64-bit DLL via elevated copy");
                            }
                            catch (Exception elevEx)
                            {
                                _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Elevated copy also failed — {elevEx.Message}");
                            }
                        }
                        catch (IOException ioEx)
                        {
                            _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Vulkan layer 64-bit copy failed (file locked?) — {ioEx.Message}");
                        }
                    }
                    else
                    {
                        _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Vulkan layer 64-bit DLL not found at '{layer64}' — skipping (layer not installed?)");
                    }
                }
                else
                {
                    _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Staged 64-bit DLL missing or too small — skipping Vulkan layer update");
                }

                // Update 32-bit layer DLL if it exists
                var staged32 = AuxInstallService.RsStagedPath32;
                var layer32 = Path.Combine(layerDir, "ReShade32.dll");
                if (File.Exists(staged32) && new FileInfo(staged32).Length > AuxInstallService.MinReShadeSize
                    && File.Exists(layer32))
                {
                    try
                    {
                        File.Copy(staged32, layer32, overwrite: true);
                        _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Updated Vulkan layer 32-bit DLL ({new FileInfo(layer32).Length} bytes)");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        try
                        {
                            ElevatedFileCopy(staged32, layer32);
                            _crashReporter.Log("[UpdateOrchestrationService.UpdateAllReShade] Updated Vulkan layer 32-bit DLL via elevated copy");
                        }
                        catch (Exception elevEx)
                        {
                            _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] 32-bit elevated copy failed — {elevEx.Message}");
                        }
                    }
                    catch (Exception ex32)
                    {
                        _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] 32-bit Vulkan layer copy failed — {ex32.Message}");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                _crashReporter.Log("[UpdateOrchestrationService.UpdateAllReShade] Cannot update Vulkan layer DLLs — admin privileges required for C:\\ProgramData\\ReShade");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Failed to update Vulkan layer DLLs — {ex.Message}");
            }

            // Update each Vulkan game's reshade.ini and status
            foreach (var vCard in vulkanTargets)
            {
                vCard.RsIsInstalling = true;
                vCard.RsActionMessage = "Updating Vulkan ReShade...";
                try
                {
                    AuxInstallService.MergeRsVulkanIni(vCard.InstallPath, vCard.GameName);
                    AuxInstallService.CopyRsPresetIniIfPresent(vCard.InstallPath);

                    var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                        VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                    dispatcherQueue?.TryEnqueue(() =>
                    {
                        vCard.RsInstalledVersion = vulkanVersion;
                        vCard.RsStatus = GameStatus.Installed;
                        vCard.RsActionMessage = layerUpdated ? "✅ Updated!" : "✅ Up to date";
                        vCard.NotifyAll();
                        vCard.FadeMessage(m => vCard.RsActionMessage = m, vCard.RsActionMessage);
                    });
                }
                catch (Exception ex)
                {
                    vCard.RsActionMessage = $"❌ Failed: {ex.Message}";
                    _crashReporter.WriteCrashReport("UpdateAllReShade.Vulkan", ex, note: $"Game: {vCard.GameName}");
                }
                finally { vCard.RsIsInstalling = false; }
            }
        }

        dispatcherQueue?.TryEnqueue(() => notifyUpdateState());
    }

    /// <summary>
    /// Copies a file to a destination using an elevated cmd.exe process (UAC prompt).
    /// Used when direct File.Copy fails due to permissions on C:\ProgramData\ReShade.
    /// </summary>
    private static void ElevatedFileCopy(string source, string destination)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c copy /y \"{source}\" \"{destination}\"",
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit(10_000);
        if (proc != null && proc.ExitCode != 0)
            throw new IOException($"Elevated copy exited with code {proc.ExitCode}");
    }

    public async Task UpdateAllREFrameworkAsync(
        IReadOnlyList<GameCardViewModel> allCards,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState)
    {
        var targets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllRef)
            .Where(c => c.IsREEngineGame && c.RefStatus == GameStatus.UpdateAvailable)
            .ToList();

        foreach (var card in targets)
        {
            card.RefIsInstalling = true;
            card.RefActionMessage = "Updating...";
            try
            {
                var progress = new Progress<(string msg, double pct)>(p =>
                {
                    card.RefActionMessage = p.msg;
                    card.RefProgress = p.pct;
                });
                var record = await _refService.InstallAsync(card.GameName, card.InstallPath, progress).ConfigureAwait(false);
                dispatcherQueue?.TryEnqueue(() =>
                {
                    card.RefRecord = record;
                    card.RefInstalledVersion = record.InstalledVersion;
                    card.RefStatus = GameStatus.Installed;
                    card.RefActionMessage = "✅ Updated!";
                    card.NotifyAll();
                    card.FadeMessage(m => card.RefActionMessage = m, card.RefActionMessage);
                });
            }
            catch (Exception ex)
            {
                card.RefActionMessage = $"❌ Failed: {ex.Message}";
                _crashReporter.WriteCrashReport("UpdateAllREFramework", ex, note: $"Game: {card.GameName}");
            }
            finally { card.RefIsInstalling = false; }
        }

        dispatcherQueue?.TryEnqueue(() => notifyUpdateState());
    }

    public async Task CheckForUpdatesAsync(
        List<GameCardViewModel> cards,
        List<InstalledModRecord> records,
        List<AuxInstalledRecord> auxRecords,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState,
        bool skipRdx = false,
        bool skipRs = false,
        bool skipRef = false)
    {
        _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {cards.Count} total cards (skipRdx={skipRdx}, skipRs={skipRs}, skipRef={skipRef})");

        if (!skipRdx)
        {
        var installed = cards
            .Where(c => c.Status == GameStatus.Installed
                     && !c.IsExternalOnly
                     && c.Mod?.SnapshotUrl != null
                     && c.InstalledRecord?.SnapshotUrl != null)
            .ToList();

        _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {installed.Count} RenoDX mods to check");

        var tasks = installed.Select(async card =>
        {
            var record = card.InstalledRecord!;
            bool updateAvailable;
            try
            {
                updateAvailable = await _installer.CheckForUpdateAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex) { _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Update check failed for '{card.GameName}' — {ex.Message}"); return; }

            if (updateAvailable)
            {
                try { _installer.SaveRecordPublic(record); }
                catch (Exception ex) { _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Failed to save record for '{card.GameName}' — {ex.Message}"); }
                dispatcherQueue?.TryEnqueue(() => { card.Status = GameStatus.UpdateAvailable; });
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        _crashReporter.Log("[UpdateOrchestrationService.CheckForUpdatesAsync] RenoDX mod checks complete");
        }

        if (!skipRs)
        {
        var auxInstalled = cards
            .Where(c => c.RsStatus == GameStatus.Installed)
            .Where(c => !c.IsLumaMode) // Luma games bundle their own ReShade — skip update check
            .ToList();

        _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {auxInstalled.Count} aux (RS) cards to check");
        foreach (var c in auxInstalled)
            _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Aux check: {c.GameName} RS={c.RsStatus} RsRec={c.RsRecord != null}");

        var auxTasks = auxInstalled.Select(card =>
            card.RsRecord != null ? CheckAuxUpdate(card, card.RsRecord, isRs: true, dispatcherQueue) : Task.CompletedTask
        );

        await Task.WhenAll(auxTasks).ConfigureAwait(false);
        _crashReporter.Log("[UpdateOrchestrationService.CheckForUpdatesAsync] All checks complete");
        }

        // ── RE Framework update check ─────────────────────────────────────────
        if (!skipRef)
        {
        try
        {
            var refInstalled = cards
                .Where(c => c.IsREEngineGame && c.IsRefInstalled && c.RefRecord != null)
                .ToList();

            _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {refInstalled.Count} REF cards to check");

            if (refInstalled.Count > 0)
            {
                // Find a card with a standard version (not PD-Upscaler) for the update check.
                // PD-Upscaler cards are on a different branch and shouldn't trigger updates.
                var standardCard = refInstalled.FirstOrDefault(c =>
                    !string.Equals(c.RefRecord!.InstalledVersion, "PD-Upscaler", StringComparison.OrdinalIgnoreCase));

                if (standardCard != null)
                {
                    var firstVersion = standardCard.RefRecord!.InstalledVersion;
                    var refUpdateAvailable = await _refService.CheckForUpdateAsync(firstVersion).ConfigureAwait(false);

                    if (refUpdateAvailable)
                    {
                        dispatcherQueue?.TryEnqueue(() =>
                        {
                            // Only flag standard REF cards as needing update, not PD-Upscaler ones
                            foreach (var card in refInstalled)
                            {
                                if (!string.Equals(card.RefRecord!.InstalledVersion, "PD-Upscaler", StringComparison.OrdinalIgnoreCase))
                                    card.RefStatus = GameStatus.UpdateAvailable;
                            }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] REF update check failed — {ex.Message}");
        }
        }

        // ── Luma update check ─────────────────────────────────────────────────
        try
        {
            var lumaInstalled = cards
                .Where(c => c.LumaRecord != null && c.LumaRecord.InstalledBuildNumber > 0)
                .ToList();

            if (lumaInstalled.Count > 0)
            {
                _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {lumaInstalled.Count} Luma cards to check");

                // Single API call — all Luma mods share the same release
                var latestBuild = await _lumaService.GetLatestBuildNumberAsync().ConfigureAwait(false);
                if (latestBuild > 0)
                {
                    foreach (var card in lumaInstalled)
                    {
                        if (latestBuild > card.LumaRecord!.InstalledBuildNumber)
                        {
                            dispatcherQueue?.TryEnqueue(() => { card.LumaStatus = GameStatus.UpdateAvailable; });
                        }
                    }
                }

                _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Luma check complete (latest build: {latestBuild})");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Luma update check failed — {ex.Message}");
        }

        dispatcherQueue?.TryEnqueue(() => notifyUpdateState());
    }

    private async Task CheckAuxUpdate(GameCardViewModel card, AuxInstalledRecord record, bool isRs,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue)
    {
        try
        {
            bool upd;
            if (isRs && record.SourceUrl == null)
            {
                upd = _auxFileService.CheckReShadeUpdateLocal(record);
            }
            else
            {
                upd = await _auxInstaller.CheckForUpdateAsync(record).ConfigureAwait(false);
            }
            if (upd)
                dispatcherQueue?.TryEnqueue(() =>
                {
                    card.RsStatus = GameStatus.UpdateAvailable;
                });
        }
        catch (Exception ex) { _crashReporter.Log($"[UpdateOrchestrationService.CheckAuxUpdate] Aux update check failed for '{card.GameName}' ({record.AddonType}) — {ex.Message}"); }
    }
}
