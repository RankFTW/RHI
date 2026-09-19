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
        // ── Management section (single row: 4 buttons side by side with separators) ──
        _window.ManagementPanel.Children.Clear();

        // Collapsible header
        const string mgmtSectionKey = "Management";
        var mgmtSettings   = _window.ViewModel.Settings;
        bool mgmtCollapsed = mgmtSettings.CollapsedDetailSections.Contains(mgmtSectionKey);

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
        mgmtHeaderRow.Children.Add(MakeDragHandle(_window.ManagementContainer));
        mgmtHeaderRow.Children.Add(mgmtArrow);
        mgmtHeaderRow.Children.Add(mgmtTitle);
        _window.ManagementPanel.Children.Add(mgmtHeaderRow);

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
            if (nowCollapsed) mgmtSettings.CollapsedDetailSections.Add(mgmtSectionKey);
            else              mgmtSettings.CollapsedDetailSections.Remove(mgmtSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        var mgmtRow = new Grid { ColumnSpacing = 0 };
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

        var removeGameBtn = new Button
        {
            Content = "Reset / Remove game",
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
        ToolTipService.SetToolTip(removeGameBtn, "Reset the install folder to auto-detected, or remove a manually added game entirely.");
        Grid.SetColumn(removeGameBtn, 2);
        mgmtRow.Children.Add(removeGameBtn);

        var sep2 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep2, 3);
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
        Grid.SetColumn(mgmtResetOverridesBtn, 4);
        ToolTipService.SetToolTip(mgmtResetOverridesBtn, "Reset all per-game overrides back to defaults (DLL names, channels, shaders, addons, launch settings, update inclusion).");
        mgmtRow.Children.Add(mgmtResetOverridesBtn);

        var sep3 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep3, 5);
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
        reportBtn.Click += async (s, ev) =>
        {
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
                await GameReportEncoder.ShowAndCopyAsync(_window.Content.XamlRoot, targetCard, _window.ViewModel);
        };
        Grid.SetColumn(reportBtn, 6);
        ToolTipService.SetToolTip(reportBtn, "Copy a diagnostic report for this game to the clipboard. Useful for Discord or GitHub support.");
        mgmtRow.Children.Add(reportBtn);

        mgmtBody.Children.Add(mgmtRow);
    }
}
