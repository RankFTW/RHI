using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Builds and shows the Addon Manager ContentDialog.
/// Displays all available addons with download/repository actions and status indicators.
/// Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 4.3
/// </summary>
public static class AddonManagerDialog
{
    /// <summary>
    /// Shows the Addon Manager Window as a ContentDialog.
    /// </summary>
    /// <param name="enabledAddons">The current globally-enabled addon list (persisted in settings).</param>
    /// <param name="onEnabledChanged">Called when the enabled set changes so the caller can persist and redeploy.</param>
    public static async Task ShowAsync(XamlRoot xamlRoot, IAddonPackService addonPackService,
        List<string> enabledAddons, Action onEnabledChanged)
    {
        var packs = addonPackService.AvailablePacks
            .Where(e => GetActionType(e) == "download")
            .ToList();

        if (packs.Count == 0)
        {
            var emptyDlg = new ContentDialog
            {
                Title = "ReShade Addon Manager",
                Content = new TextBlock
                {
                    Text = "No addons available. Try refreshing.",
                    FontSize = 13,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                },
                CloseButtonText = "Close",
                XamlRoot = xamlRoot,
                Background = Brush(ResourceKeys.SurfaceOverlayBrush),
                MinWidth = 750,
                RequestedTheme = ElementTheme.Dark,
            };
            await emptyDlg.ShowAsync();
            return;
        }

        var panel = new StackPanel { Spacing = 8 };

        foreach (var entry in packs)
        {
            var row = BuildAddonRow(entry, addonPackService, xamlRoot, enabledAddons, onEnabledChanged);
            panel.Children.Add(row);
        }

        var scrollViewer = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 600,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dlg = new ContentDialog
        {
            Title = "ReShade Addon Manager",
            Content = scrollViewer,
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
            Background = Brush(ResourceKeys.SurfaceOverlayBrush),
            MinWidth = 750,
            RequestedTheme = ElementTheme.Dark,
        };

        await dlg.ShowAsync();
    }

    /// <summary>
    /// Determines the action type for an addon entry.
    /// Returns "download" if any download URL is present, "repository" if only RepositoryUrl, or "none".
    /// </summary>
    internal static string GetActionType(AddonEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.DownloadUrl) ||
            !string.IsNullOrEmpty(entry.DownloadUrl32) ||
            !string.IsNullOrEmpty(entry.DownloadUrl64))
            return "download";

        if (!string.IsNullOrEmpty(entry.RepositoryUrl))
            return "repository";

        return "none";
    }

    /// <summary>
    /// Builds a single addon row. Toggle on = download (if needed) + enable.
    /// Toggle off = disable (files stay in staging). No separate Download/Update buttons.
    /// Repository-only entries get a link.
    /// </summary>
    private static Border BuildAddonRow(AddonEntry entry, IAddonPackService addonPackService,
        XamlRoot xamlRoot, List<string> enabledAddons, Action onEnabledChanged)
    {
        var isDownloaded = addonPackService.IsDownloaded(entry.PackageName);
        var actionType = GetActionType(entry);
        var isEnabled = enabledAddons.Contains(entry.PackageName, StringComparer.OrdinalIgnoreCase);

        // Left side: name (with green ✓ if downloaded) + description
        var textPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nameRow.Children.Add(new TextBlock
        {
            Text = entry.PackageName,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
        });
        var tickMark = new TextBlock
        {
            Text = "✓",
            FontSize = 13,
            Foreground = Brush(ResourceKeys.AccentGreenBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isDownloaded ? Visibility.Visible : Visibility.Collapsed,
        };
        nameRow.Children.Add(tickMark);
        textPanel.Children.Add(nameRow);

        if (!string.IsNullOrEmpty(entry.PackageDescription))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = entry.PackageDescription,
                FontSize = 11,
                Opacity = 0.6,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
            });
        }

        // Right side: toggle or repository link
        FrameworkElement rightElement;

        if (actionType == "download")
        {
            bool suppressToggle = false;

            var toggle = new ToggleSwitch
            {
                IsOn = isEnabled,
                OnContent = "On",
                OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center,
            };

            toggle.Toggled += async (s, ev) =>
            {
                if (suppressToggle) return;

                if (toggle.IsOn)
                {
                    // Download if not already staged, then enable
                    if (!addonPackService.IsDownloaded(entry.PackageName))
                    {
                        toggle.IsEnabled = false;
                        try
                        {
                            await addonPackService.DownloadAddonAsync(entry);
                            tickMark.Visibility = Visibility.Visible;
                        }
                        catch
                        {
                            // Revert on failure
                            suppressToggle = true;
                            toggle.IsOn = false;
                            suppressToggle = false;
                            toggle.IsEnabled = true;
                            return;
                        }
                        toggle.IsEnabled = true;
                    }

                    if (!enabledAddons.Contains(entry.PackageName, StringComparer.OrdinalIgnoreCase))
                        enabledAddons.Add(entry.PackageName);
                    onEnabledChanged();
                }
                else
                {
                    // Disable — files stay in staging
                    enabledAddons.RemoveAll(n => n.Equals(entry.PackageName, StringComparison.OrdinalIgnoreCase));
                    onEnabledChanged();
                }
            };

            rightElement = toggle;
        }
        else if (actionType == "repository")
        {
            rightElement = new HyperlinkButton
            {
                Content = "Repository",
                NavigateUri = new Uri(entry.RepositoryUrl!),
                FontSize = 11,
                Foreground = Brush(ResourceKeys.AccentBlueBrush),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
        }
        else
        {
            rightElement = new TextBlock();
        }

        // Compose the row: [text] [toggle/link]
        var rowGrid = new Grid { ColumnSpacing = 12 };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textPanel, 0);
        Grid.SetColumn(rightElement, 1);
        rowGrid.Children.Add(textPanel);
        rowGrid.Children.Add(rightElement);

        return new Border
        {
            Child = rowGrid,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = Brush(ResourceKeys.SurfaceRaisedBrush),
            BorderBrush = Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
