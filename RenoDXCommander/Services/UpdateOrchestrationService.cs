using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Owns batch update workflows: UpdateAllRenoDxAsync, UpdateAllReShadeAsync, UpdateAllDcAsync,
/// CheckForUpdatesAsync, CheckAuxUpdate.
/// Extracted from MainViewModel per Requirement 1.3.
/// </summary>
public class UpdateOrchestrationService : IUpdateOrchestrationService
{
    private readonly IModInstallService _installer;
    private readonly IAuxInstallService _auxInstaller;

    public UpdateOrchestrationService(
        IModInstallService installer,
        IAuxInstallService auxInstaller)
    {
        _installer = installer;
        _auxInstaller = auxInstaller;
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
                    card.Status                 = GameStatus.Installed;
                    card.ActionMessage          = "✅ Updated!";
                    card.NotifyAll();
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
        int dcModeLevel,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState)
    {
        var targets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllReShade)
            .Where(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable)
            .Where(c => !c.RsBlockedByDcMode)
            .ToList();

        foreach (var card in targets)
        {
            var effectiveDcMode = !card.DllOverrideEnabled && (card.PerGameDcMode ?? dcModeLevel) > 0;
            if (!effectiveDcMode)
            {
                var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
                if (File.Exists(dxgiPath))
                {
                    var fileType = AuxInstallService.IdentifyDxgiFile(dxgiPath);
                    if (fileType == AuxInstallService.DxgiFileType.Unknown)
                    {
                        CrashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Skipping {card.GameName} — foreign dxgi.dll detected");
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
                var dcInstalled     = card.DcStatus == GameStatus.Installed
                                  || card.DcStatus == GameStatus.UpdateAvailable;
                var rsOverride = card.DllOverrideEnabled
                    ? dllOverrideService.GetDllOverride(card.GameName)?.ReShadeFileName
                    : null;
                var record = await _auxInstaller.InstallReShadeAsync(
                    card.GameName, card.InstallPath, effectiveDcMode,
                    dcIsInstalled:  dcInstalled,
                    shaderModeOverride: card.ShaderModeOverride,
                    use32Bit:       card.Is32Bit,
                    filenameOverride: rsOverride,
                    progress:       progress).ConfigureAwait(false);
                dispatcherQueue?.TryEnqueue(() =>
                {
                    card.RsRecord           = record;
                    card.RsInstalledFile    = record.InstalledAs;
                    card.RsInstalledVersion = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);
                    card.RsStatus           = GameStatus.Installed;
                    card.RsActionMessage    = "✅ Updated!";
                    card.NotifyAll();
                });
            }
            catch (Exception ex)
            {
                card.RsActionMessage = $"❌ Failed: {ex.Message}";
                CrashReporter.WriteCrashReport("UpdateAllReShade", ex, note: $"Game: {card.GameName}");
            }
            finally { card.RsIsInstalling = false; }
        }

        dispatcherQueue?.TryEnqueue(() => notifyUpdateState());
    }

    public async Task UpdateAllDcAsync(
        IReadOnlyList<GameCardViewModel> allCards,
        IDllOverrideService dllOverrideService,
        int dcModeLevel,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState)
    {
        var targets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllDc)
            .Where(c => c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable)
            .ToList();

        foreach (var card in targets)
        {
            var effectiveDcModeLevel = card.DllOverrideEnabled ? 0 : (card.PerGameDcMode ?? dcModeLevel);
            if (effectiveDcModeLevel == 1)
            {
                var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
                if (File.Exists(dxgiPath))
                {
                    var fileType = AuxInstallService.IdentifyDxgiFile(dxgiPath);
                    if (fileType == AuxInstallService.DxgiFileType.Unknown)
                    {
                        CrashReporter.Log($"[UpdateOrchestrationService.UpdateAllDc] Skipping {card.GameName} — foreign dxgi.dll detected");
                        dispatcherQueue?.TryEnqueue(() =>
                        {
                            card.DcActionMessage = "⚠ Skipped — unknown dxgi.dll";
                        });
                        continue;
                    }
                }
            }

            if (effectiveDcModeLevel == 2)
            {
                var winmmPath = Path.Combine(card.InstallPath, "winmm.dll");
                if (File.Exists(winmmPath))
                {
                    var fileType = AuxInstallService.IdentifyWinmmFile(winmmPath);
                    if (fileType == AuxInstallService.WinmmFileType.Unknown)
                    {
                        CrashReporter.Log($"[UpdateOrchestrationService.UpdateAllDc] Skipping {card.GameName} — foreign winmm.dll detected");
                        dispatcherQueue?.TryEnqueue(() =>
                        {
                            card.DcActionMessage = "⚠ Skipped — unknown winmm.dll";
                        });
                        continue;
                    }
                }
            }

            card.DcIsInstalling  = true;
            card.DcActionMessage = "Updating...";
            try
            {
                var progress = new Progress<(string msg, double pct)>(p =>
                {
                    card.DcActionMessage = p.msg;
                    card.DcProgress      = p.pct;
                });
                var dcOverride = card.DllOverrideEnabled
                    ? dllOverrideService.GetDllOverride(card.GameName)?.DcFileName
                    : null;
                var record = await _auxInstaller.InstallDcAsync(
                    card.GameName, card.InstallPath, effectiveDcModeLevel,
                    existingDcRecord: card.DcRecord,
                    existingRsRecord: card.RsRecord,
                    shaderModeOverride: card.ShaderModeOverride,
                    use32Bit:         card.Is32Bit,
                    filenameOverride: dcOverride,
                    progress:         progress).ConfigureAwait(false);
                dispatcherQueue?.TryEnqueue(() =>
                {
                    card.DcRecord           = record;
                    card.DcInstalledFile    = record.InstalledAs;
                    card.DcInstalledVersion = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);
                    card.DcStatus           = GameStatus.Installed;
                    card.DcActionMessage    = "✅ Updated!";
                    card.NotifyAll();
                });
            }
            catch (Exception ex)
            {
                card.DcActionMessage = $"❌ Failed: {ex.Message}";
                CrashReporter.WriteCrashReport("UpdateAllDc", ex, note: $"Game: {card.GameName}");
            }
            finally { card.DcIsInstalling = false; }
        }

        dispatcherQueue?.TryEnqueue(() => notifyUpdateState());
    }

    public async Task CheckForUpdatesAsync(
        List<GameCardViewModel> cards,
        List<InstalledModRecord> records,
        List<AuxInstalledRecord> auxRecords,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState)
    {
        CrashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {cards.Count} total cards");

        var installed = cards
            .Where(c => c.Status == GameStatus.Installed
                     && !c.IsExternalOnly
                     && c.Mod?.SnapshotUrl != null
                     && c.InstalledRecord?.SnapshotUrl != null)
            .ToList();

        CrashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {installed.Count} RenoDX mods to check");

        var tasks = installed.Select(async card =>
        {
            var record = card.InstalledRecord!;
            bool updateAvailable;
            try
            {
                updateAvailable = await _installer.CheckForUpdateAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex) { CrashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Update check failed for '{card.GameName}' — {ex.Message}"); return; }

            if (updateAvailable)
            {
                try { _installer.SaveRecordPublic(record); }
                catch (Exception ex) { CrashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Failed to save record for '{card.GameName}' — {ex.Message}"); }
                dispatcherQueue?.TryEnqueue(() => { card.Status = GameStatus.UpdateAvailable; });
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        CrashReporter.Log("[UpdateOrchestrationService.CheckForUpdatesAsync] RenoDX mod checks complete");

        var auxInstalled = cards
            .Where(c => c.DcStatus == GameStatus.Installed || c.RsStatus == GameStatus.Installed)
            .ToList();

        CrashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {auxInstalled.Count} aux (DC/RS) cards to check");
        foreach (var c in auxInstalled)
            CrashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Aux check: {c.GameName} DC={c.DcStatus} DcRec={c.DcRecord != null} RS={c.RsStatus} RsRec={c.RsRecord != null} RsBlocked={c.RsBlockedByDcMode}");

        var auxTasks = auxInstalled.SelectMany(card => new[]
        {
            card.DcRecord != null ? CheckAuxUpdate(card, card.DcRecord, isRs: false, dispatcherQueue) : Task.CompletedTask,
            card.RsRecord != null && !card.RsBlockedByDcMode ? CheckAuxUpdate(card, card.RsRecord, isRs: true, dispatcherQueue) : Task.CompletedTask,
        });

        await Task.WhenAll(auxTasks).ConfigureAwait(false);
        CrashReporter.Log("[UpdateOrchestrationService.CheckForUpdatesAsync] All checks complete");

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
                upd = AuxInstallService.CheckReShadeUpdateLocal(record);
            }
            else
            {
                upd = await _auxInstaller.CheckForUpdateAsync(record).ConfigureAwait(false);
            }
            if (upd)
                dispatcherQueue?.TryEnqueue(() =>
                {
                    if (isRs) card.RsStatus = GameStatus.UpdateAvailable;
                    else      card.DcStatus = GameStatus.UpdateAvailable;
                });
        }
        catch (Exception ex) { CrashReporter.Log($"[UpdateOrchestrationService.CheckAuxUpdate] Aux update check failed for '{card.GameName}' ({record.AddonType}) — {ex.Message}"); }
    }
}
