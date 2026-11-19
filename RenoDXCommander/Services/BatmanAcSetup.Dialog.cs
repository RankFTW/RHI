// BatmanAcSetup.Dialog.cs -- the pop-up shown before boostedwarr's Batman: Arkham City mod installs: what the mod is,
// what RHI does with it and the few things to do in the game. Same look as the other games' install pop-ups.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RenoDXCommander.Services;

internal static partial class BatmanAcSetup
{
    /// <summary>Shows the install pop-up. True = Install, false = Cancel. No window to show it in = true.</summary>
    internal static async Task<bool> ShowInstallDialogAsync()
    {
        try
        {
            XamlRoot? xamlRoot = null;
            if (Application.Current is App app
                && typeof(App).GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(app) is MainWindow mw)
                xamlRoot = mw.Content?.XamlRoot;
            if (xamlRoot == null) return true;

            var content = new StackPanel { Spacing = 10, MaxWidth = 620 };
            void Heading(string text) => content.Children.Add(new TextBlock
            {
                Text = text, FontSize = 13, FontWeight = new Windows.UI.Text.FontWeight(600),
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 4, 0, 0),
            });
            void Body(string text, string brush = ResourceKeys.TextSecondaryBrush, double size = 13) => content.Children.Add(new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, Foreground = UIFactory.Brush(brush),
            });

            Body("HDR for the game's DirectX 11 renderer by boostedwarr, with optional DLAA or FSR anti-aliasing at native " +
                 "resolution. The game is 32-bit, so the anti-aliasing runs in a small 64-bit helper program that the mod starts by itself.");

            Heading("Clicking Install will also:");
            Body("  •  Install the mod's helper (renodx-batmanac-dlaa-host64.exe) next to it\n" +
                 "  •  Add the newest DLSS (nvngx_dlss.dll) for DLAA and AMD's FSR files for FSR\n" +
                 "  •  Set the launcher's options the mod needs: DirectX 11, MSAA off, FXAA off, Depth of Field on");

            Heading("In the game:");
            Body("  •  Turn Anti-Aliasing on in the mod's settings (ReShade menu) once you're in the game, not at the main menu: " +
                 "it can fail to start there and then stays off until the game restarts\n" +
                 "  •  Leave the brightness slider at its default and use the mod's Game Brightness instead\n" +
                 "  •  Keep MSAA and FXAA off and Depth of Field on in the launcher (use the mod's Depth of Field slider for less blur)\n" +
                 "  •  The game runs borderless; the mod needs that for HDR",
                 ResourceKeys.InlineDescriptionBrush, 12);

            Body("Removing the mod removes the helper, DLSS and FSR files. The launcher options stay as they are.",
                 ResourceKeys.TextTertiaryBrush, 12);

            var dialog = new ContentDialog
            {
                Title = "Batman: Arkham City — RenoDX Mod",
                Content = new ScrollViewer { Content = content, MaxHeight = 560 },
                PrimaryButtonText = "Install",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 700.0;
            return await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[BatmanAcSetup.ShowInstallDialogAsync] {ex.Message}");
            return true;
        }
    }
}
