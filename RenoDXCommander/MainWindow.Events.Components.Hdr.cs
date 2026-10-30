// MainWindow.Events.Components.Hdr.cs — RTX HDR and Auto HDR config dialog handlers.
// Both are slider dialogs built with CogDialog in Apply / Cancel mode: values are only
// written (NVIDIA profile / registry) when the user clicks Apply.

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
    // ── Cog dialog helpers for the slider rows below ──

    /// <summary>Value readout shown to the right of a slider in a cog dialog row.</summary>
    private static TextBlock HdrCogValueText(string text, double width = 48) => new()
    {
        Text = text,
        FontSize = 12,
        Width = width,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
    };

    /// <summary>Small text aligned with the control column of a cog dialog row (warnings, readouts).</summary>
    private static TextBlock HdrCogControlText(string text, string brushKey, double opacity = 1.0) => new()
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UIFactory.Brush(brushKey),
        Opacity = opacity,
        Margin = new Thickness(CogDialog.LabelColumnWidth + CogDialog.ColumnGap, -4, 0, 0),
    };

    /// <summary>Horizontal row of compact preset buttons aligned with the control column.</summary>
    private static StackPanel HdrCogPresetRow(params Button[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(CogDialog.LabelColumnWidth + CogDialog.ColumnGap, -2, 0, 0),
        };
        foreach (var b in buttons) panel.Children.Add(b);
        return panel;
    }

    private static string HdrCogSigned(int val) => $"{(val > 0 ? "+" : "")}{val}";

    internal async void RtxHdrConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var card = (sender as FrameworkElement)?.Tag as GameCardViewModel
                    ?? (sender as Button)?.Tag as GameCardViewModel;
            if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

            var dlssPresetService = App.Services.GetRequiredService<DlssPresetService>();

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

            var cog = new CogDialog("RTX HDR", Content.XamlRoot);
            cog.UseApplyButton();
            cog.AddIntro("These settings are written to the game's NVIDIA driver profile when you click Apply.");

            cog.AddSection("Picture");

            // ── Peak Brightness ───────────────────────────────────────────────────
            var nitsSlider = new Slider { Minimum = 400, Maximum = 2000, StepFrequency = 10, Value = peakBrightnessDisplay };
            var nitsValue = HdrCogValueText($"{peakBrightnessDisplay} nits", 64);
            cog.AddRow("Peak Brightness", CogDialog.Inline(nitsSlider, nitsValue),
                "Set this to your display's peak brightness in nits.");
            var nitsWarning = HdrCogControlText("High values may look unnatural", ResourceKeys.AccentAmberBrush,
                peakBrightnessDisplay > 600 ? 1.0 : 0.0);
            cog.Body.Children.Add(nitsWarning);
            nitsSlider.ValueChanged += (s, ev) =>
            {
                nitsValue.Text = $"{(int)nitsSlider.Value} nits";
                nitsWarning.Opacity = (int)nitsSlider.Value > 600 ? 1.0 : 0.0;
            };

            // ── Contrast ──────────────────────────────────────────────────────────
            var contrastSlider = new Slider { Minimum = -100, Maximum = 100, StepFrequency = 1, Value = contrastDisplay };
            var contrastValue = HdrCogValueText(HdrCogSigned(contrastDisplay));
            contrastSlider.ValueChanged += (s, ev) => contrastValue.Text = HdrCogSigned((int)contrastSlider.Value);
            cog.AddRow("Contrast", CogDialog.Inline(contrastSlider, contrastValue),
                "0 is gamma 2.0 (default), +25 is gamma 2.2 and +50 is gamma 2.4. Higher values give deeper shadows.");

            // Gamma preset buttons
            var gammaButtons = new List<Button>();
            foreach (var (label, value) in new[] { ("Gamma 2.0", 0), ("Gamma 2.2", 25), ("Gamma 2.4", 50) })
            {
                var btn = CogDialog.StyledButton(label, CogButtonKind.Secondary, compact: true);
                var capturedValue = value;
                btn.Click += (s, ev) => contrastSlider.Value = capturedValue;
                gammaButtons.Add(btn);
            }
            cog.Body.Children.Add(HdrCogPresetRow(gammaButtons.ToArray()));

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
            static double GammaFor(int contrastVal)
            {
                double gamma = contrastVal switch { 25 => 2.2, 50 => 2.4, _ => 2.0 };
                // For non-preset contrast values interpolate gamma linearly between anchors
                if (contrastVal != 0 && contrastVal != 25 && contrastVal != 50)
                    gamma = 2.0 + (contrastVal / 100.0) * 0.4; // rough linear: 0→2.0, 100→2.4
                return gamma;
            }
            static int CalcAutoMiddleGrey(double peakNits, int contrastVal)
            {
                var pw = CalcPaperWhiteNits(peakNits);
                var mg = pw * Math.Pow(0.5, GammaFor(contrastVal));
                return Math.Clamp((int)Math.Round(mg), 10, 100);
            }
            // Perceived paperwhite from middle grey and gamma: paperwhite = midGrey / (0.5 ^ gamma)
            static int CalcPerceivedPaperwhite(int midGrey, int contrastVal)
                => (int)Math.Round(midGrey / Math.Pow(0.5, GammaFor(contrastVal)));

            int mgInitial = (currentMiddleGrey >= 10 && currentMiddleGrey <= 100) ? currentMiddleGrey : 50;

            var mgSlider = new Slider { Minimum = 10, Maximum = 100, StepFrequency = 1, Value = mgInitial };
            var mgValue = HdrCogValueText(mgInitial.ToString());
            cog.AddRow("Middle Grey", CogDialog.Inline(mgSlider, mgValue),
                "Sets how bright mid-tones and paper white look. Auto works it out from Peak Brightness and Contrast; the number buttons aim for that paper white in nits.");

            // Paper white readout + washed-out warning, aligned with the control column
            var mgInfoPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(CogDialog.LabelColumnWidth + CogDialog.ColumnGap, -4, 0, 0),
            };
            var mgPaperWhite = new TextBlock { FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
            var mgWarning = new TextBlock { Text = "High values may look washed out", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush) };
            mgInfoPanel.Children.Add(mgPaperWhite);
            mgInfoPanel.Children.Add(mgWarning);
            cog.Body.Children.Add(mgInfoPanel);

            void UpdateMiddleGreyReadout()
            {
                int mg = (int)mgSlider.Value;
                int pw = CalcPerceivedPaperwhite(mg, (int)contrastSlider.Value);
                mgValue.Text = mg.ToString();
                mgPaperWhite.Text = $"Paper white about {pw} nits";
                mgWarning.Opacity = pw > 203 ? 1.0 : 0.0;
            }
            UpdateMiddleGreyReadout();
            mgSlider.ValueChanged += (s, ev) => UpdateMiddleGreyReadout();
            // Also update when contrast changes (gamma affects perceived paperwhite)
            contrastSlider.ValueChanged += (s, ev) => UpdateMiddleGreyReadout();

            // Auto button + paper white preset buttons
            var mgButtons = new List<Button>();
            var autoMgBtn = CogDialog.StyledButton("Auto", CogButtonKind.Secondary, compact: true);
            ToolTipService.SetToolTip(autoMgBtn, "Calculate Middle Grey from Peak Brightness and Gamma using the ITU formula");
            autoMgBtn.Click += (s, ev) =>
            {
                mgSlider.Value = CalcAutoMiddleGrey((int)nitsSlider.Value, (int)contrastSlider.Value);
                UpdateMiddleGreyReadout();
            };
            mgButtons.Add(autoMgBtn);
            foreach (var presetPw in new[] { 100, 125, 150, 175, 200 })
            {
                var presetBtn = CogDialog.StyledButton(presetPw.ToString(), CogButtonKind.Secondary, compact: true);
                var capturedPw = presetPw;
                ToolTipService.SetToolTip(presetBtn, $"Set Middle Grey to achieve about {presetPw} nits paper white");
                presetBtn.Click += (s, ev) =>
                {
                    // Reverse formula: midGrey = paperwhite × (0.5 ^ gamma)
                    var gamma = GammaFor((int)contrastSlider.Value);
                    mgSlider.Value = Math.Clamp((int)Math.Round(capturedPw * Math.Pow(0.5, gamma)), 10, 100);
                    UpdateMiddleGreyReadout();
                };
                mgButtons.Add(presetBtn);
            }
            cog.Body.Children.Add(HdrCogPresetRow(mgButtons.ToArray()));

            // ── Saturation ────────────────────────────────────────────────────────
            var satSlider = new Slider { Minimum = -100, Maximum = 100, StepFrequency = 1, Value = saturationDisplay };
            var satValue = HdrCogValueText(HdrCogSigned(saturationDisplay));
            satSlider.ValueChanged += (s, ev) => satValue.Text = HdrCogSigned((int)satSlider.Value);
            cog.AddRow("Saturation", CogDialog.Inline(satSlider, satValue),
                "-25 gives neutral saturation. Higher values make colours more vivid, lower values more muted.");

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
            int selectedDbIndex = 0;
            for (int i = 0; i < debandingOptions.Length; i++)
                if (currentDebanding == (int)debandingOptions[i].value) selectedDbIndex = i;
            var debandingCombo = CogDialog.MakeCombo(debandingOptions.Select(o => o.name).ToList(), selectedDbIndex, isAdmin);
            debandingCombo.Opacity = isAdmin ? 1.0 : CogDialog.DisabledOpacity;
            cog.AddRow("Debanding", debandingCombo,
                "Smooths colour banding in gradients. The Indicator options add an on-screen indicator, useful for testing.");
            if (!isAdmin)
                cog.Body.Children.Add(HdrCogControlText("Requires admin mode to change.", ResourceKeys.AccentAmberBrush));

            // ── Default preset buttons ────────────────────────────────────────────
            var defaultsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "rtx_hdr_defaults.json");

            // Load current defaults (if any) to show whether "Set Default" is available
            bool hasDefaults = File.Exists(defaultsPath);

            cog.AddSection("Defaults");
            cog.AddNote("Save as Default stores these values as your RTX HDR preset. Set Default loads that preset into the sliders; click Apply to write it.");

            var saveDefaultBtn = CogDialog.StyledButton("Save as Default", CogButtonKind.Secondary);
            ToolTipService.SetToolTip(saveDefaultBtn, "Save current slider values as your default RTX HDR preset");
            var setDefaultBtn = CogDialog.StyledButton("Set Default", CogButtonKind.Secondary);
            setDefaultBtn.IsEnabled = hasDefaults;
            ToolTipService.SetToolTip(setDefaultBtn, hasDefaults ? "Apply your saved default preset to the sliders" : "No default saved yet. Use Save as Default first.");

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
                        ["Debanding"]      = (int)debandingOptions[Math.Max(0, debandingCombo.SelectedIndex)].value,
                    };
                    Directory.CreateDirectory(Path.GetDirectoryName(defaultsPath)!);
                    File.WriteAllText(defaultsPath, JsonSerializer.Serialize(defaults,
                        new JsonSerializerOptions { WriteIndented = true }));
                    setDefaultBtn.IsEnabled = true;
                    ToolTipService.SetToolTip(setDefaultBtn, "Apply your saved default preset to the sliders");
                    cog.SetSuccess("Saved as your default RTX HDR preset.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    CrashReporter.Log($"[RtxHdrConfigButton_Click] Failed to save defaults — {ex.Message}");
                    cog.SetError($"Couldn't save the default preset: {ex.Message}");
                }
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
                    UpdateMiddleGreyReadout();
                    cog.SetStatus("Default preset loaded. Click Apply to write it.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                              or InvalidOperationException or FormatException)
                {
                    CrashReporter.Log($"[RtxHdrConfigButton_Click] Failed to apply defaults — {ex.Message}");
                    cog.SetError($"Couldn't load the default preset: {ex.Message}");
                }
            };

            cog.AddButtons(saveDefaultBtn, setDefaultBtn);

            // ── Dialog ────────────────────────────────────────────────────────────
            var result = await cog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            // Write all values on a background thread — NVAPI profile writes are kernel-mode calls
            var peakNits = (uint)nitsSlider.Value;
            var contrastStored = (uint)(100 + (int)contrastSlider.Value);
            var satStored = (uint)(100 + (int)satSlider.Value);
            var middleGrey = (uint)mgSlider.Value;
            var debanding = debandingOptions[Math.Max(0, debandingCombo.SelectedIndex)].value;

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

            var cog = new CogDialog("Auto HDR", Content.XamlRoot);
            cog.UseApplyButton();
            cog.AddIntro("Windows Auto HDR settings for this game. Changes take effect on the next game launch.");

            // ── Brightness / Intensity ────────────────────────────────────────────
            var strengthSlider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Value = currentStrength };
            var strengthValue = HdrCogValueText(currentStrength.ToString());
            cog.AddRow("Brightness", CogDialog.Inline(strengthSlider, strengthValue),
                "How much Auto HDR brightens the game. 0 is no boost, 50 is the default and 100 is the maximum (about 1000 nits peak).");
            var strengthWarning = HdrCogControlText("High values may look unnatural", ResourceKeys.AccentAmberBrush,
                currentStrength > 75 ? 1.0 : 0.0);
            cog.Body.Children.Add(strengthWarning);
            strengthSlider.ValueChanged += (s, ev) =>
            {
                strengthValue.Text = ((int)strengthSlider.Value).ToString();
                strengthWarning.Opacity = (int)strengthSlider.Value > 75 ? 1.0 : 0.0;
            };

            // ── Preset buttons ────────────────────────────────────────────────────
            var presetButtons = new List<Button>();
            foreach (var (label, value) in new[] { ("Low (25)", 25), ("Default (50)", 50), ("High (75)", 75), ("Max (100)", 100) })
            {
                var btn = CogDialog.StyledButton(label, CogButtonKind.Secondary, compact: true);
                var capturedValue = value;
                btn.Click += (s, ev) => strengthSlider.Value = capturedValue;
                presetButtons.Add(btn);
            }
            cog.Body.Children.Add(HdrCogPresetRow(presetButtons.ToArray()));

            if (string.IsNullOrEmpty(exePath))
                cog.AddWarning("Couldn't find the game's exe, so this setting won't be written to the Windows registry.");
            else
                cog.AddNote($"Exe: {exePath}");

            var result = await cog.ShowAsync();
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
