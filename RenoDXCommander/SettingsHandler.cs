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
public class SettingsHandler
{
    // ── Instance members ───────────────────────────────────────────────

    private readonly MainWindow _window;

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
    }

    private MainViewModel ViewModel => _window.ViewModel;

    public void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToSettingsCommand.Execute(null);
        _window.GameViewPanel.Visibility = Visibility.Collapsed;
        _window.SettingsPanel.Visibility = Visibility.Visible;
        _window.LoadingPanel.Visibility = Visibility.Collapsed;
        // Sync toggle state with ViewModel
        _window.CustomShadersToggle.IsOn = ViewModel.Settings.UseCustomShaders;
        _window.AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  Simplified PC Gaming by RankFTW";
        // Populate addon watch folder textbox
        _window.AddonWatchFolderBox.Text = ViewModel.Settings.AddonWatchFolder;
        // Populate screenshot path and per-game toggle
        _window.ScreenshotPathBox.Text = ViewModel.Settings.ScreenshotPath;
        _window.PerGameScreenshotToggle.IsOn = ViewModel.Settings.PerGameScreenshotFolders;
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

        // Initialize DLSS inputs combo and visibility
        _window.OsDlssInputsCombo.SelectedIndex = ViewModel.Settings.OsDlssInputs ? 0 : 1; // 0=Yes, 1=No
        bool showDlss = !string.Equals(gpuType, "NVIDIA", StringComparison.OrdinalIgnoreCase);
        _window.OsDlssInputsPanel.Visibility = showDlss ? Visibility.Visible : Visibility.Collapsed;

        // Initialize Global Update Checks summary
        RefreshGlobalUpdateSummary();
        _window.CacheAllShadersToggle.IsOn = ViewModel.Settings.CacheAllShaders;

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

        // Initialize DLSS Indicator combo from registry
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\NGXCore");
            var val = key?.GetValue("ShowDlssIndicator");
            // 0x400 = enabled, 0 = disabled. Default to disabled if key doesn't exist.
            bool indicatorEnabled = val is int intVal && intVal != 0;
            _window.DlssIndicatorCombo.SelectedIndex = indicatorEnabled ? 0 : 1; // 0=Enabled, 1=Disabled
        }
        catch
        {
            _window.DlssIndicatorCombo.SelectedIndex = 1; // Default to Disabled if registry unreadable
        }
        _window._dlssIndicatorInitializing = false;

        // Populate DLSS defaults summary
        _window.RefreshDlssDefaultsSummary();

        // Populate shader cache combos
        _window._shaderCacheComboInit = true;
        var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
        if (presetSvc.IsSupported)
        {
            _window.ShaderCacheSizeCombo.ItemsSource = DlssPresetService.ShaderCacheSizeOptions.Select(o => o.Name).ToArray();
            var cacheSize = presetSvc.GetShaderCacheSize();
            var cacheIdx = Array.FindIndex(DlssPresetService.ShaderCacheSizeOptions, o => o.Value == cacheSize);
            _window.ShaderCacheSizeCombo.SelectedIndex = cacheIdx >= 0 ? cacheIdx : 0;

            _window.ShaderPrecompileCombo.ItemsSource = DlssPresetService.ShaderPrecompileOptions.Select(o => o.Name).ToArray();
            var precompile = presetSvc.GetShaderPrecompile();
            var precompIdx = Array.FindIndex(DlssPresetService.ShaderPrecompileOptions, o => o.Value == precompile);
            _window.ShaderPrecompileCombo.SelectedIndex = precompIdx >= 0 ? precompIdx : 0;

            _window.GSyncModeCombo.ItemsSource = DlssPresetService.GSyncModeOptions.Select(o => o.Name).ToArray();
            var gsync = presetSvc.GetGSyncMode();
            var gsyncIdx = Array.FindIndex(DlssPresetService.GSyncModeOptions, o => o.Value == gsync);
            _window.GSyncModeCombo.SelectedIndex = gsyncIdx >= 0 ? gsyncIdx : 1; // Default: Fullscreen only

            _window.PreferredRefreshRateCombo.ItemsSource = DlssPresetService.PreferredRefreshRateOptions.Select(o => o.Name).ToArray();
            var refreshRate = presetSvc.GetPreferredRefreshRate();
            var refreshIdx = Array.FindIndex(DlssPresetService.PreferredRefreshRateOptions, o => o.Value == refreshRate);
            _window.PreferredRefreshRateCombo.SelectedIndex = refreshIdx >= 0 ? refreshIdx : 0; // Default: App Setting

            // Global ReBAR
            var isAdminForReBar = VulkanLayerService.IsRunningAsAdmin();
            _window.GlobalReBarEnableCombo.ItemsSource = new[] { "Off", "On" };
            var globalReBar = presetSvc.GetGlobalReBarEnabled();
            _window.GlobalReBarEnableCombo.SelectedIndex = globalReBar == true ? 1 : 0;
            _window.GlobalReBarEnableCombo.IsEnabled = isAdminForReBar;
            _window.GlobalReBarEnableCombo.Opacity = isAdminForReBar ? 1.0 : 0.4;

            _window.GlobalReBarSizeCombo.ItemsSource = DlssPresetService.ReBarSizeLimits.Select(o => o.Name).ToArray();
            var globalReBarSize = presetSvc.GetGlobalReBarSizeLimit();
            var rebarSizeIdx = Array.FindIndex(DlssPresetService.ReBarSizeLimits, o => o.Value == globalReBarSize);
            _window.GlobalReBarSizeCombo.SelectedIndex = rebarSizeIdx >= 0 ? rebarSizeIdx : 1; // Default: 1GB
            _window.GlobalReBarSizeCombo.IsEnabled = isAdminForReBar && globalReBar == true;
            _window.GlobalReBarSizeCombo.Opacity = (isAdminForReBar && globalReBar == true) ? 1.0 : 0.4;

            // Show admin warning if not elevated
            _window.ReBarAdminWarning.Visibility = isAdminForReBar
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;

            // Global VSync
            _window.GlobalVSyncCombo.ItemsSource = DlssPresetService.VSyncModeOptions.Select(o => o.Name).ToArray();
            var globalVSync = presetSvc.GetGlobalVSyncMode();
            var vsyncIdx = globalVSync.HasValue
                ? Array.FindIndex(DlssPresetService.VSyncModeOptions, o => o.Value == globalVSync.Value)
                : 0; // Default: App Controlled
            _window.GlobalVSyncCombo.SelectedIndex = vsyncIdx >= 0 ? vsyncIdx : 0;
        }
        _window._shaderCacheComboInit = false;

        // Initialize admin mode combo
        InitAdminModeCombo(_window.AdminModeCombo);
    }

    /// <summary>
    /// Re-reads global NVIDIA settings from the driver and refreshes the Settings page combos.
    /// Called after Reset All to reflect the cleared values.
    /// </summary>
    public void RefreshGlobalNvidiaSettings()
    {
        var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
        if (!presetSvc.IsSupported) return;

        _window._shaderCacheComboInit = true;

        var cacheSize = presetSvc.GetShaderCacheSize();
        var cacheIdx = Array.FindIndex(DlssPresetService.ShaderCacheSizeOptions, o => o.Value == cacheSize);
        _window.ShaderCacheSizeCombo.SelectedIndex = cacheIdx >= 0 ? cacheIdx : 0;

        var precompile = presetSvc.GetShaderPrecompile();
        var precompIdx = Array.FindIndex(DlssPresetService.ShaderPrecompileOptions, o => o.Value == precompile);
        _window.ShaderPrecompileCombo.SelectedIndex = precompIdx >= 0 ? precompIdx : 0;

        var gsync = presetSvc.GetGSyncMode();
        var gsyncIdx = Array.FindIndex(DlssPresetService.GSyncModeOptions, o => o.Value == gsync);
        _window.GSyncModeCombo.SelectedIndex = gsyncIdx >= 0 ? gsyncIdx : 1; // Default: Fullscreen only

        var refreshRate = presetSvc.GetPreferredRefreshRate();
        var refreshIdx = Array.FindIndex(DlssPresetService.PreferredRefreshRateOptions, o => o.Value == refreshRate);
        _window.PreferredRefreshRateCombo.SelectedIndex = refreshIdx >= 0 ? refreshIdx : 0;

        var isAdminForReBar = VulkanLayerService.IsRunningAsAdmin();
        var globalReBar = presetSvc.GetGlobalReBarEnabled();
        _window.GlobalReBarEnableCombo.SelectedIndex = globalReBar == true ? 1 : 0;
        _window.GlobalReBarSizeCombo.IsEnabled = isAdminForReBar && globalReBar == true;
        _window.GlobalReBarSizeCombo.Opacity = (isAdminForReBar && globalReBar == true) ? 1.0 : 0.4;
        var globalReBarSize = presetSvc.GetGlobalReBarSizeLimit();
        var rebarSizeIdx = Array.FindIndex(DlssPresetService.ReBarSizeLimits, o => o.Value == globalReBarSize);
        _window.GlobalReBarSizeCombo.SelectedIndex = rebarSizeIdx >= 0 ? rebarSizeIdx : 1;

        var globalVSync = presetSvc.GetGlobalVSyncMode();
        var vsyncIdx = globalVSync.HasValue
            ? Array.FindIndex(DlssPresetService.VSyncModeOptions, o => o.Value == globalVSync.Value)
            : 0;
        _window.GlobalVSyncCombo.SelectedIndex = vsyncIdx >= 0 ? vsyncIdx : 0;

        _window._shaderCacheComboInit = false;
    }

    public void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToGameViewCommand.Execute(null);
        _window.SettingsPanel.Visibility = Visibility.Collapsed;
        // Always show GameViewPanel — skeleton loading handles the loading state visually
        _window.GameViewPanel.Visibility = Visibility.Visible;
    }

    // SkipUpdateToggle and VerboseLoggingToggle removed from UI — logic retained in ViewModel

    public void BetaOptInToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.BetaOptIn = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void CustomShadersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.UseCustomShaders = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public async void ApplyScreenshotPath_Click(object sender, RoutedEventArgs e)
    {
        var screenshotPath = _window.ScreenshotPathBox.Text?.Trim() ?? "";
        var perGame = _window.PerGameScreenshotToggle.IsOn;

        // Persist settings
        ViewModel.Settings.ScreenshotPath = screenshotPath;
        ViewModel.Settings.PerGameScreenshotFolders = perGame;
        ViewModel.SaveSettingsPublic();

        // If path is empty, clear persisted settings and return — no INI modifications
        if (string.IsNullOrEmpty(screenshotPath))
        {
            return;
        }

        // Iterate all game cards and apply screenshot path to eligible games
        int updatedCount = 0;
        foreach (var card in ViewModel.AllCards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            // Find all reshade*.ini files (reshade.ini, reshade2.ini, reshade3.ini, etc.)
            var iniFiles = System.IO.Directory.EnumerateFiles(card.InstallPath, "reshade*.ini")
                .Where(f => System.IO.Path.GetFileName(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                         && System.IO.Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (iniFiles.Count == 0) continue;

            try
            {
                var savePath = perGame
                    ? BuildSavePath(screenshotPath, card.GameName)
                    : screenshotPath;

                foreach (var iniFile in iniFiles)
                {
                    AuxInstallService.ApplyScreenshotPath(iniFile, savePath);
                    // Also apply screenshot hotkey if non-default
                    var ssHotkey = ViewModel.Settings.ScreenshotHotkey;
                    if (ssHotkey != "44,0,0,0")
                        AuxInstallService.ApplyScreenshotHotkey(iniFile, ssHotkey);
                }
                updatedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyScreenshotPath_Click] Failed for '{card.GameName}' — {ex.Message}");
            }
        }

        // Show confirmation dialog
        var dialog = new ContentDialog
        {
            Title = "Screenshots",
            Content = $"Updated {updatedCount} reshade.ini file{(updatedCount == 1 ? "" : "s")}.",
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    /// <summary>
    /// Builds the effective screenshot save path for a game, appending a sanitized
    /// game name subfolder when per-game folders are enabled.
    /// </summary>
    private static string BuildSavePath(string basePath, string gameName)
    {
        var sanitized = AuxInstallService.SanitizeDirectoryName(gameName);
        if (string.IsNullOrEmpty(sanitized)) return basePath;
        return basePath + @"\" + sanitized;
    }

    public void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var logsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "logs");
        System.IO.Directory.CreateDirectory(logsDir);
        CrashReporter.Log("[SettingsHandler.OpenLogsFolder_Click] User opened logs folder");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logsDir) { UseShellExecute = true });
    }

    private const string AdminTaskName = "RHI Admin Mode";
    private bool _adminComboInit = true;

    /// <summary>
    /// Checks if the scheduled task exists and sets the combo box accordingly.
    /// Called during settings page initialization.
    /// </summary>
    public void InitAdminModeCombo(ComboBox combo)
    {
        _adminComboInit = true;
        bool taskExists = IsAdminTaskRegistered();
        combo.SelectedIndex = taskExists ? 1 : 0; // 0=Off, 1=On
        _adminComboInit = false;
    }

    public async void AdminModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_adminComboInit) return;
        if (sender is not ComboBox combo) return;

        bool enable = combo.SelectedIndex == 1;
        CrashReporter.Log($"[SettingsHandler.AdminModeCombo] Admin mode = {(enable ? "On" : "Off")}");

        try
        {
            if (enable)
                CreateAdminTask();
            else
                DeleteAdminTask();

            // Show restart notice
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Admin Mode",
                Content = enable
                    ? "Admin Mode enabled. Restart RHI for it to take effect."
                    : "Admin Mode disabled. RHI will launch normally on next start.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.AdminModeCombo] Failed: {ex.Message}");
            // Revert the selection on failure
            _adminComboInit = true;
            combo.SelectedIndex = enable ? 0 : 1;
            _adminComboInit = false;
        }
    }

    private static bool IsAdminTaskRegistered()
    {
        try
        {
            var result = RunSchtasks($"/Query /TN \"{AdminTaskName}\" /FO LIST");
            return result.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void CreateAdminTask()
    {
        var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        // Create a scheduled task that runs RHI with highest privileges — no auto-start trigger.
        // Uses /SC ONCE with a past date so the task exists but never auto-triggers.
        // RHI launches itself through the task via "schtasks /Run" for UAC-free elevation.
        var args = $"/Create /TN \"{AdminTaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONCE /ST 00:00 /SD 01/01/2000 /RL HIGHEST /F";
        var result = RunSchtasksElevated(args);
        if (result != 0)
            throw new InvalidOperationException($"schtasks /Create failed (exit {result})");
    }

    private static void DeleteAdminTask()
    {
        var result = RunSchtasksElevated($"/Delete /TN \"{AdminTaskName}\" /F");
        if (result != 0)
            throw new InvalidOperationException($"schtasks /Delete failed (exit {result})");
    }

    /// <summary>Runs schtasks.exe elevated (UAC prompt) and waits for completion.</summary>
    private static int RunSchtasksElevated(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(15000);
        return proc.ExitCode;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(5000);
        return (proc.ExitCode, output.Trim());
    }

    public void OpenAppDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var appDataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
        System.IO.Directory.CreateDirectory(appDataDir);
        CrashReporter.Log("[SettingsHandler.OpenAppDataFolder_Click] User opened AppData folder");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appDataDir) { UseShellExecute = true });
    }

    public void OpenCustomFolder_Click(object sender, RoutedEventArgs e)
    {
        var customDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Custom");
        System.IO.Directory.CreateDirectory(customDir);
        CrashReporter.Log("[SettingsHandler.OpenCustomFolder_Click] User opened Custom folder");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(customDir) { UseShellExecute = true });
    }

    public void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(DownloadPaths.Root);
        CrashReporter.Log("[SettingsHandler.OpenDownloadsFolder_Click] User opened downloads cache folder");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DownloadPaths.Root) { UseShellExecute = true });
    }

    public async void CopyLogsArchive_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "logs");
            if (!Directory.Exists(logsDir))
            {
                CrashReporter.Log("[SettingsHandler.CopyLogsArchive_Click] Logs directory does not exist");
                return;
            }

            var logFiles = Directory.GetFiles(logsDir, "*.txt");
            if (logFiles.Length == 0)
            {
                CrashReporter.Log("[SettingsHandler.CopyLogsArchive_Click] No log files found");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var archivePath = Path.Combine(Path.GetTempPath(), $"RHI_Logs_{timestamp}.zip");

            // Delete if exists from a previous attempt
            if (File.Exists(archivePath)) File.Delete(archivePath);

            System.IO.Compression.ZipFile.CreateFromDirectory(logsDir, archivePath);

            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(archivePath);
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetStorageItems(new[] { storageFile });
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            CrashReporter.Log($"[SettingsHandler.CopyLogsArchive_Click] Logs archive copied to clipboard: {archivePath}");

            // Show confirmation
            if (sender is FrameworkElement fe && fe.XamlRoot != null)
            {
                await DialogService.ShowSafeAsync(new ContentDialog
                {
                    Title = "Logs Copied",
                    Content = "All session logs have been archived and copied to your clipboard. Paste directly into Discord to share.",
                    CloseButtonText = "OK",
                    XamlRoot = fe.XamlRoot,
                });
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.CopyLogsArchive_Click] Failed: {ex.Message}");
        }
    }

    public async void PurgeCachedFiles_Click(object sender, RoutedEventArgs e)
    {
        // Show warning dialog
        var warningDialog = new ContentDialog
        {
            Title = "⚠ Purge Cached Files",
            Content = "This will delete cached DLSS, Streamline, and download files (excluding shaders) to free disk space.\n\nThese files will be re-downloaded automatically when needed.\n\nContinue?",
            PrimaryButtonText = "Purge",
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(warningDialog);
        if (result != ContentDialogResult.Primary) return;

        int filesDeleted = 0;
        long bytesFreed = 0;
        var rhiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

        // Folders to purge completely (delete all contents, keep folder)
        var foldersToClean = new[]
        {
            Path.Combine(rhiRoot, "DLSS"),
            Path.Combine(rhiRoot, "DLSS-D"),
            Path.Combine(rhiRoot, "DLSS-G"),
            Path.Combine(rhiRoot, "Streamline"),
        };

        // Downloads subfolders to clean (all except shaders)
        var downloadsRoot = DownloadPaths.Root;

        try
        {
            // Clean DLSS/Streamline folders
            foreach (var folder in foldersToClean)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var dir in Directory.GetDirectories(folder))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var dirSize = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                        var dirCount = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Count();
                        Directory.Delete(dir, recursive: true);
                        filesDeleted += dirCount;
                        bytesFreed += dirSize;
                    }
                    catch { /* skip locked files */ }
                }
                // Also delete any files directly in the folder
                foreach (var file in Directory.GetFiles(folder))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        bytesFreed += fi.Length;
                        fi.Delete();
                        filesDeleted++;
                    }
                    catch { }
                }
            }

            // Clean downloads subfolders (except shaders)
            if (Directory.Exists(downloadsRoot))
            {
                foreach (var dir in Directory.GetDirectories(downloadsRoot))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.Equals("shaders", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var dirSize = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                        var dirCount = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Count();
                        Directory.Delete(dir, recursive: true);
                        filesDeleted += dirCount;
                        bytesFreed += dirSize;
                    }
                    catch { }
                }
                // Delete loose files in downloads root (e.g. .reorganised marker)
                foreach (var file in Directory.GetFiles(downloadsRoot))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        bytesFreed += fi.Length;
                        fi.Delete();
                        filesDeleted++;
                    }
                    catch { }
                }
            }

            // Format size
            string sizeStr = bytesFreed switch
            {
                >= 1_073_741_824 => $"{bytesFreed / 1_073_741_824.0:F1} GB",
                >= 1_048_576 => $"{bytesFreed / 1_048_576.0:F1} MB",
                >= 1024 => $"{bytesFreed / 1024.0:F1} KB",
                _ => $"{bytesFreed} bytes"
            };

            CrashReporter.Log($"[SettingsHandler.PurgeCachedFiles_Click] Purged {filesDeleted} files, freed {sizeStr}");

            var resultDialog = new ContentDialog
            {
                Title = "✅ Cache Purged",
                Content = $"Deleted {filesDeleted} files, freed {sizeStr} of disk space.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(resultDialog);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.PurgeCachedFiles_Click] Failed: {ex.Message}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Purge Failed",
                Content = $"An error occurred: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
        }
    }

    // ── Hotkey UI event handlers (placeholder — implemented in Tasks 6.2 / 6.3) ──

    /// <summary>
    /// Handles PreviewKeyDown on the HotkeyBox to capture key combinations.
    /// Full implementation in Task 6.2.
    /// </summary>
    public void HotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;

        // Req 7.4: Ignore modifier-only keys — do not update display when only a modifier is pressed
        if (key == Windows.System.VirtualKey.Control ||
            key == Windows.System.VirtualKey.Shift ||
            key == Windows.System.VirtualKey.Menu ||       // Alt
            key == (Windows.System.VirtualKey)91 ||        // Left Windows
            key == (Windows.System.VirtualKey)92 ||        // Right Windows
            key == Windows.System.VirtualKey.LeftControl ||
            key == Windows.System.VirtualKey.RightControl ||
            key == Windows.System.VirtualKey.LeftShift ||
            key == Windows.System.VirtualKey.RightShift ||
            key == Windows.System.VirtualKey.LeftMenu ||
            key == Windows.System.VirtualKey.RightMenu)
        {
            return;
        }

        // Extract VK code from the pressed key (Req 2.2)
        int vk = (int)key;

        // Read modifier state from the current keyboard state (Req 2.1)
        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool alt = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // Build and store the hotkey string in KeyOverlay format (Req 2.2)
        _currentHotkeyString = HotkeyManager.BuildHotkeyString(vk, shift, ctrl, alt);

        // Update the TextBox display with human-readable format (Req 2.1)
        if (sender is TextBox hotkeyBox)
        {
            hotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(vk, shift, ctrl, alt);
        }

        // Prevent the TextBox from receiving the character
        e.Handled = true;
    }

    /// <summary>
    /// Handles the Apply to All Games button click for the overlay hotkey.
    /// Persists the hotkey, applies it to all managed reshade*.ini files, and shows a confirmation dialog.
    /// </summary>
    public async void ApplyOverlayHotkey_Click(object sender, RoutedEventArgs e)
    {
        // Req 4.6: Persist hotkey to SettingsViewModel before iterating game cards
        ViewModel.Settings.OverlayHotkey = _currentHotkeyString;
        ViewModel.SaveSettingsPublic();

        bool isDefault = HotkeyManager.IsDefaultHotkey(_currentHotkeyString);

        // Req 4.1: Iterate all game cards with a non-empty InstallPath
        int updatedCount = 0;
        foreach (var card in ViewModel.AllCards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            // When the hotkey is the default (Home), skip RDR2 — its template uses
            // END and we don't want to overwrite that with the generic default.
            if (isDefault && AuxInstallService.IsRdr2(card.GameName))
                continue;

            // Req 4.2: Locate all reshade*.ini files in the game's install directory
            var iniFiles = System.IO.Directory.EnumerateFiles(card.InstallPath, "reshade*.ini")
                .Where(f => System.IO.Path.GetFileName(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                         && System.IO.Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (iniFiles.Count == 0) continue;

            try
            {
                // Req 4.3: Write KeyOverlay to [INPUT] section of each reshade*.ini
                foreach (var iniFile in iniFiles)
                {
                    AuxInstallService.ApplyOverlayHotkey(iniFile, _currentHotkeyString);
                }
                updatedCount++;
            }
            catch (System.IO.IOException ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyOverlayHotkey_Click] Failed for '{card.GameName}' — {ex.Message}");
            }
        }

        // Req 4.5: Show confirmation dialog with count of updated files
        var dialog = new ContentDialog
        {
            Title = "ReShade UI Hotkey",
            Content = $"Updated {updatedCount} reshade.ini file{(updatedCount == 1 ? "" : "s")}.",
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    // ── Combined ReShade Hotkeys (overlay + screenshot) ───────────────────────

    /// <summary>
    /// Applies both the overlay hotkey and screenshot hotkey to all managed reshade*.ini files.
    /// </summary>
    public async void ApplyReShadeHotkeys_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.OverlayHotkey = _currentHotkeyString;
        ViewModel.Settings.ScreenshotHotkey = _currentScreenshotHotkeyString;
        ViewModel.SaveSettingsPublic();

        bool isDefault = HotkeyManager.IsDefaultHotkey(_currentHotkeyString);

        int updatedCount = 0;
        foreach (var card in ViewModel.AllCards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            if (isDefault && AuxInstallService.IsRdr2(card.GameName))
                continue;

            var iniFiles = System.IO.Directory.EnumerateFiles(card.InstallPath, "reshade*.ini")
                .Where(f => System.IO.Path.GetFileName(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                         && System.IO.Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (iniFiles.Count == 0) continue;

            try
            {
                foreach (var iniFile in iniFiles)
                {
                    AuxInstallService.ApplyOverlayHotkey(iniFile, _currentHotkeyString);
                    AuxInstallService.ApplyScreenshotHotkey(iniFile, _currentScreenshotHotkeyString);
                }
                updatedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyReShadeHotkeys_Click] Failed for '{card.GameName}' — {ex.Message}");
            }
        }

        var dialog = new ContentDialog
        {
            Title = "ReShade Hotkeys",
            Content = $"Updated {updatedCount} reshade.ini file{(updatedCount == 1 ? "" : "s")}.",
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    // ── Screenshot Hotkey ─────────────────────────────────────────────────────

    /// <summary>
    /// Handles PreviewKeyDown on the ScreenshotHotkeyBox to capture key combinations.
    /// </summary>
    public void ScreenshotHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;

        if (key == Windows.System.VirtualKey.Control ||
            key == Windows.System.VirtualKey.Shift ||
            key == Windows.System.VirtualKey.Menu ||
            key == (Windows.System.VirtualKey)91 ||
            key == (Windows.System.VirtualKey)92 ||
            key == Windows.System.VirtualKey.LeftControl ||
            key == Windows.System.VirtualKey.RightControl ||
            key == Windows.System.VirtualKey.LeftShift ||
            key == Windows.System.VirtualKey.RightShift ||
            key == Windows.System.VirtualKey.LeftMenu ||
            key == Windows.System.VirtualKey.RightMenu)
        {
            return;
        }

        int vk = (int)key;

        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool alt = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        _currentScreenshotHotkeyString = HotkeyManager.BuildHotkeyString(vk, shift, ctrl, alt);

        if (sender is TextBox hotkeyBox)
        {
            hotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(vk, shift, ctrl, alt);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Handles the Apply to All Games button click for the screenshot hotkey.
    /// Persists the hotkey, applies it to all managed reshade*.ini files, and shows a confirmation dialog.
    /// </summary>
    public async void ApplyScreenshotHotkey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.ScreenshotHotkey = _currentScreenshotHotkeyString;
        ViewModel.SaveSettingsPublic();

        int updatedCount = 0;
        foreach (var card in ViewModel.AllCards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            var iniFiles = System.IO.Directory.EnumerateFiles(card.InstallPath, "reshade*.ini")
                .Where(f => System.IO.Path.GetFileName(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                         && System.IO.Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (iniFiles.Count == 0) continue;

            try
            {
                foreach (var iniFile in iniFiles)
                {
                    AuxInstallService.ApplyScreenshotHotkey(iniFile, _currentScreenshotHotkeyString);
                }
                updatedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyScreenshotHotkey_Click] Failed for '{card.GameName}' — {ex.Message}");
            }
        }

        var dialog = new ContentDialog
        {
            Title = "ReShade Screenshot Hotkey",
            Content = $"Updated {updatedCount} reshade.ini file{(updatedCount == 1 ? "" : "s")}.",
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    /// <summary>Resets the screenshot hotkey to the default Print Screen key.</summary>
    public void ResetScreenshotHotkey_Click(object sender, RoutedEventArgs e)
    {
        _currentScreenshotHotkeyString = "44,0,0,0";
        _window.ScreenshotHotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay("44,0,0,0");
        ViewModel.Settings.ScreenshotHotkey = "44,0,0,0";
        ViewModel.SaveSettingsPublic();
    }

    // ── ReLimiter OSD Hotkey ──────────────────────────────────────────────────

    public void UlHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;

        if (key == Windows.System.VirtualKey.Control ||
            key == Windows.System.VirtualKey.Shift ||
            key == Windows.System.VirtualKey.Menu ||
            key == (Windows.System.VirtualKey)91 ||
            key == (Windows.System.VirtualKey)92 ||
            key == Windows.System.VirtualKey.LeftControl ||
            key == Windows.System.VirtualKey.RightControl ||
            key == Windows.System.VirtualKey.LeftShift ||
            key == Windows.System.VirtualKey.RightShift ||
            key == Windows.System.VirtualKey.LeftMenu ||
            key == Windows.System.VirtualKey.RightMenu)
        {
            return;
        }

        int vk = (int)key;

        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool alt = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        _currentUlHotkeyString = HotkeyManager.BuildUlHotkeyString(vk, shift, ctrl, alt);

        if (sender is TextBox hotkeyBox)
            hotkeyBox.Text = _currentUlHotkeyString;

        e.Handled = true;
    }

    public async void ApplyUlOsdHotkey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.UlOsdHotkey = _currentUlHotkeyString;
        ViewModel.SaveSettingsPublic();

        bool sharedPresets = ViewModel.Settings.UlSharedPresets;
        bool dlssHooks = ViewModel.Settings.UlDlssHooks;
        int updatedCount = 0;
        foreach (var card in ViewModel.AllCards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
            var iniFile = Path.Combine(deployPath, "relimiter.ini");
            if (!File.Exists(iniFile)) continue;

            try
            {
                AuxInstallService.ApplyUlOsdHotkey(iniFile, _currentUlHotkeyString);
                AuxInstallService.ApplyUlSharedPresets(iniFile, sharedPresets);
                AuxInstallService.ApplyUlDlssHooks(iniFile, dlssHooks);

                // RE Framework games also store relimiter.ini in _storage_
                if (card.IsRefInstalled)
                {
                    var storagePath = Path.Combine(card.InstallPath, "_storage_", "relimiter.ini");
                    if (File.Exists(storagePath))
                    {
                        AuxInstallService.ApplyUlOsdHotkey(storagePath, _currentUlHotkeyString);
                        AuxInstallService.ApplyUlSharedPresets(storagePath, sharedPresets);
                        AuxInstallService.ApplyUlDlssHooks(storagePath, dlssHooks);
                    }
                }

                updatedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyUlOsdHotkey_Click] Failed for '{card.GameName}' — {ex.Message}");
            }
        }

        // Also update the template in AppData
        if (File.Exists(AuxInstallService.UlIniPath))
        {
            try
            {
                AuxInstallService.ApplyUlOsdHotkey(AuxInstallService.UlIniPath, _currentUlHotkeyString);
                AuxInstallService.ApplyUlSharedPresets(AuxInstallService.UlIniPath, sharedPresets);
                AuxInstallService.ApplyUlDlssHooks(AuxInstallService.UlIniPath, dlssHooks);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyUlOsdHotkey_Click] Failed to update template — {ex.Message}");
            }
        }

        var dialog = new ContentDialog
        {
            Title = "ReLimiter OSD Hotkey",
            Content = $"Updated {updatedCount} relimiter.ini file{(updatedCount == 1 ? "" : "s")}.",
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    // ── ReLimiter Shared Presets ──────────────────────────────────────────────

    public void UlSharedPresetsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            ViewModel.Settings.UlSharedPresets = combo.SelectedIndex == 1;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void UlDlssHooksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            ViewModel.Settings.UlDlssHooks = combo.SelectedIndex == 1;
            ViewModel.SaveSettingsPublic();
        }
    }

    // ── OptiScaler Hotkey ─────────────────────────────────────────────────────

    public void OsGpuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is string selected)
        {
            ViewModel.Settings.OsGpuType = selected;
            ViewModel.SaveSettingsPublic();

            // Show DLSS inputs combo only for AMD or Intel
            bool showDlss = !string.Equals(selected, "NVIDIA", StringComparison.OrdinalIgnoreCase);
            _window.OsDlssInputsPanel.Visibility = showDlss ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void OsDlssInputsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedIndex >= 0)
        {
            ViewModel.Settings.OsDlssInputs = combo.SelectedIndex == 0; // 0=Yes, 1=No
            ViewModel.SaveSettingsPublic();
        }
    }

    public async Task GlobalUpdateInclusion_ClickAsync(object sender, RoutedEventArgs e)
    {
        var settings = ViewModel.Settings;
        var xamlRoot = (sender as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
        if (xamlRoot == null) return;

        var rsCheck = new CheckBox { Content = "ReShade", IsChecked = !settings.GlobalSkipRsUpdates, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
        var rdxCheck = new CheckBox { Content = "RenoDX", IsChecked = !settings.GlobalSkipRdxUpdates, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
        var ulCheck = new CheckBox { Content = "ReLimiter", IsChecked = !settings.GlobalSkipUlUpdates, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
        var dcCheck = new CheckBox { Content = "Display Commander", IsChecked = !settings.GlobalSkipDcUpdates, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
        var osCheck = new CheckBox { Content = "OptiScaler", IsChecked = !settings.GlobalSkipOsUpdates, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };
        var refCheck = new CheckBox { Content = "RE Framework", IsChecked = !settings.GlobalSkipRefUpdates, FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), Margin = new Thickness(0, 4, 0, 4) };

        var checkPanel = new StackPanel { Spacing = 0 };
        checkPanel.Children.Add(new TextBlock { Text = "Include components in Update All globally:", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), Margin = new Thickness(0, 0, 0, 8) });
        checkPanel.Children.Add(rsCheck);
        checkPanel.Children.Add(rdxCheck);
        checkPanel.Children.Add(ulCheck);
        checkPanel.Children.Add(dcCheck);
        checkPanel.Children.Add(osCheck);
        checkPanel.Children.Add(refCheck);

        var dialog = new ContentDialog
        {
            Title = "Global Update Inclusion",
            Content = checkPanel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            settings.GlobalSkipRsUpdates = !(rsCheck.IsChecked == true);
            settings.GlobalSkipRdxUpdates = !(rdxCheck.IsChecked == true);
            settings.GlobalSkipUlUpdates = !(ulCheck.IsChecked == true);
            settings.GlobalSkipDcUpdates = !(dcCheck.IsChecked == true);
            settings.GlobalSkipOsUpdates = !(osCheck.IsChecked == true);
            settings.GlobalSkipRefUpdates = !(refCheck.IsChecked == true);
            ViewModel.SaveSettingsPublic();
            RefreshGlobalUpdateSummary();
        }
    }

    public void RefreshGlobalUpdateSummary()
    {
        var tb = _window.GlobalUpdateSummaryText;
        tb.Inlines.Clear();
        var settings = ViewModel.Settings;
        var items = new (string label, bool isOn)[]
        {
            ("RS", !settings.GlobalSkipRsUpdates),
            ("RDX", !settings.GlobalSkipRdxUpdates),
            ("RL", !settings.GlobalSkipUlUpdates),
            ("DC", !settings.GlobalSkipDcUpdates),
            ("OS", !settings.GlobalSkipOsUpdates),
            ("REF", !settings.GlobalSkipRefUpdates),
        };
        for (int i = 0; i < items.Length; i++)
        {
            var (label, isOn) = items[i];
            tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"{label}: ",
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
            tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = isOn ? "On" : "Off",
                Foreground = UIFactory.Brush(isOn ? ResourceKeys.AccentGreenBrush : ResourceKeys.AccentRedBrush),
            });
            if (i < items.Length - 1)
            {
                tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = "  ·  ",
                    Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                });
            }
        }
    }

    public void CacheAllShadersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.CacheAllShaders = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void OsHotkeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is string selected)
        {
            _currentOsHotkeyString = selected;

            // Persist immediately and write to INIs_Folder
            ViewModel.Settings.OsHotkey = _currentOsHotkeyString;
            ViewModel.SaveSettingsPublic();

            // Write ShortcutKey to the OptiScaler.ini template in INIs_Folder
            try
            {
                Directory.CreateDirectory(AuxInstallService.InisDir);
                OptiScalerService.WriteShortcutKey(OptiScalerService.OsIniPath, _currentOsHotkeyString);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.OsHotkeyCombo_SelectionChanged] Failed to write ShortcutKey — {ex.Message}");
            }
        }
    }

    public async void ApplyOsHotkey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.OsHotkey = _currentOsHotkeyString;
        ViewModel.SaveSettingsPublic();

        // Write to INIs_Folder template
        try
        {
            Directory.CreateDirectory(AuxInstallService.InisDir);
            OptiScalerService.WriteShortcutKey(OptiScalerService.OsIniPath, _currentOsHotkeyString);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.ApplyOsHotkey_Click] Failed to write template — {ex.Message}");
        }

        // Apply to all games where OptiScaler is installed
        int updatedCount = 0;
        foreach (var card in ViewModel.AllCards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            if (!card.IsOsInstalled) continue;

            var gameIniPath = Path.Combine(card.InstallPath, OptiScalerService.IniFileName);
            if (!File.Exists(gameIniPath)) continue;

            try
            {
                OptiScalerService.WriteShortcutKey(gameIniPath, _currentOsHotkeyString);
                updatedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ApplyOsHotkey_Click] Failed for '{card.GameName}' — {ex.Message}");
            }
        }

        var dialog = new ContentDialog
        {
            Title = "OptiScaler Hotkey",
            Content = $"Updated {updatedCount} OptiScaler.ini file{(updatedCount == 1 ? "" : "s")}.",
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    // ── DXVK Variant Selector ─────────────────────────────────────────────────

    /// <summary>
    /// Handles the DXVK variant ComboBox selection change.
    /// Persists the variant, clears the staging cache, and prompts about re-deployment.
    /// </summary>
    public async void DxvkVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is not ComboBox combo || combo.SelectedItem is not string selected) return;

        var newVariant = selected switch
        {
            var s when s.Contains("Stable", StringComparison.OrdinalIgnoreCase) => DxvkVariant.Stable,
            var s when s.Contains("Lilium", StringComparison.OrdinalIgnoreCase) => DxvkVariant.LiliumHdr,
            _ => DxvkVariant.Development,
        };

        var currentVariant = ViewModel.DxvkServiceInstance.SelectedVariant;
        if (newVariant == currentVariant) return;

        // Persist the variant preference
        ViewModel.Settings.DxvkVariant = newVariant switch
        {
            DxvkVariant.Stable => "Stable",
            DxvkVariant.LiliumHdr => "LiliumHdr",
            _ => "Development"
        };
        ViewModel.SaveSettingsPublic();

        // Update the service
        ViewModel.DxvkServiceInstance.SelectedVariant = newVariant;

        // Ensure the new variant's staging is ready (download if needed)
        _ = Task.Run(async () =>
        {
            try { await ViewModel.DxvkServiceInstance.EnsureStagingAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.DxvkVariantCombo] Staging download failed — {ex.Message}"); }
        });

        // Auto-reinstall DXVK on all affected games (those without per-game override)
        var gamesWithDxvk = ViewModel.AllCards
            .Where(c => c.DxvkStatus == GameStatus.Installed || c.DxvkStatus == GameStatus.UpdateAvailable)
            .Where(c => ViewModel.GetDxvkVariantOverride(c.GameName) == null) // Only games using global default
            .ToList();

        if (gamesWithDxvk.Count > 0)
        {
            foreach (var card in gamesWithDxvk)
            {
                _ = ViewModel.InstallDxvkAsync(card);
            }
        }

        var variantLabel = newVariant switch
        {
            DxvkVariant.Stable => "Stable",
            DxvkVariant.LiliumHdr => "Lilium HDR",
            _ => "Development",
        };

        var dialog = new ContentDialog
        {
            Title = "DXVK Variant Changed",
            Content = $"DXVK variant changed to {variantLabel}."
                + (gamesWithDxvk.Count > 0
                    ? $"\n\nSwitching {gamesWithDxvk.Count} game(s) to the {variantLabel} build."
                    : "\n\nNo games currently have DXVK installed."),
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    // ── ReShade Build Channel Selector ────────────────────────────────────────

    /// <summary>
    /// Handles the ReShade build channel ComboBox selection change.
    /// Persists the channel, clears the addon ReShade staging cache, downloads
    /// from the new source, and flags all installed ReShade games as UpdateAvailable.
    /// </summary>
    public async void ReShadeChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is not ComboBox combo || combo.SelectedItem is not string selected) return;

        var newChannel = selected.Contains("Nightly", StringComparison.OrdinalIgnoreCase)
            ? "Nightly" : "Stable";

        if (string.Equals(newChannel, ViewModel.Settings.ReShadeChannel, StringComparison.OrdinalIgnoreCase))
            return;

        // Persist the channel preference
        ViewModel.Settings.ReShadeChannel = newChannel;
        ViewModel.SaveSettingsPublic();

        // Download from the new source and update Vulkan layer when done
        _ = Task.Run(async () =>
        {
            try
            {
                // Ensure both variants are available
                var stableTask = ViewModel.ReShadeUpdateServiceInstance.EnsureLatestAsync();
                var nightlyTask = ViewModel.ReShadeNightlyServiceInstance.EnsureLatestAsync();
                await Task.WhenAll(stableTask, nightlyTask);

                // Update the global Vulkan layer DLLs if they exist
                // Only update if no per-game Vulkan override is active
                var hasVulkanOverride = ViewModel.AllCards
                    .Any(c => c.RequiresVulkanInstall
                        && ViewModel.GetReShadeChannelOverride(c.GameName) != null);

                if (!hasVulkanOverride)
                {
                    try
                    {
                        var layerDir = VulkanLayerService.LayerDirectory;
                        var stagedPath64 = AuxInstallService.GetStagedPathForChannel(newChannel, false);
                        var stagedPath32 = AuxInstallService.GetStagedPathForChannel(newChannel, true);

                        // 64-bit
                        var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                        if (File.Exists(stagedPath64)
                            && new FileInfo(stagedPath64).Length > AuxInstallService.MinReShadeSize
                            && File.Exists(layer64))
                        {
                            try
                            {
                                File.Copy(stagedPath64, layer64, overwrite: true);
                                CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 64-bit DLL to {newChannel} build");
                            }
                            catch (UnauthorizedAccessException)
                            {
                                CrashReporter.Log("[SettingsHandler.ReShadeChannelCombo] Direct copy denied, attempting elevated copy...");
                                try
                                {
                                    ElevatedFileCopy(stagedPath64, layer64);
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 64-bit DLL via elevated copy to {newChannel} build");
                                }
                                catch (Exception elevEx)
                                {
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Elevated copy failed — {elevEx.Message}");
                                }
                            }
                            catch (IOException ioEx)
                            {
                                CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Vulkan layer 64-bit copy failed (file locked?) — {ioEx.Message}");
                            }
                        }

                        // 32-bit
                        var layer32 = Path.Combine(layerDir, "ReShade32.dll");
                        if (File.Exists(stagedPath32)
                            && new FileInfo(stagedPath32).Length > AuxInstallService.MinReShadeSize
                            && File.Exists(layer32))
                        {
                            try
                            {
                                File.Copy(stagedPath32, layer32, overwrite: true);
                                CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 32-bit DLL to {newChannel} build");
                            }
                            catch (UnauthorizedAccessException)
                            {
                                try
                                {
                                    ElevatedFileCopy(stagedPath32, layer32);
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 32-bit DLL via elevated copy to {newChannel} build");
                                }
                                catch (Exception elevEx)
                                {
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] 32-bit elevated copy failed — {elevEx.Message}");
                                }
                            }
                            catch (Exception ex32)
                            {
                                CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] 32-bit Vulkan layer copy failed — {ex32.Message}");
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        CrashReporter.Log("[SettingsHandler.ReShadeChannelCombo] Cannot update Vulkan layer — admin privileges required");
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Failed to update Vulkan layer — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Background download failed — {ex.Message}");
            }
        });

        // Auto-reinstall ReShade on all affected games (those without a per-game override)
        // Games with a per-game override keep their override channel — only "Global" games switch.
        var gamesWithRs = ViewModel.AllCards
            .Where(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable)
            .Where(c => !c.RequiresVulkanInstall) // Vulkan handled above via layer copy
            .Where(c => ViewModel.GetReShadeChannelOverride(c.GameName) == null) // Only games using global default
            .Where(c => !c.UseNormalReShade) // Normal ReShade games are unaffected
            .ToList();

        foreach (var card in gamesWithRs)
        {
            _ = ViewModel.InstallReShadeCommand.ExecuteAsync(card);
        }

        var totalCount = gamesWithRs.Count;
        var vulkanCount = ViewModel.AllCards.Count(c => c.RequiresVulkanInstall && c.IsRsInstalled);
        var channelLabel = string.Equals(newChannel, "Nightly", StringComparison.OrdinalIgnoreCase)
            ? "Nightly" : "Stable";

        var dialog = new ContentDialog
        {
            Title = "ReShade Build Channel Changed",
            Content = $"ReShade build channel changed to {channelLabel}.\n\n"
                + (totalCount > 0
                    ? $"Switching {totalCount} game(s) to the {channelLabel} build."
                      + (vulkanCount > 0 ? $"\n{vulkanCount} Vulkan game(s) updated via global layer." : "")
                    : "No games currently have ReShade installed."),
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    /// <summary>
    /// Copies a file using an elevated cmd.exe process (UAC prompt).
    /// Used when direct File.Copy fails due to permissions on C:\ProgramData\ReShade.
    /// </summary>
    private static void ElevatedFileCopy(string source, string destination)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c copy /y \"{source}\" \"{destination}\"",
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit(10_000);
        if (proc != null && proc.ExitCode != 0)
            throw new IOException($"Elevated copy exited with code {proc.ExitCode}");
    }

}
