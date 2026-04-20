// MainWindow.Nexus.cs — Nexus Mods startup re-validation and update check integration.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>
    /// Background task that re-validates the stored Nexus API key at startup
    /// and, if authenticated, triggers a Nexus mod update check for all game cards.
    /// Follows the same fire-and-forget pattern as shader pack and addon pack init.
    /// </summary>
    private async Task NexusStartupRevalidateAndCheckAsync()
    {
        // Wait for the game list to finish loading before checking updates
        while (ViewModel.IsLoading)
            await Task.Delay(500).ConfigureAwait(false);

        var authService = App.Services.GetRequiredService<INexusAuthService>();

        // Re-validate the stored API key (Requirement 1.6)
        var isValid = await authService.RevalidateStoredKeyAsync().ConfigureAwait(false);
        if (!isValid)
        {
            _crashReporter.Log("[MainWindow.NexusStartupRevalidateAndCheckAsync] No valid Nexus API key — skipping update check");
            return;
        }

        _crashReporter.Log("[MainWindow.NexusStartupRevalidateAndCheckAsync] Nexus API key validated — starting mod update check");

        // Trigger update check for all game cards (Requirement 3.1)
        var updateChecker = App.Services.GetRequiredService<INexusUpdateChecker>();
        await updateChecker.CheckAllAsync(ViewModel.AllCards, dispatcherQueue: DispatcherQueue).ConfigureAwait(false);

        _crashReporter.Log("[MainWindow.NexusStartupRevalidateAndCheckAsync] Nexus mod update check complete");
    }

    /// <summary>
    /// Manually triggers a Nexus mod update check with cache bypass.
    /// Called when the user explicitly requests a refresh (Requirement 3.5).
    /// </summary>
    internal async Task NexusManualRefreshAsync()
    {
        var authService = App.Services.GetRequiredService<INexusAuthService>();
        if (!authService.IsAuthenticated)
            return;

        var updateChecker = App.Services.GetRequiredService<INexusUpdateChecker>();
        await updateChecker.CheckAllAsync(ViewModel.AllCards, bypassCache: true, dispatcherQueue: DispatcherQueue).ConfigureAwait(false);
    }
}
