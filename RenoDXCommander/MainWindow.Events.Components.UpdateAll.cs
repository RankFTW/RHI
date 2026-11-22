// MainWindow.Events.Components.UpdateAll.cs — the "Update All" row each component's ⚙ has (v2.9.6): whether Update All and
// automatic updates update that component in this game. The same per-game choice as Game Overrides › Update inclusion
// (UpdateInclusionHelper), which goes away in 2.9.8. A component switched off for every game in Settings (Components to
// check) shows that instead.

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    internal enum UpdateAllComponent { ReShade, RenoDx, ReLimiter, DisplayCommander, OptiScaler, ReFramework, Dxvk }

    /// <summary>Adds an "Updates" section with the component's Update All row.</summary>
    private void AddUpdateAllSection(CogDialog cog, GameCardViewModel card, UpdateAllComponent component)
    {
        cog.AddSection("Updates");
        AddUpdateAllRow(cog, card, component);
    }

    /// <summary>Include / Exclude this component from Update All for this game.</summary>
    private ComboBox AddUpdateAllRow(CogDialog cog, GameCardViewModel card, UpdateAllComponent component)
    {
        var vm = ViewModel;
        var s = vm.Settings;
        var gameName = card.GameName;
        var store = card.Source ?? "";
        var (name, isExcluded, toggle, globallyOff) = component switch
        {
            UpdateAllComponent.ReShade => ("ReShade", (Func<bool>)(() => vm.IsUpdateAllExcludedReShade(gameName, store)),
                (Action)(() => vm.ToggleUpdateAllExclusionReShade(gameName, store)), s.GlobalSkipRsUpdates),
            UpdateAllComponent.RenoDx => ("RenoDX", () => vm.IsUpdateAllExcludedRenoDx(gameName, store),
                () => vm.ToggleUpdateAllExclusionRenoDx(gameName, store), s.GlobalSkipRdxUpdates),
            UpdateAllComponent.ReLimiter => ("ReLimiter", () => vm.IsUpdateAllExcludedUl(gameName, store),
                () => vm.ToggleUpdateAllExclusionUl(gameName, store), s.GlobalSkipUlUpdates),
            UpdateAllComponent.DisplayCommander => ("Display Commander", () => vm.IsUpdateAllExcludedDc(gameName, store),
                () => vm.ToggleUpdateAllExclusionDc(gameName, store), s.GlobalSkipDcUpdates),
            UpdateAllComponent.OptiScaler => ("OptiScaler", () => vm.IsUpdateAllExcludedOs(gameName, store),
                () => vm.ToggleUpdateAllExclusionOs(gameName, store), s.GlobalSkipOsUpdates),
            UpdateAllComponent.ReFramework => ("RE Framework", () => vm.IsUpdateAllExcludedRef(gameName, store),
                () => vm.ToggleUpdateAllExclusionRef(gameName, store), s.GlobalSkipRefUpdates),
            _ => ("DXVK", () => vm.IsUpdateAllExcludedDxvk(gameName, store),
                () => vm.ToggleUpdateAllExclusionDxvk(gameName, store), false),
        };

        string help = globallyOff
            ? $"Updates for {name} are off for every game in Settings › General › Updates (Components to check)."
            : $"Include: Update All and automatic updates update {name} in this game. Exclude: they leave it as it is."
              + (component == UpdateAllComponent.ReShade && card.RequiresVulkanInstall
                  ? " Vulkan games share one ReShade, so this applies to every Vulkan game." : "");
        return cog.AddCombo("Update All", help, new[] { "Include", "Exclude" }, isExcluded() || globallyOff ? 1 : 0,
            i =>
            {
                bool exclude = i == 1;
                if (isExcluded() != exclude) toggle();
                _detailPanelBuilder?.UpdateDetailComponentRows(card);
                _detailPanelBuilder?.QueueOverridesRebuild(gameName); // Game Overrides' Update inclusion summary
                cog.SetSuccess(exclude ? $"Update All leaves {name} alone in this game." : $"Update All updates {name} in this game.");
            },
            isEnabled: !globallyOff);
    }
}
