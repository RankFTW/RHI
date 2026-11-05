// DetailPanelBuilder.Extras.DllNames.cs — shared name list for the extras' DLL name pickers
// (20/30 FG Unlock, Universal MFG Unlocker, RTX Encore, DLSS Enabler): which names are taken by
// another RHI component, and the "<name>Hooked.dll" option when ASI Loader holds a name.

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>One row of an extra's DLL name picker.</summary>
    private sealed record ExtraDllOption(string Name, bool IsRecommended, bool IsCurrent, string? BlockedBy, bool IsHooked, string? UalName);

    /// <summary>
    /// Builds the picker rows for an extra. Names used by ReShade, OptiScaler, Display Commander, ASI Loader
    /// or another extra are blocked (the extra's own current name isn't). When ASI Loader is installed as one
    /// of <paramref name="baseNames"/>, its &lt;name&gt;Hooked.dll follows it; that row is blocked if the file
    /// is already there and isn't this extra (ASI Loader chained the game's own DLL there).
    /// </summary>
    private List<ExtraDllOption> GetExtraDllOptions(GameCardViewModel card, IEnumerable<string> baseNames,
        string? currentDllName, Func<string, bool>? noHooked = null)
    {
        var ual    = _window.ViewModel.GetUalInstalledAs(card.GameName, card.Source ?? "");
        var owners = _window.ViewModel.GetRhiDllOwners(card);
        if (!string.IsNullOrEmpty(currentDllName)) owners.Remove(currentDllName);
        var recommended = HookedDllNames.Recommended(ual);

        var options = new List<ExtraDllOption>();
        foreach (var name in HookedDllNames.WithHooked(baseNames, ual, noHooked))
        {
            bool isCurrent = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);
            bool isHooked  = HookedDllNames.IsHookedFor(name, ual);
            string? blockedBy = owners.TryGetValue(name, out var owner) ? owner : null;
            if (blockedBy == null && isHooked && !isCurrent && !string.IsNullOrEmpty(card.InstallPath)
                && File.Exists(Path.Combine(card.InstallPath, name)))
                blockedBy = $"the game's own {ual}";
            bool isRecommended = blockedBy == null && string.Equals(name, recommended, StringComparison.OrdinalIgnoreCase);
            options.Add(new ExtraDllOption(name, isRecommended, isCurrent, blockedBy, isHooked, ual));
        }
        return options;
    }

    /// <summary>Badges and tooltip shared by the extras' pickers for the blocked / hooked states.</summary>
    private static void AddExtraDllBadges(StackPanel contentRow, Button btn, ExtraDllOption o, string extraName)
    {
        if (o.IsHooked)
            contentRow.Children.Add(MakeBadge("Loaded by ASI Loader", "#1A2A3A", "#7AACDD", "#2A4A6A"));
        if (o.BlockedBy != null)
        {
            contentRow.Children.Add(MakeBadge($"In use by {o.BlockedBy}", "#2A1818", "#CC6666", "#5A2828"));
            ToolTipService.SetToolTip(btn, $"In use by {o.BlockedBy}. Choose a different name.");
        }
        else if (o.IsHooked)
            ToolTipService.SetToolTip(btn,
                $"ASI Loader is installed as {o.UalName} and loads {o.Name} as the real {o.UalName}, so {extraName} runs alongside it. " +
                $"If ASI Loader is removed, RHI renames this file back to {o.UalName}.");
    }
}
