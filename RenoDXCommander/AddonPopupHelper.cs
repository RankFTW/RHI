using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using System.IO;

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
        PopupContext context,
        string? installPath = null)
    {
        var availableAddons = addonPackService.AvailablePacks
            .Where(a => !a.HideFromPicker
                     && (!string.IsNullOrEmpty(a.DownloadUrl)
                      || !string.IsNullOrEmpty(a.DownloadUrl32)
                      || !string.IsNullOrEmpty(a.DownloadUrl64)
                      || !string.IsNullOrEmpty(a.ReleaseApiUrl)
                      || a.SectionId.Equals("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                      || a.SectionId.Equals("renodx-dlss-sf", StringComparison.OrdinalIgnoreCase))) // managed by Renodx5AddonService
            .ToList();

        // Include custom addons (local files, no download URLs)
        var customAddons = addonPackService.GetCustomAddons();
        availableAddons.AddRange(customAddons);

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
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(emptyDlg);
            return null;
        }

        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);
        var toggles = new List<(string PackageName, ToggleSwitch Toggle)>();

        var panel = new StackPanel { Spacing = 8 };

        // Folder link at top
        var customFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Custom", "Addons");
        var folderLink = new HyperlinkButton
        {
            Content = "Place custom .addon64/.addon32 files here",
            FontSize = 11,
            Foreground = Brush(ResourceKeys.AccentBlueBrush),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4),
        };
        folderLink.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(customFolderPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = customFolderPath,
                    UseShellExecute = true,
                });
            }
            catch { }
        };
        panel.Children.Add(folderLink);

        // Separate managed and custom addons
        var managedAddons = availableAddons.Where(a => !a.SectionId.StartsWith("custom-", StringComparison.OrdinalIgnoreCase)).ToList();
        var customAddonsList = availableAddons.Where(a => a.SectionId.StartsWith("custom-", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var entry in managedAddons)
        {
            var isCustom = false;
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
            var versionLabel = addonPackService.GetVersionLabel(entry.SectionId);
            if (!string.IsNullOrEmpty(versionLabel))
                nameRow.Children.Add(new TextBlock
                {
                    Text = versionLabel,
                    FontSize = 11,
                    Foreground = Brush(ResourceKeys.TextSecondaryBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.7,
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

            if (!string.IsNullOrEmpty(entry.RepositoryUrl)
                && !entry.SectionId.StartsWith("custom-", StringComparison.OrdinalIgnoreCase))
            {
                textPanel.Children.Add(new HyperlinkButton
                {
                    Content = "How to use",
                    NavigateUri = new Uri(entry.RepositoryUrl),
                    FontSize = 11,
                    Foreground = Brush(ResourceKeys.AccentBlueBrush),
                    Padding = new Thickness(0),
                });
            }

            // Right side: toggle — same behavior as global manager
            bool suppressToggle = false;

            // Determine if this entry is blocked by mutual exclusivity (dlss5 ↔ dlss-sf, mfgunlock ↔ RTX40MFG.asi)
            bool isMutuallyExclusive = entry.SectionId.Equals("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                                    || entry.SectionId.Equals("renodx-dlss-sf", StringComparison.OrdinalIgnoreCase)
                                    || entry.SectionId.Equals("mfgunlock", StringComparison.OrdinalIgnoreCase);
            string? mutualExclusivePeer = entry.SectionId.Equals("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                ? "DLSS Tool (ShortFuse)"
                : entry.SectionId.Equals("renodx-dlss-sf", StringComparison.OrdinalIgnoreCase)
                ? "DLSS5 Tool" : null;
            bool peerIsSelected = mutualExclusivePeer != null && selected.Contains(mutualExclusivePeer);

            // RTX 40 MFG conflict — MFG Ada Unlock blocked when new standalone DLL version is installed
            bool rtx40MfgConflict = entry.SectionId.Equals("mfgunlock", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(installPath)
                && Rtx40MfgService.KnownProxyNames.Any(n =>
                {
                    var candidate = Path.Combine(installPath, n);
                    if (!File.Exists(candidate)) return false;
                    // Confirm it's actually RTXMFG by checking the sentinel
                    return File.Exists(candidate + ".original");
                });
            if (rtx40MfgConflict) peerIsSelected = true;

            // Extras button conflict — MFG Ada Unlock blocked when already installed directly via Extras
            bool extrasInstalledConflict = entry.SectionId.Equals("mfgunlock", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(installPath)
                && File.Exists(Path.Combine(installPath, "renodx-mfgunlock.addon64"));
            if (extrasInstalledConflict) peerIsSelected = true;

            var toggle = new ToggleSwitch
            {
                IsOn = isSelected,
                OnContent = "On",
                OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !peerIsSelected,
                Opacity = peerIsSelected ? 0.35 : 1.0,
            };
            if (peerIsSelected)
                ToolTipService.SetToolTip(toggle, rtx40MfgConflict
                    ? "RTX 40 MFG Unlock is already installed and conflicts with this addon. Remove it from the Extras section first."
                    : $"Disable {mutualExclusivePeer} first to enable this addon.");

            // Capture for the lambda
            var capturedEntry = entry;
            var capturedTickMark = tickMark;
            var isCustomAddon = entry.SectionId.StartsWith("custom-", StringComparison.OrdinalIgnoreCase);

            toggle.Toggled += async (s, ev) =>
            {
                if (suppressToggle) return;

                if (toggle.IsOn)
                {
                    // Download if not already staged (skip for custom addons — they're local files)
                    if (!isCustomAddon && !addonPackService.IsDownloaded(capturedEntry.PackageName))
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

            // Wire mutual exclusivity — when this toggle turns on, grey out the peer
            if (isMutuallyExclusive && mutualExclusivePeer != null)
            {
                var capturedPeerName = mutualExclusivePeer;
                toggle.Toggled += (s, ev) =>
                {
                    if (suppressToggle) return;
                    var peer = toggles.FirstOrDefault(t => t.PackageName.Equals(capturedPeerName, StringComparison.OrdinalIgnoreCase));
                    if (peer.Toggle != null)
                    {
                        peer.Toggle.IsEnabled = !toggle.IsOn;
                        peer.Toggle.Opacity = toggle.IsOn ? 0.35 : 1.0;
                        if (toggle.IsOn)
                            ToolTipService.SetToolTip(peer.Toggle, $"Disable {entry.PackageName} first to enable this addon.");
                        else
                            ToolTipService.SetToolTip(peer.Toggle, null);
                    }
                };
            }

            toggles.Add((entry.PackageName, toggle));

            // If already selected but not yet downloaded (e.g. added to selection on a previous session
            // before staging completed), trigger the download now so it's ready when the dialog closes.
            if (isSelected && !isCustomAddon && !addonPackService.IsDownloaded(capturedEntry.PackageName))
            {
                toggle.IsEnabled = false;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await addonPackService.DownloadAddonAsync(capturedEntry);
                        toggle.DispatcherQueue?.TryEnqueue(() => capturedTickMark.Visibility = Visibility.Visible);
                    }
                    catch { }
                    finally
                    {
                        toggle.DispatcherQueue?.TryEnqueue(() => toggle.IsEnabled = !peerIsSelected);
                    }
                });
            }

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

        // Custom addons section
        if (customAddonsList.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Custom Addons",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                Opacity = 0.6,
                Margin = new Thickness(0, 12, 0, 4),
            });

            foreach (var entry in customAddonsList)
            {
                var isCustom = true;
                var isDownloaded = true;
                var isSelected = selected.Contains(entry.PackageName);

                var textPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                nameRow.Children.Add(new TextBlock
                {
                    Text = entry.PackageName,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                });
                nameRow.Children.Add(new TextBlock
                {
                    Text = "✓",
                    FontSize = 13,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                });
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

                bool suppressToggle = false;
                var toggle = new ToggleSwitch
                {
                    IsOn = isSelected,
                    OnContent = "On",
                    OffContent = "Off",
                    VerticalAlignment = VerticalAlignment.Center,
                };
                toggle.Toggled += (s, ev) => { if (suppressToggle) return; };
                toggles.Add((entry.PackageName, toggle));

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
            RequestedTheme = ElementTheme.Dark,
        };

        var dialogResult = await DialogService.ShowSafeAsync(dlg);
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
