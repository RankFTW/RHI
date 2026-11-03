// DetailPanelBuilder.Overrides.Dxvk.cs — Management section.
// DXVK controls have moved to DetailPanelBuilder.Extras.cs under the API Upgrades sub-header.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal static readonly string[] DcDllOverrideNames =
    [
        "dxgi.dll", "d3d9.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll",
        "hid.dll", "version.dll", "opengl32.dll", "dbghelp.dll",
        "vulkan-1.dll", "winmm.dll",
    ];

    private void BuildManagementSection(GameCardViewModel card, string capturedName, OverridesPanelCtx ctx)
    {
        _window.ViewModel.SetLastUiAction($"BuildManagementSection({card.GameName})");
        // ── Management section (single row: 5 buttons side by side with separators) ──
        _window.ManagementPanel.Children.Clear();

        // Collapsible header
        const string mgmtSectionKey = "Management";
        bool mgmtCollapsed = Layout.IsCollapsed(mgmtSectionKey);

        var mgmtArrow = new TextBlock
        {
            Text      = mgmtCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var mgmtTitle = new TextBlock
        {
            Text       = "Management",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var mgmtHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        ToolTipService.SetToolTip(mgmtArrow, SectionHeaderTooltip);
        ToolTipService.SetToolTip(mgmtTitle, SectionHeaderTooltip);
        mgmtHeaderRow.Children.Add(MakeDragHandle(_window.ManagementContainer));
        mgmtHeaderRow.Children.Add(mgmtArrow);
        mgmtHeaderRow.Children.Add(mgmtTitle);
        _window.ManagementPanel.Children.Add(mgmtHeaderRow);
        Layout.RegisterHeader(mgmtSectionKey, mgmtHeaderRow);

        // Body wrapper
        var mgmtBody = new StackPanel { Spacing = 6, Visibility = mgmtCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.ManagementPanel.Children.Add(mgmtBody);

        mgmtHeaderRow.PointerEntered += (s, e) => mgmtTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        mgmtHeaderRow.PointerExited  += (s, e) => mgmtTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var mgmtHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var mgmtArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var mgmtCursorProp  = DetailPanelBuilder.CursorProp;
        mgmtHeaderRow.PointerEntered += (s, e) => mgmtCursorProp?.SetValue(mgmtHeaderRow, mgmtHandCursor);
        mgmtHeaderRow.PointerExited  += (s, e) => mgmtCursorProp?.SetValue(mgmtHeaderRow, mgmtArrowCursor);
        mgmtHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = mgmtBody.Visibility == Visibility.Visible;
            mgmtBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            mgmtArrow.Text = nowCollapsed ? "▶" : "▼";
            Layout.SetCollapsed(mgmtSectionKey, nowCollapsed);
        };

        var mgmtRow = new Grid { ColumnSpacing = 0 };
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var changeFolderBtn = new Button
        {
            Content = "Change install folder",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
        };
        changeFolderBtn.Click += (s, ev) => _window.BrowseFolder_Click(s, ev);
        ToolTipService.SetToolTip(changeFolderBtn, "Change the install folder for this game. Use when auto-detection picked the wrong directory.");
        Grid.SetColumn(changeFolderBtn, 0);
        mgmtRow.Children.Add(changeFolderBtn);

        var sep1 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep1, 1);
        mgmtRow.Children.Add(sep1);

        var resetFolderBtn = new Button
        {
            Content = "Reset install folder",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
            IsEnabled = !card.IsManuallyAdded,
        };
        resetFolderBtn.Click += (s, ev) => _window.ResetInstallFolder_Click(s, ev);
        ToolTipService.SetToolTip(resetFolderBtn, card.IsManuallyAdded
            ? "You added this game yourself, so there's no detected folder to go back to. Use Change install folder."
            : "Go back to the install folder RHI detected, if you changed it.");
        Grid.SetColumn(resetFolderBtn, 2);
        mgmtRow.Children.Add(resetFolderBtn);

        var sepReset = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sepReset, 3);
        mgmtRow.Children.Add(sepReset);

        var removeGameBtn = new Button
        {
            Content = "Remove from RHI",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
        };
        removeGameBtn.Click += (s, ev) => _window.RemoveManualGame_Click(s, ev);
        ToolTipService.SetToolTip(removeGameBtn, card.IsManuallyAdded
            ? "Remove this game from RHI. You added it yourself, so add it again to get it back."
            : "Remove this game from RHI for good: it won't come back when RHI scans your games (unlike Hide). Undo in Settings › Tools & Maintenance › Removed games.");
        Grid.SetColumn(removeGameBtn, 4);
        mgmtRow.Children.Add(removeGameBtn);

        var sep2 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep2, 5);
        mgmtRow.Children.Add(sep2);

        var mgmtResetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        mgmtResetOverridesBtn.Click += (s, ev) =>
        {
            // Call reset action directly — automation peer invoke fails on Visibility.Collapsed buttons
            ctx.ResetAction?.Invoke();
        };
        Grid.SetColumn(mgmtResetOverridesBtn, 6);
        ToolTipService.SetToolTip(mgmtResetOverridesBtn, "Reset all per-game overrides back to defaults (DLL names, channels, shaders, addons, launch settings, update inclusion).");
        mgmtRow.Children.Add(mgmtResetOverridesBtn);

        var sep3 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep3, 7);
        mgmtRow.Children.Add(sep3);

        var reportBtn = new Button
        {
            Content = "Copy Report",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        async void CopyReport()
        {
            try
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    await GameReportEncoder.ShowAndCopyAsync(_window.Content.XamlRoot, targetCard, _window.ViewModel);
            }
            catch (Exception ex)
            {
                Services.CrashReporter.Log($"[BuildManagementSection] Copy Report failed — {ex.Message}");
            }
        }
        reportBtn.Click += (s, ev) => CopyReport();
        Grid.SetColumn(reportBtn, 8);
        ToolTipService.SetToolTip(reportBtn, "Copy a diagnostic report for this game to the clipboard. Useful for Discord or GitHub support.");
        mgmtRow.Children.Add(reportBtn);

        if (Layout.IsTabs)
        {
            // Tabs (mockup A): the same four actions as tiles
            var tiles = new Controls.AutoFitGrid { MinColumnWidth = 220 };
            tiles.Children.Add(TabsTheme.MakeTile("Change install folder", "Point RHI at a different folder for this game", false,
                (s, ev) => _window.BrowseFolder_Click(changeFolderBtn, ev), TabsTooltip(changeFolderBtn)));
            tiles.Children.Add(TabsTheme.MakeTile("Reset Overrides", "Put every per-game setting back to global", false,
                (s, ev) => ctx.ResetAction?.Invoke(), TabsTooltip(mgmtResetOverridesBtn)));
            tiles.Children.Add(TabsTheme.MakeTile("Copy Report", "Copy this game’s setup to share for support", false,
                (s, ev) => CopyReport(), TabsTooltip(reportBtn)));
            if (!card.IsManuallyAdded)
                tiles.Children.Add(TabsTheme.MakeTile("Reset install folder", "Go back to the folder RHI detected", false,
                    (s, ev) => _window.ResetInstallFolder_Click(resetFolderBtn, ev), TabsTooltip(resetFolderBtn)));
            tiles.Children.Add(TabsTheme.MakeTile("Remove from RHI", card.IsManuallyAdded
                    ? "Remove this game you added yourself"
                    : "Take this game out of RHI for good; undo in Settings", true,
                (s, ev) => _window.RemoveManualGame_Click(removeGameBtn, ev), TabsTooltip(removeGameBtn)));
            mgmtBody.Children.Add(tiles);
            return;
        }

        mgmtBody.Children.Add(mgmtRow);
    }

    private static string? TabsTooltip(DependencyObject d) => ToolTipService.GetToolTip(d) as string;
}
