// MainWindow.Events.Components.OptiScaler.cs — OptiScaler cog dialog handler (OsCogButton_Click).

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
    private async void OsCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            var content = new StackPanel { Spacing = 10 };

            // ── Helper: build a 4-column settings grid (label | combo | label | combo) ──
            // Returns the grid; use AddRow() to populate it.
            Grid MakeSettingsGrid()
            {
                var g = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
                return g;
            }
            void AddRow(Grid g, int row, string leftLabel, ComboBox leftCombo, string? rightLabel = null, ComboBox? rightCombo = null)
            {
                while (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lbl1 = new TextBlock { Text = leftLabel, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(lbl1, row); Grid.SetColumn(lbl1, 0); g.Children.Add(lbl1);
                leftCombo.FontSize = 12; leftCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetRow(leftCombo, row); Grid.SetColumn(leftCombo, 1); g.Children.Add(leftCombo);
                if (rightLabel != null && rightCombo != null)
                {
                    var lbl2 = new TextBlock { Text = rightLabel, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(lbl2, row); Grid.SetColumn(lbl2, 2); g.Children.Add(lbl2);
                    rightCombo.FontSize = 12; rightCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
                    Grid.SetRow(rightCombo, row); Grid.SetColumn(rightCombo, 3); g.Children.Add(rightCombo);
                }
            }

            Border MakeSeparator() => new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 4, 0, 4) };

            // ── Unified grid: version row + all nightly rows share the same 4-col layout ──
            // This guarantees the Nightly combo aligns perfectly with Deploy Streamline etc.
            var unifiedGrid = MakeSettingsGrid();

            // Read current FramerateLimit from the game's OptiScaler.ini
            var vrrPresetsOs = new (float Fps, string Label)[]
            {
                (59f,  "59 (60Hz VRR)"),
                (73f,  "73 (75Hz VRR)"),
                (97f,  "97 (100Hz VRR)"),
                (116f, "116 (120Hz VRR)"),
                (138f, "138 (144Hz VRR)"),
                (157f, "157 (165Hz VRR)"),
                (171f, "171 (180Hz VRR)"),
                (189f, "189 (200Hz VRR)"),
                (224f, "224 (240Hz VRR)"),
                (258f, "258 (280Hz VRR)"),
                (275f, "275 (300Hz VRR)"),
                (324f, "324 (360Hz VRR)"),
                (416f, "416 (480Hz VRR)"),
                (431f, "431 (500Hz VRR)"),
            };
            var fpsLabels = new[] { "Off" }.Concat(vrrPresetsOs.Select(p => p.Label)).ToArray();
            var fpsLimitCombo = new ComboBox { ItemsSource = fpsLabels, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTipService.SetToolTip(fpsLimitCombo, "Framerate limit using Reflex whenever possible. 'Off' disables the limit.");

            // Read current value from OptiScaler.ini
            string currentFpsStr = "Off";
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                var iniPath = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
                if (File.Exists(iniPath))
                {
                    var lines = File.ReadAllLines(iniPath);
                    var fpsLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("FramerateLimit", StringComparison.OrdinalIgnoreCase)
                        && l.Contains("=") && !l.TrimStart().StartsWith(";"));
                    if (fpsLine != null)
                    {
                        var val = fpsLine.Split('=', 2)[1].Trim();
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fpsVal) && fpsVal > 0)
                        {
                            var match = vrrPresetsOs.FirstOrDefault(p => Math.Abs(p.Fps - fpsVal) < 1f);
                            currentFpsStr = match.Label ?? "Off";
                        }
                    }
                }
            }
            fpsLimitCombo.SelectedItem = currentFpsStr;

            AddRow(unifiedGrid, 0, "OptiScaler Version",
                new ComboBox { ItemsSource = new[] { "Stable", "Nightly", "DLSS NR", "F5 DLSSNR" }, SelectedItem = ViewModel.GetOsVariant(card.GameName, card.Source ?? "") == "DlssNr" ? "DLSS NR" : ViewModel.GetOsVariant(card.GameName, card.Source ?? "") == "F5" ? "F5 DLSSNR" : ViewModel.GetOsVariant(card.GameName, card.Source ?? ""), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch },
                "Framerate Limit", fpsLimitCombo);
            // Grab the variant combo we just added
            var variantCombo = (ComboBox)unifiedGrid.Children.Cast<UIElement>().Where(c => c is ComboBox).First();
            ToolTipService.SetToolTip(variantCombo, "Stable: official release. Nightly: daily build. DLSS NR: Neural Rendering fork with multi-pass NR support. F5 DLSSNR: F5 multipass NR fork with bundled forwarder.");

            fpsLimitCombo.SelectionChanged += (s, ev) =>
            {
                var sel = fpsLimitCombo.SelectedItem as string;
                if (sel == null || !card.IsOsInstalled || string.IsNullOrEmpty(card.InstallPath)) return;
                if (sel == "Off")
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit", "0");
                else
                {
                    var match = vrrPresetsOs.FirstOrDefault(p => p.Label == sel);
                    if (match.Label != null)
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit", match.Fps.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
                }
            };

            // ── Upscaler row (row 1): API selector | upscaler value ──────────────────
            // Left combo: which API to configure. Right combo: upscaler for that API.
            static string[] GetUpscalerOptions(string api) => api switch
            {
                "DX12"   => new[] { "Auto (Default)", "DLSS", "XeSS", "FSR 2.1", "FSR 2.2", "FSR 3.x / FFX" },
                "Vulkan" => new[] { "Auto (Default)", "DLSS", "FSR 2.1", "FSR 2.2", "FSR 3.x / FFX", "XeSS", "FSR2.1 on DX12", "FSR3 on DX12" },
                _        => new[] { "Auto (Default)", "DLSS", "FSR 2.2", "FSR 3.1", "XeSS (Arc only)", "XeSS on DX12", "FSR2.1 on DX12", "FSR2.2 on DX12", "FSR3 on DX12" }, // DX11
            };
            static string UpscalerOptionToIni(string api, string display) => api switch
            {
                "DX12"   => display switch { "DLSS" => "dlss", "XeSS" => "xess", "FSR 2.1" => "fsr21", "FSR 2.2" => "fsr22", "FSR 3.x / FFX" => "ffx", _ => "auto" },
                "Vulkan" => display switch { "DLSS" => "dlss", "FSR 2.1" => "fsr21", "FSR 2.2" => "fsr22", "FSR 3.x / FFX" => "ffx", "XeSS" => "xess", "FSR2.1 on DX12" => "fsr21_12", "FSR3 on DX12" => "ffx_12", _ => "auto" },
                _        => display switch { "DLSS" => "dlss", "FSR 2.2" => "fsr22", "FSR 3.1" => "fsr31", "XeSS (Arc only)" => "xess", "XeSS on DX12" => "xess_12", "FSR2.1 on DX12" => "fsr21_12", "FSR2.2 on DX12" => "fsr22_12", "FSR3 on DX12" => "ffx_12", _ => "auto" },
            };
            static string IniToUpscalerOption(string api, string ini) => api switch
            {
                "DX12"   => ini switch { "dlss" => "DLSS", "xess" => "XeSS", "fsr21" => "FSR 2.1", "fsr22" => "FSR 2.2", "ffx" => "FSR 3.x / FFX", _ => "Auto (Default)" },
                "Vulkan" => ini switch { "dlss" => "DLSS", "fsr21" => "FSR 2.1", "fsr22" => "FSR 2.2", "ffx" => "FSR 3.x / FFX", "xess" => "XeSS", "fsr21_12" => "FSR2.1 on DX12", "ffx_12" => "FSR3 on DX12", _ => "Auto (Default)" },
                _        => ini switch { "dlss" => "DLSS", "fsr22" => "FSR 2.2", "fsr31" => "FSR 3.1", "xess" => "XeSS (Arc only)", "xess_12" => "XeSS on DX12", "fsr21_12" => "FSR2.1 on DX12", "fsr22_12" => "FSR2.2 on DX12", "ffx_12" => "FSR3 on DX12", _ => "Auto (Default)" },
            };
            static string ApiToIniKey(string api) => api switch { "DX12" => "Dx12Upscaler", "Vulkan" => "VulkanUpscaler", _ => "Dx11Upscaler" };

            // Read current upscaler values from OptiScaler.ini
            string ReadUpscalerIni(string key)
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return "auto";
                var iniPath = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
                if (!File.Exists(iniPath)) return "auto";
                var line = File.ReadAllLines(iniPath).FirstOrDefault(l =>
                    l.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                    l.Contains("=") && !l.TrimStart().StartsWith(";"));
                if (line == null) return "auto";
                return line.Split('=', 2)[1].Trim().ToLowerInvariant();
            }

            // Default the API combo to the game's detected graphics API
            var apiDefault = card.GraphicsApi switch
            {
                Models.GraphicsApiType.DirectX12 => "DX12",
                Models.GraphicsApiType.Vulkan    => "Vulkan",
                _                                => "DX11",
            };

            var apiCombo = new ComboBox { ItemsSource = new[] { "DX11", "DX12", "Vulkan" }, SelectedItem = apiDefault, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            var apiUpscalerCombo = new ComboBox { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTipService.SetToolTip(apiCombo, "Select which graphics API's upscaler to configure.");
            ToolTipService.SetToolTip(apiUpscalerCombo, "Upscaler for the selected API. 'Auto' lets OptiScaler choose based on your GPU.");

            // Populate upscaler combo for initial API and select current INI value
            void RefreshUpscalerCombo(string api)
            {
                bool upscalerComboInitializing = true;
                apiUpscalerCombo.ItemsSource = GetUpscalerOptions(api);
                var currentIni = ReadUpscalerIni(ApiToIniKey(api));
                apiUpscalerCombo.SelectedItem = IniToUpscalerOption(api, currentIni);
                upscalerComboInitializing = false;
                _ = upscalerComboInitializing; // suppress unused warning
            }
            RefreshUpscalerCombo(apiDefault);

            // ── Nightly Build selector (row 1, nightly only — between Version and Upscaler) ──
            if (ViewModel.GetOsVariant(card.GameName, card.Source ?? "") == "Nightly")
            {
                var currentBuild = ViewModel.GetOsNightlyBuild(card.GameName, card.Source ?? "");
                var availableBuilds = _optiScalerService.AvailableNightlyBuilds;

                var buildItems = new List<string> { "Latest (auto-update)" };
                foreach (var b in availableBuilds)
                {
                    if (b.Length == 8 && b.All(char.IsDigit))
                        buildItems.Add($"{b[..4]}-{b[4..6]}-{b[6..8]}");
                }
                if (availableBuilds.Count == 0)
                    buildItems.Add("(fetching builds...)");

                bool isInstalled = card.IsOsInstalled;
                var buildCombo = new ComboBox
                {
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = !isInstalled,
                    Opacity = isInstalled ? 0.45 : 1.0,
                };
                var tooltip = isInstalled
                    ? "Uninstall OptiScaler first to change the build version."
                    : "Pin to a specific nightly build. Pinned games are excluded from auto-update. 'Latest' always tracks the newest build.";
                ToolTipService.SetToolTip(buildCombo, tooltip);
                buildCombo.ItemsSource = buildItems;

                string selectedBuildItem = "Latest (auto-update)";
                if (!string.IsNullOrEmpty(currentBuild) && currentBuild.Length == 8 && currentBuild.All(char.IsDigit))
                    selectedBuildItem = $"{currentBuild[..4]}-{currentBuild[4..6]}-{currentBuild[6..8]}";
                buildCombo.SelectedItem = buildItems.Contains(selectedBuildItem) ? selectedBuildItem : "Latest (auto-update)";

                bool buildComboInitializing = true;
                AddRow(unifiedGrid, 1, "Nightly Build", buildCombo, null, null);
                buildComboInitializing = false;

                buildCombo.SelectionChanged += async (s, ev) =>
                {
                    if (buildComboInitializing) return;
                    var sel = buildCombo.SelectedItem as string ?? "Latest (auto-update)";
                    if (sel == "Latest (auto-update)" || sel == "(fetching builds...)")
                    {
                        ViewModel.SetOsNightlyBuild(card.GameName, null, card.Source ?? "");
                        return;
                    }
                    var buildDate = sel.Replace("-", "");
                    if (buildDate.Length != 8 || !buildDate.All(char.IsDigit)) return;

                    ViewModel.SetOsNightlyBuild(card.GameName, buildDate, card.Source ?? "");
                    CrashReporter.Log($"[OsCog] Pinned nightly build {buildDate} for '{card.GameName}'");

                    if (!_optiScalerService.IsNightlyBuildStaged(buildDate))
                    {
                        CrashReporter.Log($"[OsCog] Build {buildDate} not staged — downloading on demand");
                        await _optiScalerService.EnsureNightlyBuildStagingAsync(buildDate);
                    }
                };
            }

            bool apiComboInitializing = true;
            AddRow(unifiedGrid, 2, "Upscaler API", apiCombo, "Upscaler", apiUpscalerCombo);
            apiComboInitializing = false;

            apiCombo.SelectionChanged += (s, ev) =>
            {
                if (apiComboInitializing) return;
                RefreshUpscalerCombo(apiCombo.SelectedItem as string ?? "DX11");
            };
            apiUpscalerCombo.SelectionChanged += (s, ev) =>
            {
                if (apiComboInitializing) return;
                if (apiUpscalerCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                var api = apiCombo.SelectedItem as string ?? "DX11";
                var iniVal = UpscalerOptionToIni(api, sel);
                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Upscalers", ApiToIniKey(api), iniVal);
            };

            content.Children.Add(unifiedGrid);

            ContentDialog? osCogDialog = null;
            bool variantComboInitializing = true;
            variantCombo.SelectionChanged += (s, ev) =>
            {
                if (variantComboInitializing) return;
                var selected = variantCombo.SelectedItem as string ?? "Stable";
                // Map display name to internal value
                var internalValue = selected switch { "DLSS NR" => "DlssNr", "F5 DLSSNR" => "F5", "Stable" => null, _ => selected };
                ViewModel.SetOsVariant(card.GameName, internalValue, card.Source ?? "");
                if (card.IsOsInstalled && !string.IsNullOrEmpty(card.InstallPath))
                {
                    try { _optiScalerService.Uninstall(card); card.NotifyAll(); }
                    catch (Exception ex) { CrashReporter.Log($"[OsCog] Uninstall on channel switch — {ex.Message}"); }
                }
                // Use DispatcherTimer for the delay — TryEnqueue(async lambda) detaches after the first
                // await, causing OsCogButton_Click to build WinUI elements off the UI thread (freeze risk).
                // DispatcherTimer.Tick always fires on the UI thread, so the rebuild is guaranteed safe.
                osCogDialog?.Hide();
                var rebuildTimer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                rebuildTimer1.Tick += (_, _) => { rebuildTimer1.Stop(); OsCogButton_Click(sender, e); };
                rebuildTimer1.Start();
            };

            bool isNightly = ViewModel.GetOsVariant(card.GameName, card.Source ?? "") == "Nightly";
            bool isDlssNr  = ViewModel.GetOsVariant(card.GameName, card.Source ?? "") == "DlssNr";
            bool isF5      = ViewModel.GetOsVariant(card.GameName, card.Source ?? "") == "F5";

            // These are declared at method scope so the presets closures (built later, outside the
            // nightly block) can capture them. They are only non-null when isNightly == true.
            ComboBox? fgInputCombo   = null;
            ComboBox? fgOutputCombo  = null;
            ComboBox? fgNvngxCombo   = null;
            ComboBox? combinedCombo  = null;
            ComboBox? srPresetCombo  = null;
            ComboBox? rrPresetCombo  = null;
            ComboBox? rsCombo        = null;
            ComboBox? flipCombo      = null;
            ComboBox? hudFixCombo    = null;
            Func<string, string>? FgInputToIni  = null;
            Func<string, string>? FgOutputToIni = null;
            Func<string, string>? FgNvngxToIni  = null;
            (string Item1, string Item2)[]? srPresetMap   = null;
            (string Item1, string Item2)[]? rrPresetMap   = null;
            (string Item1, float Item2)[]?  renderScaleMap = null;
            // NR combo variables — only non-null when isDlssNr == true
            ComboBox? nrRuntimeCombo      = null;
            ComboBox? nrEnabledCombo      = null;
            ComboBox? nrRunBeforeSrCombo  = null;
            ComboBox? nrPassesCombo       = null;
            ComboBox? nrWorkingScaleCombo = null;
            ComboBox? nrFinishedPicCombo  = null;
            (string Item1, string Item2)[]? nrScaleMap = null;
            // Nightly-only combos also hoisted to method scope for preset capture
            ComboBox? fgEnabledCombo      = null;
            ComboBox? forceReflexCombo    = null;
            ComboBox? reflexMarkersCombo  = null;

            if (isNightly || isDlssNr || isF5)
            {
                // ── INI value converters ───────────────────────────────────────
                FgInputToIni  = (string d) => d switch { "OptiFG (Upscaler)" => "upscaler", "DLSSG via Streamline" => "dlssg", "DLSSG via Nvngx" => "nvngxfg", "FSR 3.1 FG" => "fsrfg", "FSR 3.0 FG" => "fsrfg30", "XeFG" => "xefg", _ => "auto" };
                FgOutputToIni = (string d) => d switch { "FSR FG" => "fsrfg", "DLSSG" => "dlssg", "XeFG" => "xefg", _ => "auto" };
                FgNvngxToIni  = (string d) => d switch { "Nukem's" => "Nukems", "Enabler" => "Arturs", "FSR 3/4 FG" => "FFX", "Combo" => "Combo", _ => "None" };
                string IniToFgInput(string v) => v switch { "upscaler" => "OptiFG (Upscaler)", "dlssg" => "DLSSG via Streamline", "nvngxfg" => "DLSSG via Nvngx", "fsrfg" => "FSR 3.1 FG", "fsrfg30" => "FSR 3.0 FG", "xefg" => "XeFG", _ => "Auto (Default)" };
                string IniToFgOutput(string v) => v switch { "fsrfg" => "FSR FG", "dlssg" => "DLSSG", "xefg" => "XeFG", _ => "Auto (Default)" };
                string IniToFgNvngx(string v) => v switch { "Nukems" => "Nukem's", "Arturs" => "Enabler", "FFX" => "FSR 3/4 FG", "Combo" => "Combo", _ => "None (Real DLSSG)" };            // Separator row between version/build and nightly settings (spans all 4 columns)
                unifiedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var versionSep = MakeSeparator();
                Grid.SetRow(versionSep, 3); Grid.SetColumn(versionSep, 0); Grid.SetColumnSpan(versionSep, 4);
                unifiedGrid.Children.Add(versionSep);

                // Section heading: Frame Generation Settings
                unifiedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var fgHeading = new TextBlock { Text = "Frame Generation Settings", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 2, 0, 0) };
                Grid.SetRow(fgHeading, 4); Grid.SetColumn(fgHeading, 0); Grid.SetColumnSpan(fgHeading, 4);
                unifiedGrid.Children.Add(fgHeading);

                // Row 4: FG Enabled — master on/off, first option in the FG section
                var currentFgEnabled = ReadOsIniValue("FrameGen", "Enabled");
                fgEnabledCombo = new ComboBox
                {
                    ItemsSource = new[] { "Auto (false)", "True" },
                    SelectedItem = currentFgEnabled.Equals("true", StringComparison.OrdinalIgnoreCase) ? "True" : "Auto (false)",
                };
                ToolTipService.SetToolTip(fgEnabledCombo,
                    "Enables Frame Generation. Default (auto) is false — set to True to enable.");
                AddRow(unifiedGrid, 5, "FG Enabled", fgEnabledCombo, null, null);

                // All nightly rows go into the same unifiedGrid so columns align with the version row above
                // Row 2: Streamline/DLSS Enabler (combined) | Streamline Version
                var dlssStreamlineSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
                var slVersions = dlssStreamlineSvc.StreamlineVersions;
                var persistedSlVersion = ViewModel.GetOsStreamlineVersion(card.GameName, card.Source ?? "");
                string slVersionDefault;
                if (!string.IsNullOrEmpty(persistedSlVersion) && slVersions.Contains(persistedSlVersion))
                    slVersionDefault = persistedSlVersion;
                else if (slVersions.Contains("2.12.0"))
                    slVersionDefault = "2.12.0";
                else
                    slVersionDefault = slVersions.Count > 0 ? slVersions[0] : "2.12.0";

                bool combinedOn = ViewModel.GetOsDeployStreamline(card.GameName, card.Source ?? "");
                combinedCombo = new ComboBox { ItemsSource = new[] { "No", "Yes" }, SelectedItem = combinedOn ? "Yes" : "No" };
                ToolTipService.SetToolTip(combinedCombo, "Deploys Streamline and DLSS Enabler to the game's OptiScaler folder. Required for DLSS Frame Generation with OptiScaler.");
                var slVersionCombo = new ComboBox { ItemsSource = slVersions.Count > 0 ? (IEnumerable<string>)slVersions : new[] { slVersionDefault }, SelectedItem = slVersionDefault, IsEnabled = combinedOn };
                AddRow(unifiedGrid, 6, "Streamline/DLSS Enabler", combinedCombo, "Streamline Version", slVersionCombo);

                // Row 4: FG Input (left) | HUD Fix (right)
                fgInputCombo = new ComboBox { ItemsSource = new[] { "Auto (Default)", "OptiFG (Upscaler)", "DLSSG via Streamline", "DLSSG via Nvngx", "FSR 3.1 FG", "FSR 3.0 FG", "XeFG" }, SelectedItem = IniToFgInput(ViewModel.GetOsFgInput(card.GameName, card.Source ?? "")) };

                // Read HUD Fix from OptiScaler.ini
                string hudFixCurrent = "auto";
                if (!string.IsNullOrEmpty(card.InstallPath))
                {
                    var hudIniPath = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
                    if (File.Exists(hudIniPath))
                    {
                        bool inOptiFg = false;
                        foreach (var hudLine in File.ReadAllLines(hudIniPath))
                        {
                            var t = hudLine.Trim();
                            if (t.StartsWith("[")) inOptiFg = t.Equals("[OptiFG]", StringComparison.OrdinalIgnoreCase);
                            else if (inOptiFg && !t.StartsWith(";") && (t.StartsWith("HUDFix=", StringComparison.OrdinalIgnoreCase) || t.StartsWith("HUDFix =", StringComparison.OrdinalIgnoreCase)))
                            { hudFixCurrent = t.Split('=', 2)[1].Trim(); break; }
                        }
                    }
                }
                var hudFixSelected = hudFixCurrent.Equals("true", StringComparison.OrdinalIgnoreCase) ? "On"
                                   : hudFixCurrent.Equals("false", StringComparison.OrdinalIgnoreCase) ? "Off"
                                   : "Default";
                hudFixCombo = new ComboBox { ItemsSource = new[] { "Default", "On", "Off" }, SelectedItem = hudFixSelected };
                ToolTipService.SetToolTip(hudFixCombo!, "HUD Fix: enables hudless resource tracking for Frame Generation. On = HUDFix=true in [OptiFG].");

                AddRow(unifiedGrid, 7, "FG Input", fgInputCombo!, "HUD Fix", hudFixCombo!);

                // Row 5: FG Output (left) | FG Nvngx Override (right)
                fgOutputCombo = new ComboBox { ItemsSource = new[] { "Auto (Default)", "FSR FG", "DLSSG", "XeFG" }, SelectedItem = IniToFgOutput(ViewModel.GetOsFgOutput(card.GameName, card.Source ?? "")) };
                bool enablerAvail = true; // Always allow Enabler — requires Streamline deployed, user responsibility
                var nvngxItems = new List<object> { "None (Real DLSSG)", "Nukem's", new ComboBoxItem { Content = "Enabler", IsEnabled = enablerAvail }, "FSR 3/4 FG", "Combo" };
                var currentNvngxDisplay = IniToFgNvngx(ViewModel.GetOsFgNvngxReplacement(card.GameName, card.Source ?? ""));
                object? nvngxSelected = nvngxItems.FirstOrDefault(i => i is ComboBoxItem cb ? (cb.Content as string) == currentNvngxDisplay : (i as string) == currentNvngxDisplay) ?? nvngxItems[0];
                fgNvngxCombo = new ComboBox { ItemsSource = nvngxItems, SelectedItem = nvngxSelected };
                ToolTipService.SetToolTip(fgNvngxCombo!, "Only relevant when FG Output = DLSSG. Enabler requires Deploy Streamline + Deploy DLSS Enabler.");
                AddRow(unifiedGrid, 8, "FG Output", fgOutputCombo!, "FG Nvngx Override", fgNvngxCombo!);

                bool fgOutputIsDlssg = fgOutputCombo!.SelectedItem as string == "DLSSG";
                fgNvngxCombo!.Opacity = fgOutputIsDlssg ? 1.0 : 0.35;
                fgNvngxCombo!.IsHitTestVisible = fgOutputIsDlssg;
                fgNvngxCombo!.IsEnabled = fgOutputIsDlssg;

                // Row 8: ForceReflex (left) | UseGamesReflexMarkers (right)
                // Read current values directly from OptiScaler.ini
                // ForceReflex lives in [fakenvapi], UseGamesReflexMarkers in [DLSSG]
                string ReadOsIniValue(string sectionName, string key)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) return "";
                    var p = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
                    if (!File.Exists(p)) return "";
                    bool inSec = false;
                    foreach (var line in File.ReadAllLines(p))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("[")) inSec = t.Equals($"[{sectionName}]", StringComparison.OrdinalIgnoreCase);
                        else if (inSec && !t.StartsWith(";"))
                        {
                            var pfx1 = key + "="; var pfx2 = key + " =";
                            if (t.StartsWith(pfx1, StringComparison.OrdinalIgnoreCase)) return t.Substring(pfx1.Length).Trim();
                            if (t.StartsWith(pfx2, StringComparison.OrdinalIgnoreCase)) return t.Substring(pfx2.Length).Trim();
                        }
                    }
                    return "";
                }
                var currentForceReflex = ReadOsIniValue("fakenvapi", "ForceReflex");
                forceReflexCombo = new ComboBox
                {
                    ItemsSource = new[] { "Auto (0)", "Force Disable (1)", "Force Enable (2)" },
                    SelectedItem = currentForceReflex switch { "1" => "Force Disable (1)", "2" => "Force Enable (2)", _ => "Auto (0)" },
                };
                ToolTipService.SetToolTip(forceReflexCombo,
                    "ForceReflex: controls Reflex state when using DLSS FG.\n0 = follow in-game setting (default), 1 = force disable, 2 = force enable.");

                var currentReflexMarkers = ReadOsIniValue("DLSSG", "UseGamesReflexMarkers");
                reflexMarkersCombo = new ComboBox
                {
                    ItemsSource = new[] { "True", "False" },
                    SelectedItem = currentReflexMarkers.Equals("false", StringComparison.OrdinalIgnoreCase) ? "False" : "True",
                };
                ToolTipService.SetToolTip(reflexMarkersCombo,
                    "UseGamesReflexMarkers: whether to use the game's Reflex markers for Frame Generation timing. Default is true.");

                AddRow(unifiedGrid, 9, "Force Reflex", forceReflexCombo, "Use Games Reflex Markers", reflexMarkersCombo);

                // ── Wire handlers ──────────────────────────────────────────────
                combinedCombo!.SelectionChanged += (s, ev) =>
                {
                    bool on = combinedCombo.SelectedItem as string == "Yes";
                    ViewModel.SetOsDeployStreamline(card.GameName, on, card.Source ?? "");
                    ViewModel.SetOsDeployDlssEnabler(card.GameName, on, card.Source ?? "");
                    slVersionCombo.IsEnabled = on;
                    if (!string.IsNullOrEmpty(card.InstallPath))
                    {
                        try
                        {
                            if (on)
                            {
                                _optiScalerService.DeployStreamlineToGame(card.InstallPath);
                                var d = Path.Combine(card.InstallPath, "OptiScaler");
                                _ = _dlssEnablerService.InstallAsync(d);
                                // Swap to the selected version immediately so the combo reflects reality
                                var selVer = slVersionCombo.SelectedItem as string;
                                if (!string.IsNullOrEmpty(selVer))
                                {
                                    var slDir = Path.Combine(card.InstallPath, "OptiScaler", "Streamline");
                                    App.Services.GetRequiredService<IDlssStreamlineService>()
                                        .SwapStreamlineAsync(slDir, selVer).SafeFireAndForget("OsCog.SwapStreamline");
                                }
                            }
                            else
                            {
                                _optiScalerService.RemoveStreamlineFromGame(card.InstallPath);
                                var d = Path.Combine(card.InstallPath, "OptiScaler");
                                _dlssEnablerService.Uninstall(d);
                            }
                        }
                        catch (Exception ex) { CrashReporter.Log($"[OsCog] Streamline/DLSS Enabler — {ex.Message}"); }
                    }
                };
                slVersionCombo.SelectionChanged += (s, ev) =>
                {
                    if (slVersionCombo.SelectedItem is not string selectedVer) return;
                    ViewModel.SetOsStreamlineVersion(card.GameName, selectedVer, card.Source ?? "");
                    if (ViewModel.GetOsDeployStreamline(card.GameName, card.Source ?? "") && !string.IsNullOrEmpty(card.InstallPath))
                    {
                        var slFolder = Path.Combine(card.InstallPath, "OptiScaler", "Streamline");
                        dlssStreamlineSvc.SwapStreamlineAsync(slFolder, selectedVer).SafeFireAndForget("OsCog.SwapStreamline");
                    }
                };
                fgInputCombo!.SelectionChanged += (s, ev) =>
                {
                    if (fgInputCombo.SelectedItem is not string sel) return;
                    var v = FgInputToIni!(sel); ViewModel.SetOsFgInput(card.GameName, v, card.Source ?? "");
                    if (!string.IsNullOrEmpty(card.InstallPath)) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "FGInput", v);
                };
                hudFixCombo!.SelectionChanged += (s, ev) =>
                {
                    if (hudFixCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                    var val = sel == "On" ? "true" : sel == "Off" ? "false" : "auto";
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "OptiFG", "HUDFix", val);
                };
                fgOutputCombo!.SelectionChanged += (s, ev) =>
                {
                    if (fgOutputCombo.SelectedItem is not string sel) return;
                    var v = FgOutputToIni!(sel); ViewModel.SetOsFgOutput(card.GameName, v, card.Source ?? "");
                    if (!string.IsNullOrEmpty(card.InstallPath)) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "FGOutput", v);
                    bool isDlssg = sel == "DLSSG";
                    fgNvngxCombo!.Opacity = isDlssg ? 1.0 : 0.35;
                    fgNvngxCombo!.IsHitTestVisible = isDlssg;
                    fgNvngxCombo!.IsEnabled = isDlssg;
                };
                fgNvngxCombo!.SelectionChanged += (s, ev) =>
                {
                    string? display = fgNvngxCombo.SelectedItem is ComboBoxItem cb ? cb.Content as string : fgNvngxCombo.SelectedItem as string;
                    if (display == null) return;
                    var v = FgNvngxToIni!(display); ViewModel.SetOsFgNvngxReplacement(card.GameName, v, card.Source ?? "");
                    if (!string.IsNullOrEmpty(card.InstallPath) && string.Equals(ViewModel.GetOsFgOutput(card.GameName, card.Source ?? ""), "dlssg", StringComparison.OrdinalIgnoreCase))
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "FGNvngxReplacement", v);
                };
                fgEnabledCombo.SelectionChanged += (s, ev) =>
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) return;
                    var v = fgEnabledCombo.SelectedItem as string == "True" ? "true" : "false";
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "Enabled", v);
                };
                forceReflexCombo.SelectionChanged += (s, ev) =>
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) return;
                    var v = forceReflexCombo.SelectedItem as string switch
                    {
                        "Force Disable (1)" => "1",
                        "Force Enable (2)"  => "2",
                        _                   => "0",
                    };
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "fakenvapi", "ForceReflex", v);
                };
                reflexMarkersCombo.SelectionChanged += (s, ev) =>
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) return;
                    var v = reflexMarkersCombo.SelectedItem as string == "False" ? "false" : "true";
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSG", "UseGamesReflexMarkers", v);
                };

                // ── Additional Settings ────────────────────────────────────────
                content.Children.Add(MakeSeparator());
                content.Children.Add(new TextBlock { Text = "Additional Settings", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                var addGrid = MakeSettingsGrid();

                // Read current DLSS SR and RR preset values from OptiScaler.ini
                string ReadIniValue(string section, string key)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) return "auto";
                    var iniP = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
                    if (!File.Exists(iniP)) return "auto";
                    var iniLines = File.ReadAllLines(iniP);
                    bool inSec = false;
                    foreach (var l in iniLines)
                    {
                        var t = l.Trim();
                        if (t.StartsWith("[")) inSec = t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                        else if (inSec && !t.StartsWith(";") && (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) || t.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase)))
                            return t.Split('=', 2)[1].Trim();
                    }
                    return "auto";
                }

                // DLSS SR preset: 10=J, 11=K, 12=L, 13=M
                srPresetMap = new[] { ("Default", "auto"), ("J", "10"), ("K", "11"), ("L", "12"), ("M", "13") };
                var srCurrent = ReadIniValue("DLSS", "RenderPresetForAll");
                var srSelected = srPresetMap.FirstOrDefault(p => p.Item2 == srCurrent).Item1 ?? "Default";
                srPresetCombo = new ComboBox { ItemsSource = srPresetMap.Select(p => p.Item1).ToArray(), SelectedItem = srSelected };
                ToolTipService.SetToolTip(srPresetCombo!, "DLSS Super Resolution render preset. J-M are the recommended modern presets.");

                // DLSS RR preset: 3=D, 4=E, 5=F
                rrPresetMap = new[] { ("Default", "auto"), ("D", "3"), ("E", "4"), ("F", "5") };
                var rrCurrent = ReadIniValue("DLSSD", "RenderPresetForAll");
                var rrSelected = rrPresetMap.FirstOrDefault(p => p.Item2 == rrCurrent).Item1 ?? "Default";
                rrPresetCombo = new ComboBox { ItemsSource = rrPresetMap.Select(p => p.Item1).ToArray(), SelectedItem = rrSelected };
                ToolTipService.SetToolTip(rrPresetCombo!, "DLSS Ray Reconstruction render preset.");

                AddRow(addGrid, 0, "DLSS SR Preset", srPresetCombo!, "DLSS RR Preset", rrPresetCombo!);

                // Disable Flip Metering + Render Scale
                var flipCurrent = ReadIniValue("NvApi", "DisableFlipMetering");
                var flipSelected = flipCurrent == "true" ? "On" : "Default";
                flipCombo = new ComboBox { ItemsSource = new[] { "Default", "On" }, SelectedItem = flipSelected };
                ToolTipService.SetToolTip(flipCombo!, "On: DisableFlipMetering=true — fixes thick frametime graph with NukemFG + fakenvapi.");

                // Render Scale — UpscaleRatioOverride
                renderScaleMap = new[] {
                    ("Off",          0f),
                    ("100% DLAA",    1.0f),
                    ("99% DLAA Alt", 1.0101f),
                    ("88% DLAA Lite",1.136f),
                    ("77% Ultra Quality", 1.3f),
                    ("75% Quality+", 1.333f),
                    ("67% Quality",  1.5f),
                    ("58% Balanced", 1.724f),
                    ("50% Performance", 2.0f),
                    ("45% Performance-", 2.222f),
                    ("33% Ultra Perf", 3.0f),
                };
                var rsCurrentEnabled = ReadIniValue("UpscaleRatio", "UpscaleRatioOverrideEnabled");
                var rsCurrentValue   = ReadIniValue("UpscaleRatio", "UpscaleRatioOverrideValue");
                string rsSelected = "Off";
                if (rsCurrentEnabled.Equals("true", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(rsCurrentValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rsVal))
                {
                    var match = renderScaleMap.Skip(1).FirstOrDefault(p => Math.Abs(p.Item2 - rsVal) < 0.01f);
                    if (match.Item1 != null) rsSelected = match.Item1;
                }
                rsCombo = new ComboBox { ItemsSource = renderScaleMap.Select(p => p.Item1).ToArray(), SelectedItem = rsSelected };
                ToolTipService.SetToolTip(rsCombo!, "Override the internal render resolution. Off = use in-game quality preset as-is.");

                AddRow(addGrid, 1, "Render Scale", rsCombo!, "Disable Flip Metering", flipCombo!);

                content.Children.Add(addGrid);

                srPresetCombo!.SelectionChanged += (s, ev) =>
                {
                    if (srPresetCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                    var val = srPresetMap!.FirstOrDefault(p => p.Item1 == sel).Item2 ?? "auto";
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSS", "RenderPresetOverride", "true");
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSS", "RenderPresetForAll", val == "auto" ? "0" : val);
                };
                rrPresetCombo!.SelectionChanged += (s, ev) =>
                {
                    if (rrPresetCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                    var val = rrPresetMap!.FirstOrDefault(p => p.Item1 == sel).Item2 ?? "auto";
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSD", "RenderPresetOverride", "true");
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSD", "RenderPresetForAll", val == "auto" ? "0" : val);
                };
                flipCombo!.SelectionChanged += (s, ev) =>
                {
                    if (flipCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "NvApi", "DisableFlipMetering", sel == "On" ? "true" : "false");
                };
                rsCombo!.SelectionChanged += (s, ev) =>
                {
                    if (rsCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                    if (sel == "Off")
                    {
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideEnabled", "false");
                    }
                    else
                    {
                        var match = renderScaleMap!.FirstOrDefault(p => p.Item1 == sel);
                        if (match.Item1 != null)
                        {
                            OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideEnabled", "true");
                            OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideValue",
                                match.Item2.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                };

                // ── UE-only Engine.ini settings ────────────────────────────────
                bool isUnreal = card.EngineHint?.Contains("Unreal", StringComparison.OrdinalIgnoreCase) == true;
                if (isUnreal)
                {
                    content.Children.Add(MakeSeparator());
                    content.Children.Add(new TextBlock { Text = "Engine.ini Settings", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                    var ueGrid = MakeSettingsGrid();

                    var dmvCombo = new ComboBox { ItemsSource = new[] { "Default", "Off" }, SelectedItem = ViewModel.GetOsDilatedMotionVectorsOff(card.GameName, card.Source ?? "") ? "Off" : "Default" };
                    ToolTipService.SetToolTip(dmvCombo, "Off: r.NGX.DLSS.DilateMotionVectors=0 + r.Streamline.DilateMotionVectors=0");
                    var fsrCombo = new ComboBox { ItemsSource = new[] { "None", "FSR2", "FSR3", "FSR3.1" }, SelectedItem = ViewModel.GetOsFsrCrashFix(card.GameName, card.Source ?? "") };
                    ToolTipService.SetToolTip(fsrCombo, "FSR2: r.FidelityFX.FSR2.UseNativeDX12=1\nFSR3: r.FidelityFX.FSR3.UseNativeDX12=1\nFSR3.1: above + r.FidelityFX.FSR3.UseRHI=0");
                    AddRow(ueGrid, 0, "Dilated Motion Vectors", dmvCombo, "FSR Crash Fix", fsrCombo);

                    var fgSwapCombo = new ComboBox { ItemsSource = new[] { "Default", "On" }, SelectedItem = ViewModel.GetOsFsrFgSwapchain(card.GameName, card.Source ?? "") ? "On" : "Default" };
                    ToolTipService.SetToolTip(fgSwapCombo, "On: r.FidelityFX.FI.OverrideSwapChainDX12=1");
                    var upscalerCombo = new ComboBox { ItemsSource = new[] { "Default", "On" }, SelectedItem = ViewModel.GetOsUpscalerPlugin(card.GameName, card.Source ?? "") ? "On" : "Default" };
                    ToolTipService.SetToolTip(upscalerCombo, "On: r.AntiAliasingMethod=4 + r.TemporalAA.Upscaler=1");
                    AddRow(ueGrid, 1, "FSR-FG Swapchain", fgSwapCombo, "Upscaler Plugin", upscalerCombo);

                    content.Children.Add(ueGrid);

                    dmvCombo.SelectionChanged += (s, ev) =>
                    {
                        bool off = dmvCombo.SelectedItem as string == "Off"; ViewModel.SetOsDilatedMotionVectorsOff(card.GameName, off, card.Source ?? "");
                        if (!string.IsNullOrEmpty(card.InstallPath)) { var keys = new (string, string, string)[] { ("SystemSettings", "r.NGX.DLSS.DilateMotionVectors", "0"), ("SystemSettings", "r.Streamline.DilateMotionVectors", "0") }; try { if (off) AuxInstallService.ApplyEngineIniCustomKeys(card.InstallPath, keys, card.EngineIniProjectOverride, card.GameName, card.Source); else AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, keys.Select(k => k.Item2), card.EngineIniProjectOverride, card.GameName, card.Source); } catch (Exception ex) { CrashReporter.Log($"[MainWindow.OsCogButton_Click] Failed to update DilateMotionVectors Engine.ini keys — {ex.Message}"); } }
                    };
                    fsrCombo.SelectionChanged += (s, ev) =>
                    {
                        var sel = fsrCombo.SelectedItem as string ?? "None"; var allK = new[] { "r.FidelityFX.FSR2.UseNativeDX12", "r.FidelityFX.FSR3.UseNativeDX12", "r.FidelityFX.FSR3.UseRHI" };
                        if (!string.IsNullOrEmpty(card.InstallPath)) { try { AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, allK, card.EngineIniProjectOverride, card.GameName, card.Source); } catch (Exception ex) { CrashReporter.Log($"[MainWindow.OsCogButton_Click] Failed to remove FSR crash-fix Engine.ini keys — {ex.Message}"); } if (sel != "None") { var k = sel switch { "FSR2" => new (string, string, string)[] { ("SystemSettings", "r.FidelityFX.FSR2.UseNativeDX12", "1") }, "FSR3" => new (string, string, string)[] { ("SystemSettings", "r.FidelityFX.FSR3.UseNativeDX12", "1") }, _ => new (string, string, string)[] { ("SystemSettings", "r.FidelityFX.FSR3.UseNativeDX12", "1"), ("SystemSettings", "r.FidelityFX.FSR3.UseRHI", "0") } }; try { AuxInstallService.ApplyEngineIniCustomKeys(card.InstallPath, k, card.EngineIniProjectOverride, card.GameName, card.Source); } catch (Exception ex) { CrashReporter.Log($"[MainWindow.OsCogButton_Click] Failed to apply FSR crash-fix Engine.ini keys — {ex.Message}"); } } }
                        ViewModel.SetOsFsrCrashFix(card.GameName, sel == "None" ? null : sel, card.Source ?? "");
                    };
                    fgSwapCombo.SelectionChanged += (s, ev) =>
                    {
                        bool on = fgSwapCombo.SelectedItem as string == "On"; ViewModel.SetOsFsrFgSwapchain(card.GameName, on, card.Source ?? "");
                        if (!string.IsNullOrEmpty(card.InstallPath)) { var k = new (string, string, string)[] { ("SystemSettings", "r.FidelityFX.FI.OverrideSwapChainDX12", "1") }; try { if (on) AuxInstallService.ApplyEngineIniCustomKeys(card.InstallPath, k, card.EngineIniProjectOverride, card.GameName, card.Source); else AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, k.Select(x => x.Item2), card.EngineIniProjectOverride, card.GameName, card.Source); } catch (Exception ex) { CrashReporter.Log($"[MainWindow.OsCogButton_Click] Failed to update FSR-FG swapchain Engine.ini key — {ex.Message}"); } }
                    };
                    upscalerCombo.SelectionChanged += (s, ev) =>
                    {
                        bool on = upscalerCombo.SelectedItem as string == "On"; ViewModel.SetOsUpscalerPlugin(card.GameName, on, card.Source ?? "");
                        if (!string.IsNullOrEmpty(card.InstallPath)) { var k = new (string, string, string)[] { ("SystemSettings", "r.AntiAliasingMethod", "4"), ("SystemSettings", "r.TemporalAA.Upscaler", "1") }; try { if (on) AuxInstallService.ApplyEngineIniCustomKeys(card.InstallPath, k, card.EngineIniProjectOverride, card.GameName, card.Source); else AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, k.Select(x => x.Item2), card.EngineIniProjectOverride, card.GameName, card.Source); } catch (Exception ex) { CrashReporter.Log($"[MainWindow.OsCogButton_Click] Failed to update upscaler plugin Engine.ini keys — {ex.Message}"); } }
                    };
                }

                // ── DLSS NR Settings section (DlssNr and F5 variants) ────────────
                if (isDlssNr || isF5)
                {
                    content.Children.Add(MakeSeparator());
                    content.Children.Add(new TextBlock { Text = "Neural Rendering Settings", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                    var nrGrid = MakeSettingsGrid();

                    // Helper: read a key from the [DlssNr] section of OptiScaler.ini
                    string ReadNrIni(string key)
                    {
                        if (string.IsNullOrEmpty(card.InstallPath)) return "auto";
                        var iniP = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
                        if (!File.Exists(iniP)) return "auto";
                        bool inNr = false;
                        foreach (var l in File.ReadAllLines(iniP))
                        {
                            var t = l.Trim();
                            if (t.StartsWith("[")) inNr = t.Equals("[DlssNr]", StringComparison.OrdinalIgnoreCase);
                            else if (inNr && !t.StartsWith(";") &&
                                (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) ||
                                 t.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase)))
                                return t.Split('=', 2)[1].Trim();
                        }
                        return "auto";
                    }

                    // Row 0: NR Runtime | NR Enabled
                    var dlssStreamlineSvc2 = App.Services.GetRequiredService<IDlssStreamlineService>();
                    var nrVersions = dlssStreamlineSvc2.DlssnrVersions.ToList();
                    var currentNrRuntime = ViewModel.GetOsNrRuntime(card.GameName, card.Source ?? "");
                    // Default to newest (first manifest entry) when no override is set
                    if (string.IsNullOrEmpty(currentNrRuntime) && nrVersions.Count > 0)
                        currentNrRuntime = nrVersions[0];
                    if (!string.IsNullOrEmpty(currentNrRuntime) && !nrVersions.Contains(currentNrRuntime))
                        nrVersions.Insert(0, currentNrRuntime);
                    nrRuntimeCombo = new ComboBox { ItemsSource = nrVersions, SelectedItem = currentNrRuntime };
                    ToolTipService.SetToolTip(nrRuntimeCombo, "NR runtime: 310.8.2 = original NVIDIA (RTX 50). 310.8.SF-v2 = ShortFuse cross-gen (RTX 20/30/40).");

                    var nrEnabledRaw = ReadNrIni("Enabled");
                    nrEnabledCombo = new ComboBox { ItemsSource = new[] { "Default", "On", "Off" },
                        SelectedItem = nrEnabledRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "On"
                                     : nrEnabledRaw.Equals("false", StringComparison.OrdinalIgnoreCase) ? "Off"
                                     : "Default" };
                    ToolTipService.SetToolTip(nrEnabledCombo, "Enable DLSS Neural Rendering. Requires nvngx_dlssnr.dll + nvngx.dll_dlssnr.dll in game folder.");
                    AddRow(nrGrid, 0, "NR Runtime", nrRuntimeCombo!, "NR Enabled", nrEnabledCombo!);

                    // Row 1: Run Before SR | Passes
                    var nrRunBeforeRaw = ReadNrIni("RunBeforeSR");
                    nrRunBeforeSrCombo = new ComboBox { ItemsSource = new[] { "Default", "On", "Off" },
                        SelectedItem = nrRunBeforeRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "On"
                                     : nrRunBeforeRaw.Equals("false", StringComparison.OrdinalIgnoreCase) ? "Off"
                                     : "Default" };
                    ToolTipService.SetToolTip(nrRunBeforeSrCombo, "Run NR before Super Resolution. On = before upscaling; Off = after upscaling.");

                    var nrPassesRaw = ReadNrIni("Passes");
                    nrPassesCombo = new ComboBox { ItemsSource = new[] { "Default", "1", "2", "3" },
                        SelectedItem = nrPassesRaw is "1" or "2" or "3" ? nrPassesRaw : "Default" };
                    ToolTipService.SetToolTip(nrPassesCombo, "Number of NR model passes. Each extra pass costs ~2x the model time but increases quality.");
                    AddRow(nrGrid, 1, "Run Before SR", nrRunBeforeSrCombo!, "Passes", nrPassesCombo!);

                    // Row 2: Working Scale | Finished Picture
                    var nrScaleRaw = ReadNrIni("WorkingScale");
                    nrScaleMap = new[] { ("Default", "auto"), ("0.5x (half)", "0.5"), ("0.75x", "0.75"), ("1.0x (full)", "1.0"), ("1.5x (supersample)", "1.5") };
                    var nrScaleSelected = nrScaleMap.FirstOrDefault(p => p.Item2 == nrScaleRaw).Item1 ?? "Default";
                    nrWorkingScaleCombo = new ComboBox { ItemsSource = nrScaleMap.Select(p => p.Item1).ToArray(), SelectedItem = nrScaleSelected };
                    ToolTipService.SetToolTip(nrWorkingScaleCombo, "Model work resolution as a fraction of frame size. Lower = faster; above 1.0 supersamples the model.");

                    var nrFinishedRaw = ReadNrIni("FinishedPicture");
                    nrFinishedPicCombo = new ComboBox { ItemsSource = new[] { "Default", "On" },
                        SelectedItem = nrFinishedRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "On" : "Default" };
                    ToolTipService.SetToolTip(nrFinishedPicCombo, "Apply NR to the finished picture (after all game lighting and effects). Helps with green noise. DX12 native only.");
                    AddRow(nrGrid, 2, "Working Scale", nrWorkingScaleCombo!, "Apply to Finished Picture", nrFinishedPicCombo!);

                    content.Children.Add(nrGrid);

                    // ── NR Runtime swap ────────────────────────────────────────
                    nrRuntimeCombo!.SelectionChanged += (s, ev) =>
                    {
                        if (nrRuntimeCombo.SelectedItem is not string selectedVer || string.IsNullOrEmpty(selectedVer)) return;
                        ViewModel.SetOsNrRuntime(card.GameName, selectedVer, card.Source ?? "");
                        // Immediately swap the nvngx_dlssnr.dll in the game folder if installed
                        if (card.IsOsInstalled && !string.IsNullOrEmpty(card.InstallPath))
                        {
                            var dlssNrSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
                            var gameNrPath = Path.Combine(card.InstallPath, "nvngx_dlssnr.dll");
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var cachedDll = await dlssNrSvc.EnsureSpecificDlssnrCachedAsync(selectedVer).ConfigureAwait(false);
                                    if (cachedDll != null)
                                    {
                                        // Use SentinelBackup to preserve the existing sentinel state,
                                        // then overwrite with the new version
                                        AuxInstallService.SentinelBackup(gameNrPath);
                                        File.Copy(cachedDll, gameNrPath, overwrite: true);
                                        CrashReporter.Log($"[OsCog] Swapped nvngx_dlssnr.dll to {selectedVer} in '{card.InstallPath}'");
                                    }
                                    else
                                    {
                                        CrashReporter.Log($"[OsCog] nvngx_dlssnr.dll {selectedVer} not available — swap skipped");
                                    }
                                }
                                catch (Exception ex) { CrashReporter.Log($"[OsCog] NR runtime swap failed — {ex.Message}"); }
                            });
                        }
                    };

                    // ── NR INI value handlers ──────────────────────────────────
                    nrEnabledCombo!.SelectionChanged += (s, ev) =>
                    {
                        if (nrEnabledCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                        var v = sel == "On" ? "true" : sel == "Off" ? "false" : "auto";
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "Enabled", v);
                    };
                    nrRunBeforeSrCombo!.SelectionChanged += (s, ev) =>
                    {
                        if (nrRunBeforeSrCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                        var v = sel == "On" ? "true" : sel == "Off" ? "false" : "auto";
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "RunBeforeSR", v);
                    };
                    nrPassesCombo!.SelectionChanged += (s, ev) =>
                    {
                        if (nrPassesCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                        var v = sel == "Default" ? "auto" : sel;
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "Passes", v);
                    };
                    nrWorkingScaleCombo!.SelectionChanged += (s, ev) =>
                    {
                        if (nrWorkingScaleCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                        var v = nrScaleMap.FirstOrDefault(p => p.Item1 == sel).Item2 ?? "auto";
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "WorkingScale", v);
                    };
                    nrFinishedPicCombo!.SelectionChanged += (s, ev) =>
                    {
                        if (nrFinishedPicCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "FinishedPicture", sel == "On" ? "true" : "false");
                    };
                }
            }

            content.Children.Add(MakeSeparator());

            // (Presets section is in the static bottom area below)

            // ── Fixed bottom section (always visible, never scrolls) ──────────
            var bottomBorder = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            bottomBorder.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush) });

            if (isNightly || isDlssNr || isF5)
            {
                var presetsLabel = new TextBlock { Text = "Presets", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
                bottomBorder.Children.Add(presetsLabel);

                // Load global presets from disk
                var presets = OsPresetService.Load();

                var presetsGrid = new Grid { ColumnSpacing = 8 };
                for (int i = 0; i < 4; i++)
                    presetsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                presetsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (int slotIndex = 0; slotIndex < 4; slotIndex++)
                {
                    int i = slotIndex; // capture for lambdas
                    var preset = presets[i];
                    bool hasData = preset != null;

                    var slotPanel = new StackPanel { Spacing = 4 };

                    // ── Name row: [TextBox][Save] ──────────────────────────────
                    var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

                    var nameBox = new TextBox
                    {
                        PlaceholderText = $"Slot {i + 1}",
                        Text = preset?.Name ?? "",
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MinWidth = 0,
                        Padding = new Thickness(6, 4, 6, 4),
                    };

                    var saveBtn = new Button
                    {
                        Content = "Save",
                        FontSize = 11,
                        Width = 42,
                        Padding = new Thickness(4),
                        CornerRadius = new CornerRadius(4),
                    };
                    ToolTipService.SetToolTip(saveBtn, "Save current cog settings into this slot");

                    // Stretch the textbox to fill remaining space
                    var nameRowGrid = new Grid { ColumnSpacing = 4 };
                    nameRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    nameRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Grid.SetColumn(nameBox, 0);
                    Grid.SetColumn(saveBtn, 1);
                    nameRowGrid.Children.Add(nameBox);
                    nameRowGrid.Children.Add(saveBtn);

                    // ── Apply button ───────────────────────────────────────────
                    var applyBtn = new Button
                    {
                        Content = hasData ? (preset!.Name ?? $"Slot {i + 1}") : $"Slot {i + 1}",
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Padding = new Thickness(6, 5, 6, 5),
                        CornerRadius = new CornerRadius(4),
                        IsEnabled = hasData,
                        Opacity = hasData ? 1.0 : 0.40,
                    };
                    ToolTipService.SetToolTip(applyBtn, hasData ? "Apply this preset to the current game" : "No preset saved yet — click Save to capture current settings");

                    // ── Wire: TextBox name editing ─────────────────────────────
                    nameBox.TextChanged += (s, ev) =>
                    {
                        if (presets[i] == null) presets[i] = new Models.OsPreset();
                        presets[i]!.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text;
                        applyBtn.Content = presets[i]!.Name ?? $"Slot {i + 1}";
                        OsPresetService.Save(presets);
                    };

                    // ── Wire: Save button ──────────────────────────────────────
                    saveBtn.Click += (s, ev) =>
                    {
                        // Capture current combo states (all combos are non-null when isNightly == true)
                        string? capturedFgInput     = fgInputCombo?.SelectedItem is string fgi ? FgInputToIni!(fgi) : null;
                        string? capturedFgOutput    = fgOutputCombo?.SelectedItem is string fgo ? FgOutputToIni!(fgo) : null;
                        string? capturedFgNvngx     = fgNvngxCombo?.SelectedItem is ComboBoxItem cbi ? FgNvngxToIni!(cbi.Content as string ?? "") : fgNvngxCombo?.SelectedItem is string fgns ? FgNvngxToIni!(fgns) : null;
                        bool?   capturedDeploySl    = combinedCombo?.SelectedItem as string == "Yes";
                        string? capturedSrPreset    = srPresetCombo?.SelectedItem is string srp ? (srPresetMap?.FirstOrDefault(p => p.Item1 == srp).Item2 ?? "auto") : null;
                        string? capturedRrPreset    = rrPresetCombo?.SelectedItem is string rrp ? (rrPresetMap?.FirstOrDefault(p => p.Item1 == rrp).Item2 ?? "auto") : null;
                        string? capturedRenderScale = rsCombo?.SelectedItem as string;
                        bool?   capturedFlip        = flipCombo?.SelectedItem as string == "On";
                        string? capturedHudFix      = hudFixCombo?.SelectedItem is string hf ? (hf == "On" ? "true" : hf == "Off" ? "false" : "auto") : null;

                        // Capture the 6 previously missing settings
                        string? capturedVariant     = variantCombo.SelectedItem as string; // "Stable", "Nightly", "DLSS NR"
                        string? capturedUpscalerApi = apiCombo.SelectedItem as string;     // "DX11", "DX12", "Vulkan"
                        string? capturedUpscaler    = apiCombo.SelectedItem is string upApi && apiUpscalerCombo.SelectedItem is string upSel
                                                      ? UpscalerOptionToIni(upApi, upSel) : null;
                        string? capturedFgEnabled   = fgEnabledCombo.SelectedItem is string fge
                                                      ? (fge == "True" ? "true" : fge == "Auto (false)" ? "auto" : null) : null;
                        string? capturedForceReflex = forceReflexCombo.SelectedItem is string fr ? fr switch
                                                      { "Force Disable (1)" => "1", "Force Enable (2)" => "2", _ => "0" } : null;
                        string? capturedReflexMarkers = reflexMarkersCombo.SelectedItem is string rm
                                                        ? (rm == "False" ? "false" : "true") : null;
                        float?  capturedFps         = null;
                        if (fpsLimitCombo.SelectedItem is string fpsSel)
                        {
                            if (fpsSel == "Off") capturedFps = 0f;
                            else { var m = vrrPresetsOs.FirstOrDefault(p => p.Label == fpsSel); if (m.Label != null) capturedFps = m.Fps; }
                        }
                        // NR fields (only non-null when isDlssNr)
                        string? capturedNrRuntime     = nrRuntimeCombo?.SelectedItem as string;
                        string? capturedNrEnabled     = nrEnabledCombo?.SelectedItem is string ne ? (ne == "On" ? "true" : ne == "Off" ? "false" : "auto") : null;
                        string? capturedNrRunBeforeSr = nrRunBeforeSrCombo?.SelectedItem is string rb ? (rb == "On" ? "true" : rb == "Off" ? "false" : "auto") : null;
                        string? capturedNrPasses      = nrPassesCombo?.SelectedItem is string np && np != "Default" ? np : nrPassesCombo != null ? "auto" : null;
                        string? capturedNrWorkingScale = nrWorkingScaleCombo?.SelectedItem is string nws
                            ? (nrScaleMap?.FirstOrDefault(p2 => p2.Item1 == nws).Item2 ?? "auto") : null;
                        string? capturedNrFinishedPic  = nrFinishedPicCombo?.SelectedItem is string nfp ? (nfp == "On" ? "true" : "false") : null;

                        if (presets[i] == null) presets[i] = new Models.OsPreset();
                        var p = presets[i]!;
                        // Preserve existing name
                        p.FgInput             = capturedFgInput;
                        p.FgOutput            = capturedFgOutput;
                        p.FgNvngxReplacement  = capturedFgNvngx;
                        p.DeployStreamline    = capturedDeploySl;
                        p.DeployDlssEnabler   = capturedDeploySl; // same toggle
                        p.SrPreset            = capturedSrPreset;
                        p.RrPreset            = capturedRrPreset;
                        p.RenderScale         = capturedRenderScale;
                        p.DisableFlipMetering = capturedFlip;
                        p.HudFix              = capturedHudFix;
                        p.FramerateLimit      = capturedFps;
                        p.NrRuntime           = capturedNrRuntime;
                        p.NrEnabled           = capturedNrEnabled;
                        p.NrRunBeforeSr       = capturedNrRunBeforeSr;
                        p.NrPasses            = capturedNrPasses;
                        p.NrWorkingScale      = capturedNrWorkingScale;
                        p.NrFinishedPicture   = capturedNrFinishedPic;
                        // Previously missing fields
                        p.OsVariant           = capturedVariant;
                        p.UpscalerApi         = capturedUpscalerApi;
                        p.Upscaler            = capturedUpscaler;
                        p.FgEnabled           = capturedFgEnabled;
                        p.ForceReflex         = capturedForceReflex;
                        p.UseGamesReflexMarkers = capturedReflexMarkers;

                        OsPresetService.Save(presets);
                        applyBtn.IsEnabled = true;
                        applyBtn.Opacity = 1.0;
                        applyBtn.Content = p.Name ?? $"Slot {i + 1}";
                        ToolTipService.SetToolTip(applyBtn, "Apply this preset to the current game");
                    };

                    // ── Wire: Apply button ─────────────────────────────────────
                    applyBtn.Click += (s, ev) =>
                    {
                        var p = presets[i];
                        if (p == null) return;

                        // Apply ViewModel settings
                        if (p.FgInput != null)            ViewModel.SetOsFgInput(card.GameName, p.FgInput, card.Source ?? "");
                        if (p.FgOutput != null)           ViewModel.SetOsFgOutput(card.GameName, p.FgOutput, card.Source ?? "");
                        if (p.FgNvngxReplacement != null) ViewModel.SetOsFgNvngxReplacement(card.GameName, p.FgNvngxReplacement, card.Source ?? "");
                        if (p.DeployStreamline.HasValue)
                        {
                            ViewModel.SetOsDeployStreamline(card.GameName, p.DeployStreamline.Value, card.Source ?? "");
                            ViewModel.SetOsDeployDlssEnabler(card.GameName, p.DeployStreamline.Value, card.Source ?? "");
                            if (!string.IsNullOrEmpty(card.InstallPath))
                            {
                                try
                                {
                                    if (p.DeployStreamline.Value)
                                    {
                                        _optiScalerService.DeployStreamlineToGame(card.InstallPath);
                                        var d = Path.Combine(card.InstallPath, "OptiScaler");
                                        _ = _dlssEnablerService.InstallAsync(d);
                                    }
                                    else
                                    {
                                        _optiScalerService.RemoveStreamlineFromGame(card.InstallPath);
                                        var d = Path.Combine(card.InstallPath, "OptiScaler");
                                        _dlssEnablerService.Uninstall(d);
                                    }
                                }
                                catch (Exception ex) { CrashReporter.Log($"[OsPreset.Apply] Streamline — {ex.Message}"); }
                            }
                        }

                        if (!string.IsNullOrEmpty(card.InstallPath))
                        {
                            // FG INI values
                            if (p.FgInput != null)
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "FGInput", p.FgInput);
                            if (p.FgOutput != null)
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "FGOutput", p.FgOutput);
                            if (p.FgNvngxReplacement != null && string.Equals(p.FgOutput, "dlssg", StringComparison.OrdinalIgnoreCase))
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "FGNvngxReplacement", p.FgNvngxReplacement);

                            // SR preset
                            if (p.SrPreset != null)
                            {
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSS", "RenderPresetOverride", "true");
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSS", "RenderPresetForAll", p.SrPreset == "auto" ? "0" : p.SrPreset);
                            }

                            // RR preset
                            if (p.RrPreset != null)
                            {
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSD", "RenderPresetOverride", "true");
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSD", "RenderPresetForAll", p.RrPreset == "auto" ? "0" : p.RrPreset);
                            }

                            // Render scale
                            if (p.RenderScale != null)
                            {
                                if (p.RenderScale == "Off")
                                {
                                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideEnabled", "false");
                                }
                                else
                                {
                                    var rsMatch = renderScaleMap.FirstOrDefault(rm => rm.Item1 == p.RenderScale);
                                    if (rsMatch.Item1 != null)
                                    {
                                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideEnabled", "true");
                                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideValue",
                                            rsMatch.Item2.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
                                    }
                                }
                            }

                            // Flip metering
                            if (p.DisableFlipMetering.HasValue)
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "NvApi", "DisableFlipMetering", p.DisableFlipMetering.Value ? "true" : "false");

                            // HUD fix
                            if (p.HudFix != null)
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "OptiFG", "HUDFix", p.HudFix);

                            // Framerate limit
                            if (p.FramerateLimit.HasValue)
                            {
                                if (p.FramerateLimit.Value <= 0f)
                                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit", "0");
                                else
                                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit",
                                        p.FramerateLimit.Value.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
                            }

                            // NR INI values (DlssNr variant only)
                            if (p.NrEnabled        != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "Enabled",        p.NrEnabled);
                            if (p.NrRunBeforeSr    != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "RunBeforeSR",     p.NrRunBeforeSr);
                            if (p.NrPasses         != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "Passes",          p.NrPasses);
                            if (p.NrWorkingScale   != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "WorkingScale",    p.NrWorkingScale);
                            if (p.NrFinishedPicture != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "FinishedPicture", p.NrFinishedPicture);

                            // Previously missing: FG Enabled, Upscaler, Force Reflex, Reflex Markers
                            if (p.FgEnabled != null && p.FgEnabled != "auto")
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "Enabled", p.FgEnabled);
                            if (p.UpscalerApi != null && p.Upscaler != null)
                            {
                                var iniKey = p.UpscalerApi switch { "DX12" => "Dx12Upscaler", "Vulkan" => "VulkanUpscaler", _ => "Dx11Upscaler" };
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Upscalers", iniKey, p.Upscaler);
                            }
                            if (p.ForceReflex != null)
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "fakenvapi", "ForceReflex", p.ForceReflex);
                            if (p.UseGamesReflexMarkers != null)
                                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSG", "UseGamesReflexMarkers", p.UseGamesReflexMarkers);
                        }

                        // OsVariant — change if preset has one and it differs from current
                        if (p.OsVariant != null)
                        {
                            var internalVariant = p.OsVariant switch { "DLSS NR" => "DlssNr", "Stable" => null, _ => p.OsVariant };
                            ViewModel.SetOsVariant(card.GameName, internalVariant, card.Source ?? "");
                        }

                        // NR runtime swap (outside InstallPath guard — needs async)
                        if (p.NrRuntime != null)
                        {
                            ViewModel.SetOsNrRuntime(card.GameName, p.NrRuntime, card.Source ?? "");
                            if (card.IsOsInstalled && !string.IsNullOrEmpty(card.InstallPath))
                            {
                                var dlssNrSvc2 = App.Services.GetRequiredService<IDlssStreamlineService>();
                                var gameNrPath2 = Path.Combine(card.InstallPath, "nvngx_dlssnr.dll");
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var cachedDll2 = await dlssNrSvc2.EnsureSpecificDlssnrCachedAsync(p.NrRuntime).ConfigureAwait(false);
                                        if (cachedDll2 != null) { AuxInstallService.SentinelBackup(gameNrPath2); File.Copy(cachedDll2, gameNrPath2, overwrite: true); }
                                    }
                                    catch (Exception ex) { CrashReporter.Log($"[OsPreset.Apply] NR runtime swap failed — {ex.Message}"); }
                                });
                            }
                        }

                        // Use DispatcherTimer for the delay — TryEnqueue(async lambda) detaches after the first
                        // await, causing OsCogButton_Click to build WinUI elements off the UI thread (freeze risk).
                        osCogDialog?.Hide();
                        var rebuildTimer2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                        rebuildTimer2.Tick += (_, _) => { rebuildTimer2.Stop(); OsCogButton_Click(sender, e); };
                        rebuildTimer2.Start();
                    };

                    slotPanel.Children.Add(nameRowGrid);
                    slotPanel.Children.Add(applyBtn);

                    Grid.SetColumn(slotPanel, i);
                    presetsGrid.Children.Add(slotPanel);
                }

                bottomBorder.Children.Add(presetsGrid);
            }

            bottomBorder.Children.Add(new TextBlock
            {
                Text = "Settings above apply in real time. Only deploy an ini if the game folder has no OptiScaler.ini yet, or you want a fresh one — it will not carry over the settings configured above.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
            });

            var deployBtn = new Button
            {
                Content = "Deploy OptiScaler.ini",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            };
            deployBtn.Click += (s, ev) => _installEventHandler.CopyOsIniButton_Click(sender, e);
            bottomBorder.Children.Add(deployBtn);

            bottomBorder.Children.Add(new TextBlock
            {
                Text = "If the game crashes with both ReShade and OptiScaler installed, try renaming ReShade to d3d12.dll (or another DLL name) using DLL Naming Overrides in the Overrides panel.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                TextWrapping = TextWrapping.Wrap,
            });

            // Gate: if OptiScaler is not installed, disable all settings except version/framerate (row 0 of unifiedGrid)
            if (!card.IsOsInstalled)
            {
                for (int i = 1; i < content.Children.Count; i++)
                {
                    if (content.Children[i] is FrameworkElement fe)
                    {
                        fe.IsHitTestVisible = false;
                        fe.Opacity = 0.45;
                    }
                }
                foreach (var child in unifiedGrid.Children.OfType<FrameworkElement>())
                {
                    int row = Grid.GetRow(child);
                    int col = Grid.GetColumn(child);
                    if (row >= 2 || col >= 2)
                    {
                        child.IsHitTestVisible = false;
                        child.Opacity = 0.45;
                    }
                }
                foreach (var child in bottomBorder.Children.OfType<FrameworkElement>())
                {
                    child.IsHitTestVisible = false;
                    child.Opacity = 0.45;
                }
            }

            // Root layout: scrollable settings on top, fixed bottom always visible
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 16, 0),
            };
            Grid.SetRow(scrollViewer, 0);
            rootGrid.Children.Add(scrollViewer);

            Grid.SetRow(bottomBorder, 1);
            rootGrid.Children.Add(bottomBorder);

            var dialog = new ContentDialog
            {
                Title = "OptiScaler Settings",
                Content = rootGrid,
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 680.0;
            osCogDialog = dialog;
            variantComboInitializing = false;
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.OsCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
