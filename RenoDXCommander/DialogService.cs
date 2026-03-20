using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Encapsulates all dialog construction and async update/patch-notes workflows.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class DialogService
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;

    private static readonly string PatchNotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander");

    public DialogService(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
    }

    private MainViewModel ViewModel => _window.ViewModel;

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    /// <summary>Parses a hex colour string like "#1C2848" into a Windows.UI.Color.</summary>
    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        int offset = 0;
        if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); offset = 2; }
        byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
        byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    // ── Foreign DLL Confirmation Dialogs ────────────────────────────────────────

    public async Task<bool> ShowForeignDxgiConfirmDialogAsync(GameCardViewModel card, string dxgiPath)
    {
        var fileSize = new System.IO.FileInfo(dxgiPath).Length;
        var sizeKB   = fileSize / 1024.0;

        var dlg = new ContentDialog
        {
            Title               = "⚠ Unknown dxgi.dll Detected",
            Content             = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground   = Brush(ResourceKeys.AccentAmberBrush),
                FontSize     = 13,
                Text         = $"A dxgi.dll file was found in:\n{card.InstallPath}\n\n" +
                               $"File size: {sizeKB:N0} KB\n\n" +
                               "RDXC cannot identify this file as ReShade or Display Commander. " +
                               "It may belong to another mod (e.g. DXVK, Special K, ENB).\n\n" +
                               "Overwriting it may break the existing mod. Do you want to proceed?",
            },
            PrimaryButtonText   = "Overwrite",
            CloseButtonText     = "Cancel",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task<bool> ShowForeignWinmmConfirmDialogAsync(GameCardViewModel card, string winmmPath)
    {
        var fileSize = new System.IO.FileInfo(winmmPath).Length;
        var sizeKB   = fileSize / 1024.0;

        var dlg = new ContentDialog
        {
            Title               = "⚠ Unknown winmm.dll Detected",
            Content             = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground   = Brush(ResourceKeys.AccentAmberBrush),
                FontSize     = 13,
                Text         = $"A winmm.dll file was found in:\n{card.InstallPath}\n\n" +
                               $"File size: {sizeKB:N0} KB\n\n" +
                               "RDXC cannot identify this file as Display Commander. " +
                               "It may belong to another mod or DLL injector.\n\n" +
                               "Overwriting it may break the existing mod. Do you want to proceed?",
            },
            PrimaryButtonText   = "Overwrite",
            CloseButtonText     = "Cancel",
            XamlRoot            = _window.Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

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
                        Text         = $"A new version of RDXC is available!\n\n" +
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

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = markdown,
        };

        var dlg = new ContentDialog
        {
            Title              = "📋 Patch Notes — What's New",
            Content            = scrollViewer,
            CloseButtonText    = "Close",
            XamlRoot           = _window.Content.XamlRoot,
            Background         = Brush(ResourceKeys.SurfaceToolbarBrush),
        };

        await dlg.ShowAsync();
    }

    // ── Game Notes Dialog ────────────────────────────────────────────────────────

    public async void NotesButton_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;

        var textColour = Brush(ResourceKeys.TextSecondaryBrush);
        var linkColour = Brush(ResourceKeys.AccentBlueBrush);
        var dimColour  = Brush(ResourceKeys.TextTertiaryBrush);

        var outerPanel = new StackPanel { Spacing = 10 };

        // ── Wiki status badge at top-left ─────────────────────────────────────────
        var statusBg     = card.WikiStatusBadgeBackground;
        var statusBorder = card.WikiStatusBadgeBorderBrush;
        var statusFg     = card.WikiStatusBadgeForeground;
        var statusBadge = new Border
        {
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background      = new SolidColorBrush(ParseColor(statusBg)),
            BorderBrush     = new SolidColorBrush(ParseColor(statusBorder)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text       = card.WikiStatusLabel,
                FontSize   = 12,
                Foreground = new SolidColorBrush(ParseColor(statusFg)),
            }
        };
        outerPanel.Children.Add(statusBadge);

        // ── Luma info (when in Luma mode) ───────────────────────────────────────────
        if (card.IsLumaMode && (card.LumaMod != null || !string.IsNullOrWhiteSpace(card.LumaNotes)))
        {
            var lumaLabel = card.LumaMod != null
                ? $"Luma — {card.LumaMod.Status} {card.LumaMod.Author}"
                : "Luma mode";
            var lumaBadge = new Border
            {
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background      = Brush(ResourceKeys.AccentGreenBgBrush),
                BorderBrush     = Brush(ResourceKeys.AccentGreenBorderBrush),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = lumaLabel,
                    FontSize   = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                }
            };
            outerPanel.Children.Add(lumaBadge);

            var lumaNotesText = "";
            if (card.LumaMod != null)
            {
                if (!string.IsNullOrWhiteSpace(card.LumaMod.SpecialNotes))
                    lumaNotesText += card.LumaMod.SpecialNotes;
                if (!string.IsNullOrWhiteSpace(card.LumaMod.FeatureNotes))
                {
                    if (lumaNotesText.Length > 0) lumaNotesText += "\n\n";
                    lumaNotesText += card.LumaMod.FeatureNotes;
                }
            }

            if (!string.IsNullOrWhiteSpace(lumaNotesText))
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text         = lumaNotesText,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }

            // ── Manifest Luma notes (supplement wiki notes) ──────────────────────
            if (!string.IsNullOrWhiteSpace(card.LumaNotes))
            {
                if (!string.IsNullOrEmpty(card.LumaNotesUrl))
                {
                    var para = new Microsoft.UI.Xaml.Documents.Paragraph();
                    para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text       = card.LumaNotes,
                        Foreground = textColour,
                        FontSize   = 13,
                    });
                    para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                    var link = new Microsoft.UI.Xaml.Documents.Hyperlink
                    {
                        NavigateUri = new Uri(card.LumaNotesUrl),
                        Foreground  = linkColour,
                    };
                    link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text     = card.LumaNotesUrlLabel ?? card.LumaNotesUrl,
                        FontSize = 13,
                    });
                    para.Inlines.Add(link);
                    var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
                    rtb.Blocks.Add(para);
                    outerPanel.Children.Add(rtb);
                }
                else
                {
                    outerPanel.Children.Add(new TextBlock
                    {
                        Text         = card.LumaNotes,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground   = textColour,
                        FontSize     = 13,
                        LineHeight   = 22,
                    });
                }
            }

            // Fallback if neither wiki nor manifest provided notes
            if (string.IsNullOrWhiteSpace(lumaNotesText) && string.IsNullOrWhiteSpace(card.LumaNotes))
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text       = "No additional Luma notes for this game.",
                    Foreground = dimColour,
                    FontSize   = 13,
                });
            }
        }
        // ── Standard RenoDX notes ───────────────────────────────────────────────────
        else if (!string.IsNullOrWhiteSpace(card.Notes))
        {
            if (!string.IsNullOrEmpty(card.NotesUrl))
            {
                var para = new Microsoft.UI.Xaml.Documents.Paragraph();
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text       = card.Notes,
                    Foreground = textColour,
                    FontSize   = 13,
                });
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                var link = new Microsoft.UI.Xaml.Documents.Hyperlink
                {
                    NavigateUri = new Uri(card.NotesUrl),
                    Foreground  = linkColour,
                };
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text     = card.NotesUrlLabel ?? card.NotesUrl,
                    FontSize = 13,
                });
                para.Inlines.Add(link);

                var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
                rtb.Blocks.Add(para);
                outerPanel.Children.Add(rtb);
            }
            else
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text         = card.Notes,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }
        }
        else
        {
            outerPanel.Children.Add(new TextBlock
            {
                Text       = "No additional notes for this game.",
                Foreground = dimColour,
                FontSize   = 13,
            });
        }

        var scrollContent = new ScrollViewer
        {
            Content   = outerPanel,
            MaxHeight = 440,
            Padding   = new Thickness(0, 4, 12, 0),
        };

        var dialog = new ContentDialog
        {
            Title           = $"ℹ  {card.GameName}",
            Content         = scrollContent,
            CloseButtonText = "Close",
            XamlRoot        = _window.Content.XamlRoot,
            Background      = Brush(ResourceKeys.SurfaceToolbarBrush),
        };
        await dialog.ShowAsync();
    }

    // ── UE Extended Warning Dialog ──────────────────────────────────────────────

    public async Task ShowUeExtendedWarningAsync(GameCardViewModel card)
    {
        try
        {
            while (_window.Content.XamlRoot == null)
                await Task.Delay(100);

            var hasNotes = !string.IsNullOrWhiteSpace(card.Notes);
            var notesHint = hasNotes
                ? "\n\nCheck the Notes section for any additional compatibility information for this game."
                : "\n\nNo specific notes are available for this game — check the RDXC Discord for community reports.";

            var dlg = new ContentDialog
            {
                Title               = "⚠ UE-Extended Compatibility Warning",
                Content             = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize     = 13,
                    Text         = "Not all Unreal Engine games are compatible with UE-Extended.\n\n" +
                                   "UE-Extended uses a different injection method that works better " +
                                   "with some games but may cause crashes or issues with others." +
                                   notesHint,
                },
                PrimaryButtonText   = "OK, I understand",
                XamlRoot            = _window.Content.XamlRoot,
                Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
            };

            await dlg.ShowAsync();
        }
        catch (Exception ex) { CrashReporter.Log($"[DialogService.ShowUeExtendedWarningAsync] Failed to show UE warning for '{card.GameName}' — {ex.Message}"); }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static GameCardViewModel? GetCardFromSender(object sender) => sender switch
    {
        Button btn          when btn.Tag  is GameCardViewModel c => c,
        MenuFlyoutItem item when item.Tag is GameCardViewModel c => c,
        _ => null
    };
}
