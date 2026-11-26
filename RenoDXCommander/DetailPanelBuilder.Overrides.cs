// DetailPanelBuilder.Overrides.cs — builds the detail page's sections below Components for a game
// (Neural Rendering, DLSS / Streamline, Driver Settings, Management, Experimental, Extras).
// The Game Overrides section that used to be built here was removed in 2.9.6.3: its settings live in the
// game settings ⚙ and the component ⚙s (shared code in the *Shared.cs partials).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    // Wiki lookup dropdown items (RenoDX ⚙ › Mod matching)
    internal const string WikiIncludedItem = "Use wiki";
    internal const string WikiExcludedItem = "Ignore wiki";

    /// <summary>
    /// Builds every section below Components for <paramref name="card"/>. The name is historical: it also
    /// built Game Overrides until 2.9.6.3.
    /// </summary>
    public void BuildOverridesPanel(GameCardViewModel card)
    {
        // Abort any in-progress drag before the header rows are rebuilt — prevents stale
        // _dragging = true from blocking pointer events on the rebuilt sections.
        if (_dragging) DragEnd(save: false);

        // Cancel any in-flight NVAPI background scans from the previous game.
        // Tasks waiting on _panelScanSemaphore will observe the cancellation and
        // bail out immediately, freeing their thread pool threads.
        if (_window.IsShuttingDown) return;
        _panelScanCts.Cancel();
        _panelScanCts = new CancellationTokenSource();

        var gameName = card.GameName;

        // Undo the 2.9.x bug that renamed a component's DLL to the "--------" placeholder
        if (_dllOverrideService.RepairPlaceholderNames(card)) card.NotifyAll();

        BuildNeuralRenderingSection(card);
        CrashReporter.Log($"[BuildOverridesPanel] NeuralRendering done: '{card.GameName}'");
        BuildNvidiaProfileSection(card, gameName);
        CrashReporter.Log($"[BuildOverridesPanel] NvidiaProfileDlss done: '{card.GameName}'");
        BuildDriverProfileSection(card, gameName);
        CrashReporter.Log($"[BuildOverridesPanel] NvidiaProfileDriver done: '{card.GameName}'");

        BuildManagementSection(card, gameName);
        CrashReporter.Log($"[BuildOverridesPanel] Dxvk+Management done: '{card.GameName}'");

        BuildExperimentalSection(card);
        CrashReporter.Log($"[BuildOverridesPanel] Experimental done: '{card.GameName}'");

        // Extras last: its SummaryChanged refreshes every section's tile / glance summary
        BuildExtrasSection(card);
        CrashReporter.Log($"[BuildOverridesPanel] Extras done: '{card.GameName}'");
    }
}
