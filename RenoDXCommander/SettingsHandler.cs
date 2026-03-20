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
        _window.AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  HDR mod manager by RankFTW";
    }

    public void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToGameViewCommand.Execute(null);
        _window.SettingsPanel.Visibility = Visibility.Collapsed;
        // Restore whichever panel was showing before Settings was opened
        if (ViewModel.IsLoading)
            _window.LoadingPanel.Visibility = Visibility.Visible;
        else
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

    public void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var logsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RenoDXCommander", "logs");
        System.IO.Directory.CreateDirectory(logsDir);
        CrashReporter.Log("[SettingsHandler.OpenLogsFolder_Click] User opened logs folder from About panel");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logsDir) { UseShellExecute = true });
    }

    public void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(ModInstallService.DownloadCacheDir);
        CrashReporter.Log("[SettingsHandler.OpenDownloadsFolder_Click] User opened downloads cache folder from About panel");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModInstallService.DownloadCacheDir) { UseShellExecute = true });
    }
}
