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
    private readonly MainWindow _window;

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
        _window.BetaOptInToggle.IsOn = ViewModel.BetaOptIn;
        _window.VerboseLoggingToggle.IsOn = ViewModel.VerboseLogging;
        _window.CustomShadersToggle.IsOn = ViewModel.Settings.UseCustomShaders;
        _window.AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  HDR mod manager by RankFTW";
        // Populate addon watch folder textbox
        _window.AddonWatchFolderBox.Text = ViewModel.Settings.AddonWatchFolder;
        // Populate screenshot path and per-game toggle
        _window.ScreenshotPathBox.Text = ViewModel.Settings.ScreenshotPath;
        _window.PerGameScreenshotToggle.IsOn = ViewModel.Settings.PerGameScreenshotFolders;
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
}
