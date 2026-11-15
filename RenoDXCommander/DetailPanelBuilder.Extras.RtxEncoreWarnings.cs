// DetailPanelBuilder.Extras.RtxEncoreWarnings.cs — warning shown after RHI installs RTX Encore's DLSS frame
// generation files in a game where ReShade is dxgi.dll: choosing DLSS-FG then crashes the game at startup
// (RTX Encore issue #133; Streamline loads ReShade's dxgi.dll and its swap chain creation loops back into itself).
// A warning rather than a block, so users decide and new RTX Encore versions can be tested without an RHI update.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal const string EncoreDlssFgReShadeIssueUrl = "https://github.com/SilyNoMeta/rtx-encore/issues/133";

    internal const string EncoreDlssFgReShadeWarningText =
        "With ReShade installed as dxgi.dll, RTX Encore's added DLSS-FG can still run, but RTX Encore's in-game menu may not open: set it up in RTX Encore's settings in RHI instead. "
        + "In games with their own FSR frame generation, choosing DLSS-FG with ReShade as dxgi.dll currently makes the game crash at startup; without ReShade it works. "
        + "If the game crashes after choosing DLSS-FG, set Engine to XeFG or Game in RTX Encore's settings in RHI.";

    /// <summary>True when this game's ReShade is installed as dxgi.dll (the setup RTX Encore's added DLSS-FG crashes with).</summary>
    internal static bool IsReShadeDxgi(GameCardViewModel card) =>
        card.IsRsInstalled && string.Equals(card.RsInstalledFile, "dxgi.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tells the user about the DLSS-FG + ReShade dxgi.dll crash after RHI installed the frame generation files.
    /// Information only (the install has happened); "Don't show this again" is saved.
    /// </summary>
    private async Task ShowEncoreDlssFgReShadeWarningAsync(XamlRoot? xamlRoot)
    {
        xamlRoot ??= _window.Content.XamlRoot;
        if (xamlRoot == null || _window.ViewModel.Settings.EncoreDlssFgReShadeWarningDismissed) return;

        var dontShowCheck = new CheckBox
        {
            Content = "Don't show this again",
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "RHI installed the NVIDIA DLSS frame generation files RTX Encore uses to add frame generation to this game.\n\n"
                 + EncoreDlssFgReShadeWarningText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 22,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        var link = new HyperlinkButton { Content = "RTX Encore issue #133", Padding = new Thickness(0), FontSize = 12 };
        link.Click += (s, e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(EncoreDlssFgReShadeIssueUrl));
        content.Children.Add(link);
        content.Children.Add(dontShowCheck);

        var dialog = new ContentDialog
        {
            Title = "DLSS-FG and ReShade",
            Content = content,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        await DialogService.ShowSafeAsync(dialog);
        if (dontShowCheck.IsChecked == true)
        {
            _window.ViewModel.Settings.EncoreDlssFgReShadeWarningDismissed = true;
            _window.ViewModel.SaveSettingsPublic();
            CrashReporter.Log("[DetailPanelBuilder.ShowEncoreDlssFgReShadeWarningAsync] Don't show again ticked");
        }
    }
}
