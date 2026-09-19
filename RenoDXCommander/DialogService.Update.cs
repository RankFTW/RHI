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

            var updateInfo = await _updateService.CheckForUpdateAsync(ViewModel.BetaOptIn);
            if (updateInfo == null) return; // up to date or check failed

            // Marshal back to the UI thread to show the dialog.
            // IMPORTANT: do NOT use TryEnqueue(async () => await ShowUpdateDialogAsync(...)) —
            // that blocks the dispatcher queue thread for up to 10s waiting for the dialog gate.
            // Instead, enqueue a non-async action that fires a new Task on the UI thread.
            // The Task runs as an async continuation without ever occupying the queue dispatch slot.
            _dispatcherQueue.TryEnqueue(() => _ = ShowUpdateDialogAsync(updateInfo));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.CheckForAppUpdateAsync] Update check error — {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for an app update and returns the result — null means up to date or check failed.
    /// Used by the version button to show a "no update" dialog when appropriate.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAndReturnAsync(bool betaOptIn)
    {
        try
        {
            while (_window.Content.XamlRoot == null)
                await Task.Delay(200);
            return await _updateService.CheckForUpdateAsync(betaOptIn);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.CheckForUpdateAndReturnAsync] Error — {ex.Message}");
            return null;
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

        var result = await DialogService.ShowSafeAsync(dlg);
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

        // Show dialog non-blocking — wait up to 15s for any concurrently-showing dialog
        // (e.g. MOTD) to finish. Using TryAcquireDialogGate (zero timeout) here would
        // silently abort the update if the MOTD dialog happened to open first.
        if (!await DialogService.WaitDialogGateAsync(15))
        {
            CrashReporter.Log("[DialogService.Update] Timed out waiting for dialog gate — proceeding with update download without progress dialog");
            // Still launch the download even if we can't show the progress UI
            var installerPathFallback = await _updateService.DownloadInstallerAsync(updateInfo.DownloadUrl, null);
            if (!string.IsNullOrEmpty(installerPathFallback))
                _updateService.LaunchInstallerAndExit(installerPathFallback, () => _dispatcherQueue.TryEnqueue(() => _window.Close()));
            return;
        }
        bool gateReleased = false;
        downloadDlg.Closed += (_, _) => { if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); } };
        var dialogTask = downloadDlg.ShowAsync();

        var progress = new Progress<(string msg, double pct)>(p =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                progressText.Text = p.msg;
                progressBar.Value = p.pct;
            });
        });

        var installerPath = await _updateService.DownloadInstallerAsync(
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
        if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); }

        // Launch installer and close RDXC
        _updateService.LaunchInstallerAndExit(installerPath, () =>
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

            var current = _updateService.CurrentVersion;
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

            _dispatcherQueue.TryEnqueue(() => _ = ShowPatchNotesDialogAsync());
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
            Padding = new Thickness(0, 0, 16, 0),
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

        await DialogService.ShowSafeAsync(dlg);
    }

    // ── MOTD Dialog ─────────────────────────────────────────────────────────────

    public async Task ShowMotdIfNewAsync()
    {
        try
        {
            // Wait until XamlRoot is ready
            while (_window.Content.XamlRoot == null)
                await Task.Delay(200);

            // Wait for UI to settle and other startup dialogs to finish
            await Task.Delay(2000);

            var motd = await Services.MotdService.CheckAsync(ViewModel.HttpClient);
            if (motd == null) return;

            _dispatcherQueue.TryEnqueue(() => _ = ShowMotdContentAsync(motd));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowMotdIfNewAsync] MOTD check error — {ex.Message}");
        }
    }

    public async Task ShowMotdDialogAsync()
    {
        try
        {
            var motd = await Services.MotdService.FetchAsync(ViewModel.HttpClient);
            if (motd == null) return;
            await ShowMotdContentAsync(motd);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowMotdDialogAsync] Failed — {ex.Message}");
        }
    }

    private async Task ShowMotdContentAsync(string motd)
    {
        var dlg = new ContentDialog
        {
            Title = "📢 Message from RHI",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = motd,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
                MaxHeight = 400,
            },
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dlg);
    }
}
