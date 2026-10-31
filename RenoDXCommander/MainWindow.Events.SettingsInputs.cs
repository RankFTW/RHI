// MainWindow.Events.SettingsInputs.cs — Settings page text boxes that save on Enter and on LostFocus
// (screenshot folder, addon watch folder, peak nits), so typed values aren't lost when leaving the page.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // ── Screenshot folder ─────────────────────────────────────────────────────

    private void ScreenshotPathBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        SaveScreenshotPathFromBox();
        e.Handled = true;
    }

    private void ScreenshotPathBox_LostFocus(object sender, RoutedEventArgs e)
        => SaveScreenshotPathFromBox();

    /// <summary>
    /// Persists the typed screenshot folder (trimmed; empty is allowed, as with
    /// "Write these to all games"). Writing it into games is still done by that button.
    /// </summary>
    private void SaveScreenshotPathFromBox()
    {
        var typed = ScreenshotPathBox.Text?.Trim() ?? "";
        if (string.Equals(typed, ViewModel.Settings.ScreenshotPath ?? "", StringComparison.OrdinalIgnoreCase)) return;

        ViewModel.Settings.ScreenshotPath = typed;
        ViewModel.SaveSettingsPublic();
        _crashReporter.Log($"[MainWindow.SaveScreenshotPathFromBox] Screenshot folder saved: '{typed}'");
    }

    // ── Addon watch folder ────────────────────────────────────────────────────

    private bool _addonWatchFolderCommitting;

    private async void AddonWatchFolderBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await CommitAddonWatchFolderFromBoxAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.AddonWatchFolderBox_KeyDown] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void AddonWatchFolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            await CommitAddonWatchFolderFromBoxAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.AddonWatchFolderBox_LostFocus] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Saves a typed addon watch folder the same way Browse does (setting + watcher restart + save).
    /// Empty text behaves like Reset (back to Downloads). A folder that doesn't exist is not saved:
    /// the text reverts to the saved folder and a short message is shown.
    /// </summary>
    private async Task CommitAddonWatchFolderFromBoxAsync()
    {
        if (_addonWatchFolderCommitting) return;
        _addonWatchFolderCommitting = true;
        try
        {
            var typed = (AddonWatchFolderBox.Text?.Trim() ?? "").Replace('/', '\\');
            var saved = ViewModel.Settings.AddonWatchFolder ?? "";
            if (string.Equals(typed, saved, StringComparison.OrdinalIgnoreCase)) return;

            if (typed.Length == 0)
            {
                ResetAddonWatchFolder_Click(AddonWatchFolderBox, new RoutedEventArgs());
                return;
            }

            bool exists = await Task.Run(() => Directory.Exists(typed));

            // Browse or Reset may have changed the folder while we checked (focus moved to them):
            // their choice wins, so don't overwrite it or complain about the old typed text.
            if (!string.Equals(ViewModel.Settings.AddonWatchFolder ?? "", saved, StringComparison.OrdinalIgnoreCase))
                return;

            if (!exists)
            {
                _crashReporter.Log($"[MainWindow.CommitAddonWatchFolderFromBoxAsync] Folder not found, not saved: '{typed}'");
                AddonWatchFolderBox.Text = saved;
                // Left Settings (e.g. Back to Games): revert quietly instead of popping a dialog over another page.
                if (SettingsPanel.Visibility != Visibility.Visible) return;
                var dialog = new ContentDialog
                {
                    Title = "Folder not found",
                    Content = $"\"{typed}\" doesn't exist. The addon watch folder was not changed.",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(dialog);
                return;
            }

            AddonWatchFolderBox.Text = typed;
            ViewModel.Settings.AddonWatchFolder = typed;
            _addonFileWatcher.SetWatchPath(typed);
            ViewModel.SaveSettingsPublic();
            _crashReporter.Log($"[MainWindow] Addon watch folder set to: {typed}");
        }
        finally
        {
            _addonWatchFolderCommitting = false;
        }
    }

    // ── Peak nits ─────────────────────────────────────────────────────────────

    private void PeakNitsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) TrySavePeakNitsFromBox(box);
    }

    /// <summary>
    /// Saves the typed peak nits if it is a whole number above 0 (same rule as Enter).
    /// Returns true when the value is valid.
    /// </summary>
    private bool TrySavePeakNitsFromBox(TextBox box)
    {
        if (!int.TryParse(box.Text, out var val) || val <= 0) return false;
        if (val == ViewModel.Settings.PeakNits && AuxInstallService.GlobalPeakNits == val) return true;

        ViewModel.Settings.PeakNits = val;
        AuxInstallService.GlobalPeakNits = val;
        ViewModel.SaveSettingsPublic();
        _crashReporter.Log($"[MainWindow.TrySavePeakNitsFromBox] Peak nits saved: {val}");
        return true;
    }
}
