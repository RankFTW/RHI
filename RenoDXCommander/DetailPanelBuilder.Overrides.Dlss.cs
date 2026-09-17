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
        string? originalVersion = null, bool driverOverrideActive = false,
        Action<bool>? onDriverOverrideToggled = null)
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
        col.Children.Add(versionLabel);

        // Build items list with (Default) marker on the game's original/default version
        var items = new List<string>();

        if (!isPresent && installedVersion == null && onDriverOverrideToggled == null)
        {
            // Game truly doesn't have this component — show "None"
            items.Add("None");
        }
        else
        {
            string? formattedOriginal = originalVersion != null
                ? DlssStreamlineService.FormatVersion(originalVersion)
                : (installedVersion != null ? installedVersion : null);

            foreach (var ver in availableVersions)
                items.Add(ver);
            items.Add("Custom");

            // Add a "Default (x.x.x)" entry at the top so the user can restore the original
            if (formattedOriginal != null)
            {
                var defaultLabel = $"Default ({formattedOriginal})";
                items.Insert(0, defaultLabel);
            }
            else
            {
                items.Insert(0, "Default");
            }

            // "NVIDIA Override" as a selectable option when the caller supports it
            if (onDriverOverrideToggled != null)
                items.Add("NVIDIA Override");
        }

        // Find selected index based on installed version (or NVIDIA Override if active)
        int selectedIndex = 0;
        if (driverOverrideActive && onDriverOverrideToggled != null)
        {
            // Select the "NVIDIA Override" entry at the end of the list
            selectedIndex = items.Count - 1;
        }
        else if (installedVersion != null && (isPresent || onDriverOverrideToggled != null))
        {
            if (installedVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                // "Custom" is second-to-last (before "NVIDIA Override" if present)
                selectedIndex = onDriverOverrideToggled != null ? items.Count - 2 : items.Count - 1;
            }
            else
            {
                bool matched = false;
                for (int i = 0; i < items.Count; i++)
                {
                    var itemBase = items[i].Replace(" (Default)", "");
                    if (installedVersion.Equals(itemBase, StringComparison.OrdinalIgnoreCase)
                        || itemBase.StartsWith(installedVersion, StringComparison.OrdinalIgnoreCase)
                        || installedVersion.StartsWith(itemBase, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        matched = true;
                        break;
                    }
                }

                // Installed version not in manifest list (e.g. early access / custom build)
                // Insert it before "Custom" so it shows correctly rather than falling back to (Default)
                if (!matched)
                {
                    var insertIdx = onDriverOverrideToggled != null ? items.Count - 2 : items.Count - 1; // before Custom / NVIDIA Override
                    items.Insert(insertIdx, installedVersion);
                    selectedIndex = insertIdx;
                }
            }
        }

        var versionCombo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIndex,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = isPresent || (onDriverOverrideToggled != null),
            Opacity = 1.0,
        };

        if (driverOverrideActive)
            ToolTipService.SetToolTip(versionCombo, "NVIDIA Override is active — the driver is injecting its own latest DLL for this game. Select any other version to disable the override and deploy that version instead.");
        else if (onDriverOverrideToggled != null)
            ToolTipService.SetToolTip(versionCombo, "Selects which DLL version is copied into the game folder. Default restores the original game DLL. Custom uses your own file from %LocalAppData%\\RHI\\Custom\\DLSS\\. NVIDIA Override lets the driver inject its own latest version instead of a file on disk — equivalent to enabling DLSS Override in NVIDIA App or Profile Inspector.");
        else
            ToolTipService.SetToolTip(versionCombo, "Selects which DLL version is copied into the game folder. Default restores the original game DLL. Custom uses your own file from %LocalAppData%\\RHI\\Custom\\DLSS\\.");

        col.Children.Add(versionCombo);

        bool versionInit = true;
        versionCombo.SelectionChanged += async (s, ev) =>
        {
            if (versionInit) return;
            var selected = versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            if (selected == "NVIDIA Override")
            {
                // Enable driver DLL override — no DLL swap needed
                onDriverOverrideToggled?.Invoke(true);
                return;
            }

            // If we were on NVIDIA Override and switched away, disable it first
            if (driverOverrideActive || (ev.RemovedItems.Count > 0 && ev.RemovedItems[0] as string == "NVIDIA Override"))
                onDriverOverrideToggled?.Invoke(false);

            versionCombo.IsEnabled = false;
            try
            {
                // If it's the (Default) item, treat as "Default" (restore original)
                if (selected.StartsWith("Default", StringComparison.OrdinalIgnoreCase))
                    await onVersionSelected("Default");
                else
                    await onVersionSelected(selected);
            }
            finally
            {
                versionCombo.IsEnabled = isPresent || (onDriverOverrideToggled != null);
            }
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
                            ke.Handled = true;
                            // Defocus the TextBox by briefly disabling it — WinUI 3 has no
                            // direct "clear focus" API, and FocusManager.TryMoveFocus is
                            // unreliable in imperative panel contexts.
                            inputBox.IsEnabled = false;
                            inputBox.IsEnabled = true;
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
                            ke.Handled = true;
                            inputBox.IsEnabled = false;
                            inputBox.IsEnabled = true;
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