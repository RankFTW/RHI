// DetailPanelBuilder.Overrides.Dlss.cs — DLSS/Streamline column builder helpers.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>
    /// Builds a single DLSS/Streamline column with label, version ComboBox, optional preset ComboBox, and optional render scale ComboBox.
    /// </summary>
    private StackPanel BuildDlssColumn(string label, bool isPresent,
        IReadOnlyList<string> availableVersions, string? installedVersion,
        (string Name, uint Value)[]? presets, uint currentPreset,
        Func<string, Task> onVersionSelected, Action<uint>? onPresetSelected,
        uint currentRenderScale = 0, Action<uint>? onRenderScaleSelected = null,
        string? originalVersion = null, bool driverOverrideActive = false)
    {
        var col = new StackPanel { Spacing = 4, Opacity = isPresent ? 1.0 : 0.4 };

        col.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // Version ComboBox
        var versionLabel = new TextBlock { Text = "Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
        if (driverOverrideActive)
            ToolTipService.SetToolTip(versionLabel, "Driver override is active — the NVIDIA driver is injecting its own DLL. Disable 'Latest DLL' in NVIDIA App or Profile Inspector to manage versions manually.");
        col.Children.Add(versionLabel);

        // Build items list with (Default) marker on the game's original/default version
        var items = new List<string>();

        if (!isPresent && installedVersion == null)
        {
            // Game truly doesn't have this component — show "None"
            items.Add("None");
        }
        else
        {
            string? formattedOriginal = originalVersion != null
                ? DlssStreamlineService.FormatVersion(originalVersion)
                : (installedVersion != null ? installedVersion : null);
            bool defaultInList = false;

            foreach (var ver in availableVersions)
            {
                if (formattedOriginal != null && (ver.Equals(formattedOriginal, StringComparison.OrdinalIgnoreCase)
                    || ver.StartsWith(formattedOriginal, StringComparison.OrdinalIgnoreCase)
                    || formattedOriginal.StartsWith(ver, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add($"{ver} (Default)");
                    defaultInList = true;
                }
                else
                    items.Add(ver);
            }
            items.Add("Custom");

            // If original version isn't in the managed list, insert it at top with (Default)
            if (!defaultInList && formattedOriginal != null)
                items.Insert(0, $"{formattedOriginal} (Default)");
        }

        // Find selected index based on installed version
        int selectedIndex = 0;
        if (installedVersion != null && isPresent)
        {
            if (installedVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = items.Count - 1;
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var itemBase = items[i].Replace(" (Default)", "");
                    if (installedVersion.Equals(itemBase, StringComparison.OrdinalIgnoreCase)
                        || itemBase.StartsWith(installedVersion, StringComparison.OrdinalIgnoreCase)
                        || installedVersion.StartsWith(itemBase, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
        }

        var versionCombo = new ComboBox
        {
            ItemsSource = driverOverrideActive ? new List<string> { "Driver Override Active" } : items,
            SelectedIndex = driverOverrideActive ? 0 : selectedIndex,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = isPresent && !driverOverrideActive,
            Opacity = driverOverrideActive ? 0.4 : 1.0,
        };
        if (driverOverrideActive)
            ToolTipService.SetToolTip(versionCombo, "Driver override is active — disable it in NVIDIA App or Profile Inspector to manage this DLL in RHI.");

        // When driver override is active, tooltip is already on the combo — no extra text needed
        col.Children.Add(versionCombo);

        bool versionInit = true;
        versionCombo.SelectionChanged += async (s, ev) =>
        {
            if (versionInit) return;
            var selected = versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            // If it's the (Default) item, treat as "Default" (restore original)
            if (selected.EndsWith(" (Default)"))
                await onVersionSelected("Default");
            else
                await onVersionSelected(selected);
        };
        versionInit = false;

        // Preset ComboBox (only for SR, RR, FG)
        if (presets != null && isPresent)
        {
            col.Children.Add(new TextBlock { Text = "Preset", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

            var presetItems = presets.Select(p => p.Name).ToList();
            int presetIdx = 0;
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i].Value == currentPreset) { presetIdx = i; break; }
            }

            var presetCombo = new ComboBox
            {
                ItemsSource = presetItems,
                SelectedIndex = presetIdx,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = isPresent,
            };

            // Add tooltip explaining presets
            string presetTooltip = label switch
            {
                "DLSS Super Resolution" => "Override the DLSS upscaling model. J/K use the 1st-gen transformer (DLSS 4.0). L/M use the 2nd-gen transformer (DLSS 4.5) with better temporal stability. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                "Ray Reconstruction" => "Override the Ray Reconstruction denoising model. Higher presets are newer model iterations. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                "Frame Generation" => "Override the Frame Generation interpolation model. Higher presets are newer model iterations. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                _ => ""
            };
            if (!string.IsNullOrEmpty(presetTooltip))
                ToolTipService.SetToolTip(presetCombo, presetTooltip);

            bool presetInit = true;
            presetCombo.SelectionChanged += (s, ev) =>
            {
                if (presetInit) return;
                var idx = presetCombo.SelectedIndex;
                if (idx >= 0 && idx < presets.Length)
                    onPresetSelected?.Invoke(presets[idx].Value);
            };
            presetInit = false;
            col.Children.Add(presetCombo);
        }

        // Render Scale ComboBox (only for SR and RR)
        if (onRenderScaleSelected != null && isPresent)
        {
            col.Children.Add(new TextBlock { Text = "Render Scale", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
            var rsOptions = DlssPresetService.RenderScaleOptions;
            var rsItems = rsOptions.Select(o => o.Name).ToList();

            // Determine current selection
            int rsIdx = 0; // Off
            if (currentRenderScale > 0)
            {
                // Check if it matches a named option
                int namedIdx = Array.FindIndex(rsOptions, o => o.Value == currentRenderScale);
                if (namedIdx >= 0)
                    rsIdx = namedIdx;
                else
                    rsIdx = rsItems.Count - 1; // Custom
            }

            // If Custom is selected, show the percentage in the item text
            if (rsIdx == rsItems.Count - 1 && currentRenderScale > 0)
                rsItems[^1] = $"Custom ({currentRenderScale}%)";

            var rsCombo = new ComboBox
            {
                ItemsSource = rsItems,
                SelectedIndex = rsIdx,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = isPresent,
            };
            ToolTipService.SetToolTip(rsCombo,
                "Override the DLSS render resolution scale. Off = game controls the scale.\nNamed presets set a fixed percentage. Custom lets you enter any value from 33-100%.");

            bool rsInit = true;
            rsCombo.SelectionChanged += (s, ev) =>
            {
                if (rsInit) return;
                var idx = rsCombo.SelectedIndex;
                if (idx < 0 || idx >= rsOptions.Length) return;

                if (rsOptions[idx].Name == "Custom")
                {
                    // Show a TextBox inline — replace the combo temporarily
                    var parent = rsCombo.Parent as StackPanel;
                    if (parent == null) return;
                    var comboIdx = parent.Children.IndexOf(rsCombo);
                    var inputBox = new TextBox
                    {
                        PlaceholderText = "33-100",
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxLength = 3,
                    };
                    inputBox.KeyDown += (ks, ke) =>
                    {
                        if (ke.Key == Windows.System.VirtualKey.Enter)
                        {
                            if (uint.TryParse(inputBox.Text, out var val) && val >= 33 && val <= 100)
                            {
                                onRenderScaleSelected(val);
                            }
                            else
                            {
                                // Invalid — revert to Off
                                onRenderScaleSelected(0);
                            }
                        }
                        else if (ke.Key == Windows.System.VirtualKey.Escape)
                        {
                            // Cancel — revert
                            onRenderScaleSelected(currentRenderScale);
                        }
                    };
                    inputBox.LostFocus += (ls, le) =>
                    {
                        if (uint.TryParse(inputBox.Text, out var val) && val >= 33 && val <= 100)
                            onRenderScaleSelected(val);
                        else
                            onRenderScaleSelected(currentRenderScale); // revert
                    };
                    parent.Children[comboIdx] = inputBox;
                    inputBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    onRenderScaleSelected(rsOptions[idx].Value);
                }
            };
            rsInit = false;
            col.Children.Add(rsCombo);
        }

        return col;
    }

    private static Border MakeDlssDivider(int column)
    {
        var divider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 0),
        };
        Grid.SetColumn(divider, column);
        return divider;
    }
}