// MainWindow.Events.Components.Rdx.ModMatching.cs — RenoDX ⚙ › Mod matching (v2.9.6, from Game Overrides, removed in
// 2.9.6.3): the wiki mod name the game is looked up by and whether it's looked up on the RenoDX / Luma wikis at all.
// The logic is in DetailPanelBuilder.Overrides.GameShared.cs.

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private void AddRdxModMatchingSection(CogDialog cog, GameCardViewModel card)
    {
        var dp = _detailPanelBuilder;
        if (dp == null) return;
        var gameName = card.GameName;

        cog.AddSection("Mod matching");
        var wikiBox = CogDialog.MakeTextBox(dp.GetWikiName(gameName), "Exact wiki name (blank = the game name)");
        void SaveWiki()
        {
            var text = wikiBox.Text?.Trim() ?? "";
            if (text == dp.GetWikiName(gameName)) return;
            dp.SetWikiName(gameName, text);
            dp.QueueOverridesRebuild(gameName);
            cog.SetSuccess(text.Length > 0
                ? $"Saved: RHI looks this game up as \"{text}\". Refresh to find its mod."
                : "Saved: RHI looks this game up by its own name. Refresh to find its mod.");
        }
        wikiBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; SaveWiki(); } };
        wikiBox.LostFocus += (s, e) => SaveWiki();
        cog.AddRow("Wiki mod name", wikiBox,
            "The name this game is listed under on the RenoDX / Luma wiki, when it differs from the game's name. Leave blank to use the game's name. "
            + "Saved when you press Enter or leave the box.");

        cog.AddCombo("Wiki lookup",
            "Use wiki: look this game up on the RenoDX and Luma wikis to find its mod. Ignore wiki: skip wiki lookups for this game.",
            new[] { DetailPanelBuilder.WikiIncludedItem, DetailPanelBuilder.WikiExcludedItem },
            dp.GetWikiLookupDisplay(gameName) == DetailPanelBuilder.WikiExcludedItem ? 1 : 0,
            i =>
            {
                dp.SetWikiLookup(gameName, i == 1 ? DetailPanelBuilder.WikiExcludedItem : DetailPanelBuilder.WikiIncludedItem);
                dp.QueueOverridesRebuild(gameName);
                cog.SetSuccess(i == 1 ? "RHI skips wiki lookups for this game." : "RHI looks this game up on the wikis.");
            });
    }
}
