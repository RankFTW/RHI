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
        _window.SkipUpdateToggle.IsOn = ViewModel.SkipUpdateCheck;
        _window.VerboseLoggingToggle.IsOn = ViewModel.VerboseLogging;
        _window.CustomShadersToggle.IsOn = ViewModel.Settings.UseCustomShaders;
        _window.AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  HDR mod manager by RankFTW";
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

        // Initialize DLSS toggle and visibility
        _window.OsDlssInputsToggle.IsOn = ViewModel.Settings.OsDlssInputs;
        bool showDlss = !string.Equals(gpuType, "NVIDIA", StringComparison.OrdinalIgnoreCase);
        _window.OsDlssInputsToggle.Visibility = showDlss ? Visibility.Visible : Visibility.Collapsed;

        // Initialize Global Update Checks toggles (inverted: ON = updates enabled, skip = false)
        _window.GlobalRdxUpdateToggle.IsOn = !ViewModel.Settings.GlobalSkipRdxUpdates;
        _window.GlobalRsUpdateToggle.IsOn = !ViewModel.Settings.GlobalSkipRsUpdates;
        _window.GlobalUlUpdateToggle.IsOn = !ViewModel.Settings.GlobalSkipUlUpdates;
        _window.GlobalDcUpdateToggle.IsOn = !ViewModel.Settings.GlobalSkipDcUpdates;
        _window.GlobalOsUpdateToggle.IsOn = !ViewModel.Settings.GlobalSkipOsUpdates;
        _window.GlobalRefUpdateToggle.IsOn = !ViewModel.Settings.GlobalSkipRefUpdates;
        _window.CacheAllShadersToggle.IsOn = ViewModel.Settings.CacheAllShaders;
    }

    public void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToGameViewCommand.Execute(null);
        _window.SettingsPanel.Visibility = Visibility.Collapsed;
        // Always show GameViewPanel — skeleton loading handles the loading state visually
        _window.GameViewPanel.Visibility = Visibility.Visible;
    }

    public void SkipUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.SkipUpdateCheck = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void BetaOptInToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.BetaOptIn = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void VerboseLoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.VerboseLogging = toggle.IsOn;
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
        CrashReporter.Log("[SettingsHandler.OpenLogsFolder_Click] User opened logs folder from About panel");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logsDir) { UseShellExecute = true });
    }

    public void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(DownloadPaths.Root);
        CrashReporter.Log("[SettingsHandler.OpenDownloadsFolder_Click] User opened downloads cache folder from About panel");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DownloadPaths.Root) { UseShellExecute = true });
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

    // ── OptiScaler Hotkey ─────────────────────────────────────────────────────

    public void OsGpuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is string selected)
        {
            ViewModel.Settings.OsGpuType = selected;
            ViewModel.SaveSettingsPublic();

            // Show DLSS toggle only for AMD or Intel
            bool showDlss = !string.Equals(selected, "NVIDIA", StringComparison.OrdinalIgnoreCase);
            _window.OsDlssInputsToggle.Visibility = showDlss ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void OsDlssInputsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.OsDlssInputs = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void GlobalRdxUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.GlobalSkipRdxUpdates = !toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void GlobalRsUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.GlobalSkipRsUpdates = !toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void GlobalUlUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.GlobalSkipUlUpdates = !toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void GlobalDcUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.GlobalSkipDcUpdates = !toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void GlobalOsUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.GlobalSkipOsUpdates = !toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    public void GlobalRefUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Settings.IsLoadingSettings) return;
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Settings.GlobalSkipRefUpdates = !toggle.IsOn;
            ViewModel.SaveSettingsPublic();
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

}
