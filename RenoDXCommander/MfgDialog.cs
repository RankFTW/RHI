using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Dialog for configuring per-game Multi Frame Generation (MFG) driver profile settings.
/// Controls: FG Mode Override, Generation Factor / Dynamic Max Count, Dynamic Target FPS.
/// </summary>
public static class MfgDialog
{
    // MFG Mode values
    private const uint MODE_OFF = 0x00000000;
    private const uint MODE_FIXED = 0x00000002;
    private const uint MODE_DYNAMIC = 0x00000004;

    // Dynamic Target FPS special values
    private const uint TARGET_FPS_OFF = 0x00000000;
    private const uint TARGET_FPS_MAX_REFRESH = 0x01000000;

    /// <summary>
    /// Shows a warning dialog on first use, then the MFG configuration dialog.
    /// Returns false if the user cancelled from the warning.
    /// </summary>
    public static async Task ShowAsync(
        DlssPresetService presetService,
        SettingsViewModel settings,
        string gameName,
        string installPath,
        XamlRoot xamlRoot,
        Action saveSettings)
    {
        // First-time warning
        if (!settings.MfgWarningDismissed)
        {
            var warningPanel = new StackPanel { Spacing = 12 };
            warningPanel.Children.Add(new TextBlock
            {
                Text = "Multi Frame Generation (MFG) and Dynamic MFG are only supported on NVIDIA 50 Series GPUs (Blackwell architecture).\n\n" +
                       "Minimum driver requirements:\n" +
                       "• MFG (Fixed): Driver 572.16+\n" +
                       "• DMFG (Dynamic): Driver 595.97+\n\n" +
                       "These settings will have no effect on 40 Series or older hardware.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
            });

            var dontShowCheck = new CheckBox
            {
                Content = "Don't show this warning again",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            };
            warningPanel.Children.Add(dontShowCheck);

            var warningDialog = new ContentDialog
            {
                Title = "⚠ Hardware Requirement",
                Content = warningPanel,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var warningResult = await DialogService.ShowSafeAsync(warningDialog);
            if (warningResult != ContentDialogResult.Primary)
                return;

            if (dontShowCheck.IsChecked == true)
            {
                settings.MfgWarningDismissed = true;
                saveSettings();
            }
        }

        // Show the MFG configuration dialog
        await ShowConfigDialogAsync(presetService, gameName, installPath, xamlRoot);
    }

    private static async Task ShowConfigDialogAsync(
        DlssPresetService presetService,
        string gameName,
        string installPath,
        XamlRoot xamlRoot)
    {
        // Read current values
        var currentMode = presetService.GetMfgMode(gameName, installPath);
        var currentFactor = presetService.GetMfgGenerationFactor(gameName, installPath);
        var currentDynamicMax = presetService.GetMfgDynamicMaxCount(gameName, installPath);
        var currentTargetFps = presetService.GetMfgDynamicTargetFps(gameName, installPath);

        var panel = new StackPanel { Spacing = 8, MinWidth = 300 };

        // ── FG Mode ──
        panel.Children.Add(new TextBlock
        {
            Text = "FG Mode",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        var modeItems = new[] { "Default", "Fixed", "Dynamic" };
        int modeIndex = currentMode switch
        {
            MODE_FIXED => 1,
            MODE_DYNAMIC => 2,
            _ => 0,
        };
        var modeCombo = new ComboBox
        {
            ItemsSource = modeItems,
            SelectedIndex = modeIndex,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        ToolTipService.SetToolTip(modeCombo,
            "Default = application controls frame generation. Fixed = always generate a fixed multiplier of frames. Dynamic = generate up to a target frame rate.");
        panel.Children.Add(modeCombo);

        // ── Frame Count ──
        panel.Children.Add(new TextBlock
        {
            Text = "Frame Count",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 8, 0, 0),
        });

        var countCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        ToolTipService.SetToolTip(countCombo,
            "Fixed: exact frame multiplier (2x-6x). Dynamic: maximum frames the driver may generate (Up to 3x-6x). 50 Series GPUs only for 3x+.");
        panel.Children.Add(countCombo);

        // ── Target Frame Rate ──
        panel.Children.Add(new TextBlock
        {
            Text = "Target Frame Rate",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 8, 0, 0),
        });

        var fpsCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        ToolTipService.SetToolTip(fpsCombo,
            "The target output frame rate for dynamic frame generation. Only active in Dynamic mode.");
        panel.Children.Add(fpsCombo);

        // ── Populate and wire up state ──
        bool suppressEvents = true;

        void PopulateCountCombo(int selectedModeIndex)
        {
            suppressEvents = true;
            countCombo.Items.Clear();

            if (selectedModeIndex == 1) // Fixed
            {
                countCombo.Items.Add("2x");
                countCombo.Items.Add("3x");
                countCombo.Items.Add("4x");
                countCombo.Items.Add("5x");
                countCombo.Items.Add("6x");
                countCombo.IsEnabled = true;

                // Select based on current generation factor
                int idx = currentFactor switch
                {
                    1 => 0, // 2x
                    2 => 1, // 3x
                    3 => 2, // 4x
                    4 => 3, // 5x
                    5 => 4, // 6x
                    _ => 0,
                };
                countCombo.SelectedIndex = idx;
            }
            else if (selectedModeIndex == 2) // Dynamic
            {
                countCombo.Items.Add("Up to 2x");
                countCombo.Items.Add("Up to 3x");
                countCombo.Items.Add("Up to 4x");
                countCombo.Items.Add("Up to 5x");
                countCombo.Items.Add("Up to 6x");
                countCombo.IsEnabled = true;

                // Select based on current dynamic max count
                // Dynamic max: 0=Off, 2=3x, 3=4x, 4=5x, 5=6x (no 2x option for dynamic)
                // But we show "Up to 2x" as index 0 (value 1)
                int idx = currentDynamicMax switch
                {
                    1 => 0, // Up to 2x
                    2 => 1, // Up to 3x
                    3 => 2, // Up to 4x
                    4 => 3, // Up to 5x
                    5 => 4, // Up to 6x
                    _ => 0,
                };
                countCombo.SelectedIndex = idx;
            }
            else // Default
            {
                countCombo.Items.Add("—");
                countCombo.SelectedIndex = 0;
                countCombo.IsEnabled = false;
            }
            suppressEvents = false;
        }

        void PopulateFpsCombo(int selectedModeIndex)
        {
            suppressEvents = true;
            fpsCombo.Items.Clear();

            if (selectedModeIndex == 2) // Dynamic
            {
                fpsCombo.Items.Add("Off");
                fpsCombo.Items.Add("Max Refresh Rate");
                for (int fps = 60; fps <= 500; fps++)
                    fpsCombo.Items.Add($"{fps} FPS");

                fpsCombo.IsEnabled = true;

                // Select based on current value
                if (currentTargetFps == TARGET_FPS_OFF)
                    fpsCombo.SelectedIndex = 0;
                else if (currentTargetFps == TARGET_FPS_MAX_REFRESH)
                    fpsCombo.SelectedIndex = 1;
                else
                {
                    // FPS value: index = fps - 60 + 2 (first two items are Off and Max Refresh Rate)
                    int fpsVal = (int)currentTargetFps;
                    if (fpsVal >= 60 && fpsVal <= 500)
                        fpsCombo.SelectedIndex = fpsVal - 60 + 2;
                    else
                        fpsCombo.SelectedIndex = 0;
                }
            }
            else // Default or Fixed
            {
                fpsCombo.Items.Add("—");
                fpsCombo.SelectedIndex = 0;
                fpsCombo.IsEnabled = false;
            }
            suppressEvents = false;
        }

        // Initial population
        PopulateCountCombo(modeIndex);
        PopulateFpsCombo(modeIndex);
        suppressEvents = false;

        // ── Event handlers ──
        modeCombo.SelectionChanged += (s, ev) =>
        {
            if (suppressEvents) return;
            var idx = modeCombo.SelectedIndex;
            uint modeValue = idx switch
            {
                1 => MODE_FIXED,
                2 => MODE_DYNAMIC,
                _ => MODE_OFF,
            };
            presetService.SetMfgMode(gameName, installPath, modeValue);

            // Reset dependent settings when mode changes
            if (modeValue == MODE_OFF)
            {
                presetService.SetMfgGenerationFactor(gameName, installPath, 0);
                presetService.SetMfgDynamicMaxCount(gameName, installPath, 0);
                presetService.SetMfgDynamicTargetFps(gameName, installPath, 0);
                currentFactor = 0;
                currentDynamicMax = 0;
                currentTargetFps = 0;
            }
            else if (modeValue == MODE_FIXED)
            {
                // Switching to Fixed: clear dynamic settings
                presetService.SetMfgDynamicMaxCount(gameName, installPath, 0);
                presetService.SetMfgDynamicTargetFps(gameName, installPath, 0);
                currentDynamicMax = 0;
                currentTargetFps = 0;
            }
            else if (modeValue == MODE_DYNAMIC)
            {
                // Switching to Dynamic: clear fixed settings
                presetService.SetMfgGenerationFactor(gameName, installPath, 0);
                currentFactor = 0;
            }

            PopulateCountCombo(idx);
            PopulateFpsCombo(idx);
        };

        countCombo.SelectionChanged += (s, ev) =>
        {
            if (suppressEvents) return;
            var idx = countCombo.SelectedIndex;
            if (idx < 0) return;

            var currentModeIdx = modeCombo.SelectedIndex;
            if (currentModeIdx == 1) // Fixed → Generation Factor
            {
                // 2x=1, 3x=2, 4x=3, 5x=4, 6x=5
                uint value = (uint)(idx + 1);
                presetService.SetMfgGenerationFactor(gameName, installPath, value);
                currentFactor = value;
            }
            else if (currentModeIdx == 2) // Dynamic → Dynamic Max Count
            {
                // Up to 2x=1, Up to 3x=2, Up to 4x=3, Up to 5x=4, Up to 6x=5
                uint value = (uint)(idx + 1);
                presetService.SetMfgDynamicMaxCount(gameName, installPath, value);
                currentDynamicMax = value;
            }
        };

        fpsCombo.SelectionChanged += (s, ev) =>
        {
            if (suppressEvents) return;
            var idx = fpsCombo.SelectedIndex;
            if (idx < 0) return;

            uint value;
            if (idx == 0) value = TARGET_FPS_OFF;
            else if (idx == 1) value = TARGET_FPS_MAX_REFRESH;
            else value = (uint)(idx - 2 + 60); // 60 FPS starts at index 2

            presetService.SetMfgDynamicTargetFps(gameName, installPath, value);
            currentTargetFps = value;
        };

        // ── Show dialog ──
        var dialog = new ContentDialog
        {
            Title = "Multi Frame Generation",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        await DialogService.ShowSafeAsync(dialog);
    }
}
