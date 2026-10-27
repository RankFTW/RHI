using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    // ── DLDSR Control ─────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the DLDSR Control card: shows it, populates the saved states combo, and refreshes current state.
    /// </summary>
    private void InitDldsrControl()
    {
        _window.DldsrControlCard.Visibility = Visibility.Visible;
        RefreshDldsrStateCombo();
        RefreshDldsrCurrentState();

        // Wire up slider value changed to update the text display
        _window.DldsrSmoothnessSlider.ValueChanged += (s, e) =>
        {
            _window.DldsrSmoothnessValueText.Text = $"{(int)e.NewValue}%";
        };
    }

    /// <summary>
    /// Populates the DLDSR saved states combo from the captures file.
    /// </summary>
    public void RefreshDldsrStateCombo()
    {
        var dldsrService = App.Services.GetRequiredService<IDldsrService>();
        var captures = dldsrService.LoadCaptures();
        _window.DldsrStateCombo.ItemsSource = captures.Select(c => c.Label).ToList();
        if (_window.DldsrStateCombo.Items.Count > 0)
            _window.DldsrStateCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Reads the current DLDSR state from the registry and displays it.
    /// </summary>
    public void RefreshDldsrCurrentState()
    {
        try
        {
            var dldsrService = App.Services.GetRequiredService<IDldsrService>();
            var states = dldsrService.GetCurrentState();

            if (states.Count == 0)
            {
                // Check if it's due to admin rights
                if (!VulkanLayerService.IsRunningAsAdmin())
                {
                    _window.DldsrCurrentStateText.Text = "Admin required to read DLDSR state";
                }
                else
                {
                    _window.DldsrCurrentStateText.Text = "No DLDSR-capable monitors detected";
                }
                return;
            }

            // Update smoothness slider from the first monitor's state
            var firstSmoothness = dldsrService.GetCurrentSmoothness();
            if (firstSmoothness >= 0 && firstSmoothness <= 100)
            {
                _window.DldsrSmoothnessSlider.Value = firstSmoothness;
                _window.DldsrSmoothnessValueText.Text = $"{firstSmoothness}%";
            }

            // Build a summary: "Monitor1: DLDSR 2.25x (matches: dldsr-on)"
            var lines = new List<string>();
            var friendlyNames = DldsrService.GetMonitorFriendlyNames(states.Select(s => s.MonitorId));
            foreach (var s in states)
            {
                var factorPart = string.IsNullOrEmpty(s.EnabledFactors) ? "Off" : s.EnabledFactors;
                var matchPart = string.IsNullOrEmpty(s.MatchesCapture) ? "" : $" (matches: {s.MatchesCapture})";
                // Use friendly monitor name if available, otherwise fall back to shortened key
                string displayName;
                if (friendlyNames.TryGetValue(s.MonitorId, out var fn))
                {
                    displayName = fn;
                }
                else
                {
                    var shortId = s.MonitorId.Contains('_')
                        ? s.MonitorId.Substring(s.MonitorId.LastIndexOf('_') + 1)
                        : s.MonitorId;
                    if (shortId.Length > 12) shortId = shortId.Substring(0, 12) + "...";
                    displayName = shortId;
                }
                lines.Add($"{displayName}: {factorPart}, {s.Smoothness}% smooth{matchPart}");
            }

            _window.DldsrCurrentStateText.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _window.DldsrCurrentStateText.Text = $"Error: {ex.Message}";
            CrashReporter.Log($"[SettingsHandler.RefreshDldsrCurrentState] {ex.Message}");
        }
    }

    /// <summary>
    /// Re-reads global NVIDIA settings from the driver and refreshes the Settings page combos.
    /// Called after Reset All to reflect the cleared values.
    /// </summary>
    public async Task RefreshGlobalNvidiaSettingsAsync()
    {
        var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
        if (!presetSvc.IsSupported) return;

        _window._shaderCacheComboInit = true;
        var snapshot = await presetSvc.FetchNvApiSettingsAsync(timeoutMs: 5000);
        PopulateNvApiCombosFromSnapshot(presetSvc, snapshot);
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

    public void CustomShadersCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            ViewModel.Settings.GlobalShadersOff = combo.SelectedIndex == 0;
            ViewModel.Settings.UseCustomShaders = combo.SelectedIndex == 2;
            ViewModel.SaveSettingsPublic();
            // Immediately sync shaders to all games so the setting takes effect without a manual refresh
            ViewModel.DeployAllShaders();
        }
    }

    public async void ApplyScreenshotPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var screenshotPath = _window.ScreenshotPathBox.Text?.Trim() ?? "";
            var perGame = _window.PerGameScreenshotCombo.SelectedIndex == 1;

            // Persist screenshot settings + hotkeys
            ViewModel.Settings.ScreenshotPath = screenshotPath;
            ViewModel.Settings.PerGameScreenshotFolders = perGame;
            ViewModel.Settings.OverlayHotkey = _currentHotkeyString;
            ViewModel.Settings.ScreenshotHotkey = _currentScreenshotHotkeyString;

            ViewModel.SaveSettingsPublic();

            // Iterate all game cards and apply screenshot path + hotkeys to eligible games
            int updatedCount = 0;
            var allCards = ViewModel.AllCards.ToList(); // Snapshot the list for background processing
            var currentOverlayHotkey = _currentHotkeyString;
            var currentScreenshotHotkey = _currentScreenshotHotkeyString;
            var rsVariableListUseTabs = ViewModel.Settings.RsVariableListUseTabs;
        
            await Task.Run(() =>
            {
                foreach (var card in allCards)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) continue;
                    if (!System.IO.Directory.Exists(card.InstallPath)) continue;

                    // Find all reshade*.ini files (reshade.ini, reshade2.ini, reshade3.ini, etc.)
                    var iniFiles = System.IO.Directory.EnumerateFiles(card.InstallPath, "reshade*.ini")
                        .Where(f => System.IO.Path.GetFileName(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                                 && System.IO.Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (iniFiles.Count == 0) continue;

                    // Skip games where the user has locked reshade.ini updates
                    if (!ViewModel.GetKeepRsIniUpdated(card.GameName, card.Source ?? "")) continue;

                    try
                    {
                        var savePath = string.IsNullOrEmpty(screenshotPath)
                            ? null
                            : perGame ? BuildSavePath(screenshotPath, card.GameName) : screenshotPath;
                        var overlayHotkey = AuxInstallService.IsRdr2(card.GameName)
                            ? null
                            : currentOverlayHotkey;

                        foreach (var iniFile in iniFiles)
                            ApplyScreenshotSettingsToIni(iniFile, savePath, overlayHotkey,
                                currentScreenshotHotkey, rsVariableListUseTabs);
                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[SettingsHandler.ApplyScreenshotPath_Click] Failed for '{card.GameName}' — {ex.Message}");
                    }
                }
            });

            // Show confirmation dialog
            var appliedSettings = string.IsNullOrEmpty(screenshotPath)
                ? "ReShade hotkeys and effect list style"
                : "Screenshot path, ReShade hotkeys, and effect list style";
            var dialog = new ContentDialog
            {
                Title = "Screenshots & Hotkeys",
                Content = $"{appliedSettings} applied to {updatedCount} game{(updatedCount == 1 ? "" : "s")}.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.ApplyScreenshotPath_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    internal static void ApplyScreenshotSettingsToIni(
        string iniFilePath,
        string? savePath,
        string? overlayHotkey,
        string screenshotHotkey,
        bool useTabs)
    {
        if (!string.IsNullOrWhiteSpace(savePath))
            AuxInstallService.ApplyScreenshotPath(iniFilePath, savePath);
        if (overlayHotkey != null)
            AuxInstallService.ApplyOverlayHotkey(iniFilePath, overlayHotkey);
        AuxInstallService.ApplyScreenshotHotkey(iniFilePath, screenshotHotkey);
        AuxInstallService.ApplyVariableListUseTabs(iniFilePath, useTabs);
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

    /// <summary>
    /// Handles the Apply to All Games button click for the HDR & Peak Brightness section.
    /// Applies only peak nits to all managed reshade*.ini files.
    /// </summary>
    public async void ApplyPeakNitsToAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Persist peak nits from textbox (user may not have pressed Enter)
            if (int.TryParse(_window.PeakNitsBox.Text?.Trim(), out var nitsVal) && nitsVal > 0)
                ViewModel.Settings.PeakNits = nitsVal;
            AuxInstallService.GlobalPeakNits = ViewModel.Settings.PeakNits;
            ViewModel.SaveSettingsPublic();

            var peakNits = ViewModel.Settings.PeakNits;
            if (peakNits <= 0 || !ViewModel.Settings.PeakNitsEnabled)
            {
                var emptyDialog = new ContentDialog
                {
                    Title = "Peak Nits",
                    Content = "Peak nits is not configured or is disabled.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(emptyDialog);
                return;
            }

            int updatedCount = 0;
            var allCards = ViewModel.AllCards.ToList(); // Snapshot the list for background processing
        
            await Task.Run(() =>
            {
                foreach (var card in allCards)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) continue;
                    if (!System.IO.Directory.Exists(card.InstallPath)) continue;

                    var iniFiles = System.IO.Directory.EnumerateFiles(card.InstallPath, "reshade*.ini")
                        .Where(f => System.IO.Path.GetFileName(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                                 && System.IO.Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (iniFiles.Count == 0) continue;

                    // Skip games where reshade.ini updates are locked
                    if (!ViewModel.GetKeepRsIniUpdated(card.GameName, card.Source ?? "")) continue;

                    try
                    {
                        foreach (var iniFile in iniFiles)
                            AuxInstallService.ApplyPeakNits(iniFile, peakNits);
                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[SettingsHandler.ApplyPeakNitsToAll_Click] Failed for '{card.GameName}' — {ex.Message}");
                    }
                }
            });

            var dialog = new ContentDialog
            {
                Title = "Peak Nits",
                Content = $"Applied peak nits ({peakNits}) to {updatedCount} reshade.ini file{(updatedCount == 1 ? "" : "s")}.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.ApplyPeakNitsToAll_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
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
    public async Task InitAdminModeComboAsync(ComboBox combo)
    {
        _adminComboInit = true;
        // When UAC is disabled or the task exists, show as On
        // Run the schtasks query on a background thread to avoid blocking UI
        bool isUacDisabled = await Task.Run(() => IsUacDisabled());
        bool taskRegistered = !isUacDisabled && await IsAdminTaskRegisteredAsync();
        bool isOn = isUacDisabled || taskRegistered;
        combo.SelectedIndex = isOn ? 1 : 0;
        // Grey out when UAC is off — no task needed, state can't be changed
        if (isUacDisabled) combo.IsEnabled = false;
        _adminComboInit = false;
    }

    public async void AdminModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_adminComboInit) return;
            if (sender is not ComboBox combo) return;

            bool enable = combo.SelectedIndex == 1;
            CrashReporter.Log($"[SettingsHandler.AdminModeCombo] Admin mode = {(enable ? "On" : "Off")}");

            // When UAC is disabled, all processes already run as admin — no task needed.
            if (await Task.Run(() => IsUacDisabled()))
            {
                await DialogService.ShowSafeAsync(new ContentDialog
                {
                    Title = "Admin Mode",
                    Content = "UAC is disabled on this system — RHI always runs as administrator.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                });
                _adminComboInit = true;
                combo.SelectedIndex = 1; // keep showing On
                _adminComboInit = false;
                return;
            }

            try
            {
                // Run schtasks operations on background thread to avoid blocking UI
                if (enable)
                    await Task.Run(() => CreateAdminTask());
                else
                    await Task.Run(() => DeleteAdminTask());

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
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.AdminModeCombo_SelectionChanged] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    private static async Task<bool> IsAdminTaskRegisteredAsync()
    {
        try
        {
            var result = await RunSchtasksAsync($"/Query /TN \"{AdminTaskName}\" /FO LIST");
            return result.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns true when UAC is disabled system-wide (EnableLUA=0).
    /// When UAC is off, all processes already run with full admin rights,
    /// so the scheduled task elevation path is unnecessary.
    /// </summary>
    private static bool IsUacDisabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return key?.GetValue("EnableLUA") is int val && val == 0;
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
        var result = RunSchtasksElevatedSync(args);
        if (result != 0)
            throw new InvalidOperationException($"schtasks /Create failed (exit {result})");
    }

    private static void DeleteAdminTask()
    {
        var result = RunSchtasksElevatedSync($"/Delete /TN \"{AdminTaskName}\" /F");
        if (result != 0)
            throw new InvalidOperationException($"schtasks /Delete failed (exit {result})");
    }

    /// <summary>Runs schtasks.exe elevated (UAC prompt) and waits for completion synchronously.
    /// This is acceptable since it's called from a background thread.</summary>
    private static int RunSchtasksElevatedSync(string arguments)
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

    private static async Task<(int ExitCode, string Output)> RunSchtasksAsync(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(); } catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.RunSchtasksAsync] Failed to kill timed-out schtasks process — {ex.Message}"); }
            return (-1, "Timeout");
        }
        
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (proc.ExitCode, (stdout + stderr).Trim());
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
}
