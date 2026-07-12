// MainWindow.Events.Settings.cs — Settings page button click and ComboBox change handlers.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // ── Settings handlers ─────────────────────────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsButton_Click(sender, e);

    private async void PatchNotesLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowPatchNotesDialogAsync();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.PatchNotesLink_Click] Patch notes dialog error — {ex.Message}");
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenLogsFolder_Click(sender, e);

    private void CopyLogsArchive_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.CopyLogsArchive_Click(sender, e);

    private void PurgeCachedFiles_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.PurgeCachedFiles_Click(sender, e);

    private void AdminModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.AdminModeCombo_SelectionChanged(sender, e);

    private void OpenAppDataFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenAppDataFolder_Click(sender, e);

    private void OpenCustomFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenCustomFolder_Click(sender, e);

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenDownloadsFolder_Click(sender, e);

    private void CustomShadersCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.CustomShadersCombo_SelectionChanged(sender, e);

    private void ApplyScreenshotPath_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyScreenshotPath_Click(sender, e);

    private void ApplyPeakNitsToAll_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyPeakNitsToAll_Click(sender, e);

    private void HotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.HotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyOverlayHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyOverlayHotkey_Click(sender, e);

    private void ApplyReShadeHotkeys_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyReShadeHotkeys_Click(sender, e);

    private void ScreenshotHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.ScreenshotHotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyScreenshotHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyScreenshotHotkey_Click(sender, e);

    private void ResetScreenshotHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ResetScreenshotHotkey_Click(sender, e);

    private void UlHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.UlHotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyUlOsdHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyUlOsdHotkey_Click(sender, e);

    private void UlSharedPresetsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.UlSharedPresetsCombo_SelectionChanged(sender, e);

    private void UlDlssHooksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.UlDlssHooksCombo_SelectionChanged(sender, e);

    private void OsHotkeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsHotkeyCombo_SelectionChanged(sender, e);

    private void OsGpuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsGpuCombo_SelectionChanged(sender, e);

    private void OsDlssInputsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsDlssInputsCombo_SelectionChanged(sender, e);

    private async void GlobalUpdateInclusion_Click(object sender, RoutedEventArgs e)
        => await _settingsHandler.GlobalUpdateInclusion_ClickAsync(sender, e);

    private void CacheAllShadersCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.CacheAllShadersCombo_SelectionChanged(sender, e);

    private void ApplyOsHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyOsHotkey_Click(sender, e);

    private void MassDeployRsIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployRsIni_Click(sender, e);

    private void MassDeployUlIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployUlIni_Click(sender, e);

    private void MassDeployDcIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployDcIni_Click(sender, e);

    private void MassDeployOsIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployOsIni_Click(sender, e);

    private async void MassPresetInstall_Click(object sender, RoutedEventArgs e)
        => await _massDeployHandler.MassPresetInstall_ClickAsync(Content.XamlRoot);

    private async void MassDlssDeploy_Click(object sender, RoutedEventArgs e)
    {
        var dlssService = App.Services.GetRequiredService<IDlssStreamlineService>();
        var dialog = new MassDlssDeployDialog(ViewModel, dlssService, Content.XamlRoot, onComplete: () =>
        {
            // Rebuild the detail panel for the currently selected game so versions update
            if (ViewModel.SelectedGame is { } selectedCard)
            {
                PopulateDetailPanel(selectedCard);
                BuildOverridesPanel(selectedCard);
            }
        });
        await dialog.ShowAsync();
    }

    private async void DlssDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dlssService = App.Services.GetRequiredService<IDlssStreamlineService>();
        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        await DlssDefaultsDialog.ShowAsync(ViewModel, dlssService, presetService, Content.XamlRoot);
        RefreshDlssDefaultsSummary();

        // Rebuild the detail panel so Quick Apply button reflects new defaults state
        if (ViewModel.SelectedGame is { } selectedCard && ViewModel.CurrentViewLayout == Models.ViewLayout.Detail)
        {
            PopulateDetailPanel(selectedCard);
            BuildOverridesPanel(selectedCard);
        }
    }

    internal void RefreshDlssDefaultsSummary()
    {
        var s = ViewModel.Settings;
        DlssDefaultsSummaryPanel.Children.Clear();

        bool hasAny = !string.IsNullOrEmpty(s.DefaultDlssVersion) || !string.IsNullOrEmpty(s.DefaultDlssdVersion)
            || !string.IsNullOrEmpty(s.DefaultDlssgVersion) || !string.IsNullOrEmpty(s.DefaultStreamlineVersion)
            || s.DefaultSrPreset != 0 || s.DefaultRrPreset != 0 || s.DefaultFgPreset != 0
            || s.DefaultSrRenderScale != 0 || s.DefaultRrRenderScale != 0;

        if (!hasAny)
        {
            DlssDefaultsSummaryPanel.Children.Add(new TextBlock
            {
                Text = "No defaults configured yet.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
            return;
        }

        // Build a 4-column grid matching the dialog layout
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var srCol = new StackPanel { Spacing = 2 };
        srCol.Children.Add(new TextBlock { Text = "DLSS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultDlssVersion)) srCol.Children.Add(MakeSummaryText(s.DefaultDlssVersion));
        if (s.DefaultSrPreset != 0) srCol.Children.Add(MakeSummaryText($"Preset {DlssPresetService.SrPresets.FirstOrDefault(p => p.Value == s.DefaultSrPreset).Name ?? "?"}"));
        if (s.DefaultSrRenderScale != 0) srCol.Children.Add(MakeSummaryText($"{s.DefaultSrRenderScale}%"));
        Grid.SetColumn(srCol, 0);
        grid.Children.Add(srCol);

        grid.Children.Add(MakeSummaryDivider(1));

        var rrCol = new StackPanel { Spacing = 2 };
        rrCol.Children.Add(new TextBlock { Text = "RR", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultDlssdVersion)) rrCol.Children.Add(MakeSummaryText(s.DefaultDlssdVersion));
        if (s.DefaultRrPreset != 0) rrCol.Children.Add(MakeSummaryText($"Preset {DlssPresetService.RrPresets.FirstOrDefault(p => p.Value == s.DefaultRrPreset).Name ?? "?"}"));
        if (s.DefaultRrRenderScale != 0) rrCol.Children.Add(MakeSummaryText($"{s.DefaultRrRenderScale}%"));
        Grid.SetColumn(rrCol, 2);
        grid.Children.Add(rrCol);

        grid.Children.Add(MakeSummaryDivider(3));

        var fgCol = new StackPanel { Spacing = 2 };
        fgCol.Children.Add(new TextBlock { Text = "FG", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultDlssgVersion)) fgCol.Children.Add(MakeSummaryText(s.DefaultDlssgVersion));
        if (s.DefaultFgPreset != 0) fgCol.Children.Add(MakeSummaryText($"Preset {DlssPresetService.FgPresets.FirstOrDefault(p => p.Value == s.DefaultFgPreset).Name ?? "?"}"));
        Grid.SetColumn(fgCol, 4);
        grid.Children.Add(fgCol);

        grid.Children.Add(MakeSummaryDivider(5));

        var slCol = new StackPanel { Spacing = 2 };
        slCol.Children.Add(new TextBlock { Text = "SL", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultStreamlineVersion)) slCol.Children.Add(MakeSummaryText(s.DefaultStreamlineVersion));
        Grid.SetColumn(slCol, 6);
        grid.Children.Add(slCol);

        DlssDefaultsSummaryPanel.Children.Add(grid);
    }

    private static TextBlock MakeSummaryText(string text) => new TextBlock
    {
        Text = text,
        FontSize = 10,
        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
    };

    private static Border MakeSummaryDivider(int column)
    {
        var div = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderSubtleBrush), VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(div, column);
        return div;
    }

    internal bool _dlssIndicatorInitializing = true;

    internal bool _shaderCacheComboInit = true;

    private void ShaderCacheSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.ShaderCacheSizeOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetShaderCacheSize(options[combo.SelectedIndex].Value);
        }
    }

    private void ShaderPrecompileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.ShaderPrecompileOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetShaderPrecompile(options[combo.SelectedIndex].Value);
        }
    }

    private void GSyncModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.GSyncModeOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGSyncMode(options[combo.SelectedIndex].Value);
        }
    }

    private void PreferredRefreshRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.PreferredRefreshRateOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetPreferredRefreshRate(options[combo.SelectedIndex].Value);
        }
    }

    private void GlobalReBarEnableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        bool enabled = combo.SelectedIndex == 1; // 0=Off, 1=On
        presetService.SetGlobalReBarEnabled(enabled);
        // Update size combo enabled state and reset to default when off
        GlobalReBarSizeCombo.IsEnabled = enabled;
        GlobalReBarSizeCombo.Opacity = enabled ? 1.0 : 0.4;
        if (!enabled)
            GlobalReBarSizeCombo.SelectedIndex = 1; // Reset to 1GB (Default)
        // Force detail panel rebuild if a game is selected
        if (ViewModel.SelectedGame != null)
            DispatcherQueue?.TryEnqueue(() => _detailPanelBuilder?.BuildOverridesPanel(ViewModel.SelectedGame));
    }

    private void GlobalReBarSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.ReBarSizeLimits;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGlobalReBarSizeLimit(options[combo.SelectedIndex].Value);
        }
        // Force detail panel rebuild if a game is selected
        if (ViewModel.SelectedGame != null)
            DispatcherQueue?.TryEnqueue(() => _detailPanelBuilder?.BuildOverridesPanel(ViewModel.SelectedGame));
    }

    private void GlobalVSyncCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.VSyncModeOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGlobalVSyncMode(options[combo.SelectedIndex].Value);
        }
        // Force detail panel rebuild if a game is selected
        if (ViewModel.SelectedGame != null)
            DispatcherQueue?.TryEnqueue(() => _detailPanelBuilder?.BuildOverridesPanel(ViewModel.SelectedGame));
    }

    private void GlobalPowerModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.PowerManagementOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGlobalPowerMode(options[combo.SelectedIndex].Value);
        }
    }

    private async void CreateMissingNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        if (!presetService.IsSupported) return;

        var created = new List<string>();
        foreach (var card in ViewModel.AllCards)
        {
            if (card.IsHidden || string.IsNullOrEmpty(card.InstallPath)) continue;
            if (presetService.EnsureProfileExists(card.GameName, card.InstallPath))
                created.Add(card.GameName);
        }

        var content = created.Count > 0
            ? $"Created {created.Count} profile(s):\n\n• " + string.Join("\n• ", created)
            : "All games already have NVIDIA profiles.";

        var dialog = new ContentDialog
        {
            Title = "Create Missing Profiles",
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void ExportNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            var games = ViewModel.AllCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath))
                .Select(c => (c.GameName, c.InstallPath))
                .ToList();

            // Run on background thread — FindProfile does exe scanning per game
            var data = await Task.Run(() => presetService.ExportProfiles(games));

            if (data.Count == 0)
            {
                await DialogService.ShowSafeAsync(new ContentDialog
                {
                    Title = "Export",
                    Content = "No custom profile settings found to export.",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                });
                return;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "nvidia_profiles_backup.json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, json);

            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Export Complete",
                Content = $"Exported {data.Count} profile(s) to:\n{path}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ExportNvidiaProfiles_Click] Error: {ex.GetType().Name}: {ex.Message}");
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Export Failed",
                Content = $"An error occurred during export:\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
    }

    private async void ImportNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "nvidia_profiles_backup.json");

        if (!System.IO.File.Exists(path))
        {
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Import",
                Content = $"No backup file found at:\n{path}\n\nExport profiles first.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
            return;
        }

        var json = System.IO.File.ReadAllText(path);
        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        if (data == null || data.Count == 0) return;

        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        var count = presetService.ImportProfiles(data);

        // Refresh settings page to reflect imported global values
        _settingsHandler.RefreshGlobalNvidiaSettings();

        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Import Complete",
            Content = $"Imported {count} profile(s) from backup.",
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }

    private async void ResetAllNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "Reset all game profiles?",
            Content = new TextBlock
            {
                Text = "This will remove ALL per-game NVIDIA driver profile overrides (DLSS/Streamline versions, presets, render scales, ReBAR, VSync, Smooth Motion, Low Latency, Power Mode — everything) AND reset global settings (Shader Cache, G-Sync, Refresh Rate, ReBAR) to defaults.\n\nAll profiles will return to NVIDIA factory defaults. This cannot be undone.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
            },
            PrimaryButtonText = "Reset All",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(confirmDialog);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var presetSvc = _dlssPresetService;
            var gamesList = ViewModel.AllCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath))
                .Select(c => (c.GameName, c.InstallPath!))
                .ToList();

            // Show progress dialog
            var progressPanel = new StackPanel { Spacing = 8 };
            var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };
            var progressText = new TextBlock { Text = "Preparing...", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
            var progressRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
            progressRow.Children.Add(progressRing);
            progressRow.Children.Add(progressText);
            progressPanel.Children.Add(progressRow);

            var progressDialog = new ContentDialog
            {
                Title = "Resetting...",
                Content = progressPanel,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            _ = DialogService.ShowSafeAsync(progressDialog);

            int resetCount = 0;
            await Task.Run(() =>
            {
                for (int i = 0; i < gamesList.Count; i++)
                {
                    var (gameName, installPath) = gamesList[i];
                    DispatcherQueue?.TryEnqueue(() =>
                        progressText.Text = $"[{i + 1}/{gamesList.Count}] {gameName}");
                    try
                    {
                        if (presetSvc.RestoreProfileDefaults(gameName, installPath))
                            resetCount++;
                    }
                    catch { }
                }

                // Reset global profile
                DispatcherQueue?.TryEnqueue(() =>
                    progressText.Text = "Resetting global settings...");
                presetSvc.ResetGlobalProfile();
            });

            progressDialog.Hide();

            // Refresh all cards so the detail panel reflects cleared presets
            foreach (var card in ViewModel.AllCards)
                card.NotifyAll();

            // Rebuild the detail panel for the selected game to refresh NVIDIA section
            if (ViewModel.SelectedGame != null)
            {
                PopulateDetailPanel(ViewModel.SelectedGame);
                _detailPanelBuilder.BuildOverridesPanel(ViewModel.SelectedGame);
            }

            // Re-initialize the Global Nvidia Settings combos to reflect cleared values
            _settingsHandler.RefreshGlobalNvidiaSettings();

            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Profiles Reset",
                Content = $"Reset {resetCount} game profile(s) and global settings to factory defaults.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ResetAllNvidiaProfiles_Click] Error: {ex.GetType().Name}: {ex.Message}");
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Reset Failed",
                Content = $"Error: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
    }

    private async void ClearNvidiaShaderCache_Click(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "Clear NVIDIA Shader Cache",
            Content = "This will permanently delete the NVIDIA DXCache and GLCache folders. This cannot be undone.\n\nAll games will need to rebuild their shader caches on next launch, which may cause brief stuttering or longer load times the first time. This can fix shader corruption, persistent stuttering, or graphical issues after driver updates.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(confirmDialog);
        if (result != ContentDialogResult.Primary) return;

        int filesDeleted = 0;
        int filesFailed = 0;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cachePaths = new[]
        {
            Path.Combine(localAppData, "NVIDIA", "DXCache"),
            Path.Combine(localAppData, "NVIDIA", "GLCache"),
        };

        foreach (var path in cachePaths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); filesDeleted++; }
                    catch { filesFailed++; }
                }
                // Try to remove empty subdirectories
                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir); } catch { }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[ClearNvidiaShaderCache] Error enumerating '{path}': {ex.Message}");
            }
        }

        var message = filesFailed > 0
            ? $"Shader cache folders cleared. {filesDeleted} files deleted, {filesFailed} files skipped (in use by running games). Shaders will recompile on next game launch."
            : $"Shader cache folders cleared. {filesDeleted} files deleted. Shaders will recompile on next game launch.";

        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Shader Cache Cleared",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }

    private async void DlssIndicatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_dlssIndicatorInitializing) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;

        // 0 = Enabled (0x400), 1 = Disabled (0x0)
        uint value = combo.SelectedIndex == 0 ? 0x400u : 0u;

        try
        {
            // Writing to HKLM requires admin — use a reg.exe process with elevation
            var regValue = value.ToString();
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe",
                $"add \"HKLM\\SOFTWARE\\NVIDIA Corporation\\Global\\NGXCore\" /v ShowDlssIndicator /t REG_DWORD /d {regValue} /f")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                _crashReporter.Log($"[DlssIndicatorCombo] Set ShowDlssIndicator to {value} (exit code: {proc.ExitCode})");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC — revert the combo without re-triggering the handler
            _crashReporter.Log("[DlssIndicatorCombo] UAC cancelled — reverting selection");
            _dlssIndicatorInitializing = true;
            combo.SelectedIndex = combo.SelectedIndex == 0 ? 1 : 0;
            _dlssIndicatorInitializing = false;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssIndicatorCombo] Failed to set registry — {ex.Message}");
        }
    }

    private void AutoUpdateDlssCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        ViewModel.Settings.AutoUpdateDlss = combo.SelectedIndex == 1;
        ViewModel.SaveSettingsPublic();
    }

    private void AutoUpdateStreamlineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        ViewModel.Settings.AutoUpdateStreamline = combo.SelectedIndex == 1;
        ViewModel.SaveSettingsPublic();
    }

    private void HdrAutoToggleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        ViewModel.Settings.HdrAutoToggle = combo.SelectedIndex == 1;
        ViewModel.SaveSettingsPublic();
    }

    private async void HdrSelectMonitors_Click(object sender, RoutedEventArgs e)
    {
        var displays = HdrToggleService.GetAllDisplays();
        if (displays.Count == 0) return;

        var currentSelection = ViewModel.Settings.HdrTargetDisplays;
        var panel = new StackPanel { Spacing = 8 };
        var checkBoxes = new List<(CheckBox cb, uint targetId)>();

        foreach (var display in displays)
        {
            var cb = new CheckBox
            {
                Content = $"{display.Name}{(display.HdrSupported ? "" : " (no HDR)")}",
                IsChecked = currentSelection.Contains(display.TargetId),
                IsEnabled = display.HdrSupported,
                FontSize = 13,
            };
            checkBoxes.Add((cb, display.TargetId));
            panel.Children.Add(cb);
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Leave all unchecked to enable HDR on the primary display only.",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush),
            Margin = new Thickness(0, 4, 0, 0),
        });

        var dialog = new ContentDialog
        {
            Title = "Select HDR Monitors",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        var selected = checkBoxes
            .Where(x => x.cb.IsChecked == true)
            .Select(x => x.targetId)
            .ToList();

        ViewModel.Settings.HdrTargetDisplays = selected;
        ViewModel.SaveSettingsPublic();
    }

    private void PerGameScreenshotCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        ViewModel.Settings.PerGameScreenshotFolders = combo.SelectedIndex == 1;
        ViewModel.SaveSettingsPublic();
    }

    private void DropHelperCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        if (_dlssIndicatorInitializing) return; // Don't fire during init
        ViewModel.Settings.DropHelperEnabled = combo.SelectedIndex == 1;
        ViewModel.SaveSettingsPublic();
    }

    private void HdrToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;

        var overrides = _gameNameService.HdrToggleOverrides;
        var current = overrides.TryGetValue(card.GameName, out var v) ? v : null;

        // Resolve current effective state and flip it
        bool currentlyActive = current != null
            ? string.Equals(current, "On", StringComparison.OrdinalIgnoreCase)
            : ViewModel.Settings.HdrAutoToggle;

        string newValue = currentlyActive ? "Off" : "On";
        overrides[card.GameName] = newValue;

        ViewModel.SaveSettingsPublic();

        // Update button visual
        bool hdrActive = string.Equals(newValue, "On", StringComparison.OrdinalIgnoreCase);
        DetailHdrToggleText.Text = "HDR";
        DetailHdrToggleBtn.Background = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBgBrush)
            : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
        DetailHdrToggleBtn.BorderBrush = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush)
            : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
        DetailHdrToggleText.Foreground = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBrush)
            : UIFactory.Brush(ResourceKeys.ChipTextBrush);
    }

    private async void BrowseScreenshotPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await PickFolderAsync(ScreenshotPathBox.Text?.Trim());
            if (!string.IsNullOrEmpty(folder))
            {
                ScreenshotPathBox.Text = folder;
                ViewModel.Settings.ScreenshotPath = folder;
                ViewModel.SaveSettingsPublic();
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.BrowseScreenshotPath_Click] Folder picker failed — {ex.Message}");
        }
    }

    private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = ScreenshotPathBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
        {
            _crashReporter.Log($"[MainWindow.OpenScreenshotFolder_Click] Path does not exist: '{path}'");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.OpenScreenshotFolder_Click] Failed to open folder — {ex.Message}");
        }
    }

    private async void PeakNitsAuto_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                Windows.Devices.Display.DisplayMonitor.GetDeviceSelector());
            if (devices.Count == 0) return;

            float maxNitsFound = 0;
            foreach (var device in devices)
            {
                try
                {
                    var mon = await Windows.Devices.Display.DisplayMonitor.FromInterfaceIdAsync(device.Id);
                    if (mon.MaxLuminanceInNits > maxNitsFound)
                        maxNitsFound = mon.MaxLuminanceInNits;
                }
                catch { }
            }
            var peakNits = (int)maxNitsFound;
            if (peakNits <= 0) return;

            PeakNitsBox.Text = peakNits.ToString();
            ViewModel.Settings.PeakNits = peakNits;
            AuxInstallService.GlobalPeakNits = peakNits;
            ViewModel.SaveSettingsPublic();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.PeakNitsAuto_Click] Failed — {ex.Message}");
        }
    }

    private void PeakNitsBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (sender is Microsoft.UI.Xaml.Controls.TextBox box && int.TryParse(box.Text, out var val) && val > 0)
            {
                ViewModel.Settings.PeakNits = val;
                AuxInstallService.GlobalPeakNits = val;
                ViewModel.SaveSettingsPublic();
                box.IsEnabled = false;
                box.IsEnabled = true;
            }
            e.Handled = true;
        }
    }

    private async void PeakNitsCog_Click(object sender, RoutedEventArgs e)
    {
        var settings = ViewModel.Settings;
        var content = new StackPanel { Spacing = 12 };

        // Enable/Disable toggle
        var enablePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        enablePanel.Children.Add(new TextBlock
        {
            Text = "Auto-apply peak nits on deploy",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var enableCombo = new ComboBox { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        enableCombo.Items.Add("Off");
        enableCombo.Items.Add("On");
        enableCombo.SelectedIndex = settings.PeakNitsEnabled ? 1 : 0;
        enablePanel.Children.Add(enableCombo);
        content.Children.Add(enablePanel);

        // Preset checkboxes
        content.Children.Add(new TextBlock
        {
            Text = "Apply to presets:",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            Margin = new Thickness(0, 4, 0, 0),
        });

        var cb1 = new CheckBox { Content = "Preset 1", IsChecked = settings.PeakNitsPresets.Contains(1), FontSize = 12 };
        var cb2 = new CheckBox { Content = "Preset 2", IsChecked = settings.PeakNitsPresets.Contains(2), FontSize = 12 };
        var cb3 = new CheckBox { Content = "Preset 3", IsChecked = settings.PeakNitsPresets.Contains(3), FontSize = 12 };
        content.Children.Add(cb1);
        content.Children.Add(cb2);
        content.Children.Add(cb3);

        content.Children.Add(new TextBlock
        {
            Text = "Unchecked presets keep their existing per-preset values.",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush),
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title = "Peak Nits Settings",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (sender as FrameworkElement)?.XamlRoot ?? Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        // Persist settings
        settings.PeakNitsEnabled = enableCombo.SelectedIndex == 1;
        var presets = new HashSet<int>();
        if (cb1.IsChecked == true) presets.Add(1);
        if (cb2.IsChecked == true) presets.Add(2);
        if (cb3.IsChecked == true) presets.Add(3);
        settings.PeakNitsPresets = presets;

        // Sync to static cache
        AuxInstallService.GlobalPeakNitsEnabled = settings.PeakNitsEnabled;
        AuxInstallService.GlobalPeakNitsPresets = settings.PeakNitsPresets;

        ViewModel.SaveSettingsPublic();
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsBack_Click(sender, e);

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  Simplified PC Gaming by RankFTW";
        ViewModel.NavigateToAboutCommand.Execute(null);
    }

    private void AboutBack_Click(object sender, RoutedEventArgs e)
        => ViewModel.NavigateToGameViewCommand.Execute(null);

}