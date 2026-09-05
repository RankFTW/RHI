// DetailPanelBuilder.Overrides.NvidiaProfile.cs — Nvidia Profile Overrides header + DLSS/Streamline section.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildNvidiaProfileSection(GameCardViewModel card, string capturedName)
    {
        // ══════════════════════════════════════════════════════════════════════
        // Nvidia Profile Overrides — separate section below Overrides
        // DLSS / Streamline / ReBAR + future additions
        // ══════════════════════════════════════════════════════════════════════
        _window.NvidiaProfilePanel.Children.Clear();

        // ── Collapsible header ────────────────────────────────────────────────
        const string nvSectionKey = "NvidiaProfile";
        var nvSettings   = _window.ViewModel.Settings;
        bool nvCollapsed = nvSettings.CollapsedDetailSections.Contains(nvSectionKey);

        var nvidiaHeaderText = "Nvidia Profile Overrides";
        var driverVer = _dlssPresetService.DriverVersionString;
        if (!string.IsNullOrEmpty(driverVer))
            nvidiaHeaderText += $" — Driver {driverVer}";

        var nvArrow = new TextBlock
        {
            Text      = nvCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var nvTitle = new TextBlock
        {
            Text       = nvidiaHeaderText,
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nvHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        nvHeaderRow.Children.Add(MakeDragHandle(_window.NvidiaProfileContainer));
        nvHeaderRow.Children.Add(nvArrow);
        nvHeaderRow.Children.Add(nvTitle);
        _window.NvidiaProfilePanel.Children.Add(nvHeaderRow);

        var nvBody = new StackPanel { Spacing = 6, Visibility = nvCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.NvidiaProfilePanel.Children.Add(nvBody);

        nvHeaderRow.PointerEntered += (s, e) => nvTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        nvHeaderRow.PointerExited  += (s, e) => nvTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var nvHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var nvArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var nvCursorProp  = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        nvHeaderRow.PointerEntered += (s, e) => nvCursorProp?.SetValue(nvHeaderRow, nvHandCursor);
        nvHeaderRow.PointerExited  += (s, e) => nvCursorProp?.SetValue(nvHeaderRow, nvArrowCursor);
        nvHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = nvBody.Visibility == Visibility.Visible;
            nvBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            nvArrow.Text = nowCollapsed ? "▶" : "▼";
            if (nowCollapsed) nvSettings.CollapsedDetailSections.Add(nvSectionKey);
            else              nvSettings.CollapsedDetailSections.Remove(nvSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        // Store the body panel so BuildDriverProfileSection can append to it
        _nvBodyPanel = nvBody;

        // Fetch all NVAPI/preset values off the UI thread, then build the body on dispatcher
        var gameName   = card.GameName;
        var installPath = card.InstallPath ?? "";
        var gameSource  = card.Source ?? "";
        var hasAnyDlss  = card.HasAnyDlssStreamline;
        var hasDlss     = card.HasDlss;
        var hasDlssd    = card.HasDlssd;
        var hasDlssg    = card.HasDlssg;
        var hasStreamline = card.HasStreamline;
        var hasDlssnr   = card.HasDlssnr;
        var capturedCard = card;

        _ = Task.Run(async () =>
        {
            await _panelScanSemaphore.WaitAsync().ConfigureAwait(false);
            DlssProfileData? dlssData = null;
            try
            {
                if (hasAnyDlss && _dlssPresetService.IsSupported)
                {
                    var svc = _dlssPresetService;
                    dlssData = new DlssProfileData(
                        SrDriverOverride: svc.IsSrDriverOverrideActive(gameName, installPath),
                        RrDriverOverride: svc.IsRrDriverOverrideActive(gameName, installPath),
                        FgDriverOverride: svc.IsFgDriverOverrideActive(gameName, installPath),
                        NrDriverOverride: FeatureFlags.DlssNr && svc.IsNrDriverOverrideActive(gameName, installPath),
                        SrPreset:         hasDlss  ? svc.GetSrPreset(gameName, installPath)  : 0u,
                        RrPreset:         hasDlssd ? svc.GetRrPreset(gameName, installPath)  : 0u,
                        FgPreset:         hasDlssg ? svc.GetFgPreset(gameName, installPath)  : 0u,
                        NrPreset:         hasDlssnr && FeatureFlags.DlssNr ? svc.GetNrPreset(gameName, installPath) : 0u,
                        SrRenderScale:    hasDlss  ? svc.GetSrRenderScale(gameName, installPath) : 0u,
                        RrRenderScale:    hasDlssd ? svc.GetRrRenderScale(gameName, installPath) : 0u,
                        MfgMode:          hasDlssg ? svc.GetMfgMode(gameName, installPath)   : 0u);
                }
            }
            finally
            {
                _panelScanSemaphore.Release();
            }

            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                // Guard: bail if the user navigated away
                var current = _window.ViewModel.SelectedGame;
                if (current == null || !current.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                    || current.Source != gameSource)
                    return;

                BuildNvidiaProfileBody(capturedCard, capturedName, nvBody, dlssData,
                    hasDlss, hasDlssd, hasDlssg, hasStreamline, hasDlssnr);
            });
        });
    }

    // Data fetched off the UI thread for the DLSS/SL grid
    private sealed record DlssProfileData(
        bool SrDriverOverride, bool RrDriverOverride, bool FgDriverOverride, bool NrDriverOverride,
        uint SrPreset, uint RrPreset, uint FgPreset, uint NrPreset,
        uint SrRenderScale, uint RrRenderScale, uint MfgMode);

    private void BuildNvidiaProfileBody(GameCardViewModel card, string capturedName,
        StackPanel nvBody, DlssProfileData? dlssData,
        bool hasDlss, bool hasDlssd, bool hasDlssg, bool hasStreamline, bool hasDlssnr)
    {
        if (card.HasAnyDlssStreamline)
        {
            var dlssService = _dlssStreamlineService;
            var presetService = _dlssPresetService;
            // hasDlss/hasDlssd/hasDlssg/hasStreamline/hasDlssnr passed as method params

            var dlssRowGrid = new Grid { ColumnSpacing = 12 };
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // NR column (dev-only): 2 extra columns added below when DevUnlockService.IsUnlocked

            // SR column
            // Disable for DLSS 1.x (not compatible with 2.x+ versions in manifest)
            bool srEnabled = hasDlss && !(card.DlssInstalledVersion?.StartsWith("1.") == true);
            bool srDriverOverride = dlssData?.SrDriverOverride == true;
            var srCol = BuildDlssColumn("DLSS Super Resolution", srEnabled, dlssService.DlssVersions,
                card.DlssInstalledVersion, DlssPresetService.SrPresets,
                presetService.IsSupported && srEnabled ? (dlssData?.SrPreset ?? 0u) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssPath);
                    else await dlssService.SwapDlssAsync(tc.DlssDetection.DlssPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetSrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                currentRenderScale: presetService.IsSupported && srEnabled ? (dlssData?.SrRenderScale ?? 0u) : 0u,
                onRenderScaleSelected: (pct) => { presetService.SetSrRenderScale(card.GameName, card.InstallPath, pct); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssVersion,
                driverOverrideActive: srDriverOverride);
            Grid.SetColumn(srCol, 0);
            dlssRowGrid.Children.Add(srCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(1));

            // RR column
            bool rrDriverOverride = dlssData?.RrDriverOverride == true;
            var rrCol = BuildDlssColumn("Ray Reconstruction", hasDlssd, dlssService.DlssdVersions,
                card.DlssdInstalledVersion, DlssPresetService.RrPresets,
                presetService.IsSupported && hasDlssd ? (dlssData?.RrPreset ?? 0u) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssdPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssdPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssdPath);
                    else await dlssService.SwapDlssdAsync(tc.DlssDetection.DlssdPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetRrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                currentRenderScale: presetService.IsSupported && hasDlssd ? (dlssData?.RrRenderScale ?? 0u) : 0u,
                onRenderScaleSelected: (pct) => { presetService.SetRrRenderScale(card.GameName, card.InstallPath, pct); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssdVersion,
                driverOverrideActive: rrDriverOverride);
            Grid.SetColumn(rrCol, 2);
            dlssRowGrid.Children.Add(rrCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(3));

            // FG column — no v1.x guard (FG can be updated from v1.0.0 to newer versions)
            bool fgEnabled = hasDlssg;
            bool fgDriverOverride = dlssData?.FgDriverOverride == true;
            var fgCol = BuildDlssColumn("Frame Generation", fgEnabled, dlssService.DlssgVersions,
                card.DlssgInstalledVersion, DlssPresetService.FgPresets,
                presetService.IsSupported && fgEnabled ? (dlssData?.FgPreset ?? 0u) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssgPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssgPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssgPath);
                    else await dlssService.SwapDlssgAsync(tc.DlssDetection.DlssgPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetFgPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssgVersion,
                driverOverrideActive: fgDriverOverride);

            // Add Multi Frame Generation button to FG column
            fgCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            var mfgBtn = new Button
            {
                Content = "Multi Frame Gen",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = fgEnabled && presetService.IsSupported,
                Opacity = (fgEnabled && presetService.IsSupported) ? 1.0 : 0.4,
            };
            ToolTipService.SetToolTip(mfgBtn, "Configure NVIDIA Multi Frame Generation: mode, frame count multiplier, and dynamic target frame rate. Requires 50 Series GPU.");
            mfgBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                await MfgDialog.ShowAsync(
                    presetService,
                    _window.ViewModel.Settings,
                    capturedName,
                    card.InstallPath ?? "",
                    xamlRoot,
                    () => _window.ViewModel.SaveSettingsPublic());
                // Rebuild panel so Restore All button reflects MFG changes
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(refreshCard));
            };
            fgCol.Children.Add(mfgBtn);

            Grid.SetColumn(fgCol, 4);
            dlssRowGrid.Children.Add(fgCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(5));

            // NR column — dev-only
            // hasDlssnr is a method parameter
            if (FeatureFlags.DlssNr)
            {
                // Expand the grid to 9 columns: SR, div, RR, div, FG, div, NR, div, SL
                dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Determine NR installed version — show "Custom" if sidecar marker exists
                var nrDllPath = card.DlssDetection?.DlssnrPath;
                var nrInstalledVersion = (hasDlssnr && nrDllPath != null
                    && File.Exists(nrDllPath + ".rhi_custom"))
                    ? "Custom"
                    : card.DlssnrInstalledVersion;

                // Track the version currently selected in the NR combo so Deploy DLL can use it
                string nrSelectedVersion = nrInstalledVersion ?? "";

                bool nrDriverOverride = dlssData?.NrDriverOverride == true;
                var nrCol = BuildDlssColumn("Neural Rendering", hasDlssnr, dlssService.DlssnrVersions,
                    nrInstalledVersion, DlssPresetService.NrPresets,
                    presetService.IsSupported && hasDlssnr ? (dlssData?.NrPreset ?? 0u) : 0u,
                    async (version) =>
                    {
                        var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                        nrSelectedVersion = version;
                        if (tc?.DlssDetection?.DlssnrPath == null) return; // no existing file — only track selection, Deploy handles it
                        if (version == "Default")
                        {
                            dlssService.Restore(tc.DlssDetection.DlssnrPath);
                            // Clean up custom marker on restore
                            try { File.Delete(tc.DlssDetection.DlssnrPath + ".rhi_custom"); } catch { }
                        }
                        else if (version == "Custom")
                            await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssnrPath);
                        else
                        {
                            await dlssService.SwapDlssnrAsync(tc.DlssDetection.DlssnrPath, version);
                            // Clean up custom marker when switching to a managed version
                            try { File.Delete(tc.DlssDetection.DlssnrPath + ".rhi_custom"); } catch { }
                        }
                        tc.RefreshDlssVersions(dlssService);
                        _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                    },
                    (preset) => { presetService.SetNrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                    originalVersion: card.DlssDetection?.OriginalDlssnrVersion,
                    driverOverrideActive: nrDriverOverride);

                // Spacer before deploy row — matches the spacing FG uses before Multi Frame Gen
                // Always add a preset placeholder so Deploy DLL aligns with Multi Frame Gen.
                // When NR is not installed the placeholder is invisible but still takes space.
                if (!hasDlssnr)
                {
                    var presetPlaceholderLabel = new TextBlock { Text = "Preset", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0), Opacity = 0 };
                    var presetPlaceholderCombo = new ComboBox { ItemsSource = new[] { "Default" }, SelectedIndex = 0, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false, Opacity = 0 };
                    nrCol.Children.Add(presetPlaceholderLabel);
                    nrCol.Children.Add(presetPlaceholderCombo);
                }
                nrCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
                var deployRow = new Grid { ColumnSpacing = 6 };
                deployRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                deployRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var deployNrBtn = new Button
                {
                    Content = "Deploy DLL",
                    FontSize = 11,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                    Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                    BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                };
                ToolTipService.SetToolTip(deployNrBtn, "Download and copy nvngx_dlssnr.dll to the game folder. Supports RTX 40 and 50 Series GPUs. Can also be deployed automatically via the RenoDX DLSS5 addon in the Addons picker.");

                var deleteNrBtn = new Button
                {
                    Width = 36,
                    Height = 32,
                    Padding = new Thickness(0),
                    Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
                    Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
                    BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    IsEnabled = hasDlssnr,
                    Opacity = hasDlssnr ? 1.0 : 0.0,
                    IsHitTestVisible = hasDlssnr,
                    Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
                };
                ToolTipService.SetToolTip(deleteNrBtn, "Delete nvngx_dlssnr.dll from the game folder.");

                deployNrBtn.Click += async (s, ev) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc == null || string.IsNullOrEmpty(tc.InstallPath)) return;

                    deployNrBtn.IsEnabled = false;
                    deployNrBtn.Content = "Downloading...";

                    try
                    {
                        var destPath = tc.DlssDetection?.DlssnrPath ?? Path.Combine(tc.InstallPath, "nvngx_dlssnr.dll");
                        var isCustom = nrSelectedVersion == "Custom";
                        var isDefault = nrSelectedVersion == "Default" || string.IsNullOrEmpty(nrSelectedVersion);

                        if (isCustom)
                        {
                            // Deploy from Custom\DLSS\nvngx_dlssnr.dll
                            if (tc.DlssDetection?.DlssnrPath != null)
                            {
                                await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssnrPath);
                                // Write custom marker so version shows as "Custom"
                                try { File.WriteAllText(tc.DlssDetection.DlssnrPath + ".rhi_custom", ""); } catch { }
                            }
                            else
                            {
                                // Fresh install — deploy custom file to game root
                                var customSrc = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RHI", "Custom", "DLSS", "nvngx_dlssnr.dll");
                                if (!File.Exists(customSrc))
                                {
                                    _window.DispatcherQueue?.TryEnqueue(() => { deployNrBtn.Content = "Not in Custom/DLSS"; deployNrBtn.IsEnabled = true; });
                                    return;
                                }
                                File.Copy(customSrc, destPath, overwrite: true);
                                try { File.WriteAllText(destPath + ".rhi_custom", ""); } catch { }
                                CrashReporter.Log($"[NrDeployBtn] Deployed custom nvngx_dlssnr.dll to '{tc.InstallPath}'");
                            }
                        }
                        else
                        {
                            // Deploy a specific managed version (or newest if nothing selected)
                            string? cachedPath;
                            var versionToDeploy = isDefault ? null : nrSelectedVersion;
                            if (string.IsNullOrEmpty(versionToDeploy))
                            {
                                cachedPath = await dlssService.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
                            }
                            else
                            {
                                await dlssService.SwapDlssnrAsync(destPath, versionToDeploy).ConfigureAwait(false);
                                // Clean custom marker if present
                                try { File.Delete(destPath + ".rhi_custom"); } catch { }
                                cachedPath = destPath; // SwapDlssnrAsync already wrote to destPath
                            }

                            if (!isDefault && !string.IsNullOrEmpty(versionToDeploy))
                            {
                                // SwapDlssnrAsync handled it — fall through to detection
                            }
                            else if (cachedPath == null)
                            {
                                _window.DispatcherQueue?.TryEnqueue(() => { deployNrBtn.Content = "Not available"; deployNrBtn.IsEnabled = true; });
                                return;
                            }
                            else if (isDefault)
                            {
                                // Fresh install of newest
                                File.Copy(cachedPath, destPath, overwrite: true);
                                try { File.Delete(destPath + ".rhi_custom"); } catch { }
                                CrashReporter.Log($"[NrDeployBtn] Deployed nvngx_dlssnr.dll (newest) to '{tc.InstallPath}'");
                            }
                        }

                        // Deploy nvngx_dlss.dll alongside nvngx_dlssnr.dll — always to install path root
                        // Creates a .original backup if the file already exists
                        try
                        {
                            var dlssDest = Path.Combine(tc.InstallPath, "nvngx_dlss.dll");
                            var cachedDlss = await dlssService.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
                            if (cachedDlss != null)
                            {
                                var backup = dlssDest + ".original";
                                if (File.Exists(dlssDest) && !File.Exists(backup))
                                    File.Copy(dlssDest, backup);
                                File.Copy(cachedDlss, dlssDest, overwrite: true);
                                CrashReporter.Log($"[NrDeployBtn] Deployed nvngx_dlss.dll to '{tc.InstallPath}'");
                            }
                        }
                        catch (Exception dlssEx)
                        {
                            CrashReporter.Log($"[NrDeployBtn] nvngx_dlss.dll deploy failed — {dlssEx.Message}");
                        }

                        var detection = dlssService.Detect(tc.InstallPath);
                        if (detection.HasAny)
                        {
                            dlssService.RecordDlssFound(tc.GameName);
                            dlssService.RecordTrustedPath(tc.GameName, detection);
                        }
                        _window.DispatcherQueue?.TryEnqueue(() =>
                        {
                            tc.DlssDetection = detection;
                            tc.ApplyDlssDetection(detection);
                            tc.RefreshDlssVersions(dlssService);
                            BuildOverridesPanel(tc);
                        });
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[NrDeployBtn] Failed — {ex.Message}");
                        _window.DispatcherQueue?.TryEnqueue(() => { deployNrBtn.Content = "Deploy DLL"; deployNrBtn.IsEnabled = true; });
                    }
                };

                deleteNrBtn.Click += (s, ev) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc == null || tc.DlssDetection?.DlssnrPath == null) return;
                    try
                    {
                        dlssService.Restore(tc.DlssDetection.DlssnrPath);
                        File.Delete(tc.DlssDetection.DlssnrPath);
                        CrashReporter.Log($"[NrDeleteBtn] Deleted nvngx_dlssnr.dll from '{tc.InstallPath}'");
                        var detection = dlssService.Detect(tc.InstallPath);
                        if (detection.HasAny)
                            dlssService.RecordTrustedPath(tc.GameName, detection);
                        else
                            dlssService.RecordNoDlssFound(tc.GameName);
                        _window.DispatcherQueue?.TryEnqueue(() =>
                        {
                            tc.DlssDetection = detection;
                            tc.ApplyDlssDetection(detection);
                            tc.RefreshDlssVersions(dlssService);
                            BuildOverridesPanel(tc);
                        });
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[NrDeleteBtn] Failed — {ex.Message}");
                    }
                };

                Grid.SetColumn(deployNrBtn, 0);
                Grid.SetColumn(deleteNrBtn, 1);
                deployRow.Children.Add(deployNrBtn);
                deployRow.Children.Add(deleteNrBtn);
                nrCol.Children.Add(deployRow);

                // Override column opacity so deploy buttons aren't dimmed when NR not present.
                // Manually dim only the version/preset controls.
                if (!hasDlssnr)
                {
                    nrCol.Opacity = 1.0;
                    foreach (var child in nrCol.Children.OfType<UIElement>())
                    {
                        if (child != deployRow)
                            child.Opacity = 0.4;
                    }
                }
                Grid.SetColumn(nrCol, 8);
                dlssRowGrid.Children.Add(nrCol);

                dlssRowGrid.Children.Add(MakeDlssDivider(7));
            }

            // SL column (no preset)
            // Disable for Streamline v1.x (not compatible with v2.x+ versions in manifest)
            bool slEnabled = hasStreamline && !(card.StreamlineInstalledVersion?.StartsWith("1.") == true);
            // Check if custom Streamline marker exists — override version to "Custom"
            // Only show "Custom" if we can't read a real version from the DLL
            var slVersionFromDll = card.StreamlineInstalledVersion;
            var slInstalledVersion = (hasStreamline && !string.IsNullOrEmpty(card.DlssDetection?.StreamlineFolder)
                && DlssStreamlineService.IsCustomStreamlineActive(card.DlssDetection.StreamlineFolder)
                && (string.IsNullOrEmpty(slVersionFromDll) || slVersionFromDll == "Unknown"))
                ? "Custom"
                : slVersionFromDll;
            var slCol = BuildDlssColumn("Streamline", slEnabled, dlssService.StreamlineVersions,
                slInstalledVersion, null, 0,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.StreamlineFolder == null) return;
                    if (version == "Default") dlssService.RestoreStreamline(tc.DlssDetection.StreamlineFolder);
                    else if (version == "Custom") await dlssService.SwapStreamlineCustomAsync(tc.DlssDetection.StreamlineFolder);
                    else await dlssService.SwapStreamlineAsync(tc.DlssDetection.StreamlineFolder, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                null,
                originalVersion: card.DlssDetection?.OriginalStreamlineVersion);

            int slColumn = FeatureFlags.DlssNr ? 6 : 6;

            // Add Restore All button into the SL column (fills the preset slot)
            // Enabled when any backup exists OR any preset is non-default
            bool hasNonDefaultPreset = (presetService.IsSupported && hasDlss  && (dlssData?.SrPreset ?? 0u) != 0)
                || (presetService.IsSupported && hasDlssd && (dlssData?.RrPreset ?? 0u) != 0)
                || (presetService.IsSupported && hasDlssg && (dlssData?.FgPreset ?? 0u) != 0)
                || (FeatureFlags.DlssNr && presetService.IsSupported && card.HasDlssnr && (dlssData?.NrPreset ?? 0u) != 0)
                || (presetService.IsSupported && hasDlss  && (dlssData?.SrRenderScale ?? 0u) != 0)
                || (presetService.IsSupported && hasDlssd && (dlssData?.RrRenderScale ?? 0u) != 0)
                || (presetService.IsSupported && hasDlssg && (dlssData?.MfgMode ?? 0u) != 0);
            bool restoreEnabled = card.HasAnyDlssBackup || hasNonDefaultPreset;
            var dlssRestoreBtn = new Button
            {
                Content = "Restore DLSS/SL",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = restoreEnabled,
            };
            dlssRestoreBtn.Click += (s, ev) =>
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard?.DlssDetection != null)
                {
                    dlssService.RestoreAll(targetCard.DlssDetection);
                    presetService.SetSrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetRrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetFgPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    if (FeatureFlags.DlssNr && targetCard.HasDlssnr)
                        presetService.SetNrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetSrRenderScale(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetRrRenderScale(targetCard.GameName, targetCard.InstallPath, 0);
                    // Reset MFG settings
                    presetService.SetMfgMode(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgGenerationFactor(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.DeleteMfgDynamicMaxCount(targetCard.GameName, targetCard.InstallPath);
                    presetService.DeleteMfgDynamicTargetFps(targetCard.GameName, targetCard.InstallPath);
                    targetCard.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(targetCard));
                }
            };
            // Add spacer label to align buttons with the Preset/RenderScale rows in other columns
            slCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });

            // Quick Apply button (created below, added here after creation)
            // Spacer + Restore All (added after Quick Apply is created)
            var hasDefaults = !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssdVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssgVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultStreamlineVersion)
                || _window.ViewModel.Settings.DefaultSrPreset != 0
                || _window.ViewModel.Settings.DefaultRrPreset != 0
                || _window.ViewModel.Settings.DefaultFgPreset != 0
                || _window.ViewModel.Settings.DefaultSrRenderScale != 0
                || _window.ViewModel.Settings.DefaultRrRenderScale != 0
                || (FeatureFlags.DlssNr && (!string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssnrVersion) || _window.ViewModel.Settings.DefaultNrPreset != 0));

            var applyBtn = new Button
            {
                Content = "Quick Apply",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = hasDefaults && card.HasAnyDlssStreamline,
            };
            ToolTipService.SetToolTip(applyBtn, "Apply your configured DLSS/Streamline default versions, presets, and render scales to this game. Downloads versions on-demand if not cached.");
            applyBtn.Click += async (s, ev) =>
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard?.DlssDetection == null) return;

                var settings = _window.ViewModel.Settings;
                var svc = _dlssStreamlineService;
                var pSvc = _dlssPresetService;

                // Check driver override state — skip DLL swaps for overridden components
                bool srOverride = pSvc.IsSupported && pSvc.IsSrDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");
                bool rrOverride = pSvc.IsSupported && pSvc.IsRrDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");
                bool fgOverride = pSvc.IsSupported && pSvc.IsFgDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");

                if (!string.IsNullOrEmpty(settings.DefaultDlssVersion) && targetCard.HasDlss && targetCard.DlssDetection.DlssPath != null
                    && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true) && !srOverride)
                {
                    if (settings.DefaultDlssVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                        await svc.SwapDlssCustomAsync(targetCard.DlssDetection.DlssPath);
                    else
                        await svc.SwapDlssAsync(targetCard.DlssDetection.DlssPath, settings.DefaultDlssVersion);
                }
                if (!string.IsNullOrEmpty(settings.DefaultDlssdVersion) && targetCard.HasDlssd && targetCard.DlssDetection.DlssdPath != null
                    && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true) && !rrOverride)
                {
                    if (settings.DefaultDlssdVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                        await svc.SwapDlssCustomAsync(targetCard.DlssDetection.DlssdPath);
                    else
                        await svc.SwapDlssdAsync(targetCard.DlssDetection.DlssdPath, settings.DefaultDlssdVersion);
                }
                if (!string.IsNullOrEmpty(settings.DefaultDlssgVersion) && targetCard.HasDlssg && targetCard.DlssDetection.DlssgPath != null
                    && !fgOverride)
                {
                    if (settings.DefaultDlssgVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                        await svc.SwapDlssCustomAsync(targetCard.DlssDetection.DlssgPath);
                    else
                        await svc.SwapDlssgAsync(targetCard.DlssDetection.DlssgPath, settings.DefaultDlssgVersion);
                }
                if (!string.IsNullOrEmpty(settings.DefaultStreamlineVersion) && targetCard.HasStreamline && targetCard.DlssDetection.StreamlineFolder != null
                    && !(targetCard.StreamlineInstalledVersion?.StartsWith("1.") == true))
                {
                    if (settings.DefaultStreamlineVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                        await svc.SwapStreamlineCustomAsync(targetCard.DlssDetection.StreamlineFolder);
                    else
                        await svc.SwapStreamlineAsync(targetCard.DlssDetection.StreamlineFolder, settings.DefaultStreamlineVersion);
                }

                if (settings.DefaultSrPreset != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetSrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultSrPreset);
                if (settings.DefaultRrPreset != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetRrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultRrPreset);
                if (settings.DefaultFgPreset != 0 && targetCard.HasDlssg)
                    pSvc.SetFgPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultFgPreset);

                if (FeatureFlags.DlssNr)
                {
                    if (!string.IsNullOrEmpty(settings.DefaultDlssnrVersion) && targetCard.HasDlssnr && targetCard.DlssDetection?.DlssnrPath != null)
                        await svc.SwapDlssnrAsync(targetCard.DlssDetection.DlssnrPath, settings.DefaultDlssnrVersion);
                    if (settings.DefaultNrPreset != 0 && targetCard.HasDlssnr)
                        pSvc.SetNrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultNrPreset);
                }

                if (settings.DefaultSrRenderScale != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetSrRenderScale(targetCard.GameName, targetCard.InstallPath, settings.DefaultSrRenderScale);
                if (settings.DefaultRrRenderScale != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetRrRenderScale(targetCard.GameName, targetCard.InstallPath, settings.DefaultRrRenderScale);

                targetCard.RefreshDlssVersions(svc);
                _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(targetCard));
            };

            // Add buttons to SL column: Quick Apply first, then spacer, then Restore All at bottom
            slCol.Children.Add(applyBtn);
            slCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            slCol.Children.Add(dlssRestoreBtn);

            // Override column opacity so buttons aren't dimmed by the SL column's 0.4 opacity.
            // Manually dim the SL label and version combo if Streamline isn't present.
            if ((hasDefaults || restoreEnabled) && !slEnabled)
            {
                slCol.Opacity = 1.0;
                // Dim the SL-specific children (label, version sub-label, combo, etc.) but not buttons
                foreach (var child in slCol.Children.OfType<UIElement>())
                {
                    if (child != applyBtn && child != dlssRestoreBtn)
                        child.Opacity = 0.4;
                }
            }
            ToolTipService.SetToolTip(dlssRestoreBtn, "Restore all DLSS and Streamline DLLs to their original game versions and reset presets to Default.");

            Grid.SetColumn(slCol, slColumn);
            dlssRowGrid.Children.Add(slCol);

            nvBody.Children.Add(dlssRowGrid);
        }

        BuildDriverProfileSection(card, capturedName);
    }
}
