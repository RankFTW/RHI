// DetailPanelBuilder.Extras.FrameGen.Dlssg2030Settings.cs — ⚙ dialog for 20/30 FG Unlock.
// Edits the game's dlssg_sm86.ini. The mod treats a mistyped value as "turn frame generation
// off", so every setting is a dropdown of valid values only.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private async Task ShowDlssg2030SettingsAsync(string installPath, bool isInstalled)
    {
        var iniPath = string.IsNullOrEmpty(installPath) ? "" : Path.Combine(installPath, Dlssg20_30Service.IniFileName);
        var logPath = string.IsNullOrEmpty(installPath) ? "" : Path.Combine(installPath, Dlssg20_30Service.LogRelPath);
        bool hasIni = isInstalled && File.Exists(iniPath);

        var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "These settings are saved in the game's dlssg_sm86.ini. Exit the game completely and start it again for changes to apply. " +
                   "Needs NVIDIA driver R580 or newer; on older drivers the mod turns itself off.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        if (!hasIni)
            panel.Children.Add(new TextBlock
            {
                Text = "Install 20/30 FG Unlock on this game to change these settings.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
            });

        var message = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) };
        bool init = true;

        // One labelled dropdown bound to [section] key. Options are (label, ini value).
        ComboBox AddSetting(string label, string help, string section, string key, string fallback,
            (string Label, string Value)[] options, Func<string, bool>? beforeWrite = null)
        {
            var current = hasIni ? Dlssg20_30Service.ReadIniValue(iniPath, section, key) : null;
            var idx = Array.FindIndex(options, o => o.Value.Equals(current ?? fallback, StringComparison.OrdinalIgnoreCase));
            var combo = new ComboBox
            {
                ItemsSource = options.Select(o => o.Label).ToList(),
                SelectedIndex = idx >= 0 ? idx : Array.FindIndex(options, o => o.Value == fallback),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = hasIni, FontSize = 12,
            };
            combo.SelectionChanged += (s, e) =>
            {
                if (init || combo.SelectedIndex < 0) return;
                var value = options[combo.SelectedIndex].Value;
                try
                {
                    if (beforeWrite != null && !beforeWrite(value)) return;
                    Dlssg20_30Service.WriteIniValue(iniPath, section, key, value);
                    message.Text = $"Saved {label}. Restart the game to apply.";
                    CrashReporter.Log($"[Dlssg2030Settings] {section}.{key}={value} in '{installPath}'");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    message.Text = $"Couldn't save {label}: {ex.Message}";
                    CrashReporter.Log($"[Dlssg2030Settings] Write failed — {ex.Message}");
                }
            };

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(lbl, help);
            ToolTipService.SetToolTip(combo, help);
            grid.Children.Add(lbl);
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
            panel.Children.Add(grid);
            panel.Children.Add(new TextBlock
            {
                Text = help, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(162, -6, 0, 2),
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
            return combo;
        }

        AddSetting("Frame Generation",
            "Off leaves the game's own DLSS Frame Generation untouched (no FG on RTX 20/30) without uninstalling.",
            "General", "Enabled", "1",
            new[] { ("On", "1"), ("Off", "0") });

        ComboBox? framesCombo = null;
        var engineCombo = AddSetting("Engine",
            "DLSS uses NVIDIA's frame generation model. XeSS uses Intel's model: up to 4X, and it doesn't work with HDR (16-bit) output.",
            "FrameGeneration", "Engine", "DLSS",
            new[] { ("DLSS (default)", "DLSS"), ("XeSS", "XeSS") },
            beforeWrite: value =>
            {
                // XeSS can't do 5X/6X: the mod would turn FG off, so drop to 4X
                if (value == "XeSS" && framesCombo != null && framesCombo.SelectedIndex >= 4)
                {
                    Dlssg20_30Service.WriteIniValue(iniPath, "FrameGeneration", "MaxGeneratedFrames", "3");
                    init = true; framesCombo.SelectedIndex = 3; init = false;
                }
                return true;
            });

        framesCombo = AddSetting("Max Frames",
            "The highest multiplier the game can offer. The game's own menu can still pick a lower one. 6X has to be chosen here.",
            "FrameGeneration", "MaxGeneratedFrames", "3",
            new[] { ("2X", "1"), ("3X", "2"), ("4X (default)", "3"), ("5X", "4"), ("6X", "5"), ("Game decides", "0") },
            beforeWrite: value =>
            {
                if ((value == "4" || value == "5") && engineCombo.SelectedIndex == 1)
                {
                    message.Text = "XeSS goes up to 4X. Switch Engine to DLSS for 5X or 6X.";
                    init = true; framesCombo!.SelectedIndex = 2; init = false;
                    Dlssg20_30Service.WriteIniValue(iniPath, "FrameGeneration", "MaxGeneratedFrames", "3");
                    return false;
                }
                return true;
            });

        AddSetting("Preset",
            "A is the default. B cleans up the HUD in generated frames, but only works if the game provides the extra images (most don't). C is FG Lite: much cheaper to run, slightly lower quality.",
            "DLSSG", "Preset", "A",
            new[] { ("A (default)", "A"), ("B: HUD recomposition", "B"), ("C: FG Lite", "C") });

        AddSetting("Speed Tier",
            "How far the image may differ from NVIDIA's official output. Exact tiers match it bit for bit; Fast and Fastest are a few percent quicker with tiny differences.",
            "DLSSG", "Optimized", "0",
            new[] { ("Exact", "0"), ("Exact, optimised (default)", "1"), ("Fast", "2"), ("Fastest", "3") });

        AddSetting("Super Resolution",
            "RTX 20/30 only. Pins DLSS preset M or L. INT8 uses the mod's faster kernels: quicker in Performance and Ultra Performance, slower than the game's default in Quality, Balanced and DLAA. No effect while Ray Reconstruction is on.",
            "SuperResolution", "Mode", "Off",
            new[] { ("Off (default)", "Off"), ("M-INT8", "M-INT8"), ("L-INT8", "L-INT8"), ("M-FP16", "M-FP16"), ("L-FP16", "L-FP16") });

        init = false;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        Button MakeOpenButton(string text, string path)
        {
            var b = new Button
            {
                Content = text, FontSize = 12, Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(8), IsEnabled = File.Exists(path),
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                BorderThickness = new Thickness(1),
            };
            b.Click += async (s, e) =>
            {
                try { if (File.Exists(path)) await Windows.System.Launcher.LaunchUriAsync(new Uri(path)); }
                catch (Exception ex) { CrashReporter.Log($"[Dlssg2030Settings] Open '{path}' failed — {ex.Message}"); }
            };
            return b;
        }
        buttons.Children.Add(MakeOpenButton("Open dlssg_sm86.ini", iniPath));
        buttons.Children.Add(MakeOpenButton("Open log", logPath));
        panel.Children.Add(buttons);
        panel.Children.Add(message);

        var dlg = new ContentDialog
        {
            Title = "20/30 FG Unlock Settings",
            Content = new ScrollViewer { Content = panel, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "Close",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 720.0;
        await DialogService.ShowSafeAsync(dlg);
    }
}
