using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Builds and shows the per-game addon selection ContentDialog.
/// Same toggle-only pattern as AddonManagerDialog: toggle on downloads if needed + enables,
/// toggle off disables (files stay in staging for global use).
/// </summary>
public static class AddonPopupHelper
{
    public enum PopupContext { Global, PerGame }

    /// <summary>
    /// Shows the per-game addon selection popup.
    /// Returns the list of enabled addon package names, or null if cancelled.
    /// </summary>
    public static async Task<List<string>?> ShowAsync(
        XamlRoot xamlRoot,
        IAddonPackService addonPackService,
        List<string>? currentSelection,
        PopupContext context)
    {
        var availableAddons = addonPackService.AvailablePacks
            .Where(a => !string.IsNullOrEmpty(a.DownloadUrl)
                     || !string.IsNullOrEmpty(a.DownloadUrl32)
                     || !string.IsNullOrEmpty(a.DownloadUrl64))
            .ToList();

        if (availableAddons.Count == 0)
        {
            var emptyDlg = new ContentDialog
            {
                Title = "Select Addons",
                Content = new TextBlock
                {
                    Text = "No addons available.",
                    FontSize = 13,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                },
                CloseButtonText = "Close",
                XamlRoot = xamlRoot,
                Background = Brush(ResourceKeys.SurfaceOverlayBrush),
                MinWidth = 750,
            };
            await emptyDlg.ShowAsync();
            return null;
        }

        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);
        var toggles = new List<(string PackageName, ToggleSwitch Toggle)>();

        var panel = new StackPanel { Spacing = 8 };

        foreach (var entry in availableAddons)
        {
            var isDownloaded = addonPackService.IsDownloaded(entry.PackageName);
            var isSelected = selected.Contains(entry.PackageName);

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

            // Right side: toggle — same behavior as global manager
            bool suppressToggle = false;
            var toggle = new ToggleSwitch
            {
                IsOn = isSelected,
                OnContent = "On",
                OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Capture for the lambda
            var capturedEntry = entry;
            var capturedTickMark = tickMark;

            toggle.Toggled += async (s, ev) =>
            {
                if (suppressToggle) return;

                if (toggle.IsOn)
                {
                    // Download if not already staged
                    if (!addonPackService.IsDownloaded(capturedEntry.PackageName))
                    {
                        toggle.IsEnabled = false;
                        try
                        {
                            await addonPackService.DownloadAddonAsync(capturedEntry);
                            capturedTickMark.Visibility = Visibility.Visible;
                        }
                        catch
                        {
                            suppressToggle = true;
                            toggle.IsOn = false;
                            suppressToggle = false;
                            toggle.IsEnabled = true;
                            return;
                        }
                        toggle.IsEnabled = true;
                    }
                }
            };

            toggles.Add((entry.PackageName, toggle));

            // Compose the row
            var rowGrid = new Grid { ColumnSpacing = 12 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textPanel, 0);
            Grid.SetColumn(toggle, 1);
            rowGrid.Children.Add(textPanel);
            rowGrid.Children.Add(toggle);

            panel.Children.Add(new Border
            {
                Child = rowGrid,
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                Background = Brush(ResourceKeys.SurfaceRaisedBrush),
                BorderBrush = Brush(ResourceKeys.BorderSubtleBrush),
                BorderThickness = new Thickness(1),
            });
        }

        var scrollViewer = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 600,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dlg = new ContentDialog
        {
            Title = "Select Addons",
            Content = scrollViewer,
            PrimaryButtonText = "Deploy",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            Background = Brush(ResourceKeys.SurfaceOverlayBrush),
            MinWidth = 750,
        };

        var dialogResult = await dlg.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary)
            return null;

        var confirmed = new List<string>();
        foreach (var (packageName, toggle) in toggles)
        {
            if (toggle.IsOn)
                confirmed.Add(packageName);
        }
        return confirmed;
    }

    /// <summary>
    /// Pure logic: computes the checkbox model for the popup.
    /// Returns one entry per available addon with its pre-checked state.
    /// Visible to tests via InternalsVisibleTo.
    /// </summary>
    internal static List<(string PackageName, bool IsChecked)> ComputeCheckboxModel(
        IReadOnlyList<AddonEntry> availableAddons,
        List<string>? currentSelection)
    {
        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);
        var model = new List<(string PackageName, bool IsChecked)>(availableAddons.Count);
        foreach (var addon in availableAddons)
            model.Add((addon.PackageName, selected.Contains(addon.PackageName)));
        return model;
    }

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
