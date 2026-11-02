using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

// Store badge in the detail header: click opens the game's Steam Community hub.
public partial class DetailPanelBuilder
{
    private bool _storeBadgeWired;

    /// <summary>
    /// Steam AppID for the game's Community hub, from any store:
    /// manifest steamAppIdOverrides, then the Steam library, then PCGamingWiki data.
    /// </summary>
    internal static int? ResolveCommunityAppId(GameCardViewModel card, Models.RemoteManifest? manifest)
    {
        if (manifest?.SteamAppIdOverrides != null
            && manifest.SteamAppIdOverrides.TryGetValue(card.GameName, out var overrideId) && overrideId > 0)
            return overrideId;
        if (card.DetectedGame?.SteamAppId is int detected && detected > 0)
            return detected;
        try
        {
            var pcgw = App.Services.GetRequiredService<IPcgwService>();
            return pcgw.GetSteamAppIdFromCentralData(card.GameName);
        }
        catch (InvalidOperationException ex)
        {
            CrashReporter.Log($"[DetailPanelBuilder.ResolveCommunityAppId] {card.GameName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Makes the store badge open the Steam Community hub when an AppID is known.</summary>
    private void UpdateStoreBadgeLink(GameCardViewModel card)
    {
        var badge = _window.DetailSourceBadge;
        if (!_storeBadgeWired)
        {
            _storeBadgeWired = true;
            badge.PointerPressed += StoreBadge_PointerPressed;
            badge.PointerEntered += (_, _) => { if (badge.Tag is int) CursorProp?.SetValue(badge, HandCursor); };
            badge.PointerExited += (_, _) => CursorProp?.SetValue(badge, ArrowCursor);
        }

        var appId = ResolveCommunityAppId(card, _window.ViewModel.Manifest);
        badge.Tag = appId;
        ToolTipService.SetToolTip(badge, appId != null ? "Open the Steam Community page" : null);
        // Underlined like the other clickable badges (engine, authors)
        _window.DetailSourceText.TextDecorations = appId != null
            ? Windows.UI.Text.TextDecorations.Underline
            : Windows.UI.Text.TextDecorations.None;
    }

    private static readonly Microsoft.UI.Input.InputCursor HandCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    private static readonly Microsoft.UI.Input.InputCursor ArrowCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);

    private async void StoreBadge_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: int appId }) return;
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            var url = $"https://steamcommunity.com/app/{appId}";
            CrashReporter.Log($"[DetailPanelBuilder.StoreBadge_PointerPressed] Opening {url}");
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DetailPanelBuilder.StoreBadge_PointerPressed] {ex.Message}");
        }
    }
}
