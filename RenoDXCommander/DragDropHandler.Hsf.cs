// DragDropHandler.Hsf.cs: an HSF test build (a zip shared on Discord, holding hsf.addon64 / hsf.addon32 and
// HSF_streamline\) dropped on RHI. No buttons for this on purpose: drag and drop only. The build goes into the game
// the user picks (the selected one by default) through MainViewModel.InstallHsfBetaAsync, and RHI keeps it until the
// next HSF release (HsfService.Beta.cs). Builds set to XeMFG are offered Universal MFG Unlocker on every card.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DragDropHandler
{
    /// <summary>Installs the HSF build in <paramref name="packageRoot"/> (inside the extracted archive). True when installed.</summary>
    private async Task<bool> OfferHsfBetaAsync(string packageRoot, string archiveName)
    {
        var label = Path.GetFileNameWithoutExtension(archiveName);
        var xamlRoot = _window.Content.XamlRoot;
        var games = _window.ViewModel.AllCards
            .Where(c => !string.IsNullOrEmpty(c.InstallPath) && MainViewModel.HsfBlock(c) == null)
            .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (games.Count == 0)
        {
            await ShowDropMessageAsync("No game can take HSF",
                "HSF needs a game with ReShade (add-ons on) and no anti-cheat. Install ReShade in the game first, then drop the file again.");
            return false;
        }

        var names = games.Select(c => c.GameName).ToList();
        int pick = _window.ViewModel.SelectedGame is { } sel ? games.IndexOf(sel) : -1;
        if (pick < 0) pick = games.FindIndex(c => HsfService.IsRhiInstalled(c.InstallPath!));
        if (pick < 0) pick = FuzzyMatchGameIndex(names, archiveName);
        var combo = new ComboBox { ItemsSource = names, SelectedIndex = Math.Max(0, pick), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"'{archiveName}' is an HSF build. RHI installs it like a release (your HSF settings are kept) and leaves it "
                 + "in place until the next HSF release comes out, then updates the game to that. Reinstall HSF in the game's "
                 + "Extras section to go back to the release sooner.",
            TextWrapping = TextWrapping.Wrap, FontSize = 13,
        });
        content.Children.Add(new TextBlock { Text = "Install it in:", FontSize = 12 });
        content.Children.Add(combo);
        var dialog = new ContentDialog
        {
            Title = "Install this HSF build?",
            Content = content,
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return false;
        var card = games[Math.Max(0, combo.SelectedIndex)];

        _window.ShowStatusBarMessage($"Installing HSF build {label} in {card.GameName}...", TimeSpan.FromSeconds(30));
        var error = await _window.ViewModel.InstallHsfBetaAsync(card, packageRoot, label);
        if (error != null)
        {
            _window.ShowStatusBarMessage("The HSF build wasn't installed", TimeSpan.FromSeconds(6));
            await ShowDropMessageAsync("The HSF build wasn't installed", error);
            return false;
        }

        // A build set to XeMFG needs the unlocker for more than 2x on every card (asks only when neither unlocker is in)
        if (MainViewModel.HsfXeMfgNeedsUnlocker(card))
        {
            var builder = _window.DetailPanelBuilderInstance;
            var mfg = await builder.AskHsfMfgUnlockerAsync(card, xamlRoot);
            if (mfg.Install && await _window.ViewModel.InstallMfgForHsfAsync(card, mfg.ProxyName) is { } mfgError)
                await ShowDropMessageAsync("Universal MFG Unlocker wasn't installed", mfgError);
        }

        _window.ShowStatusBarMessage($"HSF build {label} installed in {card.GameName}", TimeSpan.FromSeconds(8));
        if (ReferenceEquals(_window.ViewModel.SelectedGame, card)) _window.DetailPanelBuilderInstance.RequestExtrasRebuild(card);
        return true;
    }

    private async Task ShowDropMessageAsync(string title, string text)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }
}
