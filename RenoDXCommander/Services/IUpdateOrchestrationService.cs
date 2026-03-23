using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Defines the contract for batch update workflows: updating all RenoDX mods,
/// ReShade, and checking for available updates.
/// </summary>
public interface IUpdateOrchestrationService
{
    /// <summary>
    /// Returns the set of cards eligible for batch update operations.
    /// Eligibility: card must not be hidden, not excluded from Update All,
    /// not using DLL overrides, and must have a valid install path.
    /// </summary>
    IEnumerable<GameCardViewModel> UpdateAllEligible(IReadOnlyList<GameCardViewModel> allCards);

    /// <summary>
    /// Batch-updates all eligible RenoDX mods.
    /// </summary>
    Task UpdateAllRenoDxAsync(
        IReadOnlyList<GameCardViewModel> allCards,
        IDllOverrideService dllOverrideService,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action saveLibrary,
        Action notifyUpdateState);

    /// <summary>
    /// Batch-updates all eligible ReShade installations.
    /// </summary>
    Task UpdateAllReShadeAsync(
        IReadOnlyList<GameCardViewModel> allCards,
        IDllOverrideService dllOverrideService,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState,
        Func<string, string?, IEnumerable<string>?>? shaderResolver = null);

    /// <summary>
    /// Checks all installed mods and aux components for available updates.
    /// </summary>
    Task CheckForUpdatesAsync(
        List<GameCardViewModel> cards,
        List<InstalledModRecord> records,
        List<AuxInstalledRecord> auxRecords,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue,
        Action notifyUpdateState);
}
