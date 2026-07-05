// DetailPanelBuilder.Overrides.DriverSettings.cs — Driver Profile Settings (VSync, Latency, Smooth Motion, Power/CPU, ReBAR).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildDriverProfileSection(GameCardViewModel card, string capturedName)
    {

        // ══════════════════════════════════════════════════════════════════════
        // Nvidia Profile Settings — VSync, Latency, Smooth Motion, Power/CPU, ReBAR
        // ══════════════════════════════════════════════════════════════════════
        var nvidiaPresetService = _dlssPresetService;
        if (nvidiaPresetService.IsSupported)
        {
            bool isAdmin = VulkanLayerService.IsRunningAsAdmin();

            _window.NvidiaProfilePanel.Children.Add(UIFactory.MakeSeparator());

            var nvidiaGrid = new Grid { ColumnSpacing = 12, Opacity = isAdmin ? 1.0 : 0.4, IsHitTestVisible = isAdmin };
            // 4 columns with dividers between: col0 | div1 | col2 | div3 | col4 | div5 | col6
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var installPathSafe = card.InstallPath ?? "";

            // ── Column 0: VSync ──
            var vsyncCol = new StackPanel { Spacing = 4 };
            var vsyncLabel = new TextBlock { Text = "VSync", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(vsyncLabel, "Vertical Sync settings — controls how the driver synchronizes frame rendering with your display's refresh rate.");
            vsyncCol.Children.Add(vsyncLabel);

            // VSync Mode
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncModeOptions;
                uint current = nvidiaPresetService.GetVSyncMode(card.GameName, installPathSafe);
                var globalVSync = nvidiaPresetService.GetGlobalVSyncMode();

                var itemsList = new List<string>();
                if (globalVSync.HasValue)
                {
                    var globalName = options.FirstOrDefault(o => o.Value == globalVSync.Value).Name ?? "App Controlled";
                    itemsList.Add($"Global ({globalName})");
                }
                itemsList.AddRange(options.Select(o => o.Name));
                var items = itemsList.ToArray();

                // Determine selected index
                int idx;
                if (globalVSync.HasValue)
                {
                    bool perGameMatchesGlobal = current == globalVSync.Value;
                    var perGameIdx = Array.FindIndex(options, o => o.Value == current);
                    idx = perGameMatchesGlobal ? 0 : (perGameIdx >= 0 ? perGameIdx + 1 : 0);
                }
                else
                {
                    idx = Array.FindIndex(options, o => o.Value == current);
                    if (idx < 0) idx = 0;
                }

                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, globalVSync.HasValue
                    ? "Global = inherit from global setting. App Controlled: let the game decide. Force Off: disables VSync. Force On: locks to refresh rate. Fast Sync: renders freely, displays latest complete frame."
                    : "VSync Mode — App Controlled: let the game decide. Force Off: disables VSync entirely. Force On: locks to refresh rate. Fast Sync: renders freely, displays latest complete frame.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= items.Length) return;
                    var selected = items[i];
                    if (selected.StartsWith("Global"))
                    {
                        // Inherit from global — write the global value
                        nvidiaPresetService.SetVSyncMode(card.GameName, installPathSafe, globalVSync ?? options[0].Value);
                    }
                    else
                    {
                        int optIdx = globalVSync.HasValue ? i - 1 : i;
                        if (optIdx >= 0 && optIdx < options.Length)
                            nvidiaPresetService.SetVSyncMode(card.GameName, installPathSafe, options[optIdx].Value);
                    }
                };
                vsyncCol.Children.Add(combo);
                init = false;
            }

            // VSync Tear Control
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Tear Control", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncTearControlOptions;
                uint current = nvidiaPresetService.GetVSyncTearControl(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "VSync Tear Control — Standard: normal VSync behavior. Adaptive: VSync on when FPS ≥ refresh rate, off when below (reduces stuttering at low FPS).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetVSyncTearControl(card.GameName, installPathSafe, options[i].Value);
                };
                vsyncCol.Children.Add(combo);
                init = false;
            }

            // Low Latency Mode (in VSync column)
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Low Latency", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.LowLatencyModeOptions;
                uint current = nvidiaPresetService.GetLowLatencyMode(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo2 = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo2, "Low Latency Mode — Off: game controls frame queue. On: limits pre-rendered frames to 1 (lower latency). Ultra: just-in-time frame submission (lowest latency, may reduce FPS slightly).");
                var init2 = true;
                combo2.SelectionChanged += (s, ev) =>
                {
                    if (init2) return;
                    int i = combo2.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetLowLatencyMode(card.GameName, installPathSafe, options[i].Value);
                };
                vsyncCol.Children.Add(combo2);
                init2 = false;
            }

            Grid.SetColumn(vsyncCol, 0);
            nvidiaGrid.Children.Add(vsyncCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(1));

            // ── Column 4: Smooth Motion ──
            var smoothCol = new StackPanel { Spacing = 4 };
            var smoothLabel = new TextBlock { Text = "Smooth Motion", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(smoothLabel, "NVIDIA Smooth Motion — driver-level frame generation. Adds interpolated frames for smoother visuals. RTX 40 Series+ required.");
            smoothCol.Children.Add(smoothLabel);

            // Enable
            bool smoothMotionEnabled;
            {
                smoothCol.Children.Add(new TextBlock { Text = "Enable", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionEnableOptions;
                uint current = nvidiaPresetService.GetSmoothMotionEnable(card.GameName, installPathSafe);
                smoothMotionEnabled = current != 0;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "Smooth Motion Enable — Off: disabled. On: enables driver-level frame generation (RTX 40 Series+ only).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetSmoothMotionEnable(card.GameName, installPathSafe, options[i].Value);
                    // Cascade: set APIs to All when enabling, None when disabling
                    bool enabling = options[i].Value != 0;
                    nvidiaPresetService.SetSmoothMotionApis(card.GameName, installPathSafe, enabling ? 0x00000007u : 0x00000000u);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // APIs
            {
                smoothCol.Children.Add(new TextBlock { Text = "Allowed APIs", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionApisOptions;
                uint current = nvidiaPresetService.GetSmoothMotionApis(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Smooth Motion APIs — which graphics APIs Smooth Motion is allowed to hook. None = disabled for all APIs.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetSmoothMotionApis(card.GameName, installPathSafe, options[i].Value);
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // Flip Pacing (combined — sets both Fullscreen and Windowed together)
            {
                smoothCol.Children.Add(new TextBlock { Text = "Flip Pacing", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionFlipPacingFsOptions;
                uint current = nvidiaPresetService.GetSmoothMotionFlipPacingFs(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Flip Pacing — Off: prioritize lower latency. On: prioritize smoother frame pacing. Sets both fullscreen and windowed modes together.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetSmoothMotionFlipPacingFs(card.GameName, installPathSafe, options[i].Value);
                    // Also set windowed pacing to the same value (use 0x00000001 for "On" instead of 0xFFFFFFFF)
                    uint winValue = options[i].Value == 0xFFFFFFFF ? 0x00000001 : options[i].Value;
                    nvidiaPresetService.SetSmoothMotionFlipPacingWin(card.GameName, installPathSafe, winValue);
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            Grid.SetColumn(smoothCol, 4);
            nvidiaGrid.Children.Add(smoothCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(5));

            // ── Column 6: Power / CPU ──
            var powerCol = new StackPanel { Spacing = 4 };
            var powerLabel = new TextBlock { Text = "Power", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(powerLabel, "GPU power management. Maximum Performance prevents clock throttling. Optimal is NVIDIA's recommended default.");
            powerCol.Children.Add(powerLabel);

            // Power Management Mode
            {
                powerCol.Children.Add(new TextBlock { Text = "Power Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.PowerManagementOptions;
                uint current = nvidiaPresetService.GetPowerManagementMode(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "Power Management — Adaptive: GPU clocks down at idle. Maximum: locks GPU to highest clocks. Optimal: balanced (NVIDIA recommended).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetPowerManagementMode(card.GameName, installPathSafe, options[i].Value);
                };
                powerCol.Children.Add(combo);
                init = false;
            }

            // Restore Profile Defaults button (aligned with Flip Pacing combo in Smooth Motion column)
            powerCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            var restoreProfileBtn = new Button
            {
                Content = "Restore Defaults",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = nvidiaPresetService.IsSupported,
            };
            ToolTipService.SetToolTip(restoreProfileBtn,
                "Restore this game's NVIDIA driver profile to factory defaults. Removes all custom settings (presets, render scale, MFG, driver overrides). This action is irreversible.");
            restoreProfileBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                var warningDialog = new ContentDialog
                {
                    Title = "Restore driver settings?",
                    Content = new TextBlock
                    {
                        Text = $"This will restore driver settings for {capturedName} back to the factory default and restore DLSS/Streamline DLLs to their original versions. This action is irreversible.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                    },
                    PrimaryButtonText = "Restore",
                    CloseButtonText = "Cancel",
                    XamlRoot = xamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var result = await DialogService.ShowSafeAsync(warningDialog);
                if (result != ContentDialogResult.Primary) return;

                var success = nvidiaPresetService.RestoreProfileDefaults(capturedName, installPathSafe);
                if (success)
                {
                    var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (refreshCard != null)
                    {
                        // Also restore DLSS/Streamline DLLs to originals
                        if (refreshCard.DlssDetection != null)
                        {
                            var dlssSvc = _dlssStreamlineService;
                            dlssSvc.RestoreAll(refreshCard.DlssDetection);
                            refreshCard.RefreshDlssVersions(dlssSvc);
                        }
                        _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(refreshCard));
                    }
                }
            };
            powerCol.Children.Add(restoreProfileBtn);

            Grid.SetColumn(powerCol, 6);
            nvidiaGrid.Children.Add(powerCol);

            // ── Column 8: ReBAR ──
            var rebarCol = new StackPanel { Spacing = 4 };
            var rebarLabel = new TextBlock { Text = "ReBAR", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(rebarLabel, "Resizable BAR — allows the CPU to access full GPU VRAM at once. Can improve performance by 5-10% in some titles. RTX 30+ and BIOS support required.");
            rebarCol.Children.Add(rebarLabel);

            bool rebarEnabled = nvidiaPresetService.GetReBarEnabled(card.GameName, installPathSafe);
            ulong rebarSizeLimit = nvidiaPresetService.GetReBarSizeLimit(card.GameName, installPathSafe);
            var globalReBarState = nvidiaPresetService.GetGlobalReBarEnabled();

            // Enable — with Global (On/Off) option when global is set
            {
                rebarCol.Children.Add(new TextBlock { Text = "Enable", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var enableItems = new List<string>();
                if (globalReBarState.HasValue)
                    enableItems.Add($"Global ({(globalReBarState.Value ? "On" : "Off")})");
                enableItems.Add("Off");
                enableItems.Add("On");

                // Determine selected index
                int enableIdx;
                if (globalReBarState.HasValue)
                {
                    // If per-game matches global, show "Global" selected; otherwise show the per-game value
                    bool perGameMatchesGlobal = rebarEnabled == globalReBarState.Value;
                    enableIdx = perGameMatchesGlobal ? 0 : (rebarEnabled ? 2 : 1); // Global=0, Off=1, On=2
                }
                else
                {
                    enableIdx = rebarEnabled ? 1 : 0; // Off=0, On=1
                }

                var rebarEnableCombo = new ComboBox
                {
                    ItemsSource = enableItems,
                    SelectedIndex = enableIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(rebarEnableCombo, globalReBarState.HasValue
                    ? "Global = inherit from global setting. On/Off = per-game override."
                    : "Off = ReBAR disabled. On = Force-enable ReBAR for this game.");
                var rebarComboInit = true;
                rebarEnableCombo.SelectionChanged += (s, ev) =>
                {
                    if (rebarComboInit) return;
                    var selected = rebarEnableCombo.SelectedItem as string;
                    if (selected != null && selected.StartsWith("Global"))
                    {
                        // Remove per-game override — inherit from global
                        // Delete the per-game setting by setting it to match global
                        bool globalVal = globalReBarState ?? false;
                        nvidiaPresetService.SetReBarEnabled(card.GameName, installPathSafe, globalVal, 2);
                    }
                    else
                    {
                        bool enabling = selected == "On";
                        nvidiaPresetService.SetReBarEnabled(card.GameName, installPathSafe, enabling, 2);
                    }
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
                };
                rebarCol.Children.Add(rebarEnableCombo);
                rebarComboInit = false;
            }

            // Mode
            {
                rebarCol.Children.Add(new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                uint rebarMode = nvidiaPresetService.GetReBarMode(card.GameName, installPathSafe);
                var modeItems = DlssPresetService.ReBarModes.Select(m => m.Name).ToList();

                // Select current effective mode: per-game value, or default to Standard (index 0)
                int modeIdx = Array.FindIndex(DlssPresetService.ReBarModes, m => m.Value == rebarMode);
                if (modeIdx < 0) modeIdx = 0;

                var rebarModeCombo = new ComboBox
                {
                    ItemsSource = modeItems,
                    SelectedIndex = modeIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = rebarEnabled,
                    Opacity = rebarEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarModeCombo, "Standard = conservative. Optimized = aggressive driver scheduling (used by NVIDIA-whitelisted titles).");
                var modeComboInit = true;
                rebarModeCombo.SelectionChanged += (s, ev) =>
                {
                    if (modeComboInit) return;
                    int idx = rebarModeCombo.SelectedIndex;
                    if (idx < 0) return;
                    uint newMode = DlssPresetService.ReBarModes[idx].Value;
                    nvidiaPresetService.SetReBarMode(card.GameName, installPathSafe, newMode);
                };
                rebarCol.Children.Add(rebarModeCombo);
                modeComboInit = false;
            }

            // Size Limit — always shows actual size values (no Global option)
            {
                rebarCol.Children.Add(new TextBlock { Text = "Size Limit", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                var sizeItems = new List<string>();
                var sizeValues = new List<ulong>();
                foreach (var sl in DlssPresetService.ReBarSizeLimits)
                {
                    sizeItems.Add(sl.Name);
                    sizeValues.Add(sl.Value);
                }

                // Select the current effective size: per-game override, or global, or 1GB default
                ulong globalSize = nvidiaPresetService.GetGlobalReBarSizeLimit();
                ulong effectiveSize = rebarSizeLimit != 0 ? rebarSizeLimit : (globalSize != 0 ? globalSize : 0x0000000040000000);
                int sizeIdx;
                var matchIdx = sizeValues.IndexOf(effectiveSize);
                sizeIdx = matchIdx >= 0 ? matchIdx : 1; // Default: 1GB (index 1)

                var rebarSizeCombo = new ComboBox
                {
                    ItemsSource = sizeItems,
                    SelectedIndex = sizeIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = rebarEnabled,
                    Opacity = rebarEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarSizeCombo, "1GB is optimal for most games. Decrease to 512MB if experiencing ReBAR-related stutters.");
                var sizeComboInit = true;
                rebarSizeCombo.SelectionChanged += (s, ev) =>
                {
                    if (sizeComboInit) return;
                    int idx = rebarSizeCombo.SelectedIndex;
                    if (idx < 0) return;
                    ulong newSize = sizeValues[idx];
                    nvidiaPresetService.SetReBarSizeLimit(card.GameName, installPathSafe, newSize);
                };
                rebarCol.Children.Add(rebarSizeCombo);
                sizeComboInit = false;
            }

            Grid.SetColumn(rebarCol, 2);
            nvidiaGrid.Children.Add(rebarCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(3));

            _window.NvidiaProfilePanel.Children.Add(nvidiaGrid);
        }

        // Admin notice at the bottom of the Nvidia Profile section
        bool isElevated = VulkanLayerService.IsRunningAsAdmin();
        _window.NvidiaProfilePanel.Children.Add(new TextBlock
        {
            Text = isElevated
                ? "✓ Running as admin — all driver profile settings are writable."
                : "⚠ Admin rights required to write driver profile settings. Enable Admin Mode in Settings or restart as admin.",
            FontSize = 10,
            Foreground = UIFactory.Brush(isElevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

    }
}