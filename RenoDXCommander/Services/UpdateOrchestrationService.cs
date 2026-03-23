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

    public UpdateOrchestrationService(
        IModInstallService installer,
        IAuxInstallService auxInstaller,
        ICrashReporter crashReporter,
        IAuxFileService auxFileService)
    {
        _installer = installer;
        _auxInstaller = auxInstaller;
        _crashReporter = crashReporter;
        _auxFileService = auxFileService;
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
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState,
        Func<string, string?, IEnumerable<string>?>? shaderResolver = null)
    {
        var targets = UpdateAllEligible(allCards)
            .Where(c => !c.ExcludeFromUpdateAllReShade)
            .Where(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable)
            .Where(c => !c.RequiresVulkanInstall) // Vulkan games use the global layer — not a per-game DLL
            .ToList();

        foreach (var card in targets)
        {
            var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
            if (File.Exists(dxgiPath))
            {
                var fileType = _auxFileService.IdentifyDxgiFile(dxgiPath);
                if (fileType == AuxInstallService.DxgiFileType.Unknown)
                {
                    _crashReporter.Log($"[UpdateOrchestrationService.UpdateAllReShade] Skipping {card.GameName} — foreign dxgi.dll detected");
                    dispatcherQueue?.TryEnqueue(() =>
                    {
                        card.RsActionMessage = "⚠ Skipped — unknown dxgi.dll";
                    });
                    continue;
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
                    : null;
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
                });
            }
            catch (Exception ex)
            {
                card.RsActionMessage = $"❌ Failed: {ex.Message}";
                _crashReporter.WriteCrashReport("UpdateAllReShade", ex, note: $"Game: {card.GameName}");
            }
            finally { card.RsIsInstalling = false; }
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
        _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {cards.Count} total cards");

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

        var auxInstalled = cards
            .Where(c => c.RsStatus == GameStatus.Installed)
            .ToList();

        _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] {auxInstalled.Count} aux (RS) cards to check");
        foreach (var c in auxInstalled)
            _crashReporter.Log($"[UpdateOrchestrationService.CheckForUpdatesAsync] Aux check: {c.GameName} RS={c.RsStatus} RsRec={c.RsRecord != null}");

        var auxTasks = auxInstalled.Select(card =>
            card.RsRecord != null ? CheckAuxUpdate(card, card.RsRecord, isRs: true, dispatcherQueue) : Task.CompletedTask
        );

        await Task.WhenAll(auxTasks).ConfigureAwait(false);
        _crashReporter.Log("[UpdateOrchestrationService.CheckForUpdatesAsync] All checks complete");

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
