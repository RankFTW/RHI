// DetailPanelBuilder.Extras.cs — Extras section: Ultimate ASI Loader and future extras.

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    public void BuildExtrasSection(GameCardViewModel card)
    {
        _window.ExtrasPanel.Children.Clear();
        _window.ExtrasContainer.Visibility = Visibility.Visible;

        // ── Collapsible header ────────────────────────────────────────────────
        const string extrasSectionKey = "Extras";
        var exSettings   = _window.ViewModel.Settings;
        bool exCollapsed = exSettings.CollapsedDetailSections.Contains(extrasSectionKey);

        var exArrow = new TextBlock
        {
            Text      = exCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var exTitle = new TextBlock
        {
            Text       = "Extras",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var exHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        exHeaderRow.Children.Add(MakeDragHandle(_window.ExtrasContainer));
        exHeaderRow.Children.Add(exArrow);
        exHeaderRow.Children.Add(exTitle);
        _window.ExtrasPanel.Children.Add(exHeaderRow);

        var exBody = new StackPanel { Spacing = 10, Visibility = exCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.ExtrasPanel.Children.Add(exBody);

        exHeaderRow.PointerEntered += (s, e) => exTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        exHeaderRow.PointerExited  += (s, e) => exTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var exHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var exArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var exCursorProp  = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        exHeaderRow.PointerEntered += (s, e) => exCursorProp?.SetValue(exHeaderRow, exHandCursor);
        exHeaderRow.PointerExited  += (s, e) => exCursorProp?.SetValue(exHeaderRow, exArrowCursor);
        exHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = exBody.Visibility == Visibility.Visible;
            exBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            exArrow.Text = nowCollapsed ? "▶" : "▼";
            if (nowCollapsed) exSettings.CollapsedDetailSections.Add(extrasSectionKey);
            else              exSettings.CollapsedDetailSections.Remove(extrasSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        // ── Ultimate ASI Loader row ───────────────────────────────────────────
        BuildUalRow(card, exBody);
    }

    private void BuildUalRow(GameCardViewModel card, StackPanel body)
    {
        var ualSvc    = _window.ViewModel.UalServiceInstance;
        var gameName  = card.GameName;
        var store     = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        // Detect current install state
        var ualRecord   = string.IsNullOrEmpty(installPath) ? null
            : _auxInstallService.FindRecord(gameName, installPath, UltimateAsiLoaderService.AddonType);
        bool isInstalled = ualRecord != null;
        string? installedAs = ualRecord?.InstalledAs;

        // Status text
        string statusText;
        string statusColor;
        if (isInstalled)
        {
            var staged = card.Is32Bit ? ualSvc.StagedVersion32 : ualSvc.StagedVersion64;
            statusText  = staged ?? "Installed";
            statusColor = "#5ECB7D";
        }
        else
        {
            statusText  = "Ready";
            statusColor = "#A0AABB";
        }

        // ── Row grid matching Components section exactly ───────────────────────
        // Col 0: label (120)  Col 1: status (80)  Col 2: Info (36)
        // Col 3: install (*)  Col 4: cog (36)     Col 5: delete (36)
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Col 0 — label
        var label = new TextBlock
        {
            Text = "ASI Loader",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, "Ultimate ASI Loader — proxy DLL that loads .asi plugins into game processes.");
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        // Col 1 — status
        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextDecorations = isInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
        };
        if (isInstalled)
        {
            ToolTipService.SetToolTip(statusBlock, $"Installed as: {installedAs}\nClick to open GitHub releases");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases"));
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Col 2 — Info button (matches Components style)
        var infoBtn = new Button
        {
            Content = "Info",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Width = 36,
            Height = 32,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(infoBtn, "Open Ultimate ASI Loader GitHub releases page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "Reinstall ASI Loader" : "Install ASI Loader",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            Background = isInstalled
                ? UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush)
                : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = isInstalled
                ? UIFactory.Brush(ResourceKeys.TextSecondaryBrush)
                : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isInstalled
                ? UIFactory.Brush(ResourceKeys.BorderDefaultBrush)
                : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(installBtn, isInstalled
            ? $"Reinstall Ultimate ASI Loader (currently '{installedAs}')"
            : "Install Ultimate ASI Loader — choose which DLL name to use");
        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            var chosen = await ShowUalDllPickerAsync(card, ualRecord?.InstalledAs);
            if (chosen == null) return;

            installBtn.IsEnabled = false;
            installBtn.Content = "Installing...";
            try
            {
                var (success, hookedOriginal) = await ualSvc.InstallAsync(card, chosen);
                if (success)
                {
                    _window.ViewModel.SetUalInstalledAs(gameName, chosen, store);
                    if (hookedOriginal != null)
                    {
                        _ = new ContentDialog
                        {
                            Title = "Original DLL chained",
                            Content = $"The existing '{chosen}' was renamed to '{hookedOriginal}' so ASI Loader can chain-load it automatically.",
                            CloseButtonText = "OK",
                            XamlRoot = _window.Content.XamlRoot,
                        }.ShowAsync();
                    }
                    _window.DispatcherQueue.TryEnqueue(() => _window.BuildOverridesPanel(card));
                }
                else
                {
                    installBtn.Content = "❌ Install failed";
                }
            }
            finally { installBtn.IsEnabled = true; }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog (empty for now, matches Components cog size/position)
        var cogBtn = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
            Tag = card,
        };
        ToolTipService.SetToolTip(cogBtn, "ASI Loader settings (coming soon)");
        cogBtn.Click += async (s, e) =>
        {
            // Placeholder — settings dialog will be added later
            var dlg = new ContentDialog
            {
                Title = "ASI Loader Settings",
                Content = new TextBlock { Text = "No settings available yet.", FontSize = 12 },
                CloseButtonText = "Close",
                XamlRoot = _window.Content.XamlRoot,
            };
            await dlg.ShowAsync();
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Remove button
        var removeBtn = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
            Opacity = isInstalled ? 1.0 : 0,
            IsHitTestVisible = isInstalled,
        };
        ToolTipService.SetToolTip(removeBtn, "Remove Ultimate ASI Loader from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            ualSvc.Uninstall(card);
            _window.ViewModel.SetUalInstalledAs(gameName, null, store);
            _window.DispatcherQueue.TryEnqueue(() => _window.BuildOverridesPanel(card));
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    private async Task<string?> ShowUalDllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;

        var names = card.Is32Bit
            ? UltimateAsiLoaderService.Win32Names
            : UltimateAsiLoaderService.Win64Names;

        // Collect files already in the game folder
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(card.InstallPath))
                foreach (var f in Directory.GetFiles(card.InstallPath, "*.dll"))
                    existingFiles.Add(Path.GetFileName(f));
        }
        catch { }

        // RHI-managed filenames to flag as conflict
        var rhiOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(card.RsInstalledFile))  rhiOwned.Add(card.RsInstalledFile);
        if (!string.IsNullOrEmpty(card.OsInstalledFile))  rhiOwned.Add(card.OsInstalledFile);
        if (!string.IsNullOrEmpty(card.DcInstalledFile))  rhiOwned.Add(card.DcInstalledFile);

        string? chosen = null;

        var listPanel = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content = listPanel,
        };

        foreach (var name in names)
        {
            bool isRecommended = UltimateAsiLoaderService.RecommendedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool isRhiConflict = UltimateAsiLoaderService.RhiConflictNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool isTaken       = existingFiles.Contains(name) && !rhiOwned.Contains(name) && name != currentDllName;
            bool isRhiOwned    = rhiOwned.Contains(name);
            bool isCurrent     = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                IsEnabled = !isRhiOwned,
                Opacity = isRhiOwned ? 0.4 : 1.0,
            };

            // Styling
            if (isCurrent)
            {
                btn.Background   = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                btn.BorderBrush  = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }
            else
            {
                btn.Background  = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                btn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush);
            }

            // Content: name + badges
            var contentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            contentRow.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = isTaken || isRhiOwned
                    ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush)
                    : UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (isRecommended)
                contentRow.Children.Add(MakeBadge("Recommended", "#1A3A20", "#6AE87A", "#2A5A30"));
            if (isTaken)
                contentRow.Children.Add(MakeBadge("In use", "#2A1818", "#CC6666", "#5A2828"));
            if (isRhiOwned)
                contentRow.Children.Add(MakeBadge("Used by RHI", "#2A1818", "#CC6666", "#5A2828"));
            if (isRhiConflict && !isRhiOwned)
                contentRow.Children.Add(MakeBadge("May conflict with ReShade/OS", "#2A1A10", "#CC9955", "#5A3A18"));
            if (isCurrent)
                contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));

            btn.Content = contentRow;

            // Tooltip for taken files
            if (isTaken)
                ToolTipService.SetToolTip(btn, $"'{name}' already exists in the game folder. Selecting it will rename the existing file to '{Path.GetFileNameWithoutExtension(name)}Hooked.dll' so ASI Loader can chain-load it.");
            else if (isRhiOwned)
                ToolTipService.SetToolTip(btn, "This filename is already used by an RHI-managed component (ReShade, OptiScaler, or DC). Choose a different name.");

            btn.Tag = name;
            btn.Click += (s, ev) =>
            {
                chosen = (s as Button)?.Tag as string;
                // Close the dialog by finding and closing it
                if (s is FrameworkElement fe)
                {
                    var dialog = FindParentContentDialog(fe);
                    dialog?.Hide();
                }
            };

            listPanel.Children.Add(btn);
        }

        var dialog = new ContentDialog
        {
            Title = "Choose ASI Loader DLL name",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the filename for ASI Loader. Most games work with version.dll or winmm.dll.",
                        FontSize = 11,
                        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    scrollViewer,
                }
            },
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
        };

        await dialog.ShowAsync();
        return chosen;
    }

    /// <summary>Walks up the visual tree to find the parent ContentDialog.</summary>
    private static ContentDialog? FindParentContentDialog(DependencyObject element)
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        while (parent != null)
        {
            if (parent is ContentDialog d) return d;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static Border MakeBadge(string text, string bg, string fg, string border)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Background = UIFactory.GetBrush(bg),
            BorderBrush = UIFactory.GetBrush(border),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = UIFactory.GetBrush(fg),
            },
        };
    }
}
