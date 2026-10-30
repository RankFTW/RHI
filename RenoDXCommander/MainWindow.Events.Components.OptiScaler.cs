// MainWindow.Events.Components.OptiScaler.cs — OptiScaler cog dialog handler (OsCogButton_Click).
// Uses the CogDialog shell (title, sections, buttons, status line) with a compact two-per-row
// settings grid (AddOsGrid) because OptiScaler has many settings; explanations are tooltips.
// Sections live in .OptiScaler.Sections.cs, the pinned footer (presets + Deploy OptiScaler.ini)
// in .OptiScaler.Footer.cs.

using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>State shared between the OptiScaler cog dialog's sections, footer and presets.</summary>
    private sealed class OsCogContext
    {
        public OsCogContext(GameCardViewModel card, CogDialog cog, object sender, RoutedEventArgs args)
        {
            Card = card;
            Cog = cog;
            Sender = sender;
            Args = args;
        }

        public GameCardViewModel Card { get; }
        public CogDialog Cog { get; }
        public object Sender { get; }
        public RoutedEventArgs Args { get; }
        public string Store => Card.Source ?? "";

        /// <summary>Elements that stay usable when OptiScaler isn't installed (version, nightly build).</summary>
        public List<UIElement> Ungated { get; } = new();

        // Top rows (always present)
        public ComboBox? VariantCombo;
        public ComboBox? FpsLimitCombo;
        public ComboBox? ApiCombo;
        public ComboBox? ApiUpscalerCombo;
        // Frame Generation (Nightly / DLSS NR / F5 only)
        public ComboBox? FgEnabledCombo;
        public ComboBox? CombinedCombo;
        public ComboBox? FgInputCombo;
        public ComboBox? FgOutputCombo;
        public ComboBox? FgNvngxCombo;
        public ComboBox? HudFixCombo;
        public ComboBox? ForceReflexCombo;
        public ComboBox? ReflexMarkersCombo;
        // Additional Settings
        public ComboBox? SrPresetCombo;
        public ComboBox? RrPresetCombo;
        public ComboBox? RsCombo;
        public ComboBox? FlipCombo;
        // Neural Rendering (DLSS NR / F5 only)
        public ComboBox? NrRuntimeCombo;
        public ComboBox? NrEnabledCombo;
        public ComboBox? NrRunBeforeSrCombo;
        public ComboBox? NrPassesCombo;
        public ComboBox? NrWorkingScaleCombo;
        public ComboBox? NrFinishedPicCombo;
    }

    // ── Value tables ─────────────────────────────────────────────────────────────

    private static readonly (float Fps, string Label)[] OsVrrPresets =
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

    // DLSS SR preset: 10=J, 11=K, 12=L, 13=M
    private static readonly (string Label, string Value)[] OsSrPresetMap =
        { ("Default", "auto"), ("J", "10"), ("K", "11"), ("L", "12"), ("M", "13") };

    // DLSS RR preset: 3=D, 4=E, 5=F
    private static readonly (string Label, string Value)[] OsRrPresetMap =
        { ("Default", "auto"), ("D", "3"), ("E", "4"), ("F", "5") };

    // Render Scale — UpscaleRatioOverrideValue
    private static readonly (string Label, float Ratio)[] OsRenderScaleMap =
    {
        ("Off",               0f),
        ("100% DLAA",         1.0f),
        ("99% DLAA Alt",      1.0101f),
        ("88% DLAA Lite",     1.136f),
        ("77% Ultra Quality", 1.3f),
        ("75% Quality+",      1.333f),
        ("67% Quality",       1.5f),
        ("58% Balanced",      1.724f),
        ("50% Performance",   2.0f),
        ("45% Performance-",  2.222f),
        ("33% Ultra Perf",    3.0f),
    };

    private static readonly (string Label, string Value)[] OsNrScaleMap =
        { ("Default", "auto"), ("0.5x (half)", "0.5"), ("0.75x", "0.75"), ("1.0x (full)", "1.0"), ("1.5x (supersample)", "1.5") };

    // ── Upscaler option mapping ──────────────────────────────────────────────────

    private static string[] OsGetUpscalerOptions(string api) => api switch
    {
        "DX12"   => new[] { "Auto (Default)", "DLSS", "XeSS", "FSR 2.1", "FSR 2.2", "FSR 3.x / FFX" },
        "Vulkan" => new[] { "Auto (Default)", "DLSS", "FSR 2.1", "FSR 2.2", "FSR 3.x / FFX", "XeSS", "FSR2.1 on DX12", "FSR3 on DX12" },
        _        => new[] { "Auto (Default)", "DLSS", "FSR 2.2", "FSR 3.1", "XeSS (Arc only)", "XeSS on DX12", "FSR2.1 on DX12", "FSR2.2 on DX12", "FSR3 on DX12" }, // DX11
    };

    private static string OsUpscalerOptionToIni(string api, string display) => api switch
    {
        "DX12"   => display switch { "DLSS" => "dlss", "XeSS" => "xess", "FSR 2.1" => "fsr21", "FSR 2.2" => "fsr22", "FSR 3.x / FFX" => "ffx", _ => "auto" },
        "Vulkan" => display switch { "DLSS" => "dlss", "FSR 2.1" => "fsr21", "FSR 2.2" => "fsr22", "FSR 3.x / FFX" => "ffx", "XeSS" => "xess", "FSR2.1 on DX12" => "fsr21_12", "FSR3 on DX12" => "ffx_12", _ => "auto" },
        _        => display switch { "DLSS" => "dlss", "FSR 2.2" => "fsr22", "FSR 3.1" => "fsr31", "XeSS (Arc only)" => "xess", "XeSS on DX12" => "xess_12", "FSR2.1 on DX12" => "fsr21_12", "FSR2.2 on DX12" => "fsr22_12", "FSR3 on DX12" => "ffx_12", _ => "auto" },
    };

    private static string OsIniToUpscalerOption(string api, string ini) => api switch
    {
        "DX12"   => ini switch { "dlss" => "DLSS", "xess" => "XeSS", "fsr21" => "FSR 2.1", "fsr22" => "FSR 2.2", "ffx" => "FSR 3.x / FFX", _ => "Auto (Default)" },
        "Vulkan" => ini switch { "dlss" => "DLSS", "fsr21" => "FSR 2.1", "fsr22" => "FSR 2.2", "ffx" => "FSR 3.x / FFX", "xess" => "XeSS", "fsr21_12" => "FSR2.1 on DX12", "ffx_12" => "FSR3 on DX12", _ => "Auto (Default)" },
        _        => ini switch { "dlss" => "DLSS", "fsr22" => "FSR 2.2", "fsr31" => "FSR 3.1", "xess" => "XeSS (Arc only)", "xess_12" => "XeSS on DX12", "fsr21_12" => "FSR2.1 on DX12", "fsr22_12" => "FSR2.2 on DX12", "ffx_12" => "FSR3 on DX12", _ => "Auto (Default)" },
    };

    private static string OsApiToIniKey(string api) => api switch { "DX12" => "Dx12Upscaler", "Vulkan" => "VulkanUpscaler", _ => "Dx11Upscaler" };

    // ── Frame generation option mapping ──────────────────────────────────────────

    private static string OsFgInputToIni(string d) => d switch { "OptiFG (Upscaler)" => "upscaler", "DLSSG via Streamline" => "dlssg", "DLSSG via Nvngx" => "nvngxfg", "FSR 3.1 FG" => "fsrfg", "FSR 3.0 FG" => "fsrfg30", "XeFG" => "xefg", _ => "auto" };
    private static string OsFgOutputToIni(string d) => d switch { "FSR FG" => "fsrfg", "DLSSG" => "dlssg", "XeFG" => "xefg", _ => "auto" };
    private static string OsFgNvngxToIni(string d) => d switch { "Nukem's" => "Nukems", "Enabler" => "Arturs", "FSR 3/4 FG" => "FFX", "Combo" => "Combo", _ => "None" };
    private static string OsIniToFgInput(string v) => v switch { "upscaler" => "OptiFG (Upscaler)", "dlssg" => "DLSSG via Streamline", "nvngxfg" => "DLSSG via Nvngx", "fsrfg" => "FSR 3.1 FG", "fsrfg30" => "FSR 3.0 FG", "xefg" => "XeFG", _ => "Auto (Default)" };
    private static string OsIniToFgOutput(string v) => v switch { "fsrfg" => "FSR FG", "dlssg" => "DLSSG", "xefg" => "XeFG", _ => "Auto (Default)" };
    private static string OsIniToFgNvngx(string v) => v switch { "Nukems" => "Nukem's", "Arturs" => "Enabler", "FFX" => "FSR 3/4 FG", "Combo" => "Combo", _ => "None (Real DLSSG)" };

    // ── OptiScaler.ini readers ───────────────────────────────────────────────────

    /// <summary>Reads <paramref name="key"/> from <c>[section]</c> of the game's OptiScaler.ini, or <paramref name="fallback"/>.</summary>
    private static string ReadOsIniSectionValue(string? installPath, string section, string key, string fallback)
    {
        if (string.IsNullOrEmpty(installPath)) return fallback;
        var p = Path.Combine(installPath, OptiScalerService.IniFileName);
        if (!File.Exists(p)) return fallback;
        bool inSec = false;
        foreach (var line in File.ReadAllLines(p))
        {
            var t = line.Trim();
            if (t.StartsWith("[")) inSec = t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
            else if (inSec && !t.StartsWith(";")
                     && (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) || t.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase)))
                return t.Split('=', 2)[1].Trim();
        }
        return fallback;
    }

    /// <summary>Reads the first uncommented line starting with <paramref name="key"/> (any section), lower-cased; "auto" if absent.</summary>
    private static string ReadOsUpscalerIni(string? installPath, string key)
    {
        if (string.IsNullOrEmpty(installPath)) return "auto";
        var iniPath = Path.Combine(installPath, OptiScalerService.IniFileName);
        if (!File.Exists(iniPath)) return "auto";
        var line = File.ReadAllLines(iniPath).FirstOrDefault(l =>
            l.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
            l.Contains("=") && !l.TrimStart().StartsWith(";"));
        if (line == null) return "auto";
        return line.Split('=', 2)[1].Trim().ToLowerInvariant();
    }

    /// <summary>Current FramerateLimit as one of the VRR preset labels, or "Off".</summary>
    private static string ReadOsFramerateLabel(string? installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return "Off";
        var iniPath = Path.Combine(installPath, OptiScalerService.IniFileName);
        if (!File.Exists(iniPath)) return "Off";
        var fpsLine = File.ReadAllLines(iniPath).FirstOrDefault(l => l.TrimStart().StartsWith("FramerateLimit", StringComparison.OrdinalIgnoreCase)
            && l.Contains("=") && !l.TrimStart().StartsWith(";"));
        if (fpsLine == null) return "Off";
        var val = fpsLine.Split('=', 2)[1].Trim();
        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fpsVal) && fpsVal > 0)
        {
            var match = OsVrrPresets.FirstOrDefault(p => Math.Abs(p.Fps - fpsVal) < 1f);
            return match.Label ?? "Off";
        }
        return "Off";
    }

    private static string FormatOsFloat(float v) => v.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Index of <paramref name="value"/> in <paramref name="items"/>, or <paramref name="fallback"/>.</summary>
    private static int OsIndexOf(IList<string> items, string? value, int fallback = -1)
    {
        if (value == null) return fallback;
        var i = items.IndexOf(value);
        return i >= 0 ? i : fallback;
    }

    // ── Compact settings grid ────────────────────────────────────────────────────

    /// <summary>One label + control cell of the compact grid. Help is shown as a tooltip on both.</summary>
    private sealed record OsCell(string Label, string? Help, FrameworkElement Control, bool Ungated = false)
    {
        public ComboBox Combo => (ComboBox)Control;
    }

    private const double OsComboColumnWidth = 160;

    /// <summary>
    /// A dropdown cell that calls <paramref name="onChanged"/> when the user picks an item (never during
    /// construction). Returning false, or throwing, reverts the pick; errors show in the dialog's status line.
    /// </summary>
    private static OsCell OsComboCell(CogDialog cog, string label, string help, IList<string> items, int selectedIndex,
        Func<int, bool> onChanged, bool isEnabled = true)
    {
        var combo = CogDialog.MakeCombo(items, selectedIndex, isEnabled);
        int last = combo.SelectedIndex;
        bool reverting = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (reverting || combo.SelectedIndex < 0 || combo.SelectedIndex == last) return;
            bool keep;
            try { keep = onChanged(combo.SelectedIndex); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.OsComboCell] '{label}' change failed — {ex.Message}");
                cog.SetError($"Couldn't change {label}: {ex.Message}");
                keep = false;
            }
            if (keep) { last = combo.SelectedIndex; return; }
            reverting = true;
            combo.SelectedIndex = last;
            reverting = false;
        };
        return new OsCell(label, help, combo);
    }

    /// <summary>Same as the Func overload, for callbacks that always keep the pick.</summary>
    private static OsCell OsComboCell(CogDialog cog, string label, string help, IList<string> items, int selectedIndex,
        Action<int> onChanged, bool isEnabled = true)
        => OsComboCell(cog, label, help, items, selectedIndex, i => { onChanged(i); return true; }, isEnabled);

    /// <summary>
    /// Adds settings two per row (label | dropdown | gap | label | dropdown) to the dialog body.
    /// A null cell leaves that slot empty (keeps a related setting in the column it belongs to).
    /// </summary>
    private static void AddOsGrid(OsCogContext ctx, params OsCell?[] cells)
    {
        if (cells.Length == 0) return;
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OsComboColumnWidth) });
        // Zero-width spacer: with ColumnSpacing it puts a 24 px gap between the two pairs.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OsComboColumnWidth) });
        for (int r = 0; r < (cells.Length + 1) / 2; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell == null) continue;
            int row = i / 2, col = i % 2 == 0 ? 0 : 3;

            var label = new TextBlock
            {
                Text = cell.Label,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            };
            var control = cell.Control;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            control.VerticalAlignment = VerticalAlignment.Center;
            if (!string.IsNullOrWhiteSpace(cell.Help))
            {
                // Disabled controls don't show tooltips, so the label carries it too.
                ToolTipService.SetToolTip(label, cell.Help);
                ToolTipService.SetToolTip(control, cell.Help);
            }

            Grid.SetRow(label, row); Grid.SetColumn(label, col);
            Grid.SetRow(control, row); Grid.SetColumn(control, col + 1);
            grid.Children.Add(label);
            grid.Children.Add(control);
            if (cell.Ungated) { ctx.Ungated.Add(label); ctx.Ungated.Add(control); }
        }
        ctx.Cog.AddContent(grid);
    }

    /// <summary>
    /// Closes the dialog and opens it again with fresh content (after a variant change or preset apply).
    /// DispatcherTimer rather than TryEnqueue(async lambda): the lambda detaches after the first await,
    /// which would build WinUI elements off the UI thread (freeze risk). Tick always runs on the UI thread.
    /// </summary>
    private void ReopenOsCog(OsCogContext ctx)
    {
        ctx.Cog.Hide();
        var rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        rebuildTimer.Tick += (_, _) => { rebuildTimer.Stop(); OsCogButton_Click(ctx.Sender, ctx.Args); };
        rebuildTimer.Start();
    }

    // ── Dialog ───────────────────────────────────────────────────────────────────

    private async void OsCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            var store = card.Source ?? "";
            var variant = ViewModel.GetOsVariant(card.GameName, store);
            bool isNightly = variant == "Nightly";
            bool isDlssNr  = variant == "DlssNr";
            bool isF5      = variant == "F5";
            bool isInstalled = card.IsOsInstalled;

            var cog = new CogDialog("OptiScaler", Content.XamlRoot);
            var ctx = new OsCogContext(card, cog, sender, e);

            // Intro and warning stay readable when OptiScaler isn't installed on this game.
            ctx.Ungated.Add(cog.AddIntro("Settings are written to this game's OptiScaler.ini as soon as you change them. Hover a setting for an explanation."));
            if (!isInstalled)
                ctx.Ungated.Add(cog.AddWarning(isNightly
                    ? "Install OptiScaler on this game to change these settings. The version and nightly build can be chosen before installing."
                    : "Install OptiScaler on this game to change these settings. The version can be chosen before installing."));

            // ── Version ──────────────────────────────────────────────────────
            var variantItems = new[] { "Stable", "Nightly", "DLSS NR", "F5 DLSSNR" };
            var variantDisplay = variant switch { "DlssNr" => "DLSS NR", "F5" => "F5 DLSSNR", _ => variant };
            var variantCombo = CogDialog.MakeCombo(variantItems, OsIndexOf(variantItems, variantDisplay));
            ctx.VariantCombo = variantCombo;
            var variantCell = new OsCell("OptiScaler Version",
                "Stable is the official release. Nightly is the daily build. DLSS NR is a fork with multi-pass Neural Rendering support. " +
                "F5 DLSSNR is the F5 multi-pass NR fork with a bundled forwarder. Changing it uninstalls OptiScaler from this game; install it again afterwards.",
                variantCombo, Ungated: true);
            variantCombo.SelectionChanged += (s, ev) =>
            {
                var selected = variantCombo.SelectedItem as string ?? "Stable";
                // Map display name to internal value
                var internalValue = selected switch { "DLSS NR" => "DlssNr", "F5 DLSSNR" => "F5", "Stable" => null, _ => selected };
                ViewModel.SetOsVariant(card.GameName, internalValue, store);
                if (card.IsOsInstalled && !string.IsNullOrEmpty(card.InstallPath))
                {
                    try { _optiScalerService.Uninstall(card); card.NotifyAll(); }
                    catch (Exception ex) { CrashReporter.Log($"[OsCog] Uninstall on channel switch — {ex.Message}"); }
                }
                ReopenOsCog(ctx);
            };

            // ── Framerate limit ──────────────────────────────────────────────
            var fpsLabels = new[] { "Off" }.Concat(OsVrrPresets.Select(p => p.Label)).ToArray();
            var fpsCell = OsComboCell(cog, "Framerate Limit",
                "Caps the frame rate, using Reflex where possible. Each value sits a few fps under a common refresh rate for VRR. Off removes the cap.",
                fpsLabels, OsIndexOf(fpsLabels, ReadOsFramerateLabel(card.InstallPath)),
                i =>
                {
                    var sel = fpsLabels[i];
                    if (!card.IsOsInstalled || string.IsNullOrEmpty(card.InstallPath)) return;
                    if (sel == "Off")
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit", "0");
                    else
                    {
                        var match = OsVrrPresets.FirstOrDefault(p => p.Label == sel);
                        if (match.Label != null)
                            OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit", FormatOsFloat(match.Fps));
                    }
                });
            ctx.FpsLimitCombo = fpsCell.Combo;

            // ── Upscaler: API selector + upscaler for that API ───────────────
            // Default the API combo to the game's detected graphics API
            var apiDefault = card.GraphicsApi switch
            {
                Models.GraphicsApiType.DirectX12 => "DX12",
                Models.GraphicsApiType.Vulkan    => "Vulkan",
                _                                => "DX11",
            };
            var apiItems = new[] { "DX11", "DX12", "Vulkan" };
            var apiCombo = CogDialog.MakeCombo(apiItems, OsIndexOf(apiItems, apiDefault));
            var apiUpscalerCombo = CogDialog.MakeCombo(Array.Empty<string>(), -1);
            ctx.ApiCombo = apiCombo;
            ctx.ApiUpscalerCombo = apiUpscalerCombo;

            // Refilling the list must not write the value back to the ini.
            bool refreshingUpscaler = false;
            void RefreshUpscalerCombo(string api)
            {
                refreshingUpscaler = true;
                try
                {
                    apiUpscalerCombo.ItemsSource = OsGetUpscalerOptions(api);
                    apiUpscalerCombo.SelectedItem = OsIniToUpscalerOption(api, ReadOsUpscalerIni(card.InstallPath, OsApiToIniKey(api)));
                }
                finally { refreshingUpscaler = false; }
            }
            RefreshUpscalerCombo(apiDefault);

            var apiCell = new OsCell("Upscaler API",
                "Which graphics API the Upscaler setting applies to. Starts on the API detected for this game.", apiCombo);
            var upscalerCell = new OsCell("Upscaler",
                "Upscaler OptiScaler uses for the selected API. Auto lets OptiScaler choose based on your GPU.", apiUpscalerCombo);

            // Nightly: version and build share a row (both usable before installing), framerate limit goes last.
            if (isNightly)
                AddOsGrid(ctx, variantCell, BuildOsNightlyBuildCell(ctx, isInstalled), apiCell, upscalerCell, fpsCell, null);
            else
                AddOsGrid(ctx, variantCell, fpsCell, apiCell, upscalerCell);

            apiCombo.SelectionChanged += (s, ev) => RefreshUpscalerCombo(apiCombo.SelectedItem as string ?? "DX11");
            apiUpscalerCombo.SelectionChanged += (s, ev) =>
            {
                if (refreshingUpscaler) return;
                if (apiUpscalerCombo.SelectedItem is not string sel || string.IsNullOrEmpty(card.InstallPath)) return;
                var api = apiCombo.SelectedItem as string ?? "DX11";
                try
                {
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Upscalers", OsApiToIniKey(api), OsUpscalerOptionToIni(api, sel));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[OsCog] Upscaler write failed — {ex.Message}");
                    cog.SetError($"Couldn't save Upscaler: {ex.Message}");
                }
            };

            // ── Variant-specific sections ────────────────────────────────────
            bool hasAdvanced = isNightly || isDlssNr || isF5;
            if (hasAdvanced)
            {
                BuildOsFrameGenSection(ctx);
                BuildOsAdditionalSection(ctx);
                if (card.EngineHint?.Contains("Unreal", StringComparison.OrdinalIgnoreCase) == true)
                    BuildOsEngineIniSection(ctx);
                if (isDlssNr || isF5)
                    BuildOsNeuralRenderingSection(ctx);
            }

            // Not installed: only the version (and nightly build) stay usable
            if (!isInstalled)
            {
                static void Dim(FrameworkElement fe)
                {
                    fe.IsHitTestVisible = false;
                    fe.Opacity = CogDialog.DisabledOpacity;
                }
                foreach (var child in cog.Body.Children.OfType<FrameworkElement>())
                {
                    if (ctx.Ungated.Contains(child)) continue;
                    // Settings grids mix usable and unusable cells: dim cell by cell
                    if (child is Grid grid && grid.Children.Any(c => ctx.Ungated.Contains(c)))
                    {
                        foreach (var cell in grid.Children.OfType<FrameworkElement>())
                            if (!ctx.Ungated.Contains(cell)) Dim(cell);
                        continue;
                    }
                    Dim(child);
                }
            }

            var footer = BuildOsCogFooter(ctx, hasAdvanced, isInstalled);
            await cog.ShowAsync(footer);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.OsCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>Nightly Build cell: pin a nightly build (only changeable while OptiScaler isn't installed).</summary>
    private OsCell BuildOsNightlyBuildCell(OsCogContext ctx, bool isInstalled)
    {
        var card = ctx.Card;
        var store = ctx.Store;
        var currentBuild = ViewModel.GetOsNightlyBuild(card.GameName, store);
        var availableBuilds = _optiScalerService.AvailableNightlyBuilds;

        var buildItems = new List<string> { "Latest (auto-update)" };
        foreach (var b in availableBuilds)
        {
            if (b.Length == 8 && b.All(char.IsDigit))
                buildItems.Add($"{b[..4]}-{b[4..6]}-{b[6..8]}");
        }
        if (availableBuilds.Count == 0)
            buildItems.Add("(fetching builds...)");

        string selectedBuildItem = "Latest (auto-update)";
        if (!string.IsNullOrEmpty(currentBuild) && currentBuild.Length == 8 && currentBuild.All(char.IsDigit))
            selectedBuildItem = $"{currentBuild[..4]}-{currentBuild[4..6]}-{currentBuild[6..8]}";

        var buildCombo = CogDialog.MakeCombo(buildItems, OsIndexOf(buildItems, selectedBuildItem, 0), isEnabled: !isInstalled);
        buildCombo.Opacity = isInstalled ? CogDialog.DisabledOpacity : 1.0;

        var cell = new OsCell("Nightly Build", isInstalled
            ? "Uninstall OptiScaler first to change the build."
            : "Pin a specific nightly build. Pinned games are skipped by auto-update. Latest always uses the newest build.",
            buildCombo, Ungated: true);

        buildCombo.SelectionChanged += async (s, ev) =>
        {
            try
            {
                var sel = buildCombo.SelectedItem as string ?? "Latest (auto-update)";
                if (sel == "Latest (auto-update)" || sel == "(fetching builds...)")
                {
                    ViewModel.SetOsNightlyBuild(card.GameName, null, store);
                    return;
                }
                var buildDate = sel.Replace("-", "");
                if (buildDate.Length != 8 || !buildDate.All(char.IsDigit)) return;

                ViewModel.SetOsNightlyBuild(card.GameName, buildDate, store);
                CrashReporter.Log($"[OsCog] Pinned nightly build {buildDate} for '{card.GameName}'");

                if (!_optiScalerService.IsNightlyBuildStaged(buildDate))
                {
                    CrashReporter.Log($"[OsCog] Build {buildDate} not staged — downloading on demand");
                    await _optiScalerService.EnsureNightlyBuildStagingAsync(buildDate);
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OsCog] Nightly build pin failed — {ex.Message}");
                ctx.Cog.SetError($"Couldn't pin the nightly build: {ex.Message}");
            }
        };
        return cell;
    }
}
