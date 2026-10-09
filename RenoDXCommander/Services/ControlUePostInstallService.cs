// ControlUePostInstallService.cs
// Special post-install steps for the Control Ultimate Edition RenoDX mod
// (renodx-control-rr.addon64). Triggered from all three install paths:
// normal InstallModAsync, drag-drop, and watch folder.
//
// Steps performed after the addon file is placed:
//   1. Upgrade nvngx_dlss.dll  → RHI's newest cached version (sentinel backup)
//   2. Deploy nvngx_dlssd.dll  → RHI's newest cached version (sentinel backup)
//   3. Edit renderer.ini       → set "m_eHDRPreset": 2
//   4. Clear SR DLSS preset    → delete from NVIDIA driver profile (inherits global)

using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

/// <summary>
/// Result of the Control UE install dialog — carries user selections.
/// </summary>
public record ControlUeInstallOptions(
    bool Proceed,
    bool InstallOptiScalerFg,
    bool UseHdr);

public static class ControlUePostInstallService
{
    /// <summary>
    /// The exact addon filename that triggers this special install flow.
    /// </summary>
    public const string TriggerAddonFileName = "renodx-control-rr.addon64";

    /// <summary>
    /// The exact game name as detected by RHI (Steam folder name).
    /// </summary>
    public const string GameName = "Control Ultimate Edition";

    /// <summary>
    /// Returns true when <paramref name="addonFileName"/> matches the Control UE trigger.
    /// Case-insensitive.
    /// </summary>
    public static bool IsControlAddon(string addonFileName)
        => addonFileName.Equals(TriggerAddonFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shows the bespoke pre-install dialog for Control Ultimate Edition.
    /// Returns install options including whether the user confirmed and which extra features to install.
    /// </summary>
    public static async Task<ControlUeInstallOptions> ShowInstallDialogAsync(string installPath)
    {
        var noOp = new ControlUeInstallOptions(false, false, false);
        try
        {
            Microsoft.UI.Xaml.XamlRoot? xamlRoot = null;
            if (Microsoft.UI.Xaml.Application.Current is App app)
            {
                var field = typeof(App).GetField("_window",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(app) is MainWindow mw)
                    xamlRoot = mw.Content?.XamlRoot;
            }
            if (xamlRoot == null) return new ControlUeInstallOptions(true, false, false);

            var content = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 10 };

            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "⚠ This is NOT an HDR mod.",
                FontSize = 14,
                FontWeight = new Windows.UI.Text.FontWeight(700),
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });

            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "It fixes RT noise using Ray Reconstruction. Two strategies (pick one, they are mutually exclusive):\n"
                     + "  •  Turn off the in-game RT denoiser and use DLSS Super Resolution preset M or L\n"
                     + "  •  Use Ray Reconstruction with extra inputs derived from the game's shaders "
                     + "(game denoiser is turned off here too — RR needs that)\n\n"
                     + "⚠ DLSS, Ray Tracing, and SSAO must all be enabled in-game for Ray Reconstruction to work correctly.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });

            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Clicking Install will also:",
                FontSize = 13,
                FontWeight = new Windows.UI.Text.FontWeight(600),
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 0),
            });

            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "  •  Upgrade nvngx_dlss.dll to the newest available version\n"
                     + "  •  Deploy nvngx_dlssd.dll (DLSS Ray Reconstruction runtime)\n"
                     + "  •  Set renderer.ini HDR preset to the correct value\n"
                     + "  •  Clear the DLSS SR preset set in the NVIDIA driver profile for this game\n"
                     + "  •  If OptiScaler FG = Yes: installs OptiScaler Nightly build 2026-10-04 with Frame Generation pre-configured, deploys Streamline and nvngx_dlssg.dll, renames OptiScaler to winmm.dll and ReShade to dxgi.dll, and applies all required FG INI settings",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });

            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "These extra changes are not reverted when uninstalling the mod.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 0),
            });

            // ── OptiScaler FG + HDR combos ────────────────────────────────────
            content.Children.Add(new Microsoft.UI.Xaml.Controls.Border
            {
                Height = 1,
                Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 2),
            });

            var comboRow = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 16 };
            comboRow.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
            comboRow.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });

            // Left: OptiScaler FG
            var optiStack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 4 };
            optiStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Install OptiScaler FG",
                FontSize = 12,
                FontWeight = new Windows.UI.Text.FontWeight(600),
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            var optiCombo = new Microsoft.UI.Xaml.Controls.ComboBox
            {
                ItemsSource = new[] { "No", "Yes" },
                SelectedIndex = 0,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                MaxDropDownHeight = 300,
            };
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(optiCombo,
                "Installs OptiScaler Nightly with Frame Generation configured for Control UE.\nRenames OptiScaler to winmm.dll and ReShade to dxgi.dll.");
            optiStack.Children.Add(optiCombo);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(optiStack, 0);
            comboRow.Children.Add(optiStack);

            // Right: Using HDR? (greyed unless OptiScaler = Yes)
            var hdrStack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 4 };
            hdrStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Using HDR?",
                FontSize = 12,
                FontWeight = new Windows.UI.Text.FontWeight(600),
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            var hdrCombo = new Microsoft.UI.Xaml.Controls.ComboBox
            {
                ItemsSource = new[] { "No", "Yes" },
                SelectedIndex = 0,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                MaxDropDownHeight = 300,
                IsEnabled = false,
                Opacity = 0.45,
            };
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(hdrCombo,
                "If Yes, writes FgSlHdr10=1 to reshade.ini so the FG shader uses HDR10. Only available when installing OptiScaler FG.");
            hdrStack.Children.Add(hdrCombo);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(hdrStack, 1);
            comboRow.Children.Add(hdrStack);

            content.Children.Add(comboRow);

            // Wire OptiScaler combo → enable/disable HDR combo
            optiCombo.SelectionChanged += (s, e) =>
            {
                bool optiOn = optiCombo.SelectedItem as string == "Yes";
                hdrCombo.IsEnabled = optiOn;
                hdrCombo.Opacity   = optiOn ? 1.0 : 0.45;
                if (!optiOn) hdrCombo.SelectedIndex = 0;
            };

            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "Control Ultimate Edition — RenoDX Mod",
                Content = content,
                PrimaryButtonText = "Install",
                CloseButtonText = "Cancel",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(dialog);
            if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                return noOp;

            return new ControlUeInstallOptions(
                Proceed: true,
                InstallOptiScalerFg: optiCombo.SelectedItem as string == "Yes",
                UseHdr: hdrCombo.SelectedItem as string == "Yes");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] ShowInstallDialogAsync failed — {ex.Message}");
            return new ControlUeInstallOptions(true, false, false);
        }
    }

    /// <summary>
    /// Runs all post-install steps for Control Ultimate Edition.
    /// Safe to call on any thread — all file operations are synchronous after awaiting DLSS cache.
    /// Each step is independent: failures are logged and do not abort subsequent steps.
    /// </summary>
    /// <param name="gameName">Card game name (used for NVIDIA profile lookup).</param>
    /// <param name="installPath">Game install directory (root, where the addon was deployed).</param>
    /// <param name="options">User selections from the install dialog.</param>
    public static async Task RunAsync(string gameName, string installPath,
        ControlUeInstallOptions? options = null)
    {
        CrashReporter.Log($"[ControlUePostInstall] Running post-install steps for '{gameName}' at '{installPath}'");

        // ── Step 1: Upgrade nvngx_dlss.dll ────────────────────────────────────
        await UpgradeDlssAsync(installPath);

        // ── Step 2: Deploy nvngx_dlssd.dll ────────────────────────────────────
        await DeployDlssdAsync(installPath);

        // ── Step 3: Edit renderer.ini — set m_eHDRPreset to 2 ─────────────────
        PatchRendererIni(installPath);

        // ── Step 4 & 5: Clear SR DLSS preset and render scale from driver ──────
        ClearNvidiaProfileSettings(gameName, installPath);

        // ── Step 6: Install OptiScaler Nightly FG (optional) ──────────────────
        if (options?.InstallOptiScalerFg == true)
            await InstallOptiScalerFgAsync(gameName, installPath);

        // ── Step 7: Write FgSlHdr10 to reshade.ini preset section ─────────────
        if (options?.InstallOptiScalerFg == true)
            WriteHdrPresetKey(installPath, options.UseHdr);

        CrashReporter.Log($"[ControlUePostInstall] All steps complete for '{gameName}'");
    }

    // ── Step 1 ────────────────────────────────────────────────────────────────

    private static async Task UpgradeDlssAsync(string installPath)
    {
        try
        {
            var dlssSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
            var newestDlssPath = await dlssSvc.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
            if (newestDlssPath == null || !File.Exists(newestDlssPath))
            {
                CrashReporter.Log("[ControlUePostInstall] Step 1 skipped — newest nvngx_dlss.dll not cached");
                return;
            }

            var destPath = Path.Combine(installPath, "nvngx_dlss.dll");
            AuxInstallService.SentinelBackup(destPath);
            File.Copy(newestDlssPath, destPath, overwrite: true);
            CrashReporter.Log($"[ControlUePostInstall] Step 1 — deployed nvngx_dlss.dll to '{destPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] Step 1 failed — {ex.Message}");
        }
    }

    // ── Step 2 ────────────────────────────────────────────────────────────────

    private static async Task DeployDlssdAsync(string installPath)
    {
        try
        {
            var dlssSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
            var newestDlssdPath = await dlssSvc.EnsureNewestDlssdCachedAsync().ConfigureAwait(false);
            if (newestDlssdPath == null || !File.Exists(newestDlssdPath))
            {
                CrashReporter.Log("[ControlUePostInstall] Step 2 skipped — newest nvngx_dlssd.dll not cached");
                return;
            }

            var destPath = Path.Combine(installPath, "nvngx_dlssd.dll");
            AuxInstallService.SentinelBackup(destPath);
            File.Copy(newestDlssdPath, destPath, overwrite: true);
            CrashReporter.Log($"[ControlUePostInstall] Step 2 — deployed nvngx_dlssd.dll to '{destPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] Step 2 failed — {ex.Message}");
        }
    }

    // ── Step 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds renderer.ini in <paramref name="installPath"/> and changes
    /// "m_eHDRPreset": &lt;any value&gt; → "m_eHDRPreset": 2.
    /// The file is a JSON-like settings file; the key may have any integer value.
    /// </summary>
    private static void PatchRendererIni(string installPath)
    {
        try
        {
            var iniPath = Path.Combine(installPath, "renderer.ini");
            if (!File.Exists(iniPath))
            {
                CrashReporter.Log($"[ControlUePostInstall] Step 3 skipped — renderer.ini not found at '{iniPath}'");
                return;
            }

            var content = File.ReadAllText(iniPath);

            // Match "m_eHDRPreset": <digits> and replace with value 2.
            // Uses regex so it handles any spacing and any current integer value.
            var patched = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"""m_eHDRPreset""\s*:\s*\d+",
                @"""m_eHDRPreset"": 2");

            if (patched == content)
            {
                CrashReporter.Log("[ControlUePostInstall] Step 3 — m_eHDRPreset key not found or already at 2, no change");
                return;
            }

            File.WriteAllText(iniPath, patched);
            CrashReporter.Log($"[ControlUePostInstall] Step 3 — patched renderer.ini: m_eHDRPreset set to 2");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] Step 3 failed — {ex.Message}");
        }
    }

    // ── Steps 4 & 5 ──────────────────────────────────────────────────────────

    private static void ClearNvidiaProfileSettings(string gameName, string installPath)
    {
        try
        {
            var presetSvc = App.Services.GetRequiredService<DlssPresetService>();

            if (!presetSvc.IsSupported)
            {
                CrashReporter.Log("[ControlUePostInstall] Steps 4 & 5 skipped — NVIDIA profile API not supported");
                return;
            }

            // Step 4: Clear SR preset (0 = delete setting → inherits from global/base profile)
            presetSvc.SetSrPreset(gameName, installPath, 0u);
            CrashReporter.Log("[ControlUePostInstall] Step 4 — cleared SR DLSS preset from driver profile");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] Steps 4 & 5 failed — {ex.Message}");
        }
    }

    // ── Step 6: Install OptiScaler Nightly FG ─────────────────────────────────

    private static async Task InstallOptiScalerFgAsync(string gameName, string installPath)
    {
        try
        {
            CrashReporter.Log($"[ControlUePostInstall] Step 6 — installing OptiScaler Nightly FG for '{gameName}'");

            var optiSvc  = App.Services.GetRequiredService<IOptiScalerService>();
            var dllSvc   = App.Services.GetRequiredService<IDllOverrideService>();
            var vm       = App.Services.GetRequiredService<ViewModels.MainViewModel>();

            // Find the card for this game
            var card = vm.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            if (card == null)
            {
                CrashReporter.Log($"[ControlUePostInstall] Step 6 skipped — card not found for '{gameName}'");
                return;
            }

            var store = card.Source ?? "";

            // ── Pre-configure settings before install ──────────────────────────
            // Set variant to Nightly so InstallAsync uses the nightly staging dir
            vm.SetOsVariant(gameName, "Nightly", store);

            // Set DLL naming overrides: OptiScaler → winmm.dll, ReShade → dxgi.dll
            // These are read by GetEffectiveOsName/GetEffectiveRsName during InstallAsync
            dllSvc.SetOsDllOverride(gameName, "winmm.dll");
            vm.SetDllOverride(gameName, "dxgi.dll", "");
            vm.SaveSettingsPublic();

            // Pre-configure FG settings so they're persisted and applied post-install
            vm.SetOsDeployStreamline(gameName, true, store);
            vm.SetOsDeployDlssEnabler(gameName, true, store);
            vm.SetOsFgInput(gameName, "upscaler", store);
            vm.SetOsFgOutput(gameName, "dlssg", store);
            vm.SetOsFgNvngxReplacement(gameName, "None", store);

            // Pin to the known-good nightly build for Control UE FG
            const string ControlUeRequiredNightlyBuild = "20261004";
            vm.SetOsNightlyBuild(gameName, ControlUeRequiredNightlyBuild, store);

            // ── Ensure the pinned build is staged ─────────────────────────────
            if (!optiSvc.IsNightlyBuildStaged(ControlUeRequiredNightlyBuild))
            {
                CrashReporter.Log($"[ControlUePostInstall] Step 6 — staging nightly build {ControlUeRequiredNightlyBuild}...");
                await optiSvc.EnsureNightlyBuildStagingAsync(ControlUeRequiredNightlyBuild).ConfigureAwait(false);
            }

            // ── Run the install ────────────────────────────────────────────────
            var gpuType    = vm.Settings.OsGpuType;
            var dlssInputs = vm.Settings.OsDlssInputs;
            var hotkey     = vm.Settings.OsHotkey;

            var record = await optiSvc.InstallAsync(
                card,
                progress: null,
                gpuType: gpuType,
                dlssInputs: dlssInputs,
                hotkey: hotkey,
                variant: "Nightly",
                nightlyBuildHint: ControlUeRequiredNightlyBuild).ConfigureAwait(false);

            if (record == null)
            {
                CrashReporter.Log("[ControlUePostInstall] Step 6 — OptiScaler install returned null (staging not ready?)");
                return;
            }

            // Update the card's displayed version immediately
            if (!string.IsNullOrEmpty(record.OsNightlyBuild))
                card.OsInstalledVersion = record.OsNightlyBuild;

            // Clear DLSS skip cache so next scan detects the deployed DLLs
            var dlssSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
            dlssSvc.RecordDlssFound(gameName);

            // Deploy Streamline to OptiScaler folder (also deploys nvngx_dlssg.dll)
            optiSvc.DeployStreamlineToGame(installPath);

            // Deploy DLSS Enabler
            var dlssEnablerSvc = App.Services.GetRequiredService<DlssEnablerService>();
            var optiScalerDir  = Path.Combine(installPath, "OptiScaler");
            _ = dlssEnablerSvc.InstallAsync(optiScalerDir);

            // ── Apply OptiScaler INI settings ──────────────────────────────────
            // [Upscalers]
            OptiScalerService.SetOptiScalerIniValue(installPath, "Upscalers", "Dx12Upscaler", "dlss");

            // [FrameGen]
            OptiScalerService.SetOptiScalerIniValue(installPath, "FrameGen", "Enabled",   "true");
            OptiScalerService.SetOptiScalerIniValue(installPath, "FrameGen", "FGInput",   "upscaler");
            OptiScalerService.SetOptiScalerIniValue(installPath, "FrameGen", "FGOutput",  "dlssg");

            // [fakenvapi]
            OptiScalerService.SetOptiScalerIniValue(installPath, "fakenvapi", "ForceReflex", "2");

            // [DLSSG]
            OptiScalerService.SetOptiScalerIniValue(installPath, "DLSSG", "UseGamesReflexMarkers", "false");

            // ── Persist DLL override on the card so it shows in Game Overrides ──
            card.DllOverrideEnabled = true;
            card.ExcludeFromUpdateAllReShade = true;
            card.NotifyAll();

            // Rebuild the detail panel so Extras and Game Overrides reflect the new state
            var mwField = typeof(App).GetField("_window",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (mwField?.GetValue(Microsoft.UI.Xaml.Application.Current) is MainWindow mw)
            {
                mw.DispatcherQueue?.TryEnqueue(() =>
                {
                    mw.PopulateDetailPanel(card);
                    mw.BuildOverridesPanel(card);
                });
            }

            CrashReporter.Log("[ControlUePostInstall] Step 6 — OptiScaler Nightly FG installed successfully");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] Step 6 failed — {ex.Message}");
        }
    }

    // ── Step 7: Write FgSlHdr10 to reshade.ini ────────────────────────────────

    private static void WriteHdrPresetKey(string installPath, bool hdrEnabled)
    {
        try
        {
            var iniPath = Path.Combine(installPath, "reshade.ini");
            if (!File.Exists(iniPath))
            {
                CrashReporter.Log($"[ControlUePostInstall] Step 7 skipped — reshade.ini not found at '{iniPath}'");
                return;
            }

            var value = hdrEnabled ? "1" : "0";
            var lines = File.ReadAllLines(iniPath).ToList();
            bool inPreset1 = false;
            int keyIdx = -1;
            int headerIdx = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("["))
                {
                    inPreset1 = t.Equals("[renodx-preset1]", StringComparison.OrdinalIgnoreCase);
                    if (inPreset1) headerIdx = i;
                }
                else if (inPreset1 && t.StartsWith("FgSlHdr10=", StringComparison.OrdinalIgnoreCase))
                {
                    keyIdx = i;
                    break;
                }
            }

            if (keyIdx >= 0)
                lines[keyIdx] = $"FgSlHdr10={value}";
            else if (headerIdx >= 0)
                lines.Insert(headerIdx + 1, $"FgSlHdr10={value}");
            else
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add("");
                lines.Add("[renodx-preset1]");
                lines.Add($"FgSlHdr10={value}");
            }

            File.WriteAllLines(iniPath, lines);
            CrashReporter.Log($"[ControlUePostInstall] Step 7 — wrote FgSlHdr10={value} to reshade.ini");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ControlUePostInstall] Step 7 failed — {ex.Message}");
        }
    }
}
