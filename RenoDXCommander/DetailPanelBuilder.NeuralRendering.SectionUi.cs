using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildNeuralRenderingSectionWithData(
        GameCardViewModel card,
        bool dlss5Installed, bool sfInstalled,
        bool nrDllPresent, bool nrDllOwnedByRhi, string? nrDllVersion,
        bool bridgePresent, bool feederPresent,
        double containerWidth = 0)
    {
        // Guard: if the user navigated away before the background scan finished, bail out
        if (_window.ViewModel.SelectedGame != card) return;

        // Re-clear the panel in case another card was selected while we were scanning
        _window.NeuralRenderingPanel.Children.Clear();
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:Init({card.GameName})");

        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();
        var addonSvc    = _window.ViewModel.AddonPackServiceInstance;
        var dlssSvc     = _dlssStreamlineService;
        bool hasDlss  = card.HasAnyDlssStreamline;
        bool isDx12   = card.GraphicsApi == GraphicsApiType.DirectX12;

        // Mutable install state — updated after install/uninstall so UpdateInstallBtnAppearance
        // never needs to re-check the filesystem (which blocks the UI thread).
        bool _dlss5Installed  = dlss5Installed;
        bool _sfInstalled     = sfInstalled;
        bool _bridgePresent   = bridgePresent;
        bool _feederPresent   = feederPresent;
        bool _nrDllOwnedByRhi = nrDllOwnedByRhi;
        bool isDx11   = card.GraphicsApi == GraphicsApiType.DirectX11;
        bool isVulkan = card.GraphicsApi == GraphicsApiType.Vulkan;
        bool isDx9    = card.GraphicsApi == GraphicsApiType.DirectX9;
        bool is32Bit  = card.Is32Bit;
        bool isDx964bit = isDx9 && !is32Bit; // 64-bit DX9 — Feeder not needed, renodx-dlss handles these

        // ── Infer current method from installed state (migration) ─────────────
        string? storedMethod = _window.ViewModel.GetNrMethodOverride(gameName, store);
        if (storedMethod == null)
        {
            // Infer from what's on disk
            if (sfInstalled)
                storedMethod = NrMethodShortFuse;
            else if (dlss5Installed && bridgePresent)
                storedMethod = NrMethodDlss5ToolBridge;
            else if (dlss5Installed)
                storedMethod = NrMethodDlss5Tool;
            else if (feederPresent)
                storedMethod = NrMethodFeeder;
        }

        // ── Auto-select best method if nothing stored/inferred ────────────────
        string effectiveMethod = storedMethod ?? (
            is32Bit                                          ? NrMethodFeeder :
            card.GraphicsApi == GraphicsApiType.OpenGL       ? NrMethodFeeder :
            !hasDlss                                         ? NrMethodFeeder :
            (isDx11 || isVulkan)                             ? NrMethodDlss5ToolBridge :
                                                               NrMethodShortFuse);

        // ── Build method combo items (show all, disable inapplicable) ─────────
        var methodItems = new[]
        {
            new { Name = "ShortFuse DLSS Tool",          Key = NrMethodShortFuse,       Enabled = !is32Bit && card.GraphicsApi != GraphicsApiType.OpenGL },
            new { Name = "DLSS5 Tool",                 Key = NrMethodDlss5Tool,       Enabled = hasDlss && !is32Bit },
            new { Name = "DLSS5 Tool + DX11 Bridge",   Key = NrMethodDlss5ToolBridge, Enabled = hasDlss && (isDx11 || isVulkan) && !is32Bit },
            new { Name = "DLSS5 Feeder",               Key = NrMethodFeeder,          Enabled = true },
        };

        // ── Header ────────────────────────────────────────────────────────────
        const string nrSectionKey = "NeuralRendering";
        var nrSettings   = _window.ViewModel.Settings;
        bool nrCollapsed = nrSettings.CollapsedDetailSections.Contains(nrSectionKey);

        var nrArrow = new TextBlock
        {
            Text      = nrCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var nrTitle = new TextBlock
        {
            Text       = "Neural Rendering",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nrHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        nrHeaderRow.Children.Add(MakeDragHandle(_window.NeuralRenderingContainer));
        nrHeaderRow.Children.Add(nrArrow);
        nrHeaderRow.Children.Add(nrTitle);
        _window.NeuralRenderingPanel.Children.Add(nrHeaderRow);

        // Body wrapper — all section content goes into this
        var nrBody = new StackPanel { Spacing = 6, Visibility = nrCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.NeuralRenderingPanel.Children.Add(nrBody);

        nrHeaderRow.PointerEntered += (s, e) => nrTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        nrHeaderRow.PointerExited  += (s, e) => nrTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var nrHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var nrArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var nrCursorProp  = DetailPanelBuilder.CursorProp;
        nrHeaderRow.PointerEntered += (s, e) => nrCursorProp?.SetValue(nrHeaderRow, nrHandCursor);
        nrHeaderRow.PointerExited  += (s, e) => nrCursorProp?.SetValue(nrHeaderRow, nrArrowCursor);

        // ── Collapsed summary ─────────────────────────────────────────────────
        bool nrAnyInstalled = dlss5Installed || sfInstalled || feederPresent || bridgePresent;
        TextBlock? nrSummary = null;
        if (nrAnyInstalled || nrDllPresent)
        {
            var nrSummaryEntries = new List<(string, string?)>();
            string methodLabel = effectiveMethod switch
            {
                NrMethodShortFuse       => "DLSS Tool (SF)",
                NrMethodDlss5Tool       => "DLSS5 Tool",
                NrMethodDlss5ToolBridge => "DLSS5 Tool + Bridge",
                NrMethodFeeder          => "Feeder",
                _                       => effectiveMethod,
            };
            if (nrAnyInstalled)
                nrSummaryEntries.Add(("NR Method", methodLabel));
            if (nrDllPresent)
                nrSummaryEntries.Add(("NR DLL", nrDllVersion));
            nrSummary = DetailPanelBuilder.MakeSectionSummaryInlines(nrSummaryEntries);
            if (nrSummary != null)
            {
                nrSummary.Visibility = nrCollapsed ? Visibility.Visible : Visibility.Collapsed;
                nrHeaderRow.Children.Add(nrSummary);
            }
        }

        nrHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = nrBody.Visibility == Visibility.Visible;
            nrBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            nrArrow.Text = nowCollapsed ? "▶" : "▼";
            if (nrSummary != null)
                nrSummary.Visibility = nowCollapsed ? Visibility.Visible : Visibility.Collapsed;
            if (nowCollapsed) nrSettings.CollapsedDetailSections.Add(nrSectionKey);
            else              nrSettings.CollapsedDetailSections.Remove(nrSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        // ── Row 1: Method combo + Addon Version combo + NR DLL version combo ──
        // ── Row 1: Method / [Pack Version] / DLSS5 Tool Version / NR DLL Version ──
        // 3 columns for DLSS5Tool/ShortFuse; 4 columns for Feeder/Bridge (adds pack version col)
        bool isFeederOrBridge = effectiveMethod == NrMethodFeeder || effectiveMethod == NrMethodDlss5ToolBridge;
        var row1 = new Grid { ColumnSpacing = 8 };
        // Fixed-pixel column widths — star columns inside StackPanel/ScrollViewer cause WinUI infinite layout loops
        const int NrRow1Cols = 4;
        const double NrRow1Spacing = 8.0;
        const double NrSectionPadding = 28.0 + 2.0; // Border Padding="14,12" (28px) + BorderThickness="1" (2px)
        double nrColW = containerWidth > NrSectionPadding
            ? (containerWidth - NrSectionPadding - (NrRow1Cols - 1) * NrRow1Spacing) / NrRow1Cols
            : 160.0;
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nrColW) }); // Method
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nrColW) }); // Pack version (Feeder/Bridge only)
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nrColW) }); // DLSS5 Tool / SF version
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nrColW) }); // NR DLL version

        // Method combo (col 0)
        var methodStack = new StackPanel { Spacing = 2 };
        methodStack.Children.Add(new TextBlock { Text = "Method", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) });

        var methodCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            MaxDropDownHeight = 300,
        };
        foreach (var item in methodItems)
        {
            var cbi = new ComboBoxItem { Content = item.Name, IsEnabled = item.Enabled };
            if (!item.Enabled) cbi.Opacity = 0.4;
            methodCombo.Items.Add(cbi);
            if (item.Key == effectiveMethod)
                methodCombo.SelectedItem = cbi;
        }
        if (methodCombo.SelectedIndex < 0) methodCombo.SelectedIndex = 0;
        ToolTipService.SetToolTip(methodCombo,
            "DLSS5 Tool: for DX12 native-DLSS games.\n" +
            "DLSS5 Tool + DX11 Bridge: for DX11/Vulkan native-DLSS games.\n" +
            "ShortFuse DLSS Tool: alternative full-stack install for native-DLSS games.\n" +
            "DLSS5 Feeder: for games with no native DLSS (DX11, DX12, Vulkan, 32-bit).");
        methodStack.Children.Add(methodCombo);
        Grid.SetColumn(methodStack, 0);
        row1.Children.Add(methodStack);

        // Pack version combo (col 1) — Feeder version or Bridge version, always "Latest" (managed by AddonPackService)
        var packVersionLabel = new TextBlock { FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) };
        packVersionLabel.Text = effectiveMethod == NrMethodFeeder ? "Feeder Version" : "Bridge Version";
        var packVersionStack = new StackPanel { Spacing = 2, Visibility = isFeederOrBridge ? Visibility.Visible : Visibility.Collapsed };
        packVersionStack.Children.Add(packVersionLabel);
        var packVersionCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            MaxDropDownHeight = 300,
        };
        // Pack version combo — populate from staged version list, wire persistence
        bool addonSwapInProgress  = false;  // shared guard — prevents re-entrant swaps across both combo handlers
        bool packComboInitializing = true;
        SelectionChangedEventHandler? packVersionHandler = null;
        // Forward declarations — assigned later in the method before any swap handler can execute
        Button installBtn       = null!;
        StackPanel statusPanel  = null!;

        void PopulatePackVersionCombo(string methodKey)
        {
            packComboInitializing = true;

            // Unsubscribe the previous handler (if any) before repopulating
            if (packVersionHandler != null)
            {
                packVersionCombo.SelectionChanged -= packVersionHandler;
                packVersionHandler = null;
            }

            packVersionCombo.Items.Clear();
            var packAddonType = methodKey == NrMethodFeeder ? Renodx5AddonService.FeederSubDir : Renodx5AddonService.BridgeSubDir;
            var latestPackVer = rdx5Svc.GetLatestAvailableVersion(packAddonType);
            packVersionCombo.Items.Add(string.IsNullOrEmpty(latestPackVer) ? "Latest" : $"Latest ({latestPackVer})");
            var versions = rdx5Svc.GetAvailableVersions(packAddonType);
            foreach (var v in versions)
                packVersionCombo.Items.Add(v);

            var storedPack = _window.ViewModel.GetNrPackVersion(gameName, store);
            int packSelIdx = 0;
            if (!string.IsNullOrEmpty(storedPack))
            {
                for (int i = 1; i < packVersionCombo.Items.Count; i++)
                {
                    if (string.Equals(packVersionCombo.Items[i] as string, storedPack, StringComparison.OrdinalIgnoreCase))
                    { packSelIdx = i; break; }
                }
                // Version list may be empty (fetch not yet completed) — insert the stored version so it shows correctly
                if (packSelIdx == 0 && versions.Count == 0)
                {
                    packVersionCombo.Items.Add(storedPack);
                    packSelIdx = 1;
                }
            }
            packVersionCombo.SelectedIndex = packSelIdx;
            packVersionCombo.IsEnabled = true;
            packVersionCombo.Opacity = 1.0;
            packComboInitializing = false;

            packVersionHandler = async (s2, ev2) =>
            {
                if (packComboInitializing || addonSwapInProgress) return;
                var sel = packVersionCombo.SelectedItem as string;
                bool useLatest = string.IsNullOrEmpty(sel) || sel.StartsWith("Latest");

                // Persist the selection
                _window.ViewModel.SetNrPackVersion(gameName, useLatest ? null : sel, store);

                // If installed, swap the pack addon file in-place
                var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;
                bool packInstalled = selKey switch
                {
                    NrMethodFeeder          => File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64)),
                    NrMethodDlss5ToolBridge => File.Exists(Path.Combine(installPath, BridgeDeployFile)),
                    _                       => false,
                };
                if (!packInstalled) return;

                // 32-bit Feeder: versioned staging only has .addon64, skip in-place swap
                if (selKey == NrMethodFeeder && card.Is32Bit)
                {
                    CrashReporter.Log($"[NeuralRendering.PackSwap] 32-bit Feeder — no versioned .addon32 staging, swap skipped. Reinstall to apply v{sel ?? "Latest"}.");
                    return;
                }

                addonSwapInProgress = true;
                var prevContent = installBtn.Content;
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    installBtn.IsEnabled = false;
                    installBtn.Content   = "Swapping addon...";
                });

                try
                {
                    await Task.Run(async () =>
                    {
                        string? sourcePath = null;
                        string addonType   = selKey == NrMethodFeeder ? Renodx5AddonService.FeederSubDir : Renodx5AddonService.BridgeSubDir;
                        string destFile    = selKey == NrMethodFeeder ? FeederDeployFile64 : BridgeDeployFile;

                        if (useLatest)
                        {
                            // For Feeder latest use AddonPackService staging; Bridge uses versioned flat
                            if (selKey == NrMethodFeeder)
                            {
                                var bitnessExt = ".addon64";
                                sourcePath = FindStagedAddon(FeederPackageName, bitnessExt);
                                if (sourcePath == null)
                                {
                                    var entry = App.Services.GetRequiredService<IAddonPackService>()
                                        .AvailablePacks.FirstOrDefault(p =>
                                            p.PackageName.Equals(FeederPackageName, StringComparison.OrdinalIgnoreCase));
                                    if (entry != null)
                                        await App.Services.GetRequiredService<IAddonPackService>()
                                            .DownloadAddonAsync(entry).ConfigureAwait(false);
                                    sourcePath = FindStagedAddon(FeederPackageName, bitnessExt);
                                }
                            }
                            else
                            {
                                // Bridge latest — use first available versioned file or re-download
                                var latestVer = rdx5Svc.GetLatestAvailableVersion(addonType);
                                if (!string.IsNullOrEmpty(latestVer))
                                {
                                    var staged = await rdx5Svc.EnsureVersionStagedAsync(addonType, latestVer).ConfigureAwait(false);
                                    sourcePath = staged ? rdx5Svc.GetVersionedStagedFilePath(addonType, latestVer) : null;
                                }
                            }
                        }
                        else
                        {
                            var staged = await rdx5Svc.EnsureVersionStagedAsync(addonType, sel!).ConfigureAwait(false);
                            sourcePath = staged ? rdx5Svc.GetVersionedStagedFilePath(addonType, sel!) : null;
                        }

                        if (sourcePath == null || !File.Exists(sourcePath))
                        {
                            CrashReporter.Log($"[NeuralRendering.PackSwap] Source not available for v{sel ?? "Latest"} ({addonType}) — swap aborted");
                            return;
                        }

                        var destPath = Path.Combine(installPath, destFile);
                        File.Copy(sourcePath, destPath, overwrite: true);
                        CrashReporter.Log($"[NeuralRendering.PackSwap] Swapped {destFile} to v{(useLatest ? "latest" : sel)} at '{destPath}'");

                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[NeuralRendering.PackSwap] Swap failed — {ex.Message}");
                }
                finally
                {
                    addonSwapInProgress = false;
                    _window.DispatcherQueue?.TryEnqueue(() =>
                    {
                        installBtn.IsEnabled = true;
                        installBtn.Content   = prevContent;
                    });
                    RefreshStatus();
                }
            };
            packVersionCombo.SelectionChanged += packVersionHandler;
        }

        if (isFeederOrBridge)
            PopulatePackVersionCombo(effectiveMethod);

        ToolTipService.SetToolTip(packVersionStack,
            effectiveMethod == NrMethodFeeder
                ? "Version of dlss5-feed.addon64 to install."
                : "Version of dlss5-bridge.addon64 to install.");
        packVersionStack.Children.Add(packVersionCombo);        Grid.SetColumn(packVersionStack, 1);
        row1.Children.Add(packVersionStack);

        // DLSS5 Tool / SF version combo (col 2)
        var addonVersionLabel = new TextBlock { FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) };
        addonVersionLabel.Text = effectiveMethod == NrMethodShortFuse ? "SF Version"
                                : effectiveMethod == NrMethodFeeder    ? "DLSS5 Tool Version"
                                : effectiveMethod == NrMethodDlss5ToolBridge ? "DLSS5 Tool Version"
                                : "DLSS5 Tool Version";
        var addonVersionStack = new StackPanel { Spacing = 2 };
        addonVersionStack.Children.Add(addonVersionLabel);

        var addonVersionCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            MaxDropDownHeight = 300,
        };

        // Swap-in-progress guard — declared above near PackVersionCombo (shared across both handlers)

        // Helper: populate addonVersionCombo for the given addonType ("dlss5tool" or "dlsstool")
        void PopulateAddonVersionCombo(string addonType)
        {
            bool addonComboInit = true;
            addonVersionCombo.Items.Clear();
            var latestAddonVer = rdx5Svc.GetLatestAvailableVersion(addonType);
            addonVersionCombo.Items.Add(string.IsNullOrEmpty(latestAddonVer) ? "Latest" : $"Latest ({latestAddonVer})");
            foreach (var v in rdx5Svc.GetAvailableVersions(addonType))
                addonVersionCombo.Items.Add(v);

            // Pre-select persisted version
            var stored = _window.ViewModel.GetNrAddonVersion(gameName, store);
            int selIdx = 0;
            if (!string.IsNullOrEmpty(stored))
            {
                for (int i = 1; i < addonVersionCombo.Items.Count; i++)
                {
                    if (string.Equals(addonVersionCombo.Items[i] as string, stored, StringComparison.OrdinalIgnoreCase))
                    { selIdx = i; break; }
                }
            }
            addonVersionCombo.SelectedIndex = selIdx;
            addonComboInit = false;

            // Wire SelectionChanged after setting initial value
            addonVersionCombo.SelectionChanged -= AddonVersionCombo_SelectionChanged;
            addonVersionCombo.SelectionChanged += AddonVersionCombo_SelectionChanged;

            async void AddonVersionCombo_SelectionChanged(object s2, SelectionChangedEventArgs ev2)
            {
                try
                {
                    if (addonComboInit || addonSwapInProgress) return;
                    var sel = addonVersionCombo.SelectedItem as string;
                    bool useLatest = string.IsNullOrEmpty(sel) || sel.StartsWith("Latest");

                    // Persist the selection first (same as before)
                    _window.ViewModel.SetNrAddonVersion(gameName, useLatest ? null : sel, store);

                    // If installed, swap the addon file in-place — no full uninstall needed
                    var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;
                    bool currentlyInstalled = selKey switch
                    {
                        NrMethodDlss5Tool       => rdx5Svc.IsInstalledIn(installPath),
                        NrMethodDlss5ToolBridge => rdx5Svc.IsInstalledIn(installPath),
                        NrMethodShortFuse       => rdx5Svc.IsSfInstalledIn(installPath),
                        NrMethodFeeder          => File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64)),
                        _                       => false,
                    };
                    if (!currentlyInstalled) return;

                    addonSwapInProgress = true;
                    var prevContent = installBtn.Content;
                    _window.DispatcherQueue?.TryEnqueue(() =>
                    {
                        installBtn.IsEnabled = false;
                        installBtn.Content   = "Swapping addon...";
                    });

                    try
                    {
                        await Task.Run(async () =>
                        {
                            // Resolve source path
                            string? sourcePath = null;
                            if (useLatest)
                            {
                                if (selKey == NrMethodShortFuse)
                                {
                                    await rdx5Svc.EnsureSfStagingAsync().ConfigureAwait(false);
                                    sourcePath = rdx5Svc.IsSfStagingReady ? rdx5Svc.SfStagedFilePath : null;
                                }
                                else
                                {
                                    await rdx5Svc.EnsureStagingAsync().ConfigureAwait(false);
                                    sourcePath = rdx5Svc.IsStagingReady ? rdx5Svc.StagedFilePath : null;
                                }
                            }
                            else
                            {
                                var type   = selKey == NrMethodShortFuse ? "dlsstool" : "dlss5tool";
                                var staged = await rdx5Svc.EnsureVersionStagedAsync(type, sel!).ConfigureAwait(false);
                                sourcePath = staged ? rdx5Svc.GetVersionedStagedFilePath(type, sel!) : null;
                            }

                            if (sourcePath == null || !File.Exists(sourcePath))
                            {
                                CrashReporter.Log($"[NeuralRendering.AddonSwap] Source not available for version '{sel ?? "Latest"}' — swap aborted");
                                return;
                            }

                            // Destination depends on method
                            string deployDir;
                            string destFileName;
                            if (selKey == NrMethodShortFuse)
                            {
                                deployDir    = ModInstallService.GetAddonDeployPath(installPath);
                                // Use whichever name is currently on disk (zzz or normal)
                                bool zzzOnDisk = File.Exists(Path.Combine(deployDir, Renodx5AddonService.SfZzzDeployFileName));
                                destFileName = zzzOnDisk ? Renodx5AddonService.SfZzzDeployFileName : "renodx-dlss.addon64";
                            }
                            else if (selKey == NrMethodFeeder && card.Is32Bit)
                            {
                                // 32-bit: neural consumer lives in host64\
                                deployDir    = Path.Combine(installPath, "host64");
                                destFileName = "renodx-dlss5.addon64";
                            }
                            else
                            {
                                deployDir    = ModInstallService.GetAddonDeployPath(installPath);
                                destFileName = "renodx-dlss5.addon64";
                            }

                            Directory.CreateDirectory(deployDir);
                            var destPath = Path.Combine(deployDir, destFileName);
                            File.Copy(sourcePath, destPath, overwrite: true);
                            CrashReporter.Log($"[NeuralRendering.AddonSwap] Swapped {destFileName} to v{(useLatest ? "latest" : sel)} at '{destPath}'");

                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[NeuralRendering.AddonSwap] Swap failed — {ex.Message}");
                    }
                    finally
                    {
                        addonSwapInProgress = false;
                        _window.DispatcherQueue?.TryEnqueue(() =>
                        {
                            installBtn.IsEnabled = true;
                            installBtn.Content   = prevContent;
                        });
                        RefreshStatus();
                    }
                }
                catch (Exception unhandledEx)
                {
                    CrashReporter.Log($"[DetailPanelBuilder.AddonVersionCombo_SelectionChanged] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
                }
            }
        }

        // Determine initial addonType from effectiveMethod
        var initialAddonType = effectiveMethod == NrMethodShortFuse ? "dlsstool" : "dlss5tool";
        PopulateAddonVersionCombo(initialAddonType);

        ToolTipService.SetToolTip(addonVersionCombo,
            effectiveMethod == NrMethodFeeder
                ? "Version of renodx-dlss5.addon64 deployed as the neural consumer. The Feeder addon itself always uses the latest version."
                : "Addon version to install. 'Latest' always installs the newest available and auto-updates.");
        addonVersionStack.Children.Add(addonVersionCombo);
        Grid.SetColumn(addonVersionStack, 2);
        row1.Children.Add(addonVersionStack);

        // NR version combo (col 3)
        var nrVersionStack = new StackPanel { Spacing = 2 };
        nrVersionStack.Children.Add(new TextBlock { Text = "NR DLL Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) });

        var nrVersionCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            MaxDropDownHeight = 300,
        };
        ToolTipService.SetToolTip(nrVersionCombo, "NR DLL version to deploy. 'Latest' always uses the newest available. Change while installed to swap the NR DLL in-place.");
        nrVersionStack.Children.Add(nrVersionCombo);
        Grid.SetColumn(nrVersionStack, 3);
        row1.Children.Add(nrVersionStack);

        // Helper: populate nrVersionCombo — called at build time and whenever the manifest is available
        bool nrComboInit = true;
        void PopulateNrVersionCombo()
        {
            nrComboInit = true;
            nrVersionCombo.Items.Clear();
            var versions = dlssSvc.DlssnrVersions.ToList();
            var latestVer = versions.FirstOrDefault();
            nrVersionCombo.Items.Add(string.IsNullOrEmpty(latestVer) ? "Latest" : $"Latest ({latestVer})");
            foreach (var v in versions)
                nrVersionCombo.Items.Add(v);

            // Restore persisted selection — find the item that matches the saved version
            var savedNrDllVer = _window.ViewModel.GetNrDllVersion(gameName, store);
            int selIdx = 0; // default: Latest
            if (!string.IsNullOrEmpty(savedNrDllVer))
            {
                for (int i = 1; i < nrVersionCombo.Items.Count; i++)
                {
                    if ((nrVersionCombo.Items[i] as string ?? "").Equals(savedNrDllVer, StringComparison.OrdinalIgnoreCase))
                    { selIdx = i; break; }
                }
            }
            nrVersionCombo.SelectedIndex = selIdx;
            nrComboInit = false;
        }
        PopulateNrVersionCombo();

        // Wire NR DLL version swap — same pattern as addon version swap
        nrVersionCombo.SelectionChanged += NrVersionCombo_SelectionChanged;
        nrComboInit = false;

        async void NrVersionCombo_SelectionChanged(object s2, SelectionChangedEventArgs ev2)
        {
            try
            {
                if (nrComboInit || addonSwapInProgress) return;
                var sel = nrVersionCombo.SelectedItem as string;
                bool useLatest = string.IsNullOrEmpty(sel) || sel.StartsWith("Latest");

                // Persist selection
                _window.ViewModel.SetNrDllVersion(gameName, useLatest ? null : sel, store);

                // Check if NR DLL is currently installed
                bool nrInstalled = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll"));
                if (!nrInstalled) return;

                addonSwapInProgress = true;
                var prevContent = installBtn.Content;
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    installBtn.IsEnabled = false;
                    installBtn.Content   = "Swapping NR DLL...";
                });

                try
                {
                    await Task.Run(async () =>
                    {
                        string? cachedNr;
                        if (useLatest)
                            cachedNr = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
                        else
                        {
                            var nrDir = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "RHI", "DLSS-NR", StripVersionSuffix(sel!));
                            cachedNr = ResolveCachedNrDllPath(sel!) ?? Path.Combine(nrDir, "nvngx_dlssnr.dll");
                            if (!File.Exists(cachedNr))
                                cachedNr = await dlssSvc.EnsureSpecificDlssnrCachedAsync(sel!).ConfigureAwait(false)
                                        ?? await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
                        }

                        if (cachedNr == null) { CrashReporter.Log($"[NeuralRendering.NrDllSwap] Version '{sel ?? "Latest"}' not available — swap aborted"); return; }

                        var destPath = Path.Combine(installPath, "nvngx_dlssnr.dll");
                        File.Copy(cachedNr, destPath, overwrite: true);
                        CrashReporter.Log($"[NeuralRendering.NrDllSwap] Swapped nvngx_dlssnr.dll to v{(useLatest ? "latest" : sel)} at '{destPath}'");
                    }).ConfigureAwait(false);
                }
                catch (Exception ex) { CrashReporter.Log($"[NeuralRendering.NrDllSwap] Swap failed — {ex.Message}"); }
                finally
                {
                    addonSwapInProgress = false;
                    _window.DispatcherQueue?.TryEnqueue(() =>
                    {
                        installBtn.IsEnabled = true;
                        installBtn.Content   = prevContent;
                    });
                    // Refresh the card's detected DLSS versions so the DLSS/SL section also shows
                    // the updated NR DLL version — same version now in both panels.
                    var refreshCard = _window.ViewModel.AllCards
                        .FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                                             (string.IsNullOrEmpty(store) || c.Source == store));
                    if (refreshCard != null)
                    {
                        await Task.Run(() => refreshCard.RefreshDlssVersions(dlssSvc)).ConfigureAwait(false);
                        _window.DispatcherQueue?.TryEnqueue(() => BuildNvidiaProfileSection(refreshCard, refreshCard.GameName));
                    }
                    RefreshStatus();
                }
            }
            catch (Exception unhandledEx)
            {
                CrashReporter.Log($"[DetailPanelBuilder.NrVersionCombo_SelectionChanged] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
            }
        }

        nrBody.Children.Add(row1);
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:Row1({card.GameName})");

        // ── Status line ───────────────────────────────────────────────────────
        statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        nrBody.Children.Add(statusPanel);
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:StatusPanel({card.GameName})");

        void RefreshStatus()
        {
            // Gather all file I/O on a background thread, then update UI
            var scanToken = _panelScanCts.Token;  // capture BEFORE Task.Run
            _ = Task.Run(async () =>
            {
                try { await _panelScanSemaphore.WaitAsync(scanToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { CrashReporter.Log($"[NeuralRendering.RefreshStatus] Semaphore cancelled: '{card.GameName}'"); return; }
                CrashReporter.Log($"[NeuralRendering.RefreshStatus] Semaphore acquired, scanning: '{card.GameName}'");

                // Declare all result variables before try so they're accessible after finally
                bool d5i = false, sfi = false, nri = false, bri = false, fei = false;
                bool dlssi = false, dlssdi = false, dlssgi = false, hostExeOk = true;
                bool srOk = false, rrOk = false, fgOk = false, nrOk = false;
                bool ualOk = false, feedFxPresent = false, lumeniteFxPresent = false;
                bool isDx9Feeder = false, dgVoodooOk = false;
                string? nrv = null, dlssv = null, dlssdv = null, dlssgv = null;
                string? srv = null, rrv = null, fgv = null, nrv2 = null;
                string? ualName = null;
                bool scanSucceeded = false;
                try
                {
                var host64Dir = Path.Combine(installPath, "host64");
                // For 32-bit games: DLSS5 Tool lives in host64\, not game addon folder
                d5i    = card.Is32Bit
                    ? File.Exists(Path.Combine(host64Dir, "renodx-dlss5.addon64"))
                    : rdx5Svc.IsInstalledIn(installPath);
                // For 32-bit games: NR DLL also lives in host64\
                nri    = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll"))
                       || (card.Is32Bit && File.Exists(Path.Combine(host64Dir, "nvngx_dlssnr.dll")));
                sfi    = rdx5Svc.IsSfInstalledIn(installPath);
                bri    = File.Exists(Path.Combine(installPath, BridgeDeployFile));
                fei    = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));
                dlssi  = File.Exists(Path.Combine(installPath, "nvngx_dlss.dll"));
                dlssdi = File.Exists(Path.Combine(installPath, "nvngx_dlssd.dll"));
                dlssgi = File.Exists(Path.Combine(installPath, "nvngx_dlssg.dll"));
                // host64 exe presence (32-bit only)
                hostExeOk = !card.Is32Bit || File.Exists(Path.Combine(host64Dir, "dlss5-feed-host64.exe"));
                nrv    = nri    ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssnr.dll"))) : null;
                dlssv  = dlssi  ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlss.dll")))   : null;
                dlssdv = dlssdi ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssd.dll")))  : null;
                dlssgv = dlssgi ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssg.dll")))  : null;

                // Pre-compute all per-method file checks on the background thread so the
                // UI thread (RefreshStatusWithData) does zero I/O.
                // DlssDetection paths (may differ from install root for deep-plugin games)
                var det = card.DlssDetection;
                var srPath = det?.DlssPath  ?? Path.Combine(installPath, "nvngx_dlss.dll");
                var rrPath = det?.DlssdPath ?? Path.Combine(installPath, "nvngx_dlssd.dll");
                var fgPath = det?.DlssgPath ?? Path.Combine(installPath, "nvngx_dlssg.dll");
                var nrPath = det?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll");
                srOk = File.Exists(srPath);
                rrOk = File.Exists(rrPath);
                fgOk = File.Exists(fgPath);
                nrOk = File.Exists(nrPath);
                srv   = srOk ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(srPath))   : null;
                rrv   = rrOk ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(rrPath))   : null;
                fgv   = fgOk ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(fgPath))   : null;
                nrv2  = nrOk ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(nrPath))   : null;

                // ASI Loader (ShortFuse)
                ualName = _window.ViewModel.GetUalInstalledAs(gameName, store);
                ualOk  = !string.IsNullOrEmpty(ualName)
                       && File.Exists(Path.Combine(installPath, ualName));

                // Feeder shader files
                var shadersDir = Path.Combine(installPath, ShaderPackService.GameReShadeShaders, "Shaders");
                feedFxPresent = Directory.Exists(shadersDir) &&
                    Directory.GetFiles(shadersDir, "DLSS5_Feed.fx", SearchOption.AllDirectories).Length > 0;
                lumeniteFxPresent = Directory.Exists(shadersDir) &&
                    Directory.GetFiles(shadersDir, "lumenite_Kernel.fx", SearchOption.AllDirectories).Length > 0;

                // dgVoodoo2 (DX9 Feeder) — only when DX9 is the PRIMARY graphics API,
                // not just present in DetectedApis (UE4 games have a legacy DX9 shim in DetectedApis).
                isDx9Feeder = card.GraphicsApi == GraphicsApiType.DirectX9;
                dgVoodooOk = isDx9Feeder && App.Services.GetRequiredService<DgVoodooService>().IsDeployed(installPath);
                scanSucceeded = true;
                }
                finally
                {
                    // Always release — even if a File.Exists or Directory.GetFiles throws.
                    // Without this, any exception leaks the semaphore and permanently blocks
                    // all subsequent BuildNvidiaProfileSection / BuildDriverProfileSection calls.
                    _panelScanSemaphore.Release();
                    CrashReporter.Log($"[NeuralRendering.RefreshStatus] Semaphore released (success={scanSucceeded}): '{card.GameName}'");
                }

                if (!scanSucceeded) return;
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    if (_window.ViewModel.SelectedGame != card) return;
                    bool rsi = card.IsRsInstalled;
                    _window.ViewModel.SetLastUiAction($"NeuralRendering.RefreshStatusWithData({card.GameName})");
                    CrashReporter.Log($"[NeuralRendering.RefreshStatus] Updating status for '{card.GameName}'");
                    RefreshStatusWithData(d5i, sfi, nri, bri, fei, rsi, dlssi, dlssdi, dlssgi, nrv, dlssv, dlssdv, dlssgv, hostExeOk,
                        srOk, rrOk, fgOk, nrOk, srv, rrv, fgv, nrv2,
                        ualName, ualOk, feedFxPresent, lumeniteFxPresent, isDx9Feeder, dgVoodooOk);
                });
            });  // end Task.Run
        }  // end RefreshStatus

        void RefreshStatusWithData(
            bool d5i, bool sfi, bool nri, bool bri, bool fei, bool rsi,
            bool dlssi, bool dlssdi, bool dlssgi,
            string? nrv, string? dlssv, string? dlssdv, string? dlssgv,
            bool hostExeOk,
            bool srOk, bool rrOk, bool fgOk, bool nrOk,
            string? srv, string? rrv, string? fgv, string? nrv2,
            string? ualName, bool ualOk,
            bool feedFxPresent, bool lumeniteFxPresent,
            bool isDx9Feeder, bool dgVoodooOk)
        {
            statusPanel.Children.Clear();
            _window.ViewModel.SetLastUiAction($"NeuralRendering.RefreshStatusWithData:Building({card.GameName})");

            void Tag(string text, bool ok)
            {
                statusPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = UIFactory.Brush(ok ? ResourceKeys.AccentGreenBrush : ResourceKeys.TextTertiaryBrush),
                });
            }

            var selectedKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string
                           ?? methodItems.ElementAtOrDefault(methodCombo.SelectedIndex)?.Key
                           ?? effectiveMethod;

            Tag(rsi ? "✓ ReShade" : "✗ ReShade", rsi);

            switch (selectedKey)
            {
                case NrMethodDlss5Tool:
                    Tag(d5i ? "✓ DLSS5 Tool" : "✗ DLSS5 Tool", d5i);
                    if (card.HasDlss)
                    {
                        Tag(srOk ? $"✓ DLSS SR {srv}" : "✗ DLSS SR", srOk);
                        Tag(rrOk ? $"✓ DLSS RR {rrv}" : "✗ DLSS RR", rrOk);
                        Tag(fgOk ? $"✓ DLSS FG {fgv}" : "✗ DLSS FG", fgOk);
                    }
                    Tag(nrOk ? $"✓ NR DLL {nrv2}" : "✗ NR DLL", nrOk);
                    break;
                case NrMethodDlss5ToolBridge:
                    Tag(d5i ? "✓ DLSS5 Tool" : "✗ DLSS5 Tool", d5i);
                    Tag(bri ? "✓ DX11 Bridge" : "✗ DX11 Bridge", bri);
                    if (card.HasDlss)
                    {
                        Tag(srOk ? $"✓ DLSS SR {srv}" : "✗ DLSS SR", srOk);
                        Tag(rrOk ? $"✓ DLSS RR {rrv}" : "✗ DLSS RR", rrOk);
                        Tag(fgOk ? $"✓ DLSS FG {fgv}" : "✗ DLSS FG", fgOk);
                    }
                    Tag(nrOk ? $"✓ NR DLL {nrv2}" : "✗ NR DLL", nrOk);
                    break;
                case NrMethodShortFuse:
                    Tag(sfi    ? "✓ ShortFuse DLSS Tool"  : "✗ ShortFuse DLSS Tool", sfi);
                    Tag(srOk   ? $"✓ DLSS SR {srv}"   : "✗ DLSS SR",  srOk);
                    Tag(rrOk   ? $"✓ DLSS RR {rrv}"   : "✗ DLSS RR", rrOk);
                    Tag(fgOk   ? $"✓ DLSS FG {fgv}"   : "✗ DLSS FG", fgOk);
                    Tag(nrOk   ? $"✓ NR DLL {nrv2}"   : "✗ NR DLL",  nrOk);
                    Tag(ualOk  ? $"✓ ASI Loader ({ualName})" : "✗ ASI Loader", ualOk);
                    break;
                case NrMethodFeeder:
                    Tag(fei   ? "✓ Feeder Addon"            : "✗ Feeder Addon",  fei);
                    // For 32-bit games DLSS5 Tool lives in host64\ — label accordingly
                    if (card.Is32Bit)
                    {
                        Tag(d5i   ? "✓ DLSS5 Tool (host64)"   : "✗ DLSS5 Tool (host64)", d5i);
                        Tag(hostExeOk ? "✓ host64.exe"         : "✗ host64.exe",           hostExeOk);
                    }
                    else
                    {
                        Tag(d5i   ? "✓ DLSS5 Tool"             : "✗ DLSS5 Tool",    d5i);
                    }
                    Tag(dlssi ? $"✓ DLSS SR {dlssv}"        : "✗ DLSS SR",       dlssi);
                    Tag(nri   ? $"✓ NR DLL {nrv}"           : "✗ NR DLL",        nri);
                    Tag(feedFxPresent    ? "✓ Feed.fx"    : "✗ Feed.fx",    feedFxPresent);
                    Tag(lumeniteFxPresent ? "✓ LumeniteFX" : "✗ LumeniteFX", lumeniteFxPresent);
                    if (isDx9Feeder)
                        Tag(dgVoodooOk ? "✓ dgVoodoo2" : "✗ dgVoodoo2", dgVoodooOk);
                    break;
                default:
                    Tag("Not installed", false);
                    break;
            }
        }

        RefreshStatus();

        // ── Description panel (switches per method) ──────────────────────────
        var descBorder = new Border
        {
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var descStack = new StackPanel { Spacing = 4 };
        var descText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        var descLink = new HyperlinkButton
        {
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            Padding = new Thickness(0),
        };
        descStack.Children.Add(descText);
        descStack.Children.Add(descLink);
        descBorder.Child = descStack;
        nrBody.Children.Add(descBorder);
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:DescBorder({card.GameName})");

        void UpdateDescription(string methodKey)
        {
            switch (methodKey)
            {
                case NrMethodDlss5Tool:
                    descText.Text = hasDlss
                        ? "For DX12 games with native DLSS. Deploys the DLSS5 Tool ReShade addon and nvngx_dlssnr.dll. Lighter alternative to ShortFuse DLSS Tool when you don't need the full Streamline stack."
                        : "For DX12 games with native DLSS. This game has no detected DLSS — consider ShortFuse DLSS Tool instead.";
                    descLink.Content = "DLSS5 Tool info →";
                    descLink.NavigateUri = new Uri("https://discord.com/channels/1408098019194310818/1543802634991968366");
                    break;
                case NrMethodDlss5ToolBridge:
                    descText.Text = "For DX11 and Vulkan games with native DLSS. The bridge mirrors the game's DLSS onto a private DX12 session so the NR addon can hook it.";
                    descLink.Content = "DX11 Bridge info →";
                    descLink.NavigateUri = new Uri("https://github.com/NIGos/dlss5-bridge");
                    break;
                case NrMethodShortFuse:
                    descText.Text = "Recommended for most games with native DLSS. Deploys the full DLSS SR/RR/FG/NR stack and Streamline alongside the ReShade addon. Supports DX12, DX11, DX9, and Vulkan.";
                    descLink.Content = "ShortFuse DLSS Tool info →";
                    descLink.NavigateUri = new Uri("https://discord.com/channels/1408098019194310818/1543975158937821315");
                    break;
                case NrMethodFeeder:
                    descText.Text = is32Bit
                        ? "For 32-bit games. Feeds a synthetic DLSS contract from ReShade depth and motion vectors. Deploys the Feeder addon, DLSS5 Tool (neural consumer), NR DLL, DLSS SR DLL, and the required shaders (DLSS5_Feed.fx + LumeniteFX)."
                        : "For games with no native DLSS (DX11, DX12, Vulkan, OpenGL). Feeds a synthetic DLSS contract from ReShade depth and motion vectors. Deploys the Feeder addon, DLSS5 Tool (neural consumer), NR DLL, DLSS SR DLL, and required shaders.";
                    descLink.Content = "Feeder setup guide →";
                    descLink.NavigateUri = new Uri("https://github.com/jlrouzies-fr/DLSS5-Feeder");
                    break;
            }
        }

        UpdateDescription(effectiveMethod);

        // ── Row 2: Install / Remove buttons ──────────────────────────────────
        // Layout: [Install button (stretch)] [⚙ cog] [✕ red X remove]
        // Cog and X are each 36px + 8px gap. X only visible when installed.
        var btnRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        double btnInstallW = containerWidth > NrSectionPadding
            ? Math.Max(120, containerWidth - NrSectionPadding - 2 - 36 - 36 - 8 - 8) // border(2) + cog(36) + X(36) + 2 gaps(8)
            : 400.0;
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(btnInstallW) });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // cog
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // ✕ remove

        installBtn = new Button
        {
            FontSize = 12,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
        };

        var removeBtn = new Button
        {
            Width = 36,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
        };

        // Cog button — always shown, opens method-specific settings
        var sfCogBtn = new Button
        {
            Width = 36, Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
            Visibility = effectiveMethod == NrMethodShortFuse ? Visibility.Visible : Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(sfCogBtn, "ShortFuse DLSS Tool settings — auto-configure ReShade for FrameGen");
        sfCogBtn.Click += async (s, e) =>
        {
            bool currentEnabled = _window.ViewModel.GetSfAutoConfigEnabled(gameName, store);
            bool newEnabled = currentEnabled;

            var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var togLabel = new TextBlock
            {
                Text = "Auto-configure ReShade for FrameGen",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var tog = new ToggleSwitch
            {
                IsOn = currentEnabled,
                OnContent = "On",
                OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
            };
            toggleRow.Children.Add(togLabel);
            toggleRow.Children.Add(tog);

            var desc = new TextBlock
            {
                Text = "When On, RHI will automatically configure ReShade when installing ShortFuse DLSS Tool:\n" +
                       "• Rename ReShade to Reshade64.asi\n" +
                       "• Install ASI Loader (winmm → version → dinput8)\n" +
                       "• Write HookStreamline=1 and HookDirectX=1 to reshade.ini\n\n" +
                       "These steps are needed for FrameGen to work correctly after ReShade.\n\n" +
                       "Note: no longer required on ShortFuse DLSS Tool v0.54 and above.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            };

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(toggleRow);
            content.Children.Add(desc);

            tog.Toggled += (ts, te) => newEnabled = tog.IsOn;

            var dlg = new ContentDialog
            {
                Title = "ShortFuse DLSS Tool Settings",
                Content = content,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = _window.Content.XamlRoot,
            };
            var result = await DialogService.ShowSafeAsync(dlg);
            if (result == ContentDialogResult.Primary && newEnabled != currentEnabled)
                _window.ViewModel.SetSfAutoConfigEnabled(gameName, newEnabled, store);
        };

        void UpdateInstallBtnAppearance()
        {
            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string
                      ?? methodItems.ElementAtOrDefault(methodCombo.SelectedIndex)?.Key
                      ?? effectiveMethod;
            // Use pre-computed install state from the background scan — no File.Exists on UI thread
            bool anyInstalled = selKey switch
            {
                NrMethodDlss5Tool       => _dlss5Installed || _nrDllOwnedByRhi,
                NrMethodDlss5ToolBridge => _dlss5Installed || _bridgePresent,
                NrMethodShortFuse       => _sfInstalled,
                NrMethodFeeder          => _feederPresent,
                _                       => false,
            };

            bool isFeeder = selKey == NrMethodFeeder;

            // Install button appearance
            if (isFeeder)
            {
                installBtn.Content = "Install Feeder Addon";
                installBtn.Background  = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                installBtn.Foreground  = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                installBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }
            else if (anyInstalled)
            {
                installBtn.Content = "Reinstall";
                installBtn.Background  = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                installBtn.Foreground  = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
                installBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush);
            }
            else
            {
                installBtn.Content = "Install Neural Rendering";
                installBtn.Background  = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                installBtn.Foreground  = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                installBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }

            // NR version combo only relevant for DLSS5 Tool / Bridge / Feeder (not ShortFuse)
            // Greyed when already installed — can't change without uninstalling
            bool nrVersionRelevant = selKey == NrMethodDlss5Tool || selKey == NrMethodDlss5ToolBridge || selKey == NrMethodFeeder || selKey == NrMethodShortFuse;
            nrVersionStack.Opacity   = nrVersionRelevant ? 1.0 : 0.4;
            nrVersionCombo.IsEnabled = nrVersionRelevant;

            // Pack version column (Feeder/Bridge only)
            bool showPackVersion = selKey == NrMethodFeeder || selKey == NrMethodDlss5ToolBridge;
            packVersionStack.Visibility = showPackVersion ? Visibility.Visible : Visibility.Collapsed;
            packVersionLabel.Text = selKey == NrMethodFeeder ? "Feeder Version" : "Bridge Version";
            if (showPackVersion)
            {
                PopulatePackVersionCombo(selKey);
                ToolTipService.SetToolTip(packVersionStack,
                    selKey == NrMethodFeeder
                        ? "Version of dlss5-feed.addon64 to install."
                        : "Version of dlss5-bridge.addon64 to install.");
            }

            // DLSS5 Tool / SF version label
            addonVersionLabel.Text = selKey == NrMethodShortFuse ? "SF Version" : "DLSS5 Tool Version";

            // Addon version combo — enabled always (swap-in-place supported while installed)
            addonVersionStack.Opacity   = 1.0;
            addonVersionCombo.IsEnabled = true;
            ToolTipService.SetToolTip(addonVersionStack, selKey == NrMethodFeeder
                ? "Version of renodx-dlss5.addon64 deployed as the neural consumer. Changing while installed swaps the file in-place."
                : "Addon version to install. Changing while installed swaps the file in-place without a full reinstall.");

            // Pack version combo — enabled always when visible (swap-in-place supported while installed)
            packVersionStack.Opacity   = showPackVersion ? 1.0 : 0.4;
            packVersionCombo.IsEnabled = showPackVersion;

            // Remove button visibility
            removeBtn.Visibility = anyInstalled ? Visibility.Visible : Visibility.Collapsed;

            // ShortFuse cog — only visible when ShortFuse is selected
            sfCogBtn.Visibility = selKey == NrMethodShortFuse ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateInstallBtnAppearance();

        // Tag each combo item with its key for easy lookup
        for (int i = 0; i < methodCombo.Items.Count; i++)
        {
            if (methodCombo.Items[i] is ComboBoxItem cbi)
                cbi.Tag = methodItems[i].Key;
        }

        // Method combo change handler
        bool methodComboInit = true;
        methodCombo.SelectionChanged += async (s, ev) =>
        {
            if (methodComboInit) return;
            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;
            if (selKey == effectiveMethod)
            {
                // Same method — just persist and refresh UI
                _window.ViewModel.SetNrMethodOverride(gameName, selKey, store);
                // Repopulate addon version combo in case it wasn't populated yet
                PopulateAddonVersionCombo(selKey == NrMethodShortFuse ? "dlsstool" : "dlss5tool");
                PopulateNrVersionCombo();
                UpdateInstallBtnAppearance();
                UpdateDescription(selKey);
                RefreshStatus();
                return;
            }

            // Repopulate addon version combo for the new method's addon type
            PopulateAddonVersionCombo(selKey == NrMethodShortFuse ? "dlsstool" : "dlss5tool");
            PopulateNrVersionCombo();

            // Different method selected — check installed state off the UI thread (File.Exists on
            // WindowsApps paths can block), then uninstall if needed
            var previousKey = effectiveMethod;
            methodCombo.IsEnabled  = false;
            installBtn.IsEnabled   = false;
            removeBtn.IsEnabled    = false;
            installBtn.Content     = "Removing...";

            try
            {
                await Task.Run(() =>
                {
                    bool anyInstalled = previousKey switch
                    {
                        NrMethodDlss5Tool       => rdx5Svc.IsInstalledIn(installPath),
                        NrMethodDlss5ToolBridge => rdx5Svc.IsInstalledIn(installPath) || File.Exists(Path.Combine(installPath, BridgeDeployFile)),
                        NrMethodShortFuse       => rdx5Svc.IsSfInstalledIn(installPath),
                        NrMethodFeeder          => File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64)),
                        _                       => false,
                    };

                    if (!anyInstalled) return;

                    // Uninstall Cost Scaler first (restores _real → nvngx_dlssnr.dll before NR cleanup)
                    var csSvcSwitch = App.Services.GetRequiredService<DlssNrCostScalerService>();
                    csSvcSwitch.Uninstall(installPath);
                    _window.ViewModel.SetNrCostScalerEnabled(gameName, false, store);

                    switch (previousKey)
                    {
                        case NrMethodDlss5Tool:
                            rdx5Svc.Uninstall(installPath);
                            RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                            break;

                        case NrMethodDlss5ToolBridge:
                            rdx5Svc.Uninstall(installPath);
                            RemoveAddonFile(installPath, BridgeDeployFile, "NeuralRendering.MethodSwitch.Bridge");
                            RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                            break;

                        case NrMethodShortFuse:
                        {
                            var det = _dlssStreamlineService.Detect(installPath);
                            rdx5Svc.UninstallSf(installPath, det.HasAny ? det : null);
                            _window.ViewModel.RevertSfAutoConfig(card);
                            break;
                        }

                        case NrMethodFeeder:
                        {
                            var file = card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
                            RemoveAddonFile(installPath, file, "NeuralRendering.MethodSwitch.Feeder");
                            rdx5Svc.Uninstall(installPath);
                            var dlssDest     = Path.Combine(installPath, "nvngx_dlss.dll");
                            var dlssSentinel = dlssDest + ".original";
                            if (File.Exists(dlssSentinel))
                            {
                                var info = new FileInfo(dlssSentinel);
                                if (info.Length == 0) { try { File.Delete(dlssDest); File.Delete(dlssSentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to remove RHI-placed nvngx_dlss.dll on method switch — {ex.Message}"); } }
                                else { try { File.Copy(dlssSentinel, dlssDest, overwrite: true); File.Delete(dlssSentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to restore original nvngx_dlss.dll on method switch — {ex.Message}"); } }
                            }
                            rdx5Svc.RemoveNrDll(installPath, "Feeder");
                            RemoveFeederShaders(installPath, gameName, store, card);
                            // Remove host64\ and dgVoodoo2 on method switch too
                            var h64 = Path.Combine(installPath, "host64");
                            if (Directory.Exists(h64)) try { Directory.Delete(h64, recursive: true); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to delete '{h64}' — {ex.Message}"); }
                            // Only remove dgVoodoo2 if neither Luma nor standalone is also present
                            bool dgvPreserve = card.LumaStatus == GameStatus.Installed
                                           || _window.ViewModel.GetDgVoodooStandalone(gameName, store);
                            if (!dgvPreserve)
                                App.Services.GetRequiredService<DgVoodooService>().RemoveFromGame(installPath);
                            else
                                CrashReporter.Log($"[NeuralRendering] {(card.LumaStatus == GameStatus.Installed ? "Luma" : "Standalone")} still installed — preserving dgVoodoo2 for '{gameName}'");
                            Models.RhiInstallManifest.RemoveComponent(installPath, "Feeder");
                            break;
                        }
                    }
                    CrashReporter.Log($"[NeuralRendering.MethodSwitch] Removed '{previousKey}', switching to '{selKey}' for '{gameName}'");
                });
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NeuralRendering.MethodSwitch] Remove failed — {ex.Message}");
            }

            _window.ViewModel.SetNrMethodOverride(gameName, selKey, store);

            // Rebuild panel so install button and status reflect the new clean state
            var tc = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) && c.Source == store);
            if (tc != null)
            {
                var detection = _dlssStreamlineService.Detect(installPath);
                tc.DlssDetection = detection;
                tc.ApplyDlssDetection(detection);
                tc.RefreshDlssVersions(_dlssStreamlineService);
                _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
            }
        };
        methodComboInit = false;

        // ── Install button click ──────────────────────────────────────────────
        installBtn.Click += async (s, ev) =>
        {
            installBtn.IsEnabled = false;
            removeBtn.IsEnabled  = false;
            installBtn.Content   = "Installing...";

            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;

            try
            {
                // Pre-seed PerGameShaderSelection before installing ReShade so the
                // SyncGameFolder call inside InstallReShadeAsync deploys shaders correctly
                // instead of wiping them (it reads ShaderModeOverride at call time).
                if (selKey == NrMethodFeeder)
                {
                    var preGameKey = Models.GameKey.From(card.GameName, card.Source ?? "").ToKey();
                    var preCurrent = _gameNameService.PerGameShaderSelection.TryGetValue(preGameKey, out var preSel)
                        ? preSel.ToList() : new List<string>();
                    if (!preCurrent.Contains("DLSS5Feeder", StringComparer.OrdinalIgnoreCase))
                        preCurrent.Add("DLSS5Feeder");
                    if (!preCurrent.Contains("LumeniteFX", StringComparer.OrdinalIgnoreCase))
                        preCurrent.Add("LumeniteFX");
                    _gameNameService.PerGameShaderSelection[preGameKey] = preCurrent;
                    _window.ViewModel.SetPerGameShaderMode(card.GameName, "Select", card.Source ?? "");
                    card.ShaderModeOverride = "Select";
                }

                // Ensure ReShade is installed first — all NR methods require it
                if (!card.IsRsInstalled)
                {
                    _window.DispatcherQueue?.TryEnqueue(() => installBtn.Content = "Installing ReShade...");

                    // For DX9 Feeder: dgVoodoo2 will own d3d9.dll, so ReShade must be dxgi.dll.
                    // Call InstallReShadeInternalAsync directly with the forced filename instead of
                    // going through InstallReShadeCommand which uses auto-detection (returns d3d9.dll for DX9).
                    bool feederDx9 = selKey == NrMethodFeeder
                        && card.GraphicsApi == GraphicsApiType.DirectX9;
                    if (feederDx9)
                        await _window.ViewModel.InstallReShadeInternalAsync(card, "dxgi.dll").ConfigureAwait(false);
                    else
                        await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(card).ConfigureAwait(false);

                    // Wait for card to reflect installed state
                    await Task.Delay(500).ConfigureAwait(false);
                }
                else if (selKey == NrMethodFeeder
                    && (card.DetectedApis.Contains(GraphicsApiType.DirectX9)
                        || (card.DetectedApis.Count == 0 && card.GraphicsApi == GraphicsApiType.DirectX9))
                    && card.RsRecord?.InstalledAs?.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // ReShade already installed as d3d9.dll (wrong for dgVoodoo) — reinstall as dxgi.dll
                    _window.DispatcherQueue?.TryEnqueue(() => installBtn.Content = "Fixing ReShade filename...");
                    await _window.ViewModel.InstallReShadeInternalAsync(card, "dxgi.dll").ConfigureAwait(false);
                    await Task.Delay(300).ConfigureAwait(false);
                }

                switch (selKey)
                {
                    case NrMethodDlss5Tool:
                        await InstallDlss5ToolAsync(card, installBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                        break;

                    case NrMethodDlss5ToolBridge:
                        await InstallDlss5ToolAsync(card, installBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                        await InstallBridgeAddonAsync(card, installBtn, addonSvc, packVersionCombo);
                        // Append bridge file to the Dlss5Tool component record
                        {
                            var bFiles = Models.RhiInstallManifest.GetComponentFiles(installPath, "Dlss5Tool").ToList();
                            if (!bFiles.Contains(BridgeDeployFile, StringComparer.OrdinalIgnoreCase))
                                bFiles.Add(BridgeDeployFile);
                            Models.RhiInstallManifest.SetComponent(installPath, "Dlss5Tool", bFiles);
                        }
                        break;

                    case NrMethodShortFuse:
                        await InstallShortFuseAsync(card, installBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc);
                        break;

                    case NrMethodFeeder:
                        await InstallFeederAddonAsync(card, installBtn, addonSvc, dlssSvc, nrVersionCombo, addonVersionCombo, packVersionCombo);
                        break;
                }

                // If Cost Scaler preference is On, deploy it now (NR DLL is freshly placed)
                if (_window.ViewModel.GetNrCostScalerEnabled(gameName, store))
                {
                    var csSvc = App.Services.GetRequiredService<DlssNrCostScalerService>();
                    if (csSvc.IsStagingReady)
                        csSvc.Install(installPath);
                }

                // Persist chosen method
                _window.ViewModel.SetNrMethodOverride(gameName, selKey, store);

                // Record the active NR method in rhi_install.txt for cross-component reference
                // (e.g. OptiScaler uninstall uses this to know nvngx_dlssnr.dll is NR-owned)
                Models.RhiInstallManifest.SetNrMethod(installPath, selKey);

                // Remove conflicting global addons — DLSS5 Tool and ShortFuse both deploy NR addons
                // that conflict with the NR section. Remove them from the global set so they don't
                // get re-deployed on every refresh.
                var globalAddons = _window.ViewModel.Settings.EnabledGlobalAddons;
                var conflicting  = new[] { "DLSS5 Tool", "ShortFuse DLSS Tool" };
                bool removedAny  = false;
                foreach (var c in conflicting)
                    if (globalAddons.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0)
                        removedAny = true;
                if (removedAny)
                {
                    _window.ViewModel.SaveSettingsPublic();
                    CrashReporter.Log($"[NeuralRendering.Install] Removed conflicting global addons (DLSS5 Tool / ShortFuse) for '{gameName}'");
                }

                // Also remove from per-game selection if the game uses one
                RemoveNrConflictingAddonsFromPerGameSelection(gameName, store, conflicting);

                // Re-deploy addons for this game so stale NR addon files are removed immediately
                _window.ViewModel.DeployAddonsForCard(gameName);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NeuralRendering.Install] Failed for '{gameName}' — {ex.Message}");
                _window.DispatcherQueue?.TryEnqueue(() => installBtn.Content = "Install failed");
            }
            finally
            {
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    installBtn.IsEnabled = true;
                    removeBtn.IsEnabled  = true;
                    // Update cached install state so UpdateInstallBtnAppearance reads correct values
                    _dlss5Installed = rdx5Svc.IsInstalledIn(installPath);
                    _sfInstalled    = rdx5Svc.IsSfInstalledIn(installPath);
                    _bridgePresent  = File.Exists(Path.Combine(installPath, BridgeDeployFile));
                    _feederPresent  = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));
                    _nrDllOwnedByRhi = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll.original"));
                    UpdateInstallBtnAppearance();
                    RefreshStatus();
                    // Persist NR DLL version selection
                    var nrSel = nrVersionCombo.SelectedItem as string;
                    _window.ViewModel.SetNrDllVersion(gameName, (string.IsNullOrEmpty(nrSel) || nrSel.StartsWith("Latest")) ? null : nrSel, store);
                    // Rebuild the full overrides panel so shader mode combo + NR section both refresh
                    var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(store) || c.Source == store));
                    if (targetCard != null)
                        BuildOverridesPanel(targetCard);
                });
            }
        };

        // ── Remove button click ───────────────────────────────────────────────
        removeBtn.Click += async (s, ev) =>
        {
            removeBtn.IsEnabled  = false;
            installBtn.IsEnabled = false;

            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;

            try
            {
                await Task.Run(() =>
                {
                    // Uninstall Cost Scaler first (restores _real → nvngx_dlssnr.dll before NR cleanup)
                    var csSvcRemove = App.Services.GetRequiredService<DlssNrCostScalerService>();
                    csSvcRemove.Uninstall(installPath);
                    _window.ViewModel.SetNrCostScalerEnabled(gameName, false, store);

                    switch (selKey)
                    {
                        case NrMethodDlss5Tool:
                            rdx5Svc.Uninstall(installPath);
                            RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                            break;

                        case NrMethodDlss5ToolBridge:
                            rdx5Svc.Uninstall(installPath);
                            RemoveAddonFile(installPath, BridgeDeployFile, "NeuralRendering.Remove.Bridge");
                            RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                            break;

                        case NrMethodShortFuse:
                        {
                            var det = _dlssStreamlineService.Detect(installPath);
                            rdx5Svc.UninstallSf(installPath, det.HasAny ? det : null);

                            // Revert ShortFuse auto-config (rename Reshade64.asi back, remove UAL)
                            _window.ViewModel.RevertSfAutoConfig(card);
                            break;
                        }

                        case NrMethodFeeder:
                        {
                            var file = card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
                            RemoveAddonFile(installPath, file, "NeuralRendering.Remove.Feeder");

                            // Remove DLSS5 Tool neural consumer
                            rdx5Svc.Uninstall(installPath);

                            // Remove nvngx_dlss.dll if we placed it (sentinel)
                            var dlssDest = Path.Combine(installPath, "nvngx_dlss.dll");
                            var dlssSentinel = dlssDest + ".original";
                            if (File.Exists(dlssSentinel))
                            {
                                var info = new FileInfo(dlssSentinel);
                                if (info.Length == 0) { try { File.Delete(dlssDest); File.Delete(dlssSentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to remove RHI-placed nvngx_dlss.dll — {ex.Message}"); } }
                                else { try { File.Copy(dlssSentinel, dlssDest, overwrite: true); File.Delete(dlssSentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to restore original nvngx_dlss.dll — {ex.Message}"); } }
                            }

                            // Remove NR dll
                            rdx5Svc.RemoveNrDll(installPath, "Feeder");

                            // Remove only DLSS5Feeder + LumeniteFX shader files — never wipe the whole folder
                            RemoveFeederShaders(installPath, gameName, store, card);

                            // Remove host64\ folder (entirely RHI-managed — no game files in it)
                            var host64Dir = Path.Combine(installPath, "host64");
                            if (Directory.Exists(host64Dir))
                            {
                                try { Directory.Delete(host64Dir, recursive: true); CrashReporter.Log($"[NeuralRendering] Removed host64\\ from '{installPath}'"); }
                                catch (Exception h64Ex) { CrashReporter.Log($"[NeuralRendering] host64\\ removal failed — {h64Ex.Message}"); }
                            }

                            // Remove dgVoodoo2 if it was deployed by RHI (sentinel present)
                            // Only remove if Luma isn't also installed (Luma needs D3D9.dll too)
                            if (card.LumaStatus != GameStatus.Installed)
                                App.Services.GetRequiredService<DgVoodooService>().RemoveFromGame(installPath);
                            else
                                CrashReporter.Log($"[NeuralRendering] Luma still installed — preserving dgVoodoo2 for '{gameName}'");

                            Models.RhiInstallManifest.RemoveComponent(installPath, "Feeder");
                            break;
                        }
                    }

                    _window.ViewModel.SetNrMethodOverride(gameName, null, store);

                    // Clear NR method from rhi_install.txt
                    Models.RhiInstallManifest.SetNrMethod(installPath, null);

                    // Remove conflicting addons from global and per-game selections
                    var conflictingRemove = new[] { "DLSS5 Tool", "ShortFuse DLSS Tool" };
                    var globalAddonsRemove = _window.ViewModel.Settings.EnabledGlobalAddons;
                    bool removedGlobal = false;
                    foreach (var c in conflictingRemove)
                        if (globalAddonsRemove.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0)
                            removedGlobal = true;
                    if (removedGlobal) _window.ViewModel.SaveSettingsPublic();
                    RemoveNrConflictingAddonsFromPerGameSelection(gameName, store, conflictingRemove);
                });
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NeuralRendering.Remove] Failed for '{gameName}' — {ex.Message}");
            }
            finally
            {
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    removeBtn.IsEnabled  = true;
                    installBtn.IsEnabled = true;
                    // Update cached install state after removal
                    _dlss5Installed = rdx5Svc.IsInstalledIn(installPath);
                    _sfInstalled    = rdx5Svc.IsSfInstalledIn(installPath);
                    _bridgePresent  = File.Exists(Path.Combine(installPath, BridgeDeployFile));
                    _feederPresent  = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));
                    _nrDllOwnedByRhi = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll.original"));
                    UpdateInstallBtnAppearance();
                    RefreshStatus();
                    // Rebuild full overrides panel so DLSS versions + shader mode reflect new state
                    var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(store) || c.Source == store));
                    if (targetCard != null)
                    {
                        // Re-detect DLSS so Nvidia Profile section shows up-to-date versions
                        var freshDetection = _dlssStreamlineService.Detect(targetCard.InstallPath ?? "");
                        targetCard.ApplyDlssDetection(freshDetection);
                        targetCard.RefreshDlssVersions(_dlssStreamlineService);
                        BuildOverridesPanel(targetCard);
                    }
                });
            }
        };

        Grid.SetColumn(installBtn, 0);
        Grid.SetColumn(sfCogBtn,   1);
        Grid.SetColumn(removeBtn,  2);
        btnRow.Children.Add(installBtn);
        btnRow.Children.Add(sfCogBtn);
        btnRow.Children.Add(removeBtn);
        nrBody.Children.Add(btnRow);
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:BtnRow({card.GameName})");

        // ── NR Cost Scaler preference toggle ─────────────────────────────────
        var costScalerSvc = App.Services.GetRequiredService<DlssNrCostScalerService>();
        bool costScalerPref = _window.ViewModel.GetNrCostScalerEnabled(gameName, store);
        bool nrMethodInstalled = dlss5Installed || sfInstalled || feederPresent || bridgePresent;
        // Toggle is disabled when NR is already installed (must be set before install) or staging not ready
        bool costScalerToggleEnabled = costScalerSvc.IsStagingReady && !nrMethodInstalled;

        var costScalerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0) };
        var costScalerLabel = new TextBlock
        {
            Text = "NR Cost Scaler",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(costScalerLabel,
            "When On, installing a Neural Rendering method will also deploy the DLSS NR Cost Scaler proxy. " +
            "Runs the neural model at reduced resolution (default 75%) for significant GPU savings while keeping native detail.");
        var costScalerToggle = new ToggleSwitch
        {
            IsOn = costScalerPref,
            OnContent = "On", OffContent = "Off",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            IsEnabled = costScalerToggleEnabled,
            Opacity = costScalerToggleEnabled ? 1.0 : 0.45,
        };
        if (!costScalerSvc.IsStagingReady)
            ToolTipService.SetToolTip(costScalerToggle, "Cost Scaler not yet staged — will be available after first launch");
        else if (nrMethodInstalled)
            ToolTipService.SetToolTip(costScalerToggle, "Remove the installed NR method first, then toggle Cost Scaler On before reinstalling");

        // Installed indicator
        bool costScalerInstalled = DlssNrCostScalerService.IsInstalled(installPath);
        var costScalerStatus = new TextBlock
        {
            Text = costScalerInstalled ? "Installed" : "",
            FontSize = 11,
            Foreground = UIFactory.GetBrush("#5ECB7D"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Toggle saves preference only — never installs or uninstalls
        costScalerToggle.Toggled += (s, ev) =>
            _window.ViewModel.SetNrCostScalerEnabled(gameName, costScalerToggle.IsOn, store);

        costScalerRow.Children.Add(costScalerLabel);
        costScalerRow.Children.Add(costScalerToggle);
        costScalerRow.Children.Add(costScalerStatus);
        nrBody.Children.Add(costScalerRow);
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:CostScalerRow({card.GameName})");

        // Note shown when ShortFuse method is selected — cost scaler is now built into 310.8.2
        if (effectiveMethod == NrMethodShortFuse)
        {
            // ── ZZZ Mode toggle — appended to the same row as Cost Scaler ────
            bool sfZzzPref = _window.ViewModel.GetSfZzzMode(gameName, store);
            costScalerRow.Children.Add(new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(4, 0, 4, 0) });
            costScalerRow.Children.Add(new TextBlock
            {
                Text = "ZZZ Load Order",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var zzzToggle = new ToggleSwitch
            {
                IsOn = sfZzzPref,
                OnContent = "On", OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
            };
            ToolTipService.SetToolTip(zzzToggle,
                "When On, the ShortFuse addon is deployed as zzz_renodx-dlss.addon64 so it loads last in the ReShade addon order. " +
                "Required for some games where other addons must initialise before the DLSS Tool.");
            zzzToggle.Toggled += (s, ev) =>
            {
                bool newVal = zzzToggle.IsOn;
                _window.ViewModel.SetSfZzzMode(gameName, newVal, store);

                // If SF is already installed, rename the file on disk immediately
                var deployDir = ModInstallService.GetAddonDeployPath(installPath);
                var normalPath = System.IO.Path.Combine(deployDir, "renodx-dlss.addon64");
                var zzzPath    = System.IO.Path.Combine(deployDir, Renodx5AddonService.SfZzzDeployFileName);
                try
                {
                    if (newVal && File.Exists(normalPath) && !File.Exists(zzzPath))
                    {
                        File.Move(normalPath, zzzPath);
                        CrashReporter.Log($"[NeuralRendering.ZzzToggle] Renamed → {Renodx5AddonService.SfZzzDeployFileName} for '{gameName}'");
                    }
                    else if (!newVal && File.Exists(zzzPath) && !File.Exists(normalPath))
                    {
                        File.Move(zzzPath, normalPath);
                        CrashReporter.Log($"[NeuralRendering.ZzzToggle] Renamed → renodx-dlss.addon64 for '{gameName}'");
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[NeuralRendering.ZzzToggle] Rename failed — {ex.Message}"); }
            };
            costScalerRow.Children.Add(zzzToggle);

            nrBody.Children.Add(new TextBlock
            {
                Text = "Cost Scaler is built into the ShortFuse addon — this toggle is no longer required but remains available if you prefer the standalone version.",
                FontSize = 10,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                Opacity = 0.8,
            });
        }

        // ── How to use links ──────────────────────────────────────────────────
        var linksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        HyperlinkButton MakeLink(string text, string url) => new HyperlinkButton
        {
            Content = text,
            NavigateUri = new Uri(url),
            FontSize = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            Padding = new Thickness(0),
        };
        linksRow.Children.Add(MakeLink("DLSS5 Tool →",  "https://discord.com/channels/1408098019194310818/1543802634991968366"));
        linksRow.Children.Add(MakeLink("DX11 Bridge →", "https://github.com/NIGos/dlss5-bridge"));
        linksRow.Children.Add(MakeLink("ShortFuse →",   "https://discord.com/channels/1408098019194310818/1543975158937821315"));
        linksRow.Children.Add(MakeLink("Feeder →",      "https://github.com/jlrouzies-fr/DLSS5-Feeder"));
        nrBody.Children.Add(linksRow);
        _window.ViewModel.SetLastUiAction($"NeuralRenderingSectionWithData:Done({card.GameName})");
    }
}
