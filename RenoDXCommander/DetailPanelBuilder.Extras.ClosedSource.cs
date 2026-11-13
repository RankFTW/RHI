// DetailPanelBuilder.Extras.ClosedSource.cs — notice shown before installing a mod whose author publishes
// only the compiled DLL (RTX Encore, 20/30 FG Unlock, ANR FG): Install / Cancel / Don't show this again.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>
    /// Tells the user <paramref name="modName"/> is closed source before a first install. Skipped once
    /// <paramref name="isDismissed"/> is true; ticking "Don't show this again" calls <paramref name="dismiss"/>
    /// and saves settings. <paramref name="githubUrl"/> replaces the line pointing at the row's Info button with a
    /// clickable link (for mods without a row of their own). Returns true to go on with the install. Static so the
    /// OptiScaler cog (MainWindow) can use it too.
    /// </summary>
    internal static async Task<bool> ShowClosedSourceNoticeAsync(string modName, string author, Func<bool> isDismissed, Action dismiss,
        XamlRoot? xamlRoot, string? githubUrl = null)
    {
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
        var line = $"RHI downloads it unchanged from {author}'s GitHub releases. Only install it if you're OK with that. ";
        if (githubUrl == null)
            Para(line + "The Info button on its row opens its GitHub page.");
        else
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 20,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            };
            tb.Inlines.Add(new Run { Text = line + "Its GitHub page: " });
            var link = new Hyperlink { NavigateUri = new Uri(githubUrl), Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush) };
            link.Inlines.Add(new Run { Text = githubUrl.Replace("https://", "") });
            tb.Inlines.Add(link);
            tb.Inlines.Add(new Run { Text = "." });
            content.Children.Add(tb);
        }
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
            App.Services.GetRequiredService<MainViewModel>().SaveSettingsPublic();
        }
        return result == ContentDialogResult.Primary;
    }
}
