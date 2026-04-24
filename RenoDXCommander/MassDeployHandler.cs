using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Handles mass INI deployment and preset installation across all games.
/// Extracted from SettingsHandler to isolate the deployment concern.
/// </summary>
public class MassDeployHandler
{
    private readonly MainWindow _window;

    public MassDeployHandler(MainWindow window)
    {
        _window = window;
    }

    public async void MassDeployRsIni_Click(object sender, RoutedEventArgs e)
    {
        int count = 0;
        foreach (var card in _window.ViewModel.AllCards.Where(c => c.RsStatus == GameStatus.Installed && !string.IsNullOrEmpty(c.InstallPath)))
        {
            try
            {
                var screenshotPath = _window.BuildScreenshotSavePath(card.GameName);
                var overlayHotkey = _window.ViewModel.Settings.OverlayHotkey;
                var screenshotHotkey = _window.ViewModel.Settings.ScreenshotHotkey;
                if (card.RequiresVulkanInstall)
                    AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey, screenshotHotkey);
                else
                    AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey, screenshotHotkey);
                AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
                count++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MassDeployRsIni] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
        CrashReporter.Log($"[MassDeployRsIni] Deployed reshade.ini to {count} game(s)");
        await ShowDeployResult("reshade.ini", count);
    }

    public async void MassDeployUlIni_Click(object sender, RoutedEventArgs e)
    {
        int count = 0;
        foreach (var card in _window.ViewModel.AllCards.Where(c => c.UlStatus == GameStatus.Installed && !string.IsNullOrEmpty(c.InstallPath)))
        {
            try
            {
                AuxInstallService.CopyUlIni(card.InstallPath);
                count++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MassDeployUlIni] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
        CrashReporter.Log($"[MassDeployUlIni] Deployed relimiter.ini to {count} game(s)");
        await ShowDeployResult("relimiter.ini", count);
    }

    public async void MassDeployDcIni_Click(object sender, RoutedEventArgs e)
    {
        int count = 0;
        foreach (var card in _window.ViewModel.AllCards.Where(c => c.DcStatus == GameStatus.Installed && !string.IsNullOrEmpty(c.InstallPath)))
        {
            try
            {
                AuxInstallService.CopyDcIni(card.InstallPath);
                count++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MassDeployDcIni] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
        CrashReporter.Log($"[MassDeployDcIni] Deployed DisplayCommander.ini to {count} game(s)");
        await ShowDeployResult("DisplayCommander.ini", count);
    }

    public async void MassDeployOsIni_Click(object sender, RoutedEventArgs e)
    {
        int count = 0;
        var sourceIni = Services.OptiScalerService.OsIniPath;
        if (!File.Exists(sourceIni))
        {
            CrashReporter.Log("[MassDeployOsIni] No OptiScaler.ini found in INIs folder — aborting");
            await ShowDeployResult("OptiScaler.ini", 0);
            return;
        }
        foreach (var card in _window.ViewModel.AllCards.Where(c => c.OsStatus == GameStatus.Installed && !string.IsNullOrEmpty(c.InstallPath)))
        {
            try
            {
                _window.ViewModel.OptiScalerServiceInstance.CopyIniToGame(card);
                count++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MassDeployOsIni] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
        CrashReporter.Log($"[MassDeployOsIni] Deployed OptiScaler.ini to {count} game(s)");
        await ShowDeployResult("OptiScaler.ini", count);
    }

    private async Task ShowDeployResult(string iniName, int count)
    {
        var message = count > 0
            ? $"✅ Deployed {iniName} to {count} game(s)."
            : $"No games found with the corresponding component installed.";
        var dialog = new ContentDialog
        {
            Title = "Mass INI Deployment",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    public async Task MassPresetInstall_ClickAsync(XamlRoot xamlRoot)
    {
        // ── 1. Show preset picker ────────────────────────────────────────────
        var selectedPresets = await PresetPopupHelper.ShowAsync(xamlRoot);
        if (selectedPresets == null || selectedPresets.Count == 0) return;

        // ── 2. Show game picker — list all games with ReShade installed ──────
        var rsGames = _window.ViewModel.AllCards
            .Where(c => c.RsStatus == GameStatus.Installed && !string.IsNullOrEmpty(c.InstallPath))
            .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rsGames.Count == 0)
        {
            var noGamesDialog = new ContentDialog
            {
                Title = "No Games Available",
                Content = "No games with ReShade installed were found. Install ReShade on at least one game first.",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(noGamesDialog);
            return;
        }

        var gamePanel = new StackPanel { Spacing = 4 };
        var gameCheckBoxes = new List<(GameCardViewModel Card, CheckBox Box)>();

        // Select All / Deselect All buttons
        var selectAllBtn = new Button
        {
            Content = "Select All",
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 8, 8),
        };
        var deselectAllBtn = new Button
        {
            Content = "Deselect All",
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(selectAllBtn);
        btnRow.Children.Add(deselectAllBtn);
        gamePanel.Children.Add(btnRow);

        foreach (var card in rsGames)
        {
            var cb = new CheckBox
            {
                Content = card.GameName,
                IsChecked = false,
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                Margin = new Thickness(0, 2, 0, 2),
            };
            gameCheckBoxes.Add((card, cb));
            gamePanel.Children.Add(cb);
        }

        selectAllBtn.Click += (s, ev) => { foreach (var (_, cb) in gameCheckBoxes) cb.IsChecked = true; };
        deselectAllBtn.Click += (s, ev) => { foreach (var (_, cb) in gameCheckBoxes) cb.IsChecked = false; };

        var gameScrollViewer = new ScrollViewer
        {
            Content = gamePanel,
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var gameDialog = new ContentDialog
        {
            Title = $"Select Games — {string.Join(", ", selectedPresets)}",
            Content = gameScrollViewer,
            PrimaryButtonText = "Deploy",
            IsPrimaryButtonEnabled = false,
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
            MinWidth = 500,
        };

        // Enable Deploy only when at least one game is ticked
        foreach (var (_, box) in gameCheckBoxes)
        {
            box.Checked += (s, ev) => gameDialog.IsPrimaryButtonEnabled = gameCheckBoxes.Any(cb => cb.Box.IsChecked == true);
            box.Unchecked += (s, ev) => gameDialog.IsPrimaryButtonEnabled = gameCheckBoxes.Any(cb => cb.Box.IsChecked == true);
        }

        var gameResult = await DialogService.ShowSafeAsync(gameDialog);
        if (gameResult != ContentDialogResult.Primary) return;

        // ── 3. Deploy presets to selected games ──────────────────────────────
        var selectedGames = gameCheckBoxes
            .Where(cb => cb.Box.IsChecked == true)
            .Select(cb => cb.Card)
            .ToList();

        int totalDeployed = 0;
        foreach (var card in selectedGames)
        {
            try
            {
                int count = PresetPopupHelper.DeployPresets(selectedPresets, card.InstallPath);
                totalDeployed += count;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MassPresetInstall] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
        CrashReporter.Log($"[MassPresetInstall] Deployed {selectedPresets.Count} preset(s) to {selectedGames.Count} game(s) ({totalDeployed} total copies)");

        if (totalDeployed == 0) return;

        // ── 4. Offer shader installation ─────────────────────────────────────
        var shaderDialog = new ContentDialog
        {
            Title = "🔧 Install Shaders?",
            Content = $"Presets deployed to {selectedGames.Count} game(s).\n\nAlso install the required shader packs for these games?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var shaderResult = await DialogService.ShowSafeAsync(shaderDialog);
        if (shaderResult == ContentDialogResult.Primary)
        {
            var presetPaths = selectedPresets.Select(f => Path.Combine(PresetPopupHelper.PresetsDir, f)).ToList();
            foreach (var card in selectedGames)
            {
                try
                {
                    await _window.ViewModel.ApplyPresetShadersAsync(card.GameName, presetPaths);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MassPresetInstall] Shader install failed for '{card.GameName}' — {ex.Message}");
                }
            }
            CrashReporter.Log($"[MassPresetInstall] Applied preset shaders to {selectedGames.Count} game(s)");

            // Rebuild overrides panel if the currently selected game was one of the targets
            if (_window.ViewModel.SelectedGame is { } selectedCard
                && selectedGames.Any(c => c.GameName.Equals(selectedCard.GameName, StringComparison.OrdinalIgnoreCase)))
            {
                _window.BuildOverridesPanel(selectedCard);
            }
        }
    }
}
