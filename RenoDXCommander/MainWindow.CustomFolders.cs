// MainWindow.CustomFolders.cs — Settings > Game Library > Custom Game Folders UI and the
// "candidates need review" header button.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _customFolderUiLoading;
    private bool _customFolderBusy;

    /// <summary>Rebuilds the folder list and toggle from settings. Called whenever the Settings page opens.</summary>
    internal void RefreshCustomGameFoldersUi()
    {
        var settings = ViewModel.Settings;
        var folders = settings.CustomGameFolders.ToList();

        CustomFolderListPanel.Children.Clear();
        if (folders.Count == 0)
        {
            CustomFolderListPanel.Children.Add(new TextBlock
            {
                Text = "No folders added yet.",
                FontSize = 12,
                Foreground = Brush(ResourceKeys.TextTertiaryBrush),
            });
        }

        var statusBlocks = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            var pathText = new TextBlock
            {
                Text = folder,
                FontSize = 12,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(pathText, folder);
            text.Children.Add(pathText);
            var status = new TextBlock { FontSize = 10, Foreground = Brush(ResourceKeys.TextTertiaryBrush), Visibility = Visibility.Collapsed };
            text.Children.Add(status);
            statusBlocks[folder] = status;

            var remove = new Button
            {
                Content = "Remove",
                Tag = folder,
                Background = Brush(ResourceKeys.SurfaceInputBrush),
                Foreground = Brush(ResourceKeys.TextTertiaryBrush),
                BorderBrush = Brush(ResourceKeys.BorderSubtleBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 12,
            };
            remove.Click += RemoveCustomGameFolder_Click;

            Grid.SetColumn(text, 0);
            Grid.SetColumn(remove, 1);
            row.Children.Add(text);
            row.Children.Add(remove);
            CustomFolderListPanel.Children.Add(row);
        }

        _customFolderUiLoading = true;
        CustomGameFoldersAutoScanCombo.SelectedIndex = settings.CustomFoldersAutoScan ? 1 : 0;
        _customFolderUiLoading = false;

        // Availability is checked off the UI thread — an unplugged network/removable drive can be slow to answer.
        if (folders.Count > 0)
        {
            _ = Task.Run(() => folders.Select(f => (f, ok: CustomFolderPaths.IsAvailable(f))).ToList())
                .ContinueWith(t =>
                {
                    if (t.Status != TaskStatus.RanToCompletion) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        foreach (var (f, ok) in t.Result)
                        {
                            if (ok || !statusBlocks.TryGetValue(f, out var block)) continue;
                            block.Text = "Not available right now — kept in the list and skipped until it is back";
                            block.Visibility = Visibility.Visible;
                        }
                    });
                });
        }
    }

    private void SetCustomFolderStatus(string message)
    {
        CustomFolderStatusText.Text = message;
        CustomFolderStatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void AddCustomGameFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picked = await PickFolderAsync();
            if (string.IsNullOrEmpty(picked)) return;

            var folders = ViewModel.Settings.CustomGameFolders.ToList();
            switch (CustomFolderPaths.TryAdd(folders, picked, out var normalized))
            {
                case CustomFolderAddResult.Duplicate:
                    SetCustomFolderStatus($"{normalized} is already in the list.");
                    return;
                case CustomFolderAddResult.Unsafe:
                    SetCustomFolderStatus("That is a Windows/system location and can't be used as a game folder.");
                    return;
                case CustomFolderAddResult.Invalid:
                    SetCustomFolderStatus("That folder path isn't valid.");
                    return;
            }

            ViewModel.Settings.CustomGameFolders = folders;
            ViewModel.SaveSettingsPublic();
            _crashReporter.Log($"[MainWindow.CustomFolders] Added custom game folder '{normalized}'");
            RefreshCustomGameFoldersUi();
            SetCustomFolderStatus($"Added {normalized}. Press Scan Now to look for games.");
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.AddCustomGameFolder] {ex.Message}"); }
    }

    private void RemoveCustomGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folder }) return;

        // Removes only the configured root. Games already imported stay in the library and no files are touched.
        var folders = ViewModel.Settings.CustomGameFolders.ToList();
        if (!CustomFolderPaths.Remove(folders, folder)) return;
        ViewModel.Settings.CustomGameFolders = folders;
        ViewModel.SaveSettingsPublic();
        _crashReporter.Log($"[MainWindow.CustomFolders] Removed custom game folder '{folder}'");
        RefreshCustomGameFoldersUi();
        SetCustomFolderStatus($"Removed {folder} from the list. Games already added stay in your library.");
    }

    private void CustomGameFoldersAutoScanCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_customFolderUiLoading || sender is not ComboBox combo) return;
        ViewModel.Settings.CustomFoldersAutoScan = combo.SelectedIndex == 1;
        ViewModel.SaveSettingsPublic();
    }

    private async void ScanCustomGameFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_customFolderBusy) return;
        if (ViewModel.Settings.CustomGameFolders.Count == 0)
        {
            SetCustomFolderStatus("Add a folder first.");
            return;
        }
        if (ViewModel.IsLoading || ViewModel.IsBackgroundScanning)
        {
            SetCustomFolderStatus("RHI is still scanning your library — try again in a moment.");
            return;
        }

        _customFolderBusy = true;
        ScanCustomFoldersBtn.IsEnabled = false;
        SetCustomFolderStatus("Scanning custom folders…");
        try
        {
            var result = await ViewModel.ScanCustomFoldersNowAsync();
            var note = result.UnavailableRoots.Count > 0
                ? $" {result.UnavailableRoots.Count} folder(s) not available and skipped."
                : "";
            if (result.Truncated) note += " Some folders were very large and were only partly scanned.";

            if (result.Candidates.Count == 0)
            {
                SetCustomFolderStatus("No new games found." + note);
                return;
            }

            SetCustomFolderStatus("");
            await ReviewCustomCandidatesAsync(result.Candidates);
            if (!string.IsNullOrEmpty(note)) SetCustomFolderStatus(CustomFolderStatusText.Text + note);
        }
        catch (OperationCanceledException) { SetCustomFolderStatus("Scan cancelled."); }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.ScanCustomGameFolders] {ex}");
            SetCustomFolderStatus("Scan failed — see the log for details.");
        }
        finally
        {
            _customFolderBusy = false;
            ScanCustomFoldersBtn.IsEnabled = true;
        }
    }

    /// <summary>Header button shown after an automatic scan found ambiguous candidates.</summary>
    private async void CustomReviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pending = ViewModel.PendingCustomCandidates.ToList();
            if (pending.Count == 0) return;
            await ReviewCustomCandidatesAsync(pending);
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.CustomReviewButton] {ex}"); }
    }

    private async Task ReviewCustomCandidatesAsync(IReadOnlyList<CustomGameCandidate> candidates)
    {
        var outcome = await CustomFolderReviewDialog.ShowAsync(candidates, Content.XamlRoot);
        if (outcome == null) return;   // cancelled: nothing changes, nothing is remembered

        var added = await ViewModel.ImportCustomCandidatesAsync(outcome.Selected, outcome.Declined);
        SetCustomFolderStatus(added > 0
            ? $"Added {added} game{(added == 1 ? "" : "s")} to your library."
            : "No games were added.");
    }
}
