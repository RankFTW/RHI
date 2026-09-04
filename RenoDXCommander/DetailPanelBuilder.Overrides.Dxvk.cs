// DetailPanelBuilder.Overrides.Dxvk.cs — DXVK section + Management section.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
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

    private ToggleSwitch? BuildDxvkAndManagementSection(GameCardViewModel card, string capturedName, string gameName, OverridesPanelCtx ctx)
    {
        ToggleSwitch? dxvkToggleResult = null;
        // ══════════════════════════════════════════════════════════════════════
        // DXVK section — separator + DXVK ComboBox (left), right reserved
        // ══════════════════════════════════════════════════════════════════════
        if (card.IsDxvkToggleVisible)
        {
            _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

            var dxvkRowGrid = new Grid { ColumnSpacing = 0 };
            dxvkRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dxvkRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dxvkRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column — DXVK ComboBox (Off / Development / Stable / Lilium HDR)
            var dxvkModeItems = new[] { "Off", "Development", "Stable", "Lilium HDR" };
            string defaultDxvkSelection;
            if (!card.DxvkEnabled)
            {
                // Show persisted variant override even when not installed, so user's selection persists
                var pendingOverride = _window.ViewModel.GetDxvkVariantOverride(gameName, card.Source);
                defaultDxvkSelection = pendingOverride switch
                {
                    "Development" => "Development",
                    "Stable" => "Stable",
                    "LiliumHdr" => "Lilium HDR",
                    _ => "Off",
                };
            }
            else
            {
                var currentDxvkOverride = _window.ViewModel.GetDxvkVariantOverride(gameName, card.Source);
                if (currentDxvkOverride != null)
                {
                    defaultDxvkSelection = currentDxvkOverride switch
                    {
                        "Development" => "Development",
                        "Stable" => "Stable",
                        "LiliumHdr" => "Lilium HDR",
                        _ => "Development",
                    };
                }
                else
                {
                    // No per-game override — show the effective global variant
                    defaultDxvkSelection = _dxvkService.SelectedVariant switch
                    {
                        DxvkVariant.Stable => "Stable",
                        DxvkVariant.LiliumHdr => "Lilium HDR",
                        _ => "Development",
                    };
                }
            }

            var dxvkModeCombo = new ComboBox
            {
                ItemsSource = dxvkModeItems,
                SelectedItem = defaultDxvkSelection,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = card.IsDxvkToggleEnabled && card.DxvkInstallEnabled,
            };
            if (card.DxvkToggleTooltip != null)
                ToolTipService.SetToolTip(dxvkModeCombo, card.DxvkToggleTooltip);
            else
                ToolTipService.SetToolTip(dxvkModeCombo,
                    "Off = DXVK disabled.\nDevelopment/Stable/Lilium HDR = DXVK variant selection.\nDXVK translates DirectX to Vulkan — enables compute shaders.");

            var dxvkToggle = new ToggleSwitch { IsOn = card.DxvkEnabled, Visibility = Visibility.Collapsed };
            dxvkToggleResult = dxvkToggle;

            var dxvkComboInitializing = true;
            dxvkModeCombo.SelectionChanged += async (s, ev) =>
            {
                if (dxvkComboInitializing) return;
                var selected = dxvkModeCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;
                // Use card directly — re-finding by name can return a stale/wrong card
                var targetCard = card;

                if (selected == "Off")
                {
                    if (targetCard.DxvkEnabled
                        || targetCard.DxvkStatus == GameStatus.Installed
                        || targetCard.DxvkStatus == GameStatus.UpdateAvailable)
                    {
                        await _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                        _window.ViewModel.SetDxvkVariantOverride(capturedName, null, targetCard.Source);
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
                    }
                    else
                    {
                        targetCard.DxvkVariantPending = false;
                        _window.ViewModel.SetDxvkVariantOverride(capturedName, null, targetCard.Source);
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
                    }
                }
                else
                {
                    string? variantValue = selected switch
                    {
                        "Development" => "Development",
                        "Stable" => "Stable",
                        "Lilium HDR" => "LiliumHdr",
                        _ => null,
                    };
                    _window.ViewModel.SetDxvkVariantOverride(capturedName, variantValue, targetCard.Source);

                    if (targetCard.DxvkEnabled || targetCard.DxvkStatus == GameStatus.Installed || targetCard.DxvkStatus == GameStatus.UpdateAvailable)
                    {
                        // DXVK already installed — uninstall it so user can install the new variant
                        await _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                        targetCard.DxvkVariantPending = true;
                        targetCard.NotifyAll();
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
                    }
                    else
                    {
                        // Not installed — mark pending so Install button appears
                        targetCard.DxvkVariantPending = true;
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
                    }
                    // User must press Install DXVK to actually install
                }
            };

            var dxvkColumn = new StackPanel { Spacing = 6 };
            dxvkColumn.Children.Add(new TextBlock
            {
                Text = "DXVK",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                Margin = new Thickness(0, 0, 0, 4),
            });
            dxvkColumn.Children.Add(dxvkModeCombo);
            dxvkComboInitializing = false;

            // If combo shows "Off" but DXVK is still physically installed (stale state from
            // a previous Off selection that failed to uninstall), trigger uninstall now.
            // Only fires when there's no per-game variant override — meaning the user already
            // chose Off but the uninstall didn't complete. Don't fire when the global variant
            // is active (override is null because user is using the global setting).
            if (defaultDxvkSelection == "Off"
                && !card.DxvkEnabled
                && (card.DxvkStatus == GameStatus.Installed || card.DxvkStatus == GameStatus.UpdateAvailable))
            {
                _ = _window.ViewModel.HandleDxvkToggleAsync(card, false, _window.Content.XamlRoot);
            }
            Grid.SetColumn(dxvkColumn, 0);
            dxvkRowGrid.Children.Add(dxvkColumn);

            var dxvkDivider = new Border
            {
                Width = 1,
                Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(12, 0, 12, 0),
            };
            Grid.SetColumn(dxvkDivider, 1);
            dxvkRowGrid.Children.Add(dxvkDivider);

            // Right column — Lilium HDR Preset (visible when Lilium HDR is selected or active)
            var liliumVariantSelected = _window.ViewModel.GetDxvkVariantOverride(gameName, card.Source) == "LiliumHdr"
                                     || defaultDxvkSelection == "Lilium HDR";
            var isLiliumActive = (card.DxvkEnabled || card.DxvkStatus == GameStatus.Installed || card.DxvkStatus == GameStatus.UpdateAvailable)
                                 && card.DxvkRecord?.IsLiliumHdrMode == true;
            if (isLiliumActive || liliumVariantSelected)
            {
                var liliumPresetCol = new StackPanel { Spacing = 6 };
                liliumPresetCol.Children.Add(new TextBlock
                {
                    Text = "Lilium HDR Preset",
                    FontSize = 12,
                    Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                    Margin = new Thickness(0, 0, 0, 4),
                });

                var dxvkRec = card.DxvkRecord;
                var isDx9Api = dxvkRec?.InstalledDlls?.Any(d => d.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) == true
                               || card.GraphicsApi is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9;
                var presetArray = isDx9Api ? DxvkService.LiliumD3d9Presets : DxvkService.LiliumD3d11Presets;
                var presetNames = presetArray.Select(p => p.Name).ToList();
                int currentPreset = _window.ViewModel.GetLiliumPreset(gameName, card.Source);
                var liliumPresetCombo = new ComboBox
                {
                    ItemsSource = presetNames,
                    SelectedIndex = currentPreset >= 0 && currentPreset < presetNames.Count ? currentPreset : 0,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                ToolTipService.SetToolTip(liliumPresetCombo,
                    "Controls how aggressively DXVK upgrades render targets for HDR.\n\n" +
                    "Safest = swap chain only (near 100% compatible).\n" +
                    "Higher tiers upgrade back buffers and render targets — better HDR but may cause visual issues.");

                var liliumComboInit = true;
                liliumPresetCombo.SelectionChanged += async (s, ev) =>
                {
                    if (liliumComboInit) return;
                    int idx = liliumPresetCombo.SelectedIndex;
                    if (idx < 0) return;
                    var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase)
                        && (c.Source ?? "").Equals(card.Source ?? "", StringComparison.OrdinalIgnoreCase));
                    _window.ViewModel.SetLiliumPreset(capturedName, idx, targetCard?.Source ?? "");

                    // Re-deploy dxvk.conf with the new preset
                    if (targetCard != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                    {
                        var confPath = Path.Combine(targetCard.InstallPath, "dxvk.conf");
                        // Determine original API from the DXVK record — d3d9.dll means DX9, otherwise DX10/DX11
                        var dxvkRec = targetCard.DxvkRecord;
                        var isDx9 = dxvkRec?.InstalledDlls?.Any(d => d.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) == true
                                    || targetCard.GraphicsApi is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9;
                        var confContent = isDx9
                            ? DxvkService.GetLiliumD3d9ConfContent(idx)
                            : DxvkService.GetLiliumD3d11ConfContent(idx);
                        try { File.WriteAllText(confPath, confContent); }
                        catch (Exception ex) { CrashReporter.Log($"[DetailPanel.LiliumPreset] Failed to write dxvk.conf — {ex.Message}"); }
                    }
                };
                liliumPresetCol.Children.Add(liliumPresetCombo);
                liliumComboInit = false;

                Grid.SetColumn(liliumPresetCol, 2);
                dxvkRowGrid.Children.Add(liliumPresetCol);
            }

            _window.OverridesPanel.Children.Add(dxvkRowGrid);
        }
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
        var mgmtCursorProp  = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
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
        ToolTipService.SetToolTip(mgmtResetOverridesBtn, "Reset all per-game overrides back to defaults (DLL names, channels, shaders, addons, DXVK, launch settings, update inclusion).");
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
        return dxvkToggleResult;
    }
}
