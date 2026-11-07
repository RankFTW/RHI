using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

// Update dialogs, patch notes, DC removal warning, and legacy cleanup workflows.
public partial class DialogService
{
    private bool _checkingForAppUpdate;

    internal async Task ShowStartupDialogsAsync()
    {
        await ShowPatchNotesIfNewVersionAsync();
        if (_window.IsShuttingDown) return;
        await ShowMotdIfNewAsync();
        if (_window.IsShuttingDown) return;
        await CheckForAppUpdateAsync();
    }

    private async Task WaitForXamlRootAsync()
    {
        while (_window.Content.XamlRoot == null)
            await Task.Delay(200, _window.LifetimeToken);
        _window.LifetimeToken.ThrowIfCancellationRequested();
    }

    // ── Auto-Update Dialogs ─────────────────────────────────────────────────────

    public async Task CheckForAppUpdateAsync()
    {
        if (_checkingForAppUpdate || _window.IsShuttingDown) return;
        _checkingForAppUpdate = true;
        try
        {
            if (ViewModel.SkipUpdateCheck)
            {
                CrashReporter.Log("[DialogService.CheckForAppUpdateAsync] Update check skipped (disabled in settings)");
                return;
            }

            // Wait until the XamlRoot is available (window needs to be fully loaded for dialogs)
            await WaitForXamlRootAsync();

            var updateInfo = await _updateService.CheckForUpdateAsync(ViewModel.BetaOptIn);
            if (updateInfo == null) return; // up to date or check failed

            if (!_window.IsShuttingDown) await ShowUpdateDialogAsync(updateInfo);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.CheckForAppUpdateAsync] Update check error — {ex.Message}");
        }
        finally { _checkingForAppUpdate = false; }
    }

    /// <summary>
    /// Checks for an app update and returns the result — null means up to date or check failed.
    /// Used by the version button to show a "no update" dialog when appropriate.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAndReturnAsync(bool betaOptIn)
    {
        try
        {
            await WaitForXamlRootAsync();
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

        await using var progressSession = await ShowProgressAsync(downloadDlg);
        if (progressSession == null) return;

        var progress = new Progress<(string msg, double pct)>(p =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_window.IsShuttingDown) return;
                progressText.Text = p.msg;
                progressBar.Value = p.pct;
            });
        });

        var installerPath = await _updateService.DownloadInstallerAsync(
            updateInfo.DownloadUrl, progress, _window.LifetimeToken);
        if (_window.IsShuttingDown) return;

        if (string.IsNullOrEmpty(installerPath))
        {
            // Download failed — update dialog to show error with a Close button
            progressText.Text = "❌ Download failed. Please try again later or download manually from GitHub.";
            progressBar.Value = 0;
            downloadDlg.CloseButtonText = "Close";
            await progressSession.Completion;
            return;
        }

        // Close the progress dialog
        await progressSession.DisposeAsync();

        // Launch installer and close RDXC
        if (!_window.IsShuttingDown)
            _updateService.LaunchInstallerAndExit(installerPath, _window.RequestExit);
    }

    // ── Patch Notes Dialogs ─────────────────────────────────────────────────────

    public async Task ShowPatchNotesIfNewVersionAsync()
    {
        try
        {
            // Wait until XamlRoot is ready
            await WaitForXamlRootAsync();

            // Wait for UI to settle and any update dialog to finish
            await Task.Delay(1500, _window.LifetimeToken);

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

            if (!_window.IsShuttingDown) await ShowPatchNotesDialogAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowPatchNotesIfNewVersionAsync] Patch notes check error — {ex.Message}");
        }
    }

    public async Task ShowPatchNotesDialogAsync()
    {
        var (preamble, sections) = MainViewModel.GetPatchNoteSections();
        const double bodyHeight = 480;

        var markdown = new CommunityToolkit.WinUI.Controls.MarkdownTextBlock
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = TabsTheme.B(TabsTheme.TextControl),
            FontSize = 12,
            UseEmphasisExtras = true,
            UseListExtras = true,
            UseTaskLists = true,
        };

        TabsTheme.StyleMarkdown(markdown);
        var markdownContainer = new Grid { RequestedTheme = ElementTheme.Dark };
        markdownContainer.Children.Add(markdown);

        var scrollViewer = new ScrollViewer
        {
            Height = bodyHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = markdownContainer,
            Padding = new Thickness(0, 0, 16, 0),
        };
        var card = TabsTheme.MakeCard(scrollViewer, 0);
        card.Padding = new Thickness(20, 16, 4, 16);
        card.Width = 680;

        // Version list down the left: click one to show its notes. One version at a time keeps
        // the Markdown small (the file has dozens of versions). The search box above narrows the
        // list to versions that mention the text and shows every matching line on the right.
        var navList = new StackPanel { Spacing = 2 };
        var navItems = new List<(Border Item, TextBlock Label, TextBlock Count, Button Button)>();
        int selected = -1;

        void Highlight(int index)
        {
            selected = index;
            for (int i = 0; i < navItems.Count; i++)
            {
                bool on = i == index;
                // A controller starts on the selected version when the window opens (MainWindow.Controller.Popups)
                navItems[i].Button.Tag = on ? "PadStart" : null;
                navItems[i].Item.Background = on ? TabsTheme.B("#1A2336") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                navItems[i].Item.BorderBrush = on ? TabsTheme.B("#2A3D63") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                navItems[i].Label.Foreground = TabsTheme.B(on ? "#A9C6FF" : TabsTheme.TextSecondary);
                navItems[i].Label.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
        }

        void ShowVersion(int index)
        {
            if (index < 0 || index >= sections.Count) return;
            Highlight(index);
            var body = sections[index].Body;
            // The GitHub token tip stays above the newest version only
            markdown.Text = index == 0 && preamble.Length > 0 ? preamble + "\n\n---\n\n" + body : body;
            scrollViewer.ChangeView(null, 0, null, true);
        }

        // Lines of a version that contain the search text, with the ### heading they sit under
        static List<string> MatchingLines(string body, string query)
        {
            var result = new List<string>();
            string? heading = null;
            bool headingShown = false;
            foreach (var raw in body.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("## ")) continue;
                if (trimmed.StartsWith("### ")) { heading = trimmed[4..].Trim(); headingShown = false; continue; }
                if (trimmed.Length == 0 || !trimmed.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                if (heading != null && !headingShown) { result.Add($"**{heading}**"); headingShown = true; }
                result.Add("- " + trimmed.TrimStart('-', '*', ' '));
            }
            return result;
        }

        void ApplySearch(string query)
        {
            query = query.Trim();
            if (query.Length == 0)
            {
                foreach (var n in navItems) { n.Button.Visibility = Visibility.Visible; n.Count.Text = ""; }
                ShowVersion(selected >= 0 ? selected : 0);
                return;
            }

            var sb = new System.Text.StringBuilder();
            int versionsHit = 0, linesHit = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                var lines = MatchingLines(sections[i].Body, query);
                navItems[i].Button.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                int hits = lines.Count(l => l.StartsWith("- "));
                navItems[i].Count.Text = hits > 0 ? hits.ToString() : "";
                if (lines.Count == 0) continue;
                versionsHit++;
                linesHit += hits;
                if (linesHit <= 400) // keep the results page a sensible size
                {
                    sb.Append("## ").Append(sections[i].Version).Append("\n\n");
                    foreach (var l in lines) sb.Append(l).Append("\n\n");
                }
            }
            Highlight(-1);
            markdown.Text = versionsHit == 0
                ? $"Nothing in the patch notes mentions \"{query}\"."
                : $"**{linesHit}** matching line{(linesHit == 1 ? "" : "s")} in **{versionsHit}** version{(versionsHit == 1 ? "" : "s")}. Click a version on the left to read all of it.\n\n" + sb;
            scrollViewer.ChangeView(null, 0, null, true);
        }

        for (int i = 0; i < sections.Count; i++)
        {
            var label = new TextBlock
            {
                Text = sections[i].Version,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var count = new TextBlock
            {
                FontSize = 11,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(label);
            Grid.SetColumn(count, 1);
            row.Children.Add(count);
            var item = new Border
            {
                Height = 32,
                Padding = new Thickness(12, 0, 8, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = row,
            };
            int idx = i;
            TabsTheme.UseHandCursor(item);
            item.PointerEntered += (s, e) => { if (idx != selected) item.Background = TabsTheme.B(TabsTheme.RowHover); };
            item.PointerExited += (s, e) => { if (idx != selected) item.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); };
            // A see-through Button around the row so a controller (or the keyboard) can focus it;
            // moving onto a version with either shows it, a click shows it as before
            var button = new Button
            {
                Content = item,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                CornerRadius = item.CornerRadius,
            };
            var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
                                        "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
                TabsTheme.SetResource(button.Resources, key, clear);
            button.Click += (s, e) => { if (idx != selected) ShowVersion(idx); };
            button.GotFocus += (s, e) =>
            {
                if (button.FocusState == FocusState.Keyboard && idx != selected) ShowVersion(idx);
            };
            navItems.Add((item, label, count, button));
            navList.Children.Add(button);
        }

        var navHeading = TabsTheme.SectionHeading("Versions");
        navHeading.FontSize = 11;
        navHeading.Margin = new Thickness(12, 2, 0, 8);
        var navPanel = new Grid { Width = 150, Height = bodyHeight + 34 };
        navPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        navPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        navPanel.Children.Add(navHeading);
        var navScroll = new ScrollViewer
        {
            Content = navList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 10, 0),
        };
        Grid.SetRow(navScroll, 1);
        navPanel.Children.Add(navScroll);

        // Search box over both columns: 250 ms after typing stops, Esc clears
        var search = new TextBox { PlaceholderText = "Search patch notes (e.g. OptiScaler, crash, Vulkan)" };
        TabsTheme.StyleInput(search);
        var searchTimer = search.DispatcherQueue.CreateTimer();
        searchTimer.Interval = TimeSpan.FromMilliseconds(250);
        searchTimer.IsRepeating = false;
        searchTimer.Tick += (t, o) => ApplySearch(search.Text);
        search.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
        search.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && search.Text.Length > 0)
            {
                search.Text = "";
                e.Handled = true; // clear the search, don't close the dialog
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                searchTimer.Stop();
                ApplySearch(search.Text);
                e.Handled = true;
            }
        };

        var layout = new Grid { ColumnSpacing = 14, RowSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(search);
        Grid.SetColumnSpan(search, 2);
        Grid.SetRow(navPanel, 1);
        layout.Children.Add(navPanel);
        Grid.SetRow(card, 1);
        Grid.SetColumn(card, 1);
        layout.Children.Add(card);

        if (sections.Count > 0) ShowVersion(0);
        else markdown.Text = "No patch notes available.";

        var dlg = new ContentDialog
        {
            Title              = CogDialog.MakeDialogTitle("What's New",
                                     "Patch notes for every RHI version. Pick one on the left, or search them all."),
            Content            = sections.Count > 1 ? layout : card,
            CloseButtonText    = "Close",
            XamlRoot           = _window.Content.XamlRoot,
            RequestedTheme     = ElementTheme.Dark,
        };
        CogDialog.ApplyDialogLook(dlg, 920);

        await DialogService.ShowSafeAsync(dlg);
    }

    // ── MOTD Dialog ─────────────────────────────────────────────────────────────

    public async Task ShowMotdIfNewAsync()
    {
        try
        {
            // Wait until XamlRoot is ready
            await WaitForXamlRootAsync();

            // Wait for UI to settle and other startup dialogs to finish
            await Task.Delay(2000, _window.LifetimeToken);

            var motd = await Services.MotdService.CheckAsync(ViewModel.HttpClient);
            if (motd == null) return;

            if (!_window.IsShuttingDown) await ShowMotdContentAsync(motd);
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
        // Markdown, so [text](url) and bare URLs are clickable. A single line break stays a line
        // break (Markdown would join the lines), so plain-text messages look as they always did.
        var text = string.Join("\n", motd.Replace("\r", "").Split('\n')
            .Select(l => l.Length == 0 ? l : l.TrimEnd() + "  "));
        var markdown = new CommunityToolkit.WinUI.Controls.MarkdownTextBlock
        {
            Text = text,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 13,
            UseEmphasisExtras = true,
            UseListExtras = true,
            UseAutoLinks = true,
        };
        markdown.Foreground = TabsTheme.B(TabsTheme.TextControl);
        TabsTheme.StyleMarkdown(markdown);
        var markdownContainer = new Grid { RequestedTheme = ElementTheme.Dark };
        markdownContainer.Children.Add(markdown);

        // The message in a card with a blue accent bar down its left edge
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = TabsTheme.B(TabsTheme.Accent),
        };
        var scroll = new ScrollViewer
        {
            Content = markdownContainer,
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 0),
        };
        var inner = new Grid { ColumnSpacing = 14 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.Children.Add(bar);
        Grid.SetColumn(scroll, 1);
        inner.Children.Add(scroll);
        var card = TabsTheme.MakeCard(inner, 0);
        card.Padding = new Thickness(16, 16, 4, 16);
        card.Width = 520;

        var dlg = new ContentDialog
        {
            Title = CogDialog.MakeDialogTitle("Message from RHI", "News and notices from RankFTW"),
            Content = card,
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        CogDialog.ApplyDialogLook(dlg, 620);
        await DialogService.ShowSafeAsync(dlg);
    }
}
