// UpdateInclusionHelper.cs — Shared Update Inclusion dialog logic used by both
// DetailPanelBuilder and OverridesFlyoutBuilder.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        bool isREEngineGame)
    {
        summaryTb.Inlines.Clear();
        var items = new List<(string label, bool isOn)>
        {
            ("RS", !viewModel.IsUpdateAllExcludedReShade(gameName)),
            ("RDX", !viewModel.IsUpdateAllExcludedRenoDx(gameName)),
            ("UL", !viewModel.IsUpdateAllExcludedUl(gameName)),
            ("DC", !viewModel.IsUpdateAllExcludedDc(gameName)),
            ("OS", !viewModel.IsUpdateAllExcludedOs(gameName)),
        };
        if (isREEngineGame)
            items.Add(("REF", !viewModel.IsUpdateAllExcludedRef(gameName)));
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
        Action? onSaved = null)
    {
        var summaryText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        RefreshSummary(summaryText, viewModel, gameName, isREEngineGame);

        var button = new Button
        {
            Content = "Update Inclusion",
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
            var rsCheck = new CheckBox { Content = "ReShade", IsChecked = !viewModel.IsUpdateAllExcludedReShade(gameName), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var rdxCheck = new CheckBox { Content = "RenoDX", IsChecked = !viewModel.IsUpdateAllExcludedRenoDx(gameName), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var ulCheck = new CheckBox { Content = "ReLimiter", IsChecked = !viewModel.IsUpdateAllExcludedUl(gameName), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var dcCheck = new CheckBox { Content = "Display Commander", IsChecked = !viewModel.IsUpdateAllExcludedDc(gameName), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            var osCheck = new CheckBox { Content = "OptiScaler", IsChecked = !viewModel.IsUpdateAllExcludedOs(gameName), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
            CheckBox? refCheck = isREEngineGame
                ? new CheckBox { Content = "RE Framework", IsChecked = !viewModel.IsUpdateAllExcludedRef(gameName), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) }
                : null;

            var checkPanel = new StackPanel { Spacing = 0 };
            checkPanel.Children.Add(new TextBlock { Text = "Include this game in Update All for:", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 0, 0, 8) });
            checkPanel.Children.Add(rsCheck);
            checkPanel.Children.Add(rdxCheck);
            checkPanel.Children.Add(ulCheck);
            checkPanel.Children.Add(dcCheck);
            checkPanel.Children.Add(osCheck);
            if (refCheck != null) checkPanel.Children.Add(refCheck);

            var dialog = new ContentDialog
            {
                Title = "Global Update Inclusion",
                Content = checkPanel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(dialog);
            if (result == ContentDialogResult.Primary)
            {
                // Apply changes
                if ((rsCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedReShade(gameName))
                    viewModel.ToggleUpdateAllExclusionReShade(gameName);
                if ((rdxCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedRenoDx(gameName))
                    viewModel.ToggleUpdateAllExclusionRenoDx(gameName);
                if ((ulCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedUl(gameName))
                    viewModel.ToggleUpdateAllExclusionUl(gameName);
                if ((dcCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedDc(gameName))
                    viewModel.ToggleUpdateAllExclusionDc(gameName);
                if ((osCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedOs(gameName))
                    viewModel.ToggleUpdateAllExclusionOs(gameName);
                if (refCheck != null && (refCheck.IsChecked == true) == viewModel.IsUpdateAllExcludedRef(gameName))
                    viewModel.ToggleUpdateAllExclusionRef(gameName);

                // Refresh summary
                RefreshSummary(summaryText, viewModel, gameName, isREEngineGame);

                // Notify caller so it can rebuild UI (e.g. component panel)
                onSaved?.Invoke();
            }
        };

        return (button, summaryText);
    }
}
