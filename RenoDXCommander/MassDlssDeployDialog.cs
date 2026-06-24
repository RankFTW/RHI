// MassDlssDeployDialog.cs — Batch DLSS & Streamline deployment dialog
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Builds and shows the Mass DLSS & Streamline batch deploy dialog.
/// Allows users to select games and deploy specific DLSS/Streamline versions to all at once.
/// </summary>
public class MassDlssDeployDialog
{
    private readonly MainViewModel _viewModel;
    private readonly IDlssStreamlineService _dlssService;
    private readonly XamlRoot _xamlRoot;
    private readonly Action? _onComplete;

    private const string NoneOption = "None";
    private const string DefaultOption = "Default (Restore)";
    private const string CustomOption = "Custom";

    public MassDlssDeployDialog(MainViewModel viewModel, IDlssStreamlineService dlssService, XamlRoot xamlRoot, Action? onComplete = null)
    {
        _viewModel = viewModel;
        _dlssService = dlssService;
        _xamlRoot = xamlRoot;
        _onComplete = onComplete;
    }

    public async Task ShowAsync()
    {
        // ── Build eligible game list (games with DLSS or Streamline detected) ──
        var eligibleCards = _viewModel.AllCards
            .Where(c => c.HasAnyDlssStreamline && !c.IsHidden)
            .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (eligibleCards.Count == 0)
        {
            var emptyDialog = new ContentDialog
            {
                Title = "No DLSS/Streamline Games",
                Content = "No games with DLSS or Streamline DLLs were detected.\nRun a Full Refresh to scan for them.",
                CloseButtonText = "OK",
                XamlRoot = _xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(emptyDialog);
            return;
        }

        // ── Left side: Game checkboxes ──
        var checkBoxes = new List<CheckBox>();
        var gameListPanel = new StackPanel { Spacing = 2 };

        foreach (var card in eligibleCards)
        {
            var isV1 = IsV1Game(card);
            var cb = new CheckBox
            {
                Content = card.GameName,
                FontSize = 12,
                IsEnabled = !isV1,
                Foreground = new SolidColorBrush(isV1
                    ? Windows.UI.Color.FromArgb(255, 120, 120, 120)
                    : Windows.UI.Color.FromArgb(255, 220, 220, 220)),
            };
            if (isV1)
                ToolTipService.SetToolTip(cb, "Skipped — v1.x DLSS/Streamline not compatible with newer versions");
            checkBoxes.Add(cb);
            gameListPanel.Children.Add(cb);
        }

        var selectAllBtn = new Button
        {
            Content = "Select All", FontSize = 11, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 40, 60)),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 190, 220)),
            CornerRadius = new CornerRadius(6),
        };
        selectAllBtn.Click += (_, _) => { foreach (var cb in checkBoxes) if (cb.IsEnabled) cb.IsChecked = true; };

        var deselectAllBtn = new Button
        {
            Content = "Deselect All", FontSize = 11, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 40, 60)),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 190, 220)),
            CornerRadius = new CornerRadius(6),
        };
        deselectAllBtn.Click += (_, _) => { foreach (var cb in checkBoxes) cb.IsChecked = false; };

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        buttonRow.Children.Add(selectAllBtn);
        buttonRow.Children.Add(deselectAllBtn);

        var leftPanel = new StackPanel { Spacing = 0, Width = 280 };
        leftPanel.Children.Add(buttonRow);
        leftPanel.Children.Add(new ScrollViewer
        {
            Content = gameListPanel,
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        // ── Right side: Version dropdowns ──
        var dlssCombo = BuildVersionCombo(_dlssService.DlssVersions);
        var dlssdCombo = BuildVersionCombo(_dlssService.DlssdVersions);
        var dlssgCombo = BuildVersionCombo(_dlssService.DlssgVersions);
        var slCombo = BuildVersionCombo(_dlssService.StreamlineVersions);

        // ── Preset dropdowns ──
        var srPresetCombo = BuildPresetCombo(DlssPresetService.SrPresets);
        var rrPresetCombo = BuildPresetCombo(DlssPresetService.RrPresets);
        var fgPresetCombo = BuildPresetCombo(DlssPresetService.FgPresets);

        var rightPanel = new StackPanel { Spacing = 8, Width = 280 };
        rightPanel.Children.Add(BuildDropdownSection("DLSS Super Resolution", dlssCombo));
        rightPanel.Children.Add(BuildDropdownSection("DLSS Ray Reconstruction", dlssdCombo));
        rightPanel.Children.Add(BuildDropdownSection("DLSS Frame Generation", dlssgCombo));
        rightPanel.Children.Add(BuildDropdownSection("Streamline", slCombo));

        // Presets section
        rightPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 70, 90)),
            Margin = new Thickness(0, 4, 0, 4),
        });
        rightPanel.Children.Add(BuildDropdownSection("SR Preset", srPresetCombo));
        rightPanel.Children.Add(BuildDropdownSection("RR Preset", rrPresetCombo));
        rightPanel.Children.Add(BuildDropdownSection("FG Preset", fgPresetCombo));

        // Auto-create profiles checkbox
        var autoCreateCheck = new CheckBox
        {
            Content = "Auto-create NVIDIA profiles",
            IsChecked = true,
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 190, 220)),
            Margin = new Thickness(0, 4, 0, 0),
        };
        rightPanel.Children.Add(autoCreateCheck);

        // ── Separator ──
        var separator = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 70, 90)),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(16, 0, 16, 0),
        };

        // ── Right-side separator (constrains combo panel visually) ──
        var rightSeparator = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 70, 90)),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(16, 0, 0, 0),
        };

        // ── Main layout ──
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(rightPanel, 2);
        Grid.SetColumn(rightSeparator, 3);
        mainGrid.Children.Add(leftPanel);
        mainGrid.Children.Add(separator);
        mainGrid.Children.Add(rightPanel);
        mainGrid.Children.Add(rightSeparator);

        // ── Dialog ──
        var dialog = new ContentDialog
        {
            Title = "Batch DLSS & Streamline Deploy",
            Content = mainGrid,
            PrimaryButtonText = "Deploy",
            SecondaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 750.0;

        var result = await DialogService.ShowSafeAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            await ExecuteDeployAsync(eligibleCards, checkBoxes, dlssCombo, dlssdCombo, dlssgCombo, slCombo, srPresetCombo, rrPresetCombo, fgPresetCombo, autoCreateCheck.IsChecked == true);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ExecuteRestoreAllAsync(eligibleCards, checkBoxes);
        }
    }

    private async Task ExecuteDeployAsync(
        List<GameCardViewModel> eligibleCards,
        List<CheckBox> checkBoxes,
        ComboBox dlssCombo, ComboBox dlssdCombo, ComboBox dlssgCombo, ComboBox slCombo,
        ComboBox srPresetCombo, ComboBox rrPresetCombo, ComboBox fgPresetCombo,
        bool autoCreateProfiles)
    {
        var dlssVersion = dlssCombo.SelectedItem as string;
        var dlssdVersion = dlssdCombo.SelectedItem as string;
        var dlssgVersion = dlssgCombo.SelectedItem as string;
        var slVersion = slCombo.SelectedItem as string;

        var srPresetSelection = srPresetCombo.SelectedItem as string;
        var rrPresetSelection = rrPresetCombo.SelectedItem as string;
        var fgPresetSelection = fgPresetCombo.SelectedItem as string;

        // Nothing selected at all
        bool anyDllSelected = dlssVersion != NoneOption || dlssdVersion != NoneOption || dlssgVersion != NoneOption || slVersion != NoneOption;
        bool anyPresetSelected = srPresetSelection != NoneOption || rrPresetSelection != NoneOption || fgPresetSelection != NoneOption;
        if (!anyDllSelected && !anyPresetSelected)
            return;

        int dlssCount = 0, dlssdCount = 0, dlssgCount = 0, slCount = 0;
        int srPresetCount = 0, rrPresetCount = 0, fgPresetCount = 0, presetMissedCount = 0;
        int skippedNoComponent = 0, skippedAlreadyAtVersion = 0;

        // Set auto-create flag on preset service
        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        var previousAutoCreate = presetService.AutoCreateProfiles;
        presetService.AutoCreateProfiles = autoCreateProfiles;
        presetService.ProfilesCreatedCount = 0;

        // Show progress
        var progressText = new TextBlock
        {
            Text = "Deploying to selected games...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var progressRing = new ProgressRing
        {
            IsActive = true,
            Width = 24,
            Height = 24,
        };
        var progressPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        progressPanel.Children.Add(progressRing);
        progressPanel.Children.Add(progressText);

        var progressContainer = new Grid
        {
            MinWidth = 350,
        };
        progressContainer.Children.Add(progressPanel);

        var progressDialog = new ContentDialog
        {
            Title = "Deploying...",
            Content = progressContainer,
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        _ = DialogService.ShowSafeAsync(progressDialog);
        await Task.Delay(100); // Let dialog render

        // Count selected games for progress
        int totalSelected = 0;
        for (int j = 0; j < checkBoxes.Count; j++)
            if (checkBoxes[j].IsChecked == true && !IsV1Game(eligibleCards[j])) totalSelected++;
        int processed = 0;

        for (int i = 0; i < eligibleCards.Count; i++)
        {
            var card = eligibleCards[i];
            var cb = checkBoxes[i];
            if (cb.IsChecked != true) continue;

            var detection = card.DlssDetection;
            if (detection == null) { skippedNoComponent++; continue; }

            processed++;
            progressText.Text = $"[{processed}/{totalSelected}] {card.GameName}";
            await Task.Delay(1); // Yield to UI thread so text updates visually

            // DLSS SR — skip v1.x
            if (dlssVersion != NoneOption && detection.DlssPath != null
                && !(card.DlssInstalledVersion?.StartsWith("1.") == true))
            {
                if (dlssVersion == DefaultOption)
                {
                    if (_dlssService.HasBackup(detection.DlssPath))
                    { _dlssService.Restore(detection.DlssPath); dlssCount++; }
                }
                else if (dlssVersion == CustomOption)
                {
                    await _dlssService.SwapDlssCustomAsync(detection.DlssPath); dlssCount++;
                }
                else
                {
                    var installed = DlssStreamlineService.FormatVersion(_dlssService.GetFileVersion(detection.DlssPath));
                    if (installed == dlssVersion) { skippedAlreadyAtVersion++; }
                    else { await _dlssService.SwapDlssAsync(detection.DlssPath, dlssVersion!); dlssCount++; }
                }
            }
            else if (dlssVersion != NoneOption && detection.DlssPath == null) { skippedNoComponent++; }

            // DLSS RR
            if (dlssdVersion != NoneOption && detection.DlssdPath != null)
            {
                if (dlssdVersion == DefaultOption)
                {
                    if (_dlssService.HasBackup(detection.DlssdPath))
                    { _dlssService.Restore(detection.DlssdPath); dlssdCount++; }
                }
                else if (dlssdVersion == CustomOption)
                {
                    await _dlssService.SwapDlssCustomAsync(detection.DlssdPath); dlssdCount++;
                }
                else
                {
                    var installed = DlssStreamlineService.FormatVersion(_dlssService.GetFileVersion(detection.DlssdPath));
                    if (installed == dlssdVersion) { skippedAlreadyAtVersion++; }
                    else { await _dlssService.SwapDlssdAsync(detection.DlssdPath, dlssdVersion!); dlssdCount++; }
                }
            }
            else if (dlssdVersion != NoneOption && detection.DlssdPath == null) { skippedNoComponent++; }

            // DLSS FG
            if (dlssgVersion != NoneOption && detection.DlssgPath != null)
            {
                if (dlssgVersion == DefaultOption)
                {
                    if (_dlssService.HasBackup(detection.DlssgPath))
                    { _dlssService.Restore(detection.DlssgPath); dlssgCount++; }
                }
                else if (dlssgVersion == CustomOption)
                {
                    await _dlssService.SwapDlssCustomAsync(detection.DlssgPath); dlssgCount++;
                }
                else
                {
                    var installed = DlssStreamlineService.FormatVersion(_dlssService.GetFileVersion(detection.DlssgPath));
                    if (installed == dlssgVersion) { skippedAlreadyAtVersion++; }
                    else { await _dlssService.SwapDlssgAsync(detection.DlssgPath, dlssgVersion!); dlssgCount++; }
                }
            }
            else if (dlssgVersion != NoneOption && detection.DlssgPath == null) { skippedNoComponent++; }

            // Streamline — skip v1.x
            if (slVersion != NoneOption && detection.StreamlineFolder != null
                && !(card.StreamlineInstalledVersion?.StartsWith("1.") == true))
            {
                if (slVersion == DefaultOption)
                {
                    _dlssService.RestoreStreamline(detection.StreamlineFolder); slCount++;
                }
                else if (slVersion == CustomOption)
                {
                    await _dlssService.SwapStreamlineCustomAsync(detection.StreamlineFolder); slCount++;
                }
                else
                {
                    var installed = DlssStreamlineService.FormatVersion(_dlssService.GetFileVersion(detection.StreamlineInterposerPath!));
                    if (installed == slVersion) { skippedAlreadyAtVersion++; }
                    else { await _dlssService.SwapStreamlineAsync(detection.StreamlineFolder, slVersion!); slCount++; }
                }
            }
            else if (slVersion != NoneOption && detection.StreamlineFolder == null) { skippedNoComponent++; }

            // ── Apply presets ──
            if (anyPresetSelected)
            {
                var profileCountBefore = presetService.IsSupported ? 0 : -1; // can't track if not supported
                bool presetApplied = false;

                if (srPresetSelection != NoneOption)
                {
                    var srValue = GetPresetValue(DlssPresetService.SrPresets, srPresetSelection!);
                    if (presetService.SetSrPreset(card.GameName, card.InstallPath, srValue))
                    { srPresetCount++; presetApplied = true; }
                    else if (!presetApplied) presetMissedCount++;
                }
                if (rrPresetSelection != NoneOption)
                {
                    var rrValue = GetPresetValue(DlssPresetService.RrPresets, rrPresetSelection!);
                    if (presetService.SetRrPreset(card.GameName, card.InstallPath, rrValue))
                    { rrPresetCount++; presetApplied = true; }
                    else if (!presetApplied) presetMissedCount++;
                }
                if (fgPresetSelection != NoneOption)
                {
                    var fgValue = GetPresetValue(DlssPresetService.FgPresets, fgPresetSelection!);
                    if (presetService.SetFgPreset(card.GameName, card.InstallPath, fgValue))
                    { fgPresetCount++; presetApplied = true; }
                    else if (!presetApplied) presetMissedCount++;
                }
            }

            // Refresh card versions
            card.RefreshDlssVersions(_dlssService);
            card.NotifyAll();
        }

        // Close progress dialog by dismissing it
        progressDialog.Hide();

        // Restore auto-create flag
        presetService.AutoCreateProfiles = previousAutoCreate;

        // ── Show results ──
        var report = new System.Text.StringBuilder();
        if (dlssCount > 0) report.AppendLine($"DLSS SR deployed to {dlssCount} game(s)");
        if (dlssdCount > 0) report.AppendLine($"DLSS RR deployed to {dlssdCount} game(s)");
        if (dlssgCount > 0) report.AppendLine($"DLSS FG deployed to {dlssgCount} game(s)");
        if (slCount > 0) report.AppendLine($"Streamline deployed to {slCount} game(s)");
        if (srPresetCount > 0) report.AppendLine($"SR Preset applied to {srPresetCount} game(s)");
        if (rrPresetCount > 0) report.AppendLine($"RR Preset applied to {rrPresetCount} game(s)");
        if (fgPresetCount > 0) report.AppendLine($"FG Preset applied to {fgPresetCount} game(s)");
        if (autoCreateProfiles && presetService.ProfilesCreatedCount > 0)
            report.AppendLine($"NVIDIA profiles created: {presetService.ProfilesCreatedCount}");
        if (skippedAlreadyAtVersion > 0) report.AppendLine($"\nSkipped: {skippedAlreadyAtVersion} (already at selected version)");
        if (skippedNoComponent > 0) report.AppendLine($"Skipped: {skippedNoComponent} (component not present)");
        if (presetMissedCount > 0) report.AppendLine($"Presets missed: {presetMissedCount} game(s) (no NVIDIA profile found)");

        if (report.Length == 0) report.Append("No changes made.");

        var resultDialog = new ContentDialog
        {
            Title = "Batch Deploy Complete",
            Content = new TextBlock { Text = report.ToString().TrimEnd(), TextWrapping = TextWrapping.Wrap, FontSize = 12 },
            CloseButtonText = "OK",
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(resultDialog);
        _onComplete?.Invoke();
    }

    private async Task ExecuteRestoreAllAsync(List<GameCardViewModel> eligibleCards, List<CheckBox> checkBoxes)
    {
        int restoredCount = 0;
        int presetsResetCount = 0;
        var presetService = App.Services.GetRequiredService<DlssPresetService>();

        // Show progress
        var progressText = new TextBlock
        {
            Text = "Restoring selected games...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var progressRing = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
        var progressPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        progressPanel.Children.Add(progressRing);
        progressPanel.Children.Add(progressText);

        var progressContainer = new Grid { MinWidth = 350 };
        progressContainer.Children.Add(progressPanel);

        var progressDialog = new ContentDialog
        {
            Title = "Restoring...",
            Content = progressContainer,
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        _ = DialogService.ShowSafeAsync(progressDialog);
        await Task.Delay(100);

        for (int i = 0; i < eligibleCards.Count; i++)
        {
            var card = eligibleCards[i];
            var cb = checkBoxes[i];
            if (cb.IsChecked != true) continue;

            var detection = card.DlssDetection;
            if (detection == null) continue;

            progressText.Text = $"Restoring {card.GameName}...";
            await Task.Delay(1);

            // Restore DLL backups
            if (card.HasAnyDlssBackup)
            {
                _dlssService.RestoreAll(detection);
                card.RefreshDlssVersions(_dlssService);
                card.NotifyAll();
                restoredCount++;
            }

            // Reset presets to Default (0x0)
            bool presetReset = false;
            if (presetService.SetSrPreset(card.GameName, card.InstallPath, 0)) presetReset = true;
            if (presetService.SetRrPreset(card.GameName, card.InstallPath, 0)) presetReset = true;
            if (presetService.SetFgPreset(card.GameName, card.InstallPath, 0)) presetReset = true;
            if (presetReset) presetsResetCount++;
        }

        progressDialog.Hide();

        var reportText = new System.Text.StringBuilder();
        if (restoredCount > 0) reportText.AppendLine($"Restored {restoredCount} game(s) to default DLLs.");
        if (presetsResetCount > 0) reportText.AppendLine($"Reset presets to Default on {presetsResetCount} game(s).");
        if (restoredCount == 0 && presetsResetCount == 0) reportText.Append("No games had backups to restore or presets to reset.");

        var resultDialog = new ContentDialog
        {
            Title = "Restore Complete",
            Content = new TextBlock
            {
                Text = reportText.ToString().TrimEnd(),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            },
            CloseButtonText = "OK",
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(resultDialog);
        _onComplete?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsV1Game(GameCardViewModel card)
    {
        // No longer used for greying out — games with v1.x are selectable.
        // Per-component v1.x skipping is handled during deployment.
        return false;
    }

    private ComboBox BuildVersionCombo(IReadOnlyList<string> versions)
    {
        var items = new List<string> { NoneOption, DefaultOption };
        items.AddRange(versions);
        items.Add(CustomOption);

        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = 0,
            FontSize = 12,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static StackPanel BuildDropdownSection(string label, ComboBox combo)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 210, 230)),
        });
        panel.Children.Add(combo);
        return panel;
    }

    private static ComboBox BuildPresetCombo((string Name, uint Value)[] presets)
    {
        var items = new List<string> { NoneOption };
        foreach (var (name, _) in presets)
            items.Add(name);

        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = 0,
            FontSize = 12,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static uint GetPresetValue((string Name, uint Value)[] presets, string selection)
    {
        foreach (var (name, value) in presets)
        {
            if (string.Equals(name, selection, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return 0; // Default
    }
}
