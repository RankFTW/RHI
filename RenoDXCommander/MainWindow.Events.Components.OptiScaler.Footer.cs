// MainWindow.Events.Components.OptiScaler.Footer.cs — Pinned bottom area of the OptiScaler cog dialog:
// preset slots (Nightly / DLSS NR / F5), Deploy OptiScaler.ini and tips. Stays visible while the body scrolls.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private FrameworkElement BuildOsCogFooter(OsCogContext ctx, bool showPresets, bool isInstalled)
    {
        var card = ctx.Card;
        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush) });

        // Status line for footer actions. The dialog's own status line sits at the end of the
        // scrolling body, where it can be out of view while the footer is visible.
        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        void ShowStatus(string text, bool ok)
        {
            status.Text = text;
            status.Foreground = UIFactory.Brush(ok ? ResourceKeys.AccentGreenBrush : ResourceKeys.AccentRedBrush);
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        if (showPresets)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "Presets",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            footer.Children.Add(BuildOsPresetSlots(ctx, ShowStatus));
            footer.Children.Add(MakeOsFooterNote(
                "Save stores the settings above in a slot shared by all games. Click a slot's name to apply it to this game."));
        }

        footer.Children.Add(MakeOsFooterNote(
            "Deploy only if the game has no OptiScaler.ini yet or you want a fresh one: it replaces the file and resets most settings above."));

        var iniPath = string.IsNullOrEmpty(card.InstallPath) ? null : Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
        var openBtn = ctx.Cog.MakeOpenFileButton("Open OptiScaler.ini", iniPath);
        var deployBtn = ctx.Cog.MakeButton("Deploy OptiScaler.ini", CogButtonKind.Primary, () =>
        {
            DeployOsIniFromCog(card, ShowStatus);
            openBtn.IsEnabled = iniPath != null && File.Exists(iniPath);
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(deployBtn);
        buttons.Children.Add(openBtn);
        footer.Children.Add(buttons);

        footer.Children.Add(MakeOsFooterNote(
            "Crashing with ReShade and OptiScaler both installed? Rename ReShade (e.g. to d3d12.dll) with DLL Naming Overrides in the Overrides panel."));

        // Not installed: presets and Deploy are unavailable
        if (!isInstalled)
        {
            foreach (var child in footer.Children.OfType<FrameworkElement>())
            {
                child.IsHitTestVisible = false;
                child.Opacity = CogDialog.DisabledOpacity;
            }
        }

        footer.Children.Add(status);
        return footer;
    }

    private static TextBlock MakeOsFooterNote(string text) => new()
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
    };

    /// <summary>Copies the OptiScaler.ini template into the game and re-applies the saved FG settings.</summary>
    private void DeployOsIniFromCog(GameCardViewModel card, Action<string, bool> showStatus)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            _optiScalerService.CopyIniToGame(card, ViewModel.Settings.OsHotkey,
                ViewModel.Settings.OsGpuType, ViewModel.Settings.OsDlssInputs);
            // Apply persisted FG settings after copying the INI
            var store = card.Source ?? "";
            var fgInput = ViewModel.GetOsFgInput(card.GameName, store);
            var fgOutput = ViewModel.GetOsFgOutput(card.GameName, store);
            var fgNvngx = ViewModel.GetOsFgNvngxReplacement(card.GameName, store);
            OptiScalerService.ApplyFgSettings(card.InstallPath, fgInput, fgOutput, fgNvngx);
            showStatus("OptiScaler.ini copied to the game folder.", true);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DeployOsIniFromCog] Failed for '{card.GameName}' — {ex.Message}");
            showStatus($"Couldn't deploy OptiScaler.ini: {ex.Message}", false);
        }
    }

    // ── Presets ──────────────────────────────────────────────────────────────────

    private Grid BuildOsPresetSlots(OsCogContext ctx, Action<string, bool> showStatus)
    {
        // Load global presets from disk
        var presets = OsPresetService.Load();

        var presetsGrid = new Grid { ColumnSpacing = 8 };
        for (int c = 0; c < 4; c++)
            presetsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int slotIndex = 0; slotIndex < 4; slotIndex++)
        {
            int i = slotIndex; // capture for lambdas
            var preset = presets[i];
            bool hasData = preset != null;

            // Name row: [TextBox][Save]
            var nameBox = CogDialog.MakeTextBox(preset?.Name ?? "", $"Slot {i + 1}");
            nameBox.MinWidth = 0;
            nameBox.Padding = new Thickness(6, 4, 6, 4);

            var saveBtn = CogDialog.StyledButton("Save", CogButtonKind.Secondary, compact: true);
            ToolTipService.SetToolTip(saveBtn, "Save the current settings into this slot");

            var nameRow = CogDialog.Inline(nameBox, saveBtn);
            nameRow.ColumnSpacing = 4;

            // Apply button
            var applyBtn = CogDialog.StyledButton(hasData ? (preset!.Name ?? $"Slot {i + 1}") : $"Slot {i + 1}", CogButtonKind.Secondary, compact: true);
            applyBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            applyBtn.IsEnabled = hasData;
            applyBtn.Opacity = hasData ? 1.0 : CogDialog.DisabledOpacity;
            ToolTipService.SetToolTip(applyBtn, hasData
                ? "Apply this preset to the current game"
                : "No preset saved yet. Click Save to store the current settings.");

            // Name editing
            nameBox.TextChanged += (s, ev) =>
            {
                if (presets[i] == null) presets[i] = new OsPreset();
                presets[i]!.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text;
                applyBtn.Content = presets[i]!.Name ?? $"Slot {i + 1}";
                OsPresetService.Save(presets);
            };

            // Save
            saveBtn.Click += (s, ev) =>
            {
                try
                {
                    if (presets[i] == null) presets[i] = new OsPreset();
                    var p = presets[i]!;
                    CaptureOsPreset(ctx, p); // preserves the existing name
                    OsPresetService.Save(presets);
                    applyBtn.IsEnabled = true;
                    applyBtn.Opacity = 1.0;
                    applyBtn.Content = p.Name ?? $"Slot {i + 1}";
                    ToolTipService.SetToolTip(applyBtn, "Apply this preset to the current game");
                    showStatus($"Saved the current settings to {p.Name ?? $"Slot {i + 1}"}.", true);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OsPreset.Save] Slot {i + 1} — {ex.Message}");
                    showStatus($"Couldn't save the preset: {ex.Message}", false);
                }
            };

            // Apply
            applyBtn.Click += (s, ev) =>
            {
                var p = presets[i];
                if (p == null) return;
                try
                {
                    ApplyOsPreset(ctx.Card, p);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OsPreset.Apply] Slot {i + 1} — {ex.Message}");
                    showStatus($"Couldn't apply the preset: {ex.Message}", false);
                    return;
                }
                ReopenOsCog(ctx);
            };

            var slotPanel = new StackPanel { Spacing = 4 };
            slotPanel.Children.Add(nameRow);
            slotPanel.Children.Add(applyBtn);
            Grid.SetColumn(slotPanel, i);
            presetsGrid.Children.Add(slotPanel);
        }
        return presetsGrid;
    }

    /// <summary>Copies the dialog's current selections into <paramref name="p"/> (name untouched).</summary>
    private static void CaptureOsPreset(OsCogContext ctx, OsPreset p)
    {
        string? capturedFgInput     = ctx.FgInputCombo?.SelectedItem is string fgi ? OsFgInputToIni(fgi) : null;
        string? capturedFgOutput    = ctx.FgOutputCombo?.SelectedItem is string fgo ? OsFgOutputToIni(fgo) : null;
        string? capturedFgNvngx     = ctx.FgNvngxCombo?.SelectedItem is string fgns ? OsFgNvngxToIni(fgns) : null;
        bool?   capturedDeploySl    = ctx.CombinedCombo?.SelectedItem as string == "Yes";
        string? capturedSrPreset    = ctx.SrPresetCombo?.SelectedItem is string srp ? (OsSrPresetMap.FirstOrDefault(m => m.Label == srp).Value ?? "auto") : null;
        string? capturedRrPreset    = ctx.RrPresetCombo?.SelectedItem is string rrp ? (OsRrPresetMap.FirstOrDefault(m => m.Label == rrp).Value ?? "auto") : null;
        string? capturedRenderScale = ctx.RsCombo?.SelectedItem as string;
        bool?   capturedFlip        = ctx.FlipCombo?.SelectedItem as string == "On";
        string? capturedHudFix      = ctx.HudFixCombo?.SelectedItem is string hf ? (hf == "On" ? "true" : hf == "Off" ? "false" : "auto") : null;

        string? capturedVariant     = ctx.VariantCombo?.SelectedItem as string; // "Stable", "Nightly", "DLSS NR", "F5 DLSSNR"
        string? capturedUpscalerApi = ctx.ApiCombo?.SelectedItem as string;     // "DX11", "DX12", "Vulkan"
        string? capturedUpscaler    = ctx.ApiCombo?.SelectedItem is string upApi && ctx.ApiUpscalerCombo?.SelectedItem is string upSel
                                      ? OsUpscalerOptionToIni(upApi, upSel) : null;
        string? capturedFgEnabled   = ctx.FgEnabledCombo?.SelectedItem is string fge
                                      ? (fge == "True" ? "true" : fge == "Auto (false)" ? "auto" : null) : null;
        string? capturedForceReflex = ctx.ForceReflexCombo?.SelectedItem is string fr ? fr switch
                                      { "Force Disable (1)" => "1", "Force Enable (2)" => "2", _ => "0" } : null;
        string? capturedReflexMarkers = ctx.ReflexMarkersCombo?.SelectedItem is string rm
                                        ? (rm == "False" ? "false" : "true") : null;
        float?  capturedFps         = null;
        if (ctx.FpsLimitCombo?.SelectedItem is string fpsSel)
        {
            if (fpsSel == "Off") capturedFps = 0f;
            else { var m = OsVrrPresets.FirstOrDefault(v => v.Label == fpsSel); if (m.Label != null) capturedFps = m.Fps; }
        }
        // NR fields (only non-null for DLSS NR / F5)
        string? capturedNrRuntime      = ctx.NrRuntimeCombo?.SelectedItem as string;
        string? capturedNrEnabled      = ctx.NrEnabledCombo?.SelectedItem is string ne ? (ne == "On" ? "true" : ne == "Off" ? "false" : "auto") : null;
        string? capturedNrRunBeforeSr  = ctx.NrRunBeforeSrCombo?.SelectedItem is string rb ? (rb == "On" ? "true" : rb == "Off" ? "false" : "auto") : null;
        string? capturedNrPasses       = ctx.NrPassesCombo?.SelectedItem is string np && np != "Default" ? np : ctx.NrPassesCombo != null ? "auto" : null;
        string? capturedNrWorkingScale = ctx.NrWorkingScaleCombo?.SelectedItem is string nws
                                         ? (OsNrScaleMap.FirstOrDefault(m => m.Label == nws).Value ?? "auto") : null;
        string? capturedNrFinishedPic  = ctx.NrFinishedPicCombo?.SelectedItem is string nfp ? (nfp == "On" ? "true" : "false") : null;

        p.FgInput               = capturedFgInput;
        p.FgOutput              = capturedFgOutput;
        p.FgNvngxReplacement    = capturedFgNvngx;
        p.DeployStreamline      = capturedDeploySl;
        p.DeployDlssEnabler     = capturedDeploySl; // same toggle
        p.SrPreset              = capturedSrPreset;
        p.RrPreset              = capturedRrPreset;
        p.RenderScale           = capturedRenderScale;
        p.DisableFlipMetering   = capturedFlip;
        p.HudFix                = capturedHudFix;
        p.FramerateLimit        = capturedFps;
        p.NrRuntime             = capturedNrRuntime;
        p.NrEnabled             = capturedNrEnabled;
        p.NrRunBeforeSr         = capturedNrRunBeforeSr;
        p.NrPasses              = capturedNrPasses;
        p.NrWorkingScale        = capturedNrWorkingScale;
        p.NrFinishedPicture     = capturedNrFinishedPic;
        p.OsVariant             = capturedVariant;
        p.UpscalerApi           = capturedUpscalerApi;
        p.Upscaler              = capturedUpscaler;
        p.FgEnabled             = capturedFgEnabled;
        p.ForceReflex           = capturedForceReflex;
        p.UseGamesReflexMarkers = capturedReflexMarkers;
    }

    /// <summary>Writes a saved preset to the game (settings, OptiScaler.ini, Streamline, NR runtime).</summary>
    private void ApplyOsPreset(GameCardViewModel card, OsPreset p)
    {
        var store = card.Source ?? "";

        // Apply ViewModel settings
        if (p.FgInput != null)            ViewModel.SetOsFgInput(card.GameName, p.FgInput, store);
        if (p.FgOutput != null)           ViewModel.SetOsFgOutput(card.GameName, p.FgOutput, store);
        if (p.FgNvngxReplacement != null) ViewModel.SetOsFgNvngxReplacement(card.GameName, p.FgNvngxReplacement, store);
        if (p.DeployStreamline.HasValue)
        {
            ViewModel.SetOsDeployStreamline(card.GameName, p.DeployStreamline.Value, store);
            ViewModel.SetOsDeployDlssEnabler(card.GameName, p.DeployStreamline.Value, store);
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                try
                {
                    var d = Path.Combine(card.InstallPath, "OptiScaler");
                    if (p.DeployStreamline.Value)
                    {
                        _optiScalerService.DeployStreamlineToGame(card.InstallPath);
                        _ = _dlssEnablerService.InstallAsync(d);
                    }
                    else
                    {
                        _optiScalerService.RemoveStreamlineFromGame(card.InstallPath);
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
                    var rsMatch = OsRenderScaleMap.FirstOrDefault(rm => rm.Label == p.RenderScale);
                    if (rsMatch.Label != null)
                    {
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideEnabled", "true");
                        OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "UpscaleRatio", "UpscaleRatioOverrideValue", FormatOsFloat(rsMatch.Ratio));
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
                    OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Framerate", "FramerateLimit", FormatOsFloat(p.FramerateLimit.Value));
            }

            // NR INI values (DLSS NR / F5 variants only)
            if (p.NrEnabled         != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "Enabled",         p.NrEnabled);
            if (p.NrRunBeforeSr     != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "RunBeforeSR",     p.NrRunBeforeSr);
            if (p.NrPasses          != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "Passes",          p.NrPasses);
            if (p.NrWorkingScale    != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "WorkingScale",    p.NrWorkingScale);
            if (p.NrFinishedPicture != null) OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DlssNr", "FinishedPicture", p.NrFinishedPicture);

            // FG Enabled, Upscaler, Force Reflex, Reflex Markers
            if (p.FgEnabled != null && p.FgEnabled != "auto")
                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "FrameGen", "Enabled", p.FgEnabled);
            if (p.UpscalerApi != null && p.Upscaler != null)
                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "Upscalers", OsApiToIniKey(p.UpscalerApi), p.Upscaler);
            if (p.ForceReflex != null)
                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "fakenvapi", "ForceReflex", p.ForceReflex);
            if (p.UseGamesReflexMarkers != null)
                OptiScalerService.SetOptiScalerIniValue(card.InstallPath, "DLSSG", "UseGamesReflexMarkers", p.UseGamesReflexMarkers);
        }

        // OsVariant — change if preset has one
        if (p.OsVariant != null)
        {
            var internalVariant = p.OsVariant switch { "DLSS NR" => "DlssNr", "F5 DLSSNR" => "F5", "Stable" => null, _ => p.OsVariant };
            ViewModel.SetOsVariant(card.GameName, internalVariant, store);
        }

        // NR runtime swap (outside InstallPath guard — needs async)
        if (p.NrRuntime != null)
        {
            ViewModel.SetOsNrRuntime(card.GameName, p.NrRuntime, store);
            if (card.IsOsInstalled && !string.IsNullOrEmpty(card.InstallPath))
            {
                var dlssNrSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
                var gameNrPath = Path.Combine(card.InstallPath, "nvngx_dlssnr.dll");
                var nrRuntime = p.NrRuntime;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var cachedDll = await dlssNrSvc.EnsureSpecificDlssnrCachedAsync(nrRuntime).ConfigureAwait(false);
                        if (cachedDll != null) { AuxInstallService.SentinelBackup(gameNrPath); File.Copy(cachedDll, gameNrPath, overwrite: true); }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[OsPreset.Apply] NR runtime swap failed — {ex.Message}"); }
                });
            }
        }
    }
}
