// DetailPanelBuilder.Extras.ClosedSource.cs — notice shown before installing an Extras mod whose author
// publishes only the compiled DLL (RTX Encore, 20/30 FG Unlock): Install / Cancel / Don't show this again.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>
    /// Tells the user <paramref name="modName"/> is closed source before a first install. Skipped once
    /// <paramref name="isDismissed"/> is true; ticking "Don't show this again" calls <paramref name="dismiss"/>
    /// and saves settings. Returns true to go on with the install.
    /// </summary>
    private async Task<bool> ShowClosedSourceNoticeAsync(string modName, string author, Func<bool> isDismissed, Action dismiss, XamlRoot? xamlRoot)
    {
        xamlRoot ??= _window.Content.XamlRoot;
        if (xamlRoot == null || isDismissed()) return true;

        var content = new StackPanel { Spacing = 8 };
        void Para(string text) => content.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        Para($"{modName} is closed source: {author} publishes only the finished DLL, not its code, so nobody outside can check what it does.");
        Para($"RHI downloads it unchanged from {author}'s GitHub releases. Only install it if you're OK with that. The Info button on its row opens its GitHub page.");
        var dontShow = new CheckBox { Content = "Don't show this again", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        content.Children.Add(dontShow);

        var dialog = new ContentDialog
        {
            Title = $"{modName} is closed source",
            Content = content,
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(dialog);
        if (dontShow.IsChecked == true)
        {
            dismiss();
            _window.ViewModel.SaveSettingsPublic();
        }
        return result == ContentDialogResult.Primary;
    }
}
