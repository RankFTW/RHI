using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

// Update dialogs, patch notes, DC removal warning, and legacy cleanup workflows.
public partial class DialogService
{
    // ── Auto-Update Dialogs ─────────────────────────────────────────────────────

    public async Task CheckForAppUpdateAsync()
    {
        try
        {
            if (ViewModel.SkipUpdateCheck)
            {
                CrashReporter.Log("[DialogService.CheckForAppUpdateAsync] Update check skipped (disabled in settings)");
                return;
            }

            // Wait until the XamlRoot is available (window needs to be fully loaded for dialogs)
            while (_window.Content.XamlRoot == null)
                await Task.Delay(200);

            var updateInfo = await ViewModel.UpdateServiceInstance.CheckForUpdateAsync(ViewModel.BetaOptIn);
            if (updateInfo == null) return; // up to date or check failed

            // Show update dialog on UI thread
            _dispatcherQueue.TryEnqueue(async () =>
            {
                await ShowUpdateDialogAsync(updateInfo);
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.CheckForAppUpdateAsync] Update check error — {ex.Message}");
        }
    }

    public async Task ShowUpdateDialogAsync(UpdateInfo updateInfo)
    {
        var dlg = new ContentDialog
        {
            Title   = "🔄 Update Available",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground   = Brush(ResourceKeys.TextSecondaryBrush),
                        FontSize     = 14,
                        Text         = $"A new version of RHI is available!\n\n" +
                                       $"Installed:  v{updateInfo.CurrentVersion}\n" +
                                       $"Available:  v{updateInfo.DisplayVersion ?? updateInfo.RemoteVersion.ToString()}\n\n" +
                                       "Would you like to update now?",
                    },
                },
            },
            PrimaryButtonText   = "Update Now",
            CloseButtonText     = "Later",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceRaisedBrush),
            RequestedTheme      = ElementTheme.Dark,
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return; // user chose "Later"

        // User chose "Update Now" — show downloading dialog
        await DownloadAndInstallUpdateAsync(updateInfo);
    }

    public async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
    {
        // Create a non-dismissable progress dialog
        var progressText = new TextBlock
        {
            Text         = "Starting download...",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize     = 13,
        };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value   = 0,
            Height  = 6,
            IsIndeterminate = false,
        };
        var downloadDlg = new ContentDialog
        {
            Title   = "⬇ Downloading Update",
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { progressText, progressBar },
            },
            XamlRoot   = _window.Content.XamlRoot,
            Background = Brush(ResourceKeys.SurfaceRaisedBrush),
            RequestedTheme = ElementTheme.Dark,
            // No buttons — dialog will be closed programmatically when download completes
        };

        // Show dialog non-blocking
        var dialogTask = downloadDlg.ShowAsync();

        var progress = new Progress<(string msg, double pct)>(p =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                progressText.Text = p.msg;
                progressBar.Value = p.pct;
            });
        });

        var installerPath = await ViewModel.UpdateServiceInstance.DownloadInstallerAsync(
            updateInfo.DownloadUrl, progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            // Download failed — update dialog to show error with a Close button
            _dispatcherQueue.TryEnqueue(() =>
            {
                progressText.Text = "❌ Download failed. Please try again later or download manually from GitHub.";
                progressBar.Value = 0;
                downloadDlg.CloseButtonText = "Close";
            });
            return;
        }

        // Close the progress dialog
        downloadDlg.Hide();

        // Launch installer and close RDXC
        ViewModel.UpdateServiceInstance.LaunchInstallerAndExit(installerPath, () =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _window.Close();
            });
        });
    }

    // ── Patch Notes Dialogs ─────────────────────────────────────────────────────

    public async Task ShowPatchNotesIfNewVersionAsync()
    {
        try
        {
            // Wait until XamlRoot is ready
            while (_window.Content.XamlRoot == null)
                await Task.Delay(200);

            // Wait for UI to settle and any update dialog to finish
            await Task.Delay(1500);

            var current = ViewModel.UpdateServiceInstance.CurrentVersion;
            var versionStr = $"{current.Major}.{current.Minor}.{current.Build}";
            var markerFile = Path.Combine(PatchNotesDir, $"PatchNotes-{versionStr}.txt");

            // Clean up markers from older versions
            try
            {
                Directory.CreateDirectory(PatchNotesDir);
                foreach (var old in Directory.EnumerateFiles(PatchNotesDir, "PatchNotes-*.txt"))
                {
                    if (!old.Equals(markerFile, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(old); } catch (Exception ex) { CrashReporter.Log($"[DialogService.ShowPatchNotesIfNewVersionAsync] Failed to delete old marker '{old}' — {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DialogService.ShowPatchNotesIfNewVersionAsync] Failed to clean up old patch note markers — {ex.Message}"); }

            // If marker exists, this version's notes have already been shown
            if (File.Exists(markerFile)) return;

            // Write the marker file FIRST — ensures we never show again
            try
            {
                Directory.CreateDirectory(PatchNotesDir);
                File.WriteAllText(markerFile, $"Patch notes shown for v{versionStr}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DialogService.ShowPatchNotesIfNewVersionAsync] Failed to write patch notes marker — {ex.Message}");
            }

            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ShowPatchNotesDialogAsync();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DialogService.ShowPatchNotesIfNewVersionAsync] Patch notes dialog failed — {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowPatchNotesIfNewVersionAsync] Patch notes check error — {ex.Message}");
        }
    }

    public async Task ShowPatchNotesDialogAsync()
    {
        var notes = MainViewModel.GetRecentPatchNotes(3);

        var headingBrush = Brush(ResourceKeys.TextPrimaryBrush);
        var markdown = new CommunityToolkit.WinUI.Controls.MarkdownTextBlock
        {
            Text = notes,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
            UseEmphasisExtras = true,
            UseListExtras = true,
            UseTaskLists = true,
        };

        // Wrap in a Grid with explicit dark theme to force heading colors
        var markdownContainer = new Grid
        {
            RequestedTheme = ElementTheme.Dark,
        };
        markdownContainer.Children.Add(markdown);

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = markdownContainer,
        };

        var dlg = new ContentDialog
        {
            Title              = "📋 Patch Notes — What's New",
            Content            = scrollViewer,
            CloseButtonText    = "Close",
            XamlRoot           = _window.Content.XamlRoot,
            Background         = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme     = ElementTheme.Dark,
        };

        await dlg.ShowAsync();
    }
}
