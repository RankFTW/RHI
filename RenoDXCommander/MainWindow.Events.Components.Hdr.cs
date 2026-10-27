// MainWindow.Events.Components.Hdr.cs — RTX HDR and Auto HDR config dialog handlers.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    internal async void RtxHdrConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var card = (sender as FrameworkElement)?.Tag as GameCardViewModel
                    ?? (sender as Button)?.Tag as GameCardViewModel;
            if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

            var dlssPresetService = App.Services.GetRequiredService<DlssPresetService>();
            var content = new StackPanel { Spacing = 6 };

            // Read current values off the UI thread — these call _sessionLock.Wait(5000) and
            // will block the UI thread for up to 25s total if NVAPI is hung (e.g. after GPU sleep/wake).
            var (currentContrast, currentSaturation, currentPeakBrightness, currentMiddleGrey, currentDebanding) =
                await Task.Run(() => (
                    (int)dlssPresetService.GetRtxHdrContrast(card.GameName, card.InstallPath),
                    (int)dlssPresetService.GetRtxHdrSaturation(card.GameName, card.InstallPath),
                    (int)dlssPresetService.GetRtxHdrPeakBrightness(card.GameName, card.InstallPath),
                    (int)dlssPresetService.GetRtxHdrMiddleGrey(card.GameName, card.InstallPath),
                    (int)dlssPresetService.GetRtxHdrDebanding(card.GameName, card.InstallPath)
                ));

            // Convert stored values to display values
            int contrastDisplay = currentContrast > 0 ? currentContrast - 100 : 0;
            int saturationDisplay = currentSaturation > 0 ? currentSaturation - 100 : 0;
            int peakBrightnessDisplay = currentPeakBrightness > 0 ? currentPeakBrightness : ViewModel.Settings.PeakNits;
            if (peakBrightnessDisplay < 400) peakBrightnessDisplay = 510; // fallback default

            // ── Peak Brightness ───────────────────────────────────────────────────
            var nitsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var nitsLabel = new TextBlock { Text = $"Peak Brightness: {peakBrightnessDisplay} nits", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), MinWidth = 175 };
            var nitsWarning = new TextBlock { Text = "⚠ High values may look unnatural", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush), VerticalAlignment = VerticalAlignment.Center, Opacity = peakBrightnessDisplay > 600 ? 1.0 : 0.0 };
            nitsRow.Children.Add(nitsLabel);
            nitsRow.Children.Add(nitsWarning);
            var nitsSlider = new Slider { Minimum = 400, Maximum = 2000, StepFrequency = 10, Value = peakBrightnessDisplay, HorizontalAlignment = HorizontalAlignment.Stretch };
            nitsSlider.ValueChanged += (s, ev) =>
            {
                nitsLabel.Text = $"Peak Brightness: {(int)nitsSlider.Value} nits";
                nitsWarning.Opacity = (int)nitsSlider.Value > 600 ? 1.0 : 0.0;
            };
            content.Children.Add(nitsRow);
            content.Children.Add(nitsSlider);

            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 2, 0, 2) });
            // ── Contrast ──────────────────────────────────────────────────────────
            string ContrastLabel(int val) => val switch
            {
                0 => "Contrast: 0 — Gamma 2.0 (Default)",
                25 => "Contrast: +25 — Gamma 2.2",
                50 => "Contrast: +50 — Gamma 2.4",
                _ => $"Contrast: {(val >= 0 ? "+" : "")}{val}",
            };
            var contrastLabel = new TextBlock { Text = ContrastLabel(contrastDisplay), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            var contrastSlider = new Slider { Minimum = -100, Maximum = 100, StepFrequency = 1, Value = contrastDisplay, HorizontalAlignment = HorizontalAlignment.Stretch };
            contrastSlider.ValueChanged += (s, ev) => contrastLabel.Text = ContrastLabel((int)contrastSlider.Value);
            content.Children.Add(contrastLabel);
            content.Children.Add(contrastSlider);

            // ── Gamma preset buttons ──────────────────────────────────────────────
            var gammaPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 4) };
            foreach (var (label, value) in new[] { ("Gamma 2.0", 0), ("Gamma 2.2", 25), ("Gamma 2.4", 50) })
            {
                var btn = new Button
                {
                    Content = label,
                    FontSize = 11,
                    Padding = new Thickness(10, 4, 10, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                var capturedValue = value;
                btn.Click += (s, ev) =>
                {
                    contrastSlider.Value = capturedValue;
                    contrastLabel.Text = ContrastLabel(capturedValue);
                };
                gammaPanel.Children.Add(btn);
            }
            content.Children.Add(gammaPanel);

            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 2, 0, 2) });
            // ── Middle Grey ───────────────────────────────────────────────────────
            // ITU-recommended paper white nits per peak brightness (interpolated for values between table entries)
            // Source: https://www.rtings.com/tv/learn/rtx-hdr (table from community research)
            // Formula: midGreyNits = paperWhiteNits × (0.5 ^ gamma)
            // gamma: 2.0 = contrast 0, 2.2 = contrast +25, 2.4 = contrast +50
            static double CalcPaperWhiteNits(double peakNits)
            {
                // ITU lookup table: (peakNits, paperWhiteNits)
                (double peak, double pw)[] table =
                {
                    (400,  101), (600,  138), (800,  172),
                    (1000, 203), (1500, 276), (2000, 343),
                };
                if (peakNits <= table[0].peak)  return table[0].pw;
                if (peakNits >= table[^1].peak) return table[^1].pw;
                for (int i = 0; i < table.Length - 1; i++)
                {
                    if (peakNits >= table[i].peak && peakNits <= table[i + 1].peak)
                    {
                        double t = (peakNits - table[i].peak) / (table[i + 1].peak - table[i].peak);
                        return table[i].pw + t * (table[i + 1].pw - table[i].pw);
                    }
                }
                return 203; // fallback
            }
            static int CalcAutoMiddleGrey(double peakNits, int contrastVal)
            {
                double gamma = contrastVal switch { 25 => 2.2, 50 => 2.4, _ => 2.0 };
                // For non-preset contrast values interpolate gamma linearly between anchors
                if (contrastVal != 0 && contrastVal != 25 && contrastVal != 50)
                    gamma = 2.0 + (contrastVal / 100.0) * 0.4; // rough linear: 0→2.0, 100→2.4
                var pw = CalcPaperWhiteNits(peakNits);
                var mg = pw * Math.Pow(0.5, gamma);
                return Math.Clamp((int)Math.Round(mg), 10, 100);
            }

            var middleGreyValues = new int[] { 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 };
            int mgInitial = (currentMiddleGrey >= 10 && currentMiddleGrey <= 100) ? currentMiddleGrey : 50;
            
            // Calculate perceived paperwhite from middle grey and gamma
            // Formula: paperwhite = midGrey / (0.5 ^ gamma)
            int CalcPerceivedPaperwhite(int midGrey, int contrastVal)
            {
                double gamma = contrastVal switch { 25 => 2.2, 50 => 2.4, _ => 2.0 };
                if (contrastVal != 0 && contrastVal != 25 && contrastVal != 50)
                    gamma = 2.0 + (contrastVal / 100.0) * 0.4;
                var pw = midGrey / Math.Pow(0.5, gamma);
                return (int)Math.Round(pw);
            }
            
            string MiddleGreyLabel(int val, int contrastVal)
            {
                var perceivedPw = CalcPerceivedPaperwhite(val, contrastVal);
                return $"Middle Grey: {val} ({perceivedPw} nits)";
            }
            
            int mgInitialPw = CalcPerceivedPaperwhite(mgInitial, (int)contrastSlider.Value);
            var mgRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var mgLabel = new TextBlock { Text = MiddleGreyLabel(mgInitial, (int)contrastSlider.Value), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), MinWidth = 175 };
            var mgWarning = new TextBlock { Text = "⚠ High values may look washed out", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush), VerticalAlignment = VerticalAlignment.Center, Opacity = mgInitialPw > 203 ? 1.0 : 0.0 };
            mgRow.Children.Add(mgLabel);
            mgRow.Children.Add(mgWarning);
            var mgSlider = new Slider { Minimum = 10, Maximum = 100, StepFrequency = 1, Value = mgInitial, HorizontalAlignment = HorizontalAlignment.Stretch };
            mgSlider.ValueChanged += (s, ev) =>
            {
                mgLabel.Text = MiddleGreyLabel((int)mgSlider.Value, (int)contrastSlider.Value);
                mgWarning.Opacity = CalcPerceivedPaperwhite((int)mgSlider.Value, (int)contrastSlider.Value) > 203 ? 1.0 : 0.0;
            };
            // Also update when contrast changes (gamma affects perceived paperwhite)
            contrastSlider.ValueChanged += (s, ev) =>
            {
                mgLabel.Text = MiddleGreyLabel((int)mgSlider.Value, (int)contrastSlider.Value);
                mgWarning.Opacity = CalcPerceivedPaperwhite((int)mgSlider.Value, (int)contrastSlider.Value) > 203 ? 1.0 : 0.0;
            };
            content.Children.Add(mgRow);
            content.Children.Add(mgSlider);

            // Auto button + preset buttons — calculates correct Middle Grey or uses predefined values
            var mgButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 4) };
            var autoMgBtn = new Button
            {
                Content = "Auto",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
            };
            ToolTipService.SetToolTip(autoMgBtn, "Calculate Middle Grey from Peak Brightness and Gamma using the ITU formula");
            autoMgBtn.Click += (s, ev) =>
            {
                var autoVal = CalcAutoMiddleGrey((int)nitsSlider.Value, (int)contrastSlider.Value);
                mgSlider.Value = autoVal;
                mgLabel.Text = MiddleGreyLabel(autoVal, (int)contrastSlider.Value);
                mgWarning.Opacity = CalcPerceivedPaperwhite(autoVal, (int)contrastSlider.Value) > 203 ? 1.0 : 0.0;
            };
            mgButtonsPanel.Children.Add(autoMgBtn);
            
            // Separator
            mgButtonsPanel.Children.Add(new TextBlock { Text = "|", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            
            // Preset buttons for common paperwhite values (100-200 nits range)
            foreach (var presetPw in new[] { 100, 125, 150, 175, 200 })
            {
                var presetBtn = new Button
                {
                    Content = presetPw.ToString(),
                    FontSize = 11,
                    Padding = new Thickness(8, 4, 8, 4),
                    MinWidth = 36,
                };
                var capturedPw = presetPw;
                ToolTipService.SetToolTip(presetBtn, $"Set Middle Grey to achieve ~{presetPw} nits paperwhite");
                presetBtn.Click += (s, ev) =>
                {
                    // Reverse formula: midGrey = paperwhite × (0.5 ^ gamma)
                    double gamma = (int)contrastSlider.Value switch { 25 => 2.2, 50 => 2.4, _ => 2.0 };
                    if ((int)contrastSlider.Value != 0 && (int)contrastSlider.Value != 25 && (int)contrastSlider.Value != 50)
                        gamma = 2.0 + ((int)contrastSlider.Value / 100.0) * 0.4;
                    var mgVal = Math.Clamp((int)Math.Round(capturedPw * Math.Pow(0.5, gamma)), 10, 100);
                    mgSlider.Value = mgVal;
                    mgLabel.Text = MiddleGreyLabel(mgVal, (int)contrastSlider.Value);
                    mgWarning.Opacity = CalcPerceivedPaperwhite(mgVal, (int)contrastSlider.Value) > 203 ? 1.0 : 0.0;
                };
                mgButtonsPanel.Children.Add(presetBtn);
            }
            content.Children.Add(mgButtonsPanel);

            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 2, 0, 2) });
            // ── Saturation ────────────────────────────────────────────────────────
            string SaturationLabel(int val) => val switch
            {
                -25 => "Saturation: -25 — Neutral Saturation",
                _ => $"Saturation: {(val >= 0 ? "+" : "")}{val}",
            };
            var satLabel = new TextBlock { Text = SaturationLabel(saturationDisplay), FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            var satSlider = new Slider { Minimum = -100, Maximum = 100, StepFrequency = 1, Value = saturationDisplay, HorizontalAlignment = HorizontalAlignment.Stretch };
            satSlider.ValueChanged += (s, ev) => satLabel.Text = SaturationLabel((int)satSlider.Value);
            content.Children.Add(satLabel);
            content.Children.Add(satSlider);

            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 2, 0, 2) });
            // ── Debanding ─────────────────────────────────────────────────────────
            var debandingOptions = new (string name, uint value)[]
            {
                ("No Debanding", 0x06),
                ("Low Debanding", 0x0A),
                ("High Debanding", 0x02),
                ("High Debanding (Indicator)", 0x03),
                ("High Debanding (Indicator + Debug)", 0x23),
            };
            bool isAdmin = VulkanLayerService.IsRunningAsAdmin();
            var debandingCombo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = isAdmin, Opacity = isAdmin ? 1.0 : 0.4 };
            int selectedDbIndex = 0;
            for (int i = 0; i < debandingOptions.Length; i++)
            {
                debandingCombo.Items.Add(debandingOptions[i].name);
                if (currentDebanding == (int)debandingOptions[i].value) selectedDbIndex = i;
            }
            debandingCombo.SelectedIndex = selectedDbIndex;
            var dbLabel = new TextBlock { Text = "Debanding", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            content.Children.Add(dbLabel);
            content.Children.Add(debandingCombo);
            if (!isAdmin)
                content.Children.Add(new TextBlock
                {
                    Text = "Requires admin mode to change",
                    FontSize = 10,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    Margin = new Thickness(0, -4, 0, 0),
                });

            // ── Default preset buttons ────────────────────────────────────────────
            var defaultsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "rtx_hdr_defaults.json");

            // Load current defaults (if any) to show whether "Set Default" is available
            bool hasDefaults = File.Exists(defaultsPath);

            var defaultsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var saveDefaultBtn = new Button
            {
                Content = "Save as Default",
                FontSize = 11,
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(saveDefaultBtn, "Save current slider values as your default RTX HDR preset");
            var setDefaultBtn = new Button
            {
                Content = "Set Default",
                FontSize = 11,
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = hasDefaults,
            };
            ToolTipService.SetToolTip(setDefaultBtn, hasDefaults ? "Apply your saved default preset to the sliders" : "No default saved yet — use 'Save as Default' first");

            saveDefaultBtn.Click += (s, ev) =>
            {
                try
                {
                    var defaults = new Dictionary<string, object>
                    {
                        ["PeakBrightness"] = (int)nitsSlider.Value,
                        ["Contrast"]       = (int)contrastSlider.Value,
                        ["Saturation"]     = (int)satSlider.Value,
                        ["MiddleGrey"]     = (int)mgSlider.Value,
                        ["Debanding"]      = (int)debandingOptions[debandingCombo.SelectedIndex].value,
                    };
                    Directory.CreateDirectory(Path.GetDirectoryName(defaultsPath)!);
                    File.WriteAllText(defaultsPath, JsonSerializer.Serialize(defaults,
                        new JsonSerializerOptions { WriteIndented = true }));
                    setDefaultBtn.IsEnabled = true;
                    ToolTipService.SetToolTip(setDefaultBtn, "Apply your saved default preset to the sliders");
                    saveDefaultBtn.Content = "Saved!";
                }
                catch (Exception ex) { CrashReporter.Log($"[RtxHdrConfigButton_Click] Failed to save defaults — {ex.Message}"); }
            };

            setDefaultBtn.Click += (s, ev) =>
            {
                try
                {
                    var json = File.ReadAllText(defaultsPath);
                    var defaults = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (defaults == null) return;

                    if (defaults.TryGetValue("PeakBrightness", out var pb)) nitsSlider.Value = Math.Clamp(pb.GetInt32(), 400, 2000);
                    if (defaults.TryGetValue("Contrast", out var ct)) contrastSlider.Value = Math.Clamp(ct.GetInt32(), -100, 100);
                    if (defaults.TryGetValue("Saturation", out var sat)) satSlider.Value = Math.Clamp(sat.GetInt32(), -100, 100);
                    if (defaults.TryGetValue("MiddleGrey", out var mg))
                    {
                        var mgVal = Math.Clamp(mg.GetInt32(), 10, 100);
                        mgSlider.Value = mgVal;
                    }
                    if (defaults.TryGetValue("Debanding", out var db))
                    {
                        var dbVal = db.GetInt32();
                        for (int i = 0; i < debandingOptions.Length; i++)
                        {
                            if ((int)debandingOptions[i].value == dbVal) { debandingCombo.SelectedIndex = i; break; }
                        }
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[RtxHdrConfigButton_Click] Failed to apply defaults — {ex.Message}"); }
            };

            defaultsPanel.Children.Add(saveDefaultBtn);
            defaultsPanel.Children.Add(setDefaultBtn);
            content.Children.Add(defaultsPanel);

            // ── Dialog ────────────────────────────────────────────────────────────
            var dialog = new ContentDialog
            {
                Title = "RTX HDR Settings",
                Content = new ScrollViewer { Content = content, MaxHeight = 600, Padding = new Thickness(0, 0, 16, 0) },
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(dialog);
            if (result != ContentDialogResult.Primary) return;

            // Write all values on a background thread — NVAPI profile writes are kernel-mode calls
            var peakNits = (uint)nitsSlider.Value;
            var contrastStored = (uint)(100 + (int)contrastSlider.Value);
            var satStored = (uint)(100 + (int)satSlider.Value);
            var middleGrey = (uint)mgSlider.Value;
            var debanding = debandingOptions[debandingCombo.SelectedIndex].value;

            _ = Task.Run(() =>
            {
                dlssPresetService.SetRtxHdrPeakBrightness(card.GameName, card.InstallPath, peakNits);
                dlssPresetService.SetRtxHdrContrast(card.GameName, card.InstallPath, contrastStored);
                dlssPresetService.SetRtxHdrSaturation(card.GameName, card.InstallPath, satStored);
                dlssPresetService.SetRtxHdrMiddleGrey(card.GameName, card.InstallPath, middleGrey);
                dlssPresetService.SetRtxHdrDebanding(card.GameName, card.InstallPath, debanding);
                CrashReporter.Log($"[RtxHdrConfigButton_Click] Applied RTX HDR settings for '{card.GameName}': PeakNits={peakNits}, Contrast={contrastStored}, Sat={satStored}, MidGrey={middleGrey}, Deband=0x{debanding:X2}");
            });
            card.ActionMessage = "✅ RTX HDR settings applied.";
            card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RtxHdrConfigButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    internal async void AutoHdrConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var card = (sender as FrameworkElement)?.Tag as GameCardViewModel
                    ?? (sender as Button)?.Tag as GameCardViewModel;
            if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

            var autoHdrService  = App.Services.GetRequiredService<IAutoHdrService>();
            var gameNameService = App.Services.GetRequiredService<IGameNameService>();

            // Resolve the exe path so we can read the live registry value
            var exePath = autoHdrService.ResolveExePath(
                card.GameName, card.InstallPath,
                gameNameService.LaunchExeOverrides,
                ViewModel.Manifest?.LaunchExeOverrides);

            // Read current strength from registry (fall back to persisted value, then 50)
            int currentStrength = 50;
            if (!string.IsNullOrEmpty(exePath))
            {
                var liveStrength = autoHdrService.GetStrength(exePath);
                currentStrength = liveStrength >= 0 ? liveStrength : card.AutoHdrStrength;
            }
            else
            {
                currentStrength = card.AutoHdrStrength;
            }

            var content = new StackPanel { Spacing = 6 };

            // ── Brightness / Intensity ────────────────────────────────────────────
            string StrengthLabel(int val)
            {
                if (val == 0)   return "Brightness: 0 — off (no boost)";
                if (val == 50)  return "Brightness: 50 — default";
                if (val == 100) return "Brightness: 100 — maximum (~1000 nits peak)";
                return $"Brightness: {val}";
            }

            var strengthRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var strengthLabel = new TextBlock
            {
                Text = StrengthLabel(currentStrength),
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                MinWidth = 220,
            };
            var strengthWarning = new TextBlock
            {
                Text = "⚠ High values may look unnatural",
                FontSize = 10,
                Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = currentStrength > 75 ? 1.0 : 0.0,
            };
            strengthRow.Children.Add(strengthLabel);
            strengthRow.Children.Add(strengthWarning);

            var strengthSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                StepFrequency = 1,
                Value = currentStrength,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            strengthSlider.ValueChanged += (s, ev) =>
            {
                strengthLabel.Text  = StrengthLabel((int)strengthSlider.Value);
                strengthWarning.Opacity = (int)strengthSlider.Value > 75 ? 1.0 : 0.0;
            };
            content.Children.Add(strengthRow);
            content.Children.Add(strengthSlider);

            // ── Preset buttons ────────────────────────────────────────────────────
            var presetsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 4) };
            foreach (var (label, value) in new[] { ("Low (25)", 25), ("Default (50)", 50), ("High (75)", 75), ("Max (100)", 100) })
            {
                var btn = new Button
                {
                    Content = label,
                    FontSize = 11,
                    Padding = new Thickness(10, 4, 10, 4),
                };
                var capturedValue = value;
                btn.Click += (s, ev) =>
                {
                    strengthSlider.Value = capturedValue;
                    strengthLabel.Text   = StrengthLabel(capturedValue);
                    strengthWarning.Opacity = capturedValue > 75 ? 1.0 : 0.0;
                };
                presetsPanel.Children.Add(btn);
            }
            content.Children.Add(presetsPanel);

            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 4, 0, 4) });

            // ── Info note ─────────────────────────────────────────────────────────
            content.Children.Add(new TextBlock
            {
                Text = "Changes take effect on the next game launch.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            });

            if (string.IsNullOrEmpty(exePath))
            {
                content.Children.Add(new TextBlock
                {
                    Text = "⚠ Could not resolve game exe path — registry will not be written.",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"Exe: {exePath}",
                    FontSize = 10,
                    Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            var dialog = new ContentDialog
            {
                Title = "Auto HDR Settings",
                Content = new ScrollViewer { Content = content, MaxHeight = 600, Padding = new Thickness(0, 0, 16, 0) },
                PrimaryButtonText = "Apply",
                CloseButtonText   = "Cancel",
                XamlRoot          = Content.XamlRoot,
                RequestedTheme    = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(dialog);
            if (result != ContentDialogResult.Primary) return;

            int newStrength = (int)strengthSlider.Value;
            card.AutoHdrStrength = newStrength;
            gameNameService.AutoHdrStrengths[card.GameName] = newStrength;
            ViewModel.SaveSettingsPublic();

            if (!string.IsNullOrEmpty(exePath))
            {
                _ = Task.Run(() =>
                {
                    autoHdrService.Enable(exePath, newStrength);
                    CrashReporter.Log($"[AutoHdrConfigButton_Click] Applied Auto HDR strength={newStrength} for '{card.GameName}' exe='{exePath}'");
                });
            }

            card.ActionMessage = "✅ Auto HDR settings applied.";
            card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.AutoHdrConfigButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
