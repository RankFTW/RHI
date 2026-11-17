// DetailPanelBuilder.Extras.FrameGen.Dlssg2030Checks.cs — 20/30 FG Unlock install checks: the OptiScaler warning
// (warn, not block: OptiScaler used for upscaling or GPU spoofing is fine, its frame generation isn't).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>OptiScaler is in the game: Continue / Cancel. True = go on with the install.</summary>
    private static async Task<bool> ConfirmDlssg2030WithOptiScalerAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot == null) return true;
        var dialog = new ContentDialog
        {
            Title = "OptiScaler is installed",
            Content = new TextBlock
            {
                Text = "OptiScaler is installed in this game. Turn off OptiScaler's frame generation: two frame generation mods in one game usually means no generated frames. "
                     + "OptiScaler used only for upscaling or GPU spoofing is fine.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 20,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var go = await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
        CrashReporter.Log($"[DetailPanelBuilder.ConfirmDlssg2030WithOptiScaler] {(go ? "continue" : "cancelled")}");
        return go;
    }
}
