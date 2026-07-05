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

    private ToggleSwitch? BuildDxvkAndManagementSection(GameCardViewModel card, string capturedName, string gameName, Button resetOverridesBtn)
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
                defaultDxvkSelection = "Off";
            }
            else
            {
                var currentDxvkOverride = _window.ViewModel.GetDxvkVariantOverride(gameName);
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
                    defaultDxvkSelection = _window.ViewModel.DxvkServiceInstance.SelectedVariant switch
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

            dxvkModeCombo.SelectionChanged += async (s, ev) =>
            {
                var selected = dxvkModeCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard == null) return;

                if (selected == "Off")
                {
                    if (targetCard.DxvkEnabled)
                    {
                        await _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                        _window.ViewModel.SetDxvkVariantOverride(capturedName, null);
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
                    _window.ViewModel.SetDxvkVariantOverride(capturedName, variantValue);

                    if (!targetCard.DxvkEnabled)
                    {
                        var resolvedVariant = _window.ViewModel.ResolveDxvkVariant(capturedName);
                        var savedVariant = _window.ViewModel.DxvkServiceInstance.SelectedVariant;
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await _window.ViewModel.HandleDxvkToggleAsync(targetCard, true, _window.Content.XamlRoot);
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                        if (!targetCard.DxvkEnabled) dxvkModeCombo.SelectedItem = "Off";
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
                    }
                    else
                    {
                        var resolvedVariant = _window.ViewModel.ResolveDxvkVariant(capturedName);
                        var savedVariant = _window.ViewModel.DxvkServiceInstance.SelectedVariant;
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await _window.ViewModel.DxvkServiceInstance.EnsureStagingAsync();
                        if (_window.ViewModel.DxvkServiceInstance.IsStagingReady)
                            await _window.ViewModel.InstallDxvkAsync(targetCard, _window.Content.XamlRoot);
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                    }
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

            // Right column — Lilium HDR Preset (only visible when Lilium HDR is active)
            var isLiliumActive = card.DxvkEnabled && card.DxvkRecord?.IsLiliumHdrMode == true;
            if (isLiliumActive || (card.DxvkEnabled && _window.ViewModel.GetDxvkVariantOverride(gameName) == "LiliumHdr"))
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
                int currentPreset = _window.ViewModel.GetLiliumPreset(gameName);
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
                    _window.ViewModel.SetLiliumPreset(capturedName, idx);

                    // Re-deploy dxvk.conf with the new preset
                    var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
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
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(resetOverridesBtn)
                as Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer;
            peer?.Invoke();
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

        _window.ManagementPanel.Children.Add(mgmtRow);
        return dxvkToggleResult;
    }
}