// UpdateInclusionHelper.cs — Shared Update Inclusion dialog logic used by both
// DetailPanelBuilder and OverridesFlyoutBuilder.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Shared Update Inclusion dialog logic used by both DetailPanelBuilder
/// and OverridesFlyoutBuilder.
/// </summary>
public static class UpdateInclusionHelper
{
    /// <summary>
    /// Refreshes the summary TextBlock with the current On/Off state for each component.
    /// Callers can use this after externally toggling exclusions (e.g. a "Reset" button).
    /// </summary>
    public static void RefreshSummary(
        TextBlock summaryTb,
        MainViewModel viewModel,
        string gameName,
        bool isREEngineGame,
        bool isDxvkEnabled = false,
        string store = "")
    {
        summaryTb.Inlines.Clear();
        // A component switched off in Settings (Components to check) is Off for every game
        var s = viewModel.Settings;
        var items = new List<(string label, bool isOn)>
        {
            ("RS", !s.GlobalSkipRsUpdates && !viewModel.IsUpdateAllExcludedReShade(gameName, store)),
            ("RDX", !s.GlobalSkipRdxUpdates && !viewModel.IsUpdateAllExcludedRenoDx(gameName, store)),
            ("RL", !s.GlobalSkipUlUpdates && !viewModel.IsUpdateAllExcludedUl(gameName, store)),
            ("DC", !s.GlobalSkipDcUpdates && !viewModel.IsUpdateAllExcludedDc(gameName, store)),
            ("OS", !s.GlobalSkipOsUpdates && !viewModel.IsUpdateAllExcludedOs(gameName, store)),
        };
        if (isDxvkEnabled)
            items.Add(("DXVK", !viewModel.IsUpdateAllExcludedDxvk(gameName, store)));
        if (isREEngineGame)
            items.Add(("REF", !s.GlobalSkipRefUpdates && !viewModel.IsUpdateAllExcludedRef(gameName, store)));
        for (int i = 0; i < items.Count; i++)
        {
            var (label, isOn) = items[i];
            summaryTb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"{label}: ",
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
            summaryTb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = isOn ? "On" : "Off",
                Foreground = UIFactory.Brush(isOn ? ResourceKeys.AccentGreenBrush : ResourceKeys.AccentRedBrush),
            });
            if (i < items.Count - 1)
            {
                summaryTb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = "  ·  ",
                    Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                });
            }
        }
    }

    /// <summary>
    /// Creates the Update Inclusion button and summary text block.
    /// Returns (button, summaryTextBlock) for the caller to add to its layout.
    /// </summary>
    public static (Button button, TextBlock summary) CreateUpdateInclusionControls(
        MainViewModel viewModel,
        string gameName,
        bool isREEngineGame,
        XamlRoot xamlRoot,
        Action? onSaved = null,
        bool isDxvkEnabled = false,
        string store = "")
    {
        var summaryText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        RefreshSummary(summaryText, viewModel, gameName, isREEngineGame, isDxvkEnabled, store);
        ToolTipService.SetToolTip(summaryText,
            "Components Update All updates for this game. Components switched off in Settings show Off for every game.\n"
            + "RS = ReShade, RDX = RenoDX, RL = ReLimiter, DC = Display Commander, OS = OptiScaler, REF = RE Framework.");

        var button = new Button
        {
            Content = "Choose components...",
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 7),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        button.Click += async (s, ev) =>
        {
            // Resolve XamlRoot at click time — the build-time value may be null
            // if the panel was constructed before the window fully loaded.
            var effectiveRoot = xamlRoot ?? (s as FrameworkElement)?.XamlRoot;
            if (effectiveRoot == null)
            {
                CrashReporter.Log("[UpdateInclusionHelper] Cannot show dialog — XamlRoot is null");
                return;
            }
            var rsCheck = new CheckBox { Content = "ReShade", IsChecked = !viewModel.IsUpdateAllExcludedReShade(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var rdxCheck = new CheckBox { Content = "RenoDX", IsChecked = !viewModel.IsUpdateAllExcludedRenoDx(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var ulCheck = new CheckBox { Content = "ReLimiter", IsChecked = !viewModel.IsUpdateAllExcludedUl(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var dcCheck = new CheckBox { Content = "Display Commander", IsChecked = !viewModel.IsUpdateAllExcludedDc(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var osCheck = new CheckBox { Content = "OptiScaler", IsChecked = !viewModel.IsUpdateAllExcludedOs(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            CheckBox? dxvkCheck = isDxvkEnabled
                ? new CheckBox { Content = "DXVK", IsChecked = !viewModel.IsUpdateAllExcludedDxvk(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) }
                : null;
            CheckBox? refCheck = isREEngineGame
                ? new CheckBox { Content = "RE Framework", IsChecked = !viewModel.IsUpdateAllExcludedRef(gameName, store), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) }
                : null;

            var checkPanel = new StackPanel { Spacing = 0 };
            checkPanel.Children.Add(new TextBlock { Text = "Include this game in Update All for:", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 0, 0, 8) });
            checkPanel.Children.Add(rsCheck);
            checkPanel.Children.Add(rdxCheck);
            checkPanel.Children.Add(ulCheck);
            checkPanel.Children.Add(dcCheck);
            checkPanel.Children.Add(osCheck);
            if (dxvkCheck != null) checkPanel.Children.Add(dxvkCheck);
            if (refCheck != null) checkPanel.Children.Add(refCheck);

            var dialog = new ContentDialog
            {
                Title = "Update Inclusion",
                Content = checkPanel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = effectiveRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(dialog);
            if (result == ContentDialogResult.Primary)
            {
                // Apply changes
                if ((rsCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedReShade(gameName, store))
                    viewModel.ToggleUpdateAllExclusionReShade(gameName, store);
                if ((rdxCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedRenoDx(gameName, store))
                    viewModel.ToggleUpdateAllExclusionRenoDx(gameName, store);
                if ((ulCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedUl(gameName, store))
                    viewModel.ToggleUpdateAllExclusionUl(gameName, store);
                if ((dcCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedDc(gameName, store))
                    viewModel.ToggleUpdateAllExclusionDc(gameName, store);
                if ((osCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedOs(gameName, store))
                    viewModel.ToggleUpdateAllExclusionOs(gameName, store);
                if (dxvkCheck != null && (dxvkCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedDxvk(gameName, store))
                    viewModel.ToggleUpdateAllExclusionDxvk(gameName, store);
                if (refCheck != null && (refCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedRef(gameName, store))
                    viewModel.ToggleUpdateAllExclusionRef(gameName, store);

                // Refresh summary
                RefreshSummary(summaryText, viewModel, gameName, isREEngineGame, isDxvkEnabled, store);

                // Notify caller so it can rebuild UI (e.g. component panel)
                onSaved?.Invoke();
            }
        };

        return (button, summaryText);
    }
}
