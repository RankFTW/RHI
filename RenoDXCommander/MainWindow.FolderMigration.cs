// MainWindow.FolderMigration.cs -- Management › Change install folder / Reset install folder, and
// moving a game's installed mods to its new folder (offered after a manual change, automatic when
// the manifest moves a game's folder: MainViewModel.FolderMigration.cs).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    internal async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var card = GetCardFromSender(sender);
            if (card == null) return;
            var suggestedPath = card.InstallPath is { Length: > 0 } p && Directory.Exists(p) ? p
                              : card.DetectedGame?.InstallPath is { Length: > 0 } dp && Directory.Exists(dp) ? dp
                              : null;
            var folder = await PickFolderAsync(suggestedPath);
            if (folder != null)
            {
                var oldFolder = card.InstallPath;
                // The detected folder, captured before the card changes, is what Reset install folder returns to
                var detectedRoot = card.DetectedGame?.InstallPath ?? card.InstallPath;
                card.InstallPath = folder;
                if (card.DetectedGame != null)
                    card.DetectedGame.InstallPath = folder;
                // Persist the override so it survives Refresh / app restart
                ViewModel.SetFolderOverride(card.GameName, folder, card.Source, detectedRoot);
                RefreshShownGameAfterFolderChange(card);
                await OfferMoveInstallsAsync(card, oldFolder, folder, sender);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.BrowseFolder_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// The game header (install path, Open folder) is filled when the page is built, so rebuild the
    /// shown game after its folder changes instead of waiting for the user to move away and back.
    /// </summary>
    private void RefreshShownGameAfterFolderChange(GameCardViewModel card)
    {
        if (!ReferenceEquals(ViewModel.SelectedGame, card)) return;
        CrashReporter.Log($"[MainWindow.RefreshShownGameAfterFolderChange] '{card.GameName}' → {card.InstallPath}");
        PopulateDetailPanel(card);
    }

    /// <summary>Management › Reset install folder: back to the auto-detected folder (detected games only).</summary>
    internal async void ResetInstallFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var card = GetCardFromSender(sender);
            if (card == null || card.IsManuallyAdded) return;
            var oldFolder = card.InstallPath;
            var detectedRoot = ViewModel.ResetFolderOverride(card);
            if (!string.IsNullOrEmpty(detectedRoot))
            {
                // The folder the scan uses for that root (exe folder / manifest subfolder), not the root itself
                var resolved = await Task.Run(() => ViewModel.ResolveDetectedInstallPath(card.GameName, detectedRoot));
                if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved)
                    && !string.Equals(resolved, card.InstallPath, StringComparison.OrdinalIgnoreCase))
                {
                    card.InstallPath = resolved;
                    card.NotifyAll();
                }
            }
            RefreshShownGameAfterFolderChange(card);
            await OfferMoveInstallsAsync(card, oldFolder, card.InstallPath, sender);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ResetInstallFolder_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// After the install folder changed: if the old folder holds this game's RHI installs, asks whether
    /// to move them (mods, settings, presets) to the new folder, then moves them and refreshes the page.
    /// </summary>
    private async Task OfferMoveInstallsAsync(GameCardViewModel card, string? oldFolder, string? newFolder, object sender)
    {
        if (string.IsNullOrEmpty(oldFolder) || string.IsNullOrEmpty(newFolder)) return;
        if (!Directory.Exists(oldFolder) || !Directory.Exists(newFolder)) return;
        if (InstallFolderMover.SamePath(oldFolder, newFolder)) return;

        bool hasInstalls = await Task.Run(() => ViewModel.HasInstallsToMove(card, oldFolder, newFolder));
        if (!hasInstalls)
        {
            CrashReporter.Log($"[MainWindow.OfferMoveInstallsAsync] '{card.GameName}': nothing of RHI's in '{oldFolder}'");
            return;
        }

        var xamlRoot = (sender as FrameworkElement)?.XamlRoot ?? Content?.XamlRoot;
        if (xamlRoot == null) return;

        if (InstallFolderMover.IsWindowsApps(newFolder))
        {
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Mods stay in the old folder",
                Content = MakeMoveDialogText(
                    "The new folder is a Game Pass / Microsoft Store (WindowsApps) folder, which RHI can't write to, "
                    + $"so the mods installed for {card.GameName} stay in the old folder:", oldFolder),
                CloseButtonText = "OK",
                XamlRoot = xamlRoot,
            });
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Move this game's installed mods to the new folder?",
            Content = MakeMoveDialogText(
                $"{card.GameName} has mods installed by RHI in the old folder. Move them, with their settings and presets, "
                + "to the new folder? RHI puts the game's own files back in the old folder.",
                $"From: {oldFolder}\nTo: {newFolder}"),
            PrimaryButtonText = "Move",
            CloseButtonText = "Leave them",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };
        var choice = await DialogService.ShowSafeAsync(dialog);
        if (choice != ContentDialogResult.Primary)
        {
            CrashReporter.Log($"[MainWindow.OfferMoveInstallsAsync] '{card.GameName}': user left the mods in '{oldFolder}'");
            return;
        }

        var outcome = await Task.Run(() => ViewModel.MigrateCardInstalls(card, oldFolder, newFolder));
        if (outcome.Success)
        {
            ViewModel.ApplyFolderMigrationToCard(card, oldFolder, newFolder);
            RefreshShownGameAfterFolderChange(card);
            ShowStatusBarMessage($"Moved {card.GameName}'s mods to the new folder", TimeSpan.FromSeconds(8));
            return;
        }
        if (outcome.NothingToMove) return;

        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Mods not moved",
            Content = MakeMoveDialogText(
                $"RHI couldn't move the mods for {card.GameName}: {outcome.Reason ?? "unknown error"}",
                "Nothing was changed, so they're still in the old folder. Try again with the Reset install folder or Change install folder buttons."),
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
        });
    }

    private static StackPanel MakeMoveDialogText(string text, string detail)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            IsTextSelectionEnabled = true,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        return panel;
    }

    /// <summary>An automatic move (manifest folder change) finished: status bar notice, refresh the page if it's shown.</summary>
    private void OnFolderMigrated(string gameName, string store, string message)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                ShowStatusBarMessage(message, TimeSpan.FromSeconds(12));
                var shown = ViewModel.SelectedGame;
                if (shown != null && shown.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                    && (shown.Source ?? "").Equals(store, StringComparison.OrdinalIgnoreCase))
                    PopulateDetailPanel(shown);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[MainWindow.OnFolderMigrated] '{gameName}' — {ex.Message}");
            }
        });
    }
}
