using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Encapsulates settings-page event handlers and toggle logic.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class SettingsHandler
{
    // ── Instance members ───────────────────────────────────────────────

    private readonly MainWindow _window;
    private readonly IDxvkService _dxvkService;
    private readonly IReShadeUpdateService _reShadeUpdateService;
    private readonly ReShadeNightlyService _reShadeNightlyService;

    /// <summary>
    /// Stores the current hotkey string in KeyOverlay format ("vk,shift,ctrl,alt").
    /// Accessible from the Apply handler (Task 6.3).
    /// </summary>
    internal string _currentHotkeyString = "36,0,0,0";
    internal string _currentUlHotkeyString = "F12";
    internal string _currentOsHotkeyString = "Insert";
    internal string _currentScreenshotHotkeyString = "44,0,0,0";

    public SettingsHandler(MainWindow window)
    {
        _window = window;
        _dxvkService = App.Services.GetRequiredService<IDxvkService>();
        _reShadeUpdateService = App.Services.GetRequiredService<IReShadeUpdateService>();
        _reShadeNightlyService = App.Services.GetRequiredService<ReShadeNightlyService>();
    }

    private MainViewModel ViewModel => _window.ViewModel;

    public async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool settingsWasOpen = _window.SettingsPanel.Visibility == Visibility.Visible;
            ViewModel.NavigateToSettingsCommand.Execute(null);
            _window.GameViewPanel.Visibility = Visibility.Collapsed;
            _window.SettingsPanel.Visibility = Visibility.Visible;
            _window.LoadingPanel.Visibility = Visibility.Collapsed;
            // Nav column / search / update countdown (one-time setup) + scroll to the last section
            OnSettingsPageOpened(settingsWasOpen);
            // Sync toggle state with ViewModel
            // (without triggering the change handler, which confirms and redeploys shaders)
            SyncShaderModeCombo();
            _window.AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  Simplified PC Gaming by RankFTW";
            // Show installed Windows App Runtime version
            // Populate addon watch folder textbox
            _window.AddonWatchFolderBox.Text = ViewModel.Settings.AddonWatchFolder;
            // Populate screenshot path and per-game combo
            _window.ScreenshotPathBox.Text = ViewModel.Settings.ScreenshotPath;
            _window.PerGameScreenshotCombo.SelectedIndex = ViewModel.Settings.PerGameScreenshotFolders ? 1 : 0;
            // Effect list style combo (Tabs=0, Tree=1)
            _window.RsVariableListUseTabsCombo.SelectedIndex = ViewModel.Settings.RsVariableListUseTabs ? 0 : 1;
            // Initialize peak nits display
            _window.PeakNitsBox.Text = ViewModel.Settings.PeakNits > 0 ? ViewModel.Settings.PeakNits.ToString() : "";
            // Initialize hotkey display from persisted value (Req 2.4, 3.2)
            _currentHotkeyString = ViewModel.Settings.OverlayHotkey;
            _window.HotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(ViewModel.Settings.OverlayHotkey);
            // Initialize screenshot hotkey display
            _currentScreenshotHotkeyString = ViewModel.Settings.ScreenshotHotkey;
            _window.ScreenshotHotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(ViewModel.Settings.ScreenshotHotkey);
            // Initialize ReLimiter OSD hotkey display
            _currentUlHotkeyString = ViewModel.Settings.UlOsdHotkey;
            _window.UlHotkeyBox.Text = ViewModel.Settings.UlOsdHotkey;
            // Initialize ReLimiter shared presets toggle
            _window.UlSharedPresetsCombo.SelectedIndex = ViewModel.Settings.UlSharedPresets ? 1 : 0;
            _window.UlDlssHooksCombo.SelectedIndex = ViewModel.Settings.UlDlssHooks ? 1 : 0;
            // Initialize ReLimiter Target FPS dropdown
            InitializeUlTargetFpsCombo();
            // Initialize OptiScaler hotkey display
            _currentOsHotkeyString = ViewModel.Settings.OsHotkey;
            var osCombo = _window.OsHotkeyCombo;
            for (int i = 0; i < osCombo.Items.Count; i++)
            {
                if (osCombo.Items[i] is string item &&
                    item.Equals(_currentOsHotkeyString, StringComparison.OrdinalIgnoreCase))
                {
                    osCombo.SelectedIndex = i;
                    break;
                }
            }
            if (osCombo.SelectedIndex < 0)
                osCombo.SelectedIndex = 0; // Default to Insert

            // Initialize OptiScaler GPU combo
            var gpuCombo = _window.OsGpuCombo;
            var gpuType = ViewModel.Settings.OsGpuType;
            for (int i = 0; i < gpuCombo.Items.Count; i++)
            {
                if (gpuCombo.Items[i] is string gpuItem &&
                    gpuItem.Equals(gpuType, StringComparison.OrdinalIgnoreCase))
                {
                    gpuCombo.SelectedIndex = i;
                    break;
                }
            }
            if (gpuCombo.SelectedIndex < 0)
                gpuCombo.SelectedIndex = 0; // Default to NVIDIA

            // Default OptiScaler version (row added from C#)
            EnsureOsDefaultVariantRow();
            // Interface size (row added from C#)
            EnsureUiScaleRow();

            // Initialize DLSS inputs combo and visibility
            _window.OsDlssInputsCombo.SelectedIndex = ViewModel.Settings.OsDlssInputs ? 1 : 0; // 0=Off, 1=On
            bool showDlss = !string.Equals(gpuType, "NVIDIA", StringComparison.OrdinalIgnoreCase);
            _window.OsDlssInputsPanel.Visibility = showDlss ? Visibility.Visible : Visibility.Collapsed;

            // Initialize Global Update Checks summary
            RefreshGlobalUpdateSummary();
            _window.CacheAllShadersCombo.SelectedIndex = ViewModel.Settings.CacheAllShaders ? 1 : 0;

            // Initialize DXVK variant combo
            ViewModel.Settings.IsLoadingSettings = true;
            var dxvkCombo = _window.DxvkVariantCombo;
            var dxvkVariant = ViewModel.Settings.DxvkVariant;
            if (string.Equals(dxvkVariant, "Stable", StringComparison.OrdinalIgnoreCase))
                dxvkCombo.SelectedIndex = 1;
            else if (string.Equals(dxvkVariant, "LiliumHdr", StringComparison.OrdinalIgnoreCase))
                dxvkCombo.SelectedIndex = 2;
            else
                dxvkCombo.SelectedIndex = 0;
            ViewModel.Settings.IsLoadingSettings = false;

            // Initialize ReShade channel combo
            var rsChannelCombo = _window.ReShadeChannelCombo;
            var rsChannel = ViewModel.Settings.ReShadeChannel;
            if (string.Equals(rsChannel, "Nightly", StringComparison.OrdinalIgnoreCase))
                rsChannelCombo.SelectedIndex = 1;
            else
                rsChannelCombo.SelectedIndex = 0;

            // Initialize DLSS Indicator combo from registry (read on background thread)
            bool dlssIndicatorEnabled = false;
            await Task.Run(() =>
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\NGXCore");
                    var val = key?.GetValue("ShowDlssIndicator");
                    // 0x400 = enabled, 0 = disabled. Default to disabled if key doesn't exist.
                    dlssIndicatorEnabled = val is int intVal && intVal != 0;
                }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.SettingsButton_Click] Failed to read DLSS indicator registry value — {ex.Message}"); }
            });
            _window.DlssIndicatorCombo.SelectedIndex = dlssIndicatorEnabled ? 1 : 0; // 0=Off, 1=On
            // Keep _dlssIndicatorInitializing = true until ALL NVAPI combos are populated below.
            // Setting it false here was too early — GSyncIndicator and other combos further down
            // would fire their SelectionChanged handlers, each calling SetGSyncIndicator / SetGSyncMode etc.
            // which call _session.Save(). That runs concurrently with the NVAPI read Task.Run for the
            // panel, causing an unsynchronized concurrent NVAPI access → UI deadlock. (#freeze bug)
            // _dlssIndicatorInitializing is set false at the END of PopulateNvApiCombosFromSnapshot.

            // Initialize DLSS/Streamline auto-update combos
            _window.AutoUpdateDlssCombo.SelectedIndex = ViewModel.Settings.AutoUpdateDlss ? 1 : 0;
            _window.AutoUpdateStreamlineCombo.SelectedIndex = ViewModel.Settings.AutoUpdateStreamline ? 1 : 0;

            // Initialize component auto-update combo
            _window.AutoUpdateComponentsCombo.SelectedIndex = ViewModel.Settings.AutoUpdateComponents ? 1 : 0;

            // Initialize background update checks combo
            _window.BackgroundUpdateChecksCombo.SelectedIndex = ViewModel.Settings.BackgroundUpdateChecks == "Minimal" ? 1 : 0;

            // Initialize HDR auto-toggle combo
            _window.HdrAutoToggleCombo.SelectedIndex = ViewModel.Settings.HdrAutoToggle ? 1 : 0;

            // Initialize Resolution Control (FeatureFlags.ResolutionControl is always true).
            // ResolutionControlCard is the whole Display card, which also holds HDR Auto-Toggle;
            // if this flag ever becomes switchable, hide only the resolution/colour rows instead.
            if (FeatureFlags.ResolutionControl)
            {
                _window.ResolutionControlCard.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                _window.ResAutoToggleCombo.SelectedIndex = ViewModel.Settings.ResolutionAutoToggle ? 1 : 0;

                // Populate resolution combo with supported modes
                var resolutions = ResolutionToggleService.GetSupportedResolutions();
                _window.ResolutionTargetCombo.ItemsSource = resolutions;
                _window.ResolutionTargetCombo.DisplayMemberPath = "Label";

                var savedKey = ViewModel.Settings.ResolutionTarget;
                if (!string.IsNullOrEmpty(savedKey))
                {
                    var match = resolutions.FirstOrDefault(r =>
                        string.Equals(r.Key, savedKey, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        _window.ResolutionTargetCombo.SelectedItem = match;
                }
                if (_window.ResolutionTargetCombo.SelectedIndex < 0 && resolutions.Count > 0)
                    _window.ResolutionTargetCombo.SelectedIndex = 0;

                // Populate colour display combo — enumerate NVIDIA displays
                var nvDisplays = NvColorService.GetDisplays();
                if (nvDisplays.Count > 0)
                {
                    _window.ColorDisplayCombo.ItemsSource    = nvDisplays;
                    _window.ColorDisplayCombo.DisplayMemberPath = "Name";
                    _window.ColorDisplayCombo.SelectedIndex  = 0; // triggers SelectionChanged → reads current values
                }
                else
                {
                    // No NVIDIA displays found — hide the colour controls
                    _window.ColorDisplayCombo.Visibility  = Microsoft.UI.Xaml.Visibility.Collapsed;
                    // Also hide its "Monitor" label (the combo's unnamed parent row)
                    if (_window.ColorDisplayCombo.Parent is FrameworkElement displayRow)
                        displayRow.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _window.ColorDepthCombo.IsEnabled     = false;
                    _window.ColorRangeCombo.IsEnabled     = false;
                    _window.ColorApplyBtn.IsEnabled       = false;
                }

                // Initialize DLDSR Control
                InitDldsrControl();
            }

            // Populate DLSS defaults summary
            _window.RefreshDlssDefaultsSummary();

            // Populate NVAPI-dependent combos (shader cache, G-Sync, FPS limit, ReBAR, VSync, Power)
            // Fetch all NVAPI values on a background thread with timeout to prevent UI freeze after GPU sleep/wake
            _window._shaderCacheComboInit = true;
            var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
            if (presetSvc.IsSupported)
            {
                var snapshot = await presetSvc.FetchNvApiSettingsAsync(timeoutMs: 5000);
                PopulateNvApiCombosFromSnapshot(presetSvc, snapshot);
            }
            _window._shaderCacheComboInit = false;

            // Initialize admin mode combo (async to avoid blocking on schtasks query)
            await InitAdminModeComboAsync(_window.AdminModeCombo);

            // Initialize drop helper combo (greyed out when not in admin mode — not needed)
            _window.DropHelperCombo.SelectedIndex = ViewModel.Settings.DropHelperEnabled ? 1 : 0;
            _window.DropHelperCombo.IsEnabled = VulkanLayerService.IsRunningAsAdmin();

            // Initialize tray combos — set a flag so SelectionChanged handlers skip
            // writing to the registry/settings during programmatic init.
            _window.TrayComboInitializing = true;
            _window.CloseToTrayCombo.SelectedIndex      = ViewModel.Settings.CloseToTray ? 1 : 0;
            _window.RecentGamesCombo.SelectedIndex      = ViewModel.Settings.RecentGamesMenu ? 1 : 0;
            _window.StartWithWindowsCombo.SelectedIndex = ViewModel.Settings.StartWithWindows ? 1 : 0;
            _window.TrayComboInitializing = false;

            // Initialize Nexus Mods card (dev-only)
            if (FeatureFlags.NexusMods)
            {
                _window.NexusModsCard.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                RefreshNexusStatus();
            }

            // RenoDX Data Source card — always visible now that RHI Database is the default
            _window.RenoDxDbSourceCard.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            InitRenoDxDbSourceCombo();

            RefreshGitHubStatus();
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.SettingsButton_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
        finally
        {
            // Populating can change heights above the restored section: re-apply the scroll if untouched
            OnSettingsPagePopulated();
        }
    }

    /// <summary>
    /// Populates all NVAPI-dependent combos from a pre-fetched snapshot.
    /// This runs on the UI thread after the background fetch completes.
    /// </summary>
    private void PopulateNvApiCombosFromSnapshot(DlssPresetService presetSvc, NvApiSettingsSnapshot snapshot)
    {
        // Shader Cache Size
        _window.ShaderCacheSizeCombo.ItemsSource = DlssPresetService.ShaderCacheSizeOptions.Select(o => o.Name).ToArray();
        var cacheIdx = Array.FindIndex(DlssPresetService.ShaderCacheSizeOptions, o => o.Value == snapshot.ShaderCacheSize);
        _window.ShaderCacheSizeCombo.SelectedIndex = cacheIdx >= 0 ? cacheIdx : 0;

        // Shader Precompile
        _window.ShaderPrecompileCombo.ItemsSource = DlssPresetService.ShaderPrecompileOptions.Select(o => o.Name).ToArray();
        var precompIdx = Array.FindIndex(DlssPresetService.ShaderPrecompileOptions, o => o.Value == snapshot.ShaderPrecompile);
        _window.ShaderPrecompileCombo.SelectedIndex = precompIdx >= 0 ? precompIdx : 0;

        // G-Sync Indicator (uses snapshot value now)
        _window.GSyncIndicatorCombo.SelectedIndex = snapshot.GSyncIndicator == true ? 1 : 0; // 0=Off, 1=On

        // G-Sync Enable
        _window.GSyncEnableCombo.ItemsSource = DlssPresetService.GSyncEnableOptions.Select(o => o.Name).ToArray();
        var gsyncEnableIdx = Array.FindIndex(DlssPresetService.GSyncEnableOptions, o => o.Value == snapshot.GSyncEnabled);
        _window.GSyncEnableCombo.SelectedIndex = gsyncEnableIdx >= 0 ? gsyncEnableIdx : 0;

        // G-Sync Mode
        _window.GSyncModeCombo.ItemsSource = DlssPresetService.GSyncModeOptions.Select(o => o.Name).ToArray();
        var gsyncIdx = Array.FindIndex(DlssPresetService.GSyncModeOptions, o => o.Value == snapshot.GSyncMode);
        _window.GSyncModeCombo.SelectedIndex = gsyncIdx >= 0 ? gsyncIdx : 1; // Default: Fullscreen only

        // FPS Limit
        var fpsItems = DlssPresetService.FpsLimiterPresets.Select(o => o.Name).ToList();
        var fpsLimit = snapshot.FpsLimit ?? 0;
        var fpsIdx = Array.FindIndex(DlssPresetService.FpsLimiterPresets, o => o.Value == fpsLimit);
        if (fpsIdx < 0 && fpsLimit > 0)
        {
            // Custom value — insert before "Custom..." at the end
            fpsItems.Insert(fpsItems.Count - 1, $"{fpsLimit} FPS (Custom)");
            fpsIdx = fpsItems.Count - 2;
        }
        _window.FpsLimitCombo.ItemsSource = fpsItems.ToArray();
        _window.FpsLimitCombo.SelectedIndex = fpsIdx >= 0 ? fpsIdx : 0;

        // Preferred Refresh Rate
        _window.PreferredRefreshRateCombo.ItemsSource = DlssPresetService.PreferredRefreshRateOptions.Select(o => o.Name).ToArray();
        var refreshIdx = Array.FindIndex(DlssPresetService.PreferredRefreshRateOptions, o => o.Value == snapshot.PreferredRefreshRate);
        _window.PreferredRefreshRateCombo.SelectedIndex = refreshIdx >= 0 ? refreshIdx : 0; // Default: App Setting

        // DMFG Frame Count
        _window.DmfgFrameCountCombo.ItemsSource = DlssPresetService.DmfgFrameCountOptions.Select(o => o.Name).ToArray();
        var dmfgCountIdx = Array.FindIndex(DlssPresetService.DmfgFrameCountOptions, o => o.Value == snapshot.DmfgFrameCount);
        _window.DmfgFrameCountCombo.SelectedIndex = dmfgCountIdx >= 0 ? dmfgCountIdx : 0;

        // DMFG Target FPS
        var dmfgFpsItems = DlssPresetService.DmfgTargetFpsOptions.Select(o => o.Name).ToList();
        var dmfgFps = snapshot.DmfgTargetFps ?? 0;
        var dmfgFpsIdx = Array.FindIndex(DlssPresetService.DmfgTargetFpsOptions, o => o.Value == dmfgFps);
        if (dmfgFpsIdx < 0 && dmfgFps > 0 && dmfgFps != 0x01000000)
        {
            // Custom value — insert before "Custom..." at the end
            dmfgFpsItems.Insert(dmfgFpsItems.Count - 1, $"{dmfgFps} FPS (Custom)");
            dmfgFpsIdx = dmfgFpsItems.Count - 2;
        }
        _window.DmfgTargetFpsCombo.ItemsSource = dmfgFpsItems.ToArray();
        _window.DmfgTargetFpsCombo.SelectedIndex = dmfgFpsIdx >= 0 ? dmfgFpsIdx : 0;

        // Global ReBAR Enable
        var isAdminForReBar = VulkanLayerService.IsRunningAsAdmin();
        _window.GlobalReBarEnableCombo.ItemsSource = new[] { "Auto (Default)", "Off", "On" };
        var globalReBarMode = snapshot.ReBarEnableMode ?? 1; // 0=Off, 1=Auto, 2=On
        _window.GlobalReBarEnableCombo.SelectedIndex = globalReBarMode == 0 ? 1 : globalReBarMode == 2 ? 2 : 0;
        _window.GlobalReBarEnableCombo.IsEnabled = isAdminForReBar;
        _window.GlobalReBarEnableCombo.Opacity = isAdminForReBar ? 1.0 : 0.4;

        // Global ReBAR Size
        _window.GlobalReBarSizeCombo.ItemsSource = DlssPresetService.ReBarSizeLimits.Select(o => o.Name).ToArray();
        var globalReBarSize = snapshot.ReBarSizeLimit ?? 0x40000000;
        var rebarSizeIdx = Array.FindIndex(DlssPresetService.ReBarSizeLimits, o => o.Value == globalReBarSize);
        _window.GlobalReBarSizeCombo.SelectedIndex = rebarSizeIdx >= 0 ? rebarSizeIdx : 1; // Default: 1GB
        bool reBarOn = globalReBarMode == 2; // Only On enables size setting; Auto and Off grey it
        _window.GlobalReBarSizeCombo.IsEnabled = isAdminForReBar && reBarOn;
        _window.GlobalReBarSizeCombo.Opacity = (isAdminForReBar && reBarOn) ? 1.0 : 0.4;

        // ReBAR admin warning
        _window.ReBarAdminWarning.Visibility = isAdminForReBar
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        // Global VSync
        _window.GlobalVSyncCombo.ItemsSource = DlssPresetService.VSyncModeOptions.Select(o => o.Name).ToArray();
        var vsyncIdx = snapshot.VSyncMode.HasValue
            ? Array.FindIndex(DlssPresetService.VSyncModeOptions, o => o.Value == snapshot.VSyncMode.Value)
            : 0; // Default: App Controlled
        _window.GlobalVSyncCombo.SelectedIndex = vsyncIdx >= 0 ? vsyncIdx : 0;

        // Global Power Mode
        _window.GlobalPowerModeCombo.ItemsSource = DlssPresetService.PowerManagementOptions.Select(o => o.Name).ToArray();
        var powerIdx = snapshot.PowerMode.HasValue
            ? Array.FindIndex(DlssPresetService.PowerManagementOptions, o => o.Value == snapshot.PowerMode.Value)
            : 0; // Default: Optimal Performance
        _window.GlobalPowerModeCombo.SelectedIndex = powerIdx >= 0 ? powerIdx : 0;

        // All NVAPI combos are now populated — safe to let SelectionChanged handlers fire.
        _window._dlssIndicatorInitializing = false;
    }

    /// <summary>
    /// Populates and wires the RenoDX data source combo.
    /// </summary>
    private void InitRenoDxDbSourceCombo()
    {
        var combo = _window.RenoDxDbSourceCombo;

        // Map the current persisted value to the correct ComboBoxItem by Tag
        var currentSource = ViewModel.Settings.RenoDxDbSource;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is Microsoft.UI.Xaml.Controls.ComboBoxItem item
                && string.Equals(item.Tag as string, currentSource, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                break;
            }
        }
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0; // Default: Wiki Only

        combo.SelectionChanged -= RenoDxDbSourceCombo_SelectionChanged;
        combo.SelectionChanged += RenoDxDbSourceCombo_SelectionChanged;
    }

    private void RenoDxDbSourceCombo_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_window.RenoDxDbSourceCombo.SelectedItem is Microsoft.UI.Xaml.Controls.ComboBoxItem item
            && item.Tag is string tag)
        {
            ViewModel.Settings.RenoDxDbSource = tag;
            ViewModel.SaveSettingsPublic();
        }
    }
}
