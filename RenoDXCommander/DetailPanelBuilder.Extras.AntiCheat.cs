// DetailPanelBuilder.Extras.AntiCheat.cs — warning before installing a frame generation unlock (Universal MFG Unlocker,
// MFG Unlock, 20/30 FG Unlock) in a game on RHI's anti-cheat list (manifest dxvkBlacklist, card.IsDxvkBlacklisted).
// User decision (Nov 2026): warn, don't block (HSF blocks). Continue / Cancel / "Don't show this again" (per mod).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>Keys for <see cref="ConfirmAntiCheatAsync"/> (stored in Settings.AntiCheatNoticesDismissed).</summary>
    internal static class AntiCheatNoticeKey
    {
        public const string UniversalMfg = "UniversalMfg";
        public const string MfgUnlock    = "MfgUnlock";
        public const string Dlssg2030    = "Dlssg2030";
    }

    /// <summary>
    /// When the game is on RHI's anti-cheat list, warns before installing <paramref name="modName"/>.
    /// Returns true to continue (not on the list, dismissed before, or Continue).
    /// </summary>
    internal async Task<bool> ConfirmAntiCheatAsync(GameCardViewModel card, string modName, string key, XamlRoot? xamlRoot)
    {
        if (!card.IsDxvkBlacklisted) return true;
        var settings = _window.ViewModel.Settings;
        var dismissed = (settings.AntiCheatNoticesDismissed ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (dismissed.Contains(key, StringComparer.OrdinalIgnoreCase)) return true;
        xamlRoot ??= _window.Content.XamlRoot;
        if (xamlRoot == null) return true;

        var content = new StackPanel { Spacing = 8 };
        void Para(string text) => content.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        Para($"{card.GameName} is on RHI's anti-cheat list.");
        Para($"{modName} isn't supported in games with online anti-cheat, and using it there can get your account banned. "
            + "Only continue if you play this game offline, or know its anti-cheat allows it.");
        var dontShow = new CheckBox { Content = "Don't show this again", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        content.Children.Add(dontShow);

        var dialog = new ContentDialog
        {
            Title = "Anti-cheat game",
            Content = content,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(dialog);
        bool go = result == ContentDialogResult.Primary;
        if (go && dontShow.IsChecked == true)
        {
            settings.AntiCheatNoticesDismissed = string.Join(",", dismissed.Append(key));
            _window.ViewModel.SaveSettingsPublic();
        }
        CrashReporter.Log($"[DetailPanelBuilder.ConfirmAntiCheatAsync] {modName} in '{card.GameName}': {(go ? "continue" : "cancelled")}");
        return go;
    }
}
