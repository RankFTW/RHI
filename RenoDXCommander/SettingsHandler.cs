using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Encapsulates settings-page event handlers and toggle logic.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class SettingsHandler
{
    // ── Hotkey helper methods ──────────────────────────────────────────

    private static readonly Dictionary<int, string> VkNames = new()
    {
        [8] = "Backspace", [9] = "Tab", [13] = "Enter", [19] = "Pause", [20] = "Caps Lock",
        [27] = "Escape", [32] = "Space", [33] = "Page Up", [34] = "Page Down",
        [35] = "End", [36] = "Home", [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down",
        [44] = "Print Screen", [45] = "Insert", [46] = "Delete",
        [48] = "0", [49] = "1", [50] = "2", [51] = "3", [52] = "4",
        [53] = "5", [54] = "6", [55] = "7", [56] = "8", [57] = "9",
        [65] = "A", [66] = "B", [67] = "C", [68] = "D", [69] = "E", [70] = "F",
        [71] = "G", [72] = "H", [73] = "I", [74] = "J", [75] = "K", [76] = "L",
        [77] = "M", [78] = "N", [79] = "O", [80] = "P", [81] = "Q", [82] = "R",
        [83] = "S", [84] = "T", [85] = "U", [86] = "V", [87] = "W", [88] = "X",
        [89] = "Y", [90] = "Z",
        [96] = "Num 0", [97] = "Num 1", [98] = "Num 2", [99] = "Num 3", [100] = "Num 4",
        [101] = "Num 5", [102] = "Num 6", [103] = "Num 7", [104] = "Num 8", [105] = "Num 9",
        [106] = "Num *", [107] = "Num +", [109] = "Num -", [110] = "Num .", [111] = "Num /",
        [112] = "F1", [113] = "F2", [114] = "F3", [115] = "F4", [116] = "F5", [117] = "F6",
        [118] = "F7", [119] = "F8", [120] = "F9", [121] = "F10", [122] = "F11", [123] = "F12",
        [124] = "F13", [125] = "F14", [126] = "F15", [127] = "F16", [128] = "F17", [129] = "F18",
        [130] = "F19", [131] = "F20", [132] = "F21", [133] = "F22", [134] = "F23", [135] = "F24",
        [144] = "Num Lock", [145] = "Scroll Lock",
        [186] = ";", [187] = "=", [188] = ",", [189] = "-", [190] = ".", [191] = "/",
        [192] = "`", [219] = "[", [220] = "\\", [221] = "]", [222] = "'",
    };

    /// <summary>
    /// Parses a KeyOverlay format string "vk,shift,ctrl,alt" into its components.
    /// Returns (vkCode, shift, ctrl, alt). Returns default (36, false, false, false) on invalid input.
    /// </summary>
    public static (int vk, bool shift, bool ctrl, bool alt) ParseHotkeyString(string value)
    {
        try
        {
            var parts = value.Split(',');
            if (parts.Length != 4) return (36, false, false, false);
            return (int.Parse(parts[0]), parts[1] != "0", parts[2] != "0", parts[3] != "0");
        }
        catch
        {
            return (36, false, false, false);
        }
    }

    /// <summary>
    /// Builds a KeyOverlay format string from components.
    /// </summary>
    public static string BuildHotkeyString(int vk, bool shift, bool ctrl, bool alt)
    {
        return $"{vk},{(shift ? 1 : 0)},{(ctrl ? 1 : 0)},{(alt ? 1 : 0)}";
    }

    /// <summary>
    /// Formats a hotkey into a human-readable display string.
    /// Modifier order: Ctrl, Shift, Alt, then the main key name.
    /// </summary>
    public static string FormatHotkeyDisplay(int vk, bool shift, bool ctrl, bool alt)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        parts.Add(VkNames.TryGetValue(vk, out var name) ? name : $"Key {vk}");
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Formats a KeyOverlay string "vk,shift,ctrl,alt" into a human-readable display string.
    /// </summary>
    public static string FormatHotkeyDisplay(string keyOverlayValue)
    {
        var (vk, shift, ctrl, alt) = ParseHotkeyString(keyOverlayValue);
        return FormatHotkeyDisplay(vk, shift, ctrl, alt);
    }

    /// <summary>
    /// Returns true if the given KeyOverlay string represents the default Home key (36,0,0,0).
    /// </summary>
    public static bool IsDefaultHotkey(string keyOverlayValue)
    {
        return keyOverlayValue == "36,0,0,0";
    }

    // ── Instance members ───────────────────────────────────────────────

    private readonly MainWindow _window;

    /// <summary>
    /// Stores the current hotkey string in KeyOverlay format ("vk,shift,ctrl,alt").
    /// Accessible from the Apply handler (Task 6.3).
    /// </summary>
    internal string _currentHotkeyString = "36,0,0,0";
    internal string _currentUlHotkeyString = "F12";

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
        _window.HotkeyBox.Text = FormatHotkeyDisplay(ViewModel.Settings.OverlayHotkey);
        // Initialize ReLimiter OSD hotkey display
        _currentUlHotkeyString = ViewModel.Settings.UlOsdHotkey;
        _window.UlHotkeyBox.Text = ViewModel.Settings.UlOsdHotkey;
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
        await dialog.ShowAsync();
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
        _currentHotkeyString = BuildHotkeyString(vk, shift, ctrl, alt);

        // Update the TextBox display with human-readable format (Req 2.1)
        if (sender is TextBox hotkeyBox)
        {
            hotkeyBox.Text = FormatHotkeyDisplay(vk, shift, ctrl, alt);
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

        bool isDefault = IsDefaultHotkey(_currentHotkeyString);

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
        await dialog.ShowAsync();
    }

    // ── ReLimiter OSD Hotkey ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a ReLimiter-format hotkey string from VK code and modifiers.
    /// Format: [Ctrl+][Alt+][Shift+]KeyName (e.g. "Ctrl+F12", "Alt+P", "F1")
    /// </summary>
    public static string BuildUlHotkeyString(int vk, bool shift, bool ctrl, bool alt)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(VkNames.TryGetValue(vk, out var name) ? name : $"0x{vk:X2}");
        return string.Join("+", parts);
    }

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

        _currentUlHotkeyString = BuildUlHotkeyString(vk, shift, ctrl, alt);

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
        await dialog.ShowAsync();
    }
}
