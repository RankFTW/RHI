// DetailPanelBuilder.Experimental.ZeroHook.cs — Experimental › Overlays and Hooks row for ZeroHook (GS-ZeroHook/ZeroHook):
// a third-party tool that blocks overlay hooks and background bloat machine-wide and shows which DLLs a running game
// has loaded. System-wide: the row is the same for every game (nothing depends on the card). RHI downloads the author's
// release to a fixed path (%LocalAppData%\RHI\zerohook, ZeroHook's Auto-Run task points at it) and opens it; ZeroHook
// itself does the blocking. Files / updates / removal through ZeroHookService.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildZeroHookRow(GameCardViewModel card, StackPanel body)
    {
        var svc          = App.Services.GetRequiredService<IZeroHookService>();
        bool downloaded  = svc.IsDownloaded;
        bool pulled      = FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalZeroHook);
        string? version  = downloaded ? svc.InstalledVersion : null;

        string statusText  = svc.IsUpdating ? "Updating..."
                           : !downloaded ? "Not downloaded"
                           : version != null ? "v" + version : "Downloaded";
        string statusColor = svc.IsUpdating ? "#E8C46A" : downloaded ? "#5ECB7D" : "#A0AABB";

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExperimentalContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var label = new TextBlock
        {
            Text = "ZeroHook",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tip = "ZeroHook blocks overlay hooks (Steam, Discord, NVIDIA and AMD capture, RTSS and others) and background bloat "
            + "system-wide, and shows which DLLs are loaded in a running game.\n"
            + "Note: a third-party tool by GS, not part of RHI. It opens as administrator (a UAC prompt unless RHI runs in Admin Mode), "
            + "and antivirus may flag it because it's unsigned.\n"
            + "Note: its changes are machine-wide, for every game, and are undone with ZeroHook's own Restore buttons.\n"
            + "Note: blocking NVIDIA's capture DLL also turns off NVIDIA's overlay, Instant Replay and recording, and most likely "
            + "RTX HDR and game filters too (they need that overlay).";
        ToolTipService.SetToolTip(label, tip);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        if (downloaded) ToolTipService.SetToolTip(statusBlock, $"Downloaded to {svc.ExePath}. RHI keeps it up to date while ZeroHook isn't running.");
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        var infoBtn = new Button
        {
            Content = "Info", FontSize = 11, Padding = new Thickness(6, 2, 6, 2), Width = 36, Height = 32,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(infoBtn, "Open ZeroHook's GitHub page");
        infoBtn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ZeroHookService.RepoUrl));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Taken out by the manifest: only shown when downloaded, and only to be removed
        bool btnEnabled = !pulled;
        var mainBtn = new Button
        {
            Content = pulled ? "⚠  Not offered right now" : downloaded ? "Open" : "⬇  Download and open",
            FontSize = 12, Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8),
            Background  = downloaded ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground  = downloaded ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = downloaded ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            IsEnabled = btnEnabled,
            Opacity = btnEnabled ? 1.0 : 0.35,
        };
        if (pulled)
        {
            const string pulledText = "ZeroHook has been taken out of RHI for now. You can still remove the downloaded copy.";
            ToolTipService.SetToolTip(mainBtn, pulledText);
            ToolTipService.SetToolTip(label, $"{tip}\n\n{pulledText}"); // disabled buttons show no tooltip
        }
        else
            ToolTipService.SetToolTip(mainBtn, downloaded
                ? "Open ZeroHook (as administrator). Its blocks apply to the whole PC; undo them with its Restore buttons."
                : "Download ZeroHook from its author's GitHub release (checked against the release's checksum) and open it as administrator.");

        mainBtn.Click += async (_, _) =>
        {
            var xamlRoot = mainBtn.XamlRoot ?? _window.Content.XamlRoot;
            var oldContent = mainBtn.Content;
            try
            {
                mainBtn.IsEnabled = false;
                if (!svc.IsDownloaded)
                {
                    mainBtn.Content = "Downloading...";
                    // Created on the UI thread, so the reports come back on it
                    var progress = new Progress<double>(f => mainBtn.Content = $"Downloading {f:P0}");
                    bool ok = await Task.Run(() => svc.EnsureDownloadedAsync(progress));
                    if (!ok)
                    {
                        mainBtn.Content = oldContent;
                        mainBtn.IsEnabled = true;
                        await ShowZeroHookMessageAsync(xamlRoot, "ZeroHook wasn't downloaded",
                            svc.LastError ?? "Couldn't download ZeroHook. Try again later.");
                        return;
                    }
                }
                mainBtn.Content = "Opening...";
                var (result, error) = await Task.Run(() => svc.Launch());
                if ((result is ZeroHookLaunchResult.Failed or ZeroHookLaunchResult.NotDownloaded) && error != null)
                    await ShowZeroHookMessageAsync(xamlRoot, "ZeroHook didn't open", error!);
                // Give it a moment to start, then show "Running" / the new version
                if (result == ZeroHookLaunchResult.Started) await Task.Delay(1500);
                RequestExperimentalRebuild(card);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or HttpRequestException or TaskCanceledException or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildZeroHookRow] Download / open failed — {ex.Message}");
                mainBtn.Content = oldContent;
                mainBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(mainBtn, 3);
        row.Children.Add(mainBtn);

        // No ⚙: ZeroHook's settings live in ZeroHook (column 4 stays empty)

        var removeBtn = new Button
        {
            Width = 36, Height = 32, Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
            Opacity = downloaded ? 1.0 : 0,
            IsHitTestVisible = downloaded,
        };
        ToolTipService.SetToolTip(removeBtn, "Delete the downloaded ZeroHook. Turn its Auto-Run off and undo its blocks in ZeroHook first if you want them gone too.");
        removeBtn.Click += async (_, _) =>
        {
            var xamlRoot = removeBtn.XamlRoot ?? _window.Content.XamlRoot;
            try
            {
                var confirm = new ContentDialog
                {
                    Title = "Remove ZeroHook?",
                    Content = new TextBlock
                    {
                        Text = "This deletes the ZeroHook that RHI downloaded. It doesn't undo what ZeroHook blocked: use its Restore buttons first "
                             + "if you want those changes reversed.",
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                    },
                    PrimaryButtonText = "Remove",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = xamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                if (await DialogService.ShowSafeAsync(confirm) != ContentDialogResult.Primary) return;
                removeBtn.IsEnabled = false;
                var refusal = await Task.Run(() => svc.Remove());
                if (refusal != null)
                {
                    removeBtn.IsEnabled = true;
                    await ShowZeroHookMessageAsync(xamlRoot, "ZeroHook wasn't removed", refusal);
                    return;
                }
                RequestExperimentalRebuild(card);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildZeroHookRow] Remove failed — {ex.Message}");
                removeBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);

        // "Running" needs a process scan: done off the UI thread every 2 s while the row is on screen, and the status
        // updated in place both ways (ZeroHook opened late behind its UAC prompt, or closed). The same TextBlock is
        // moved into the Tabs / Spotlight pill and Spotlight's slide-in panel, so this works in every layout.
        // Not Loaded / Unloaded: when Spotlight moves the section, the old place's Unloaded can arrive after the new
        // place's Loaded, which stopped the watch while the row was still shown (status stuck on "Running").
        if (downloaded && !svc.IsUpdating)
            _ = WatchZeroHookRunningAsync(svc, statusBlock, statusText);
    }

    /// <summary>
    /// Keeps the row's status at "Running" / its version while it's in the window (its parent chain reaches the
    /// window's content; checked live, not from Loaded / Unloaded events or XamlRoot, which can stay set). Stops once the
    /// TextBlock has been out of the window for 3 checks in a row (about 6 s: a rebuilt page made a new row, or
    /// the panel closed), which also covers the moment before it's first shown. Runs on the UI thread between scans.
    /// </summary>
    private async Task WatchZeroHookRunningAsync(IZeroHookService svc, TextBlock statusBlock, string idleText)
    {
        var token = _window.LifetimeToken;
        int away = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsInWindow(statusBlock))
                {
                    if (++away >= 3)
                    {
                        if (CrashReporter.VerboseLogging) CrashReporter.Log("[BuildZeroHookRow] Running watch stopped (row no longer shown)");
                        break;
                    }
                }
                else
                {
                    away = 0;
                    bool running = await Task.Run(svc.IsRunning);
                    if (token.IsCancellationRequested) break;
                    var text = running ? "Running" : idleText;
                    if (statusBlock.Text != text) statusBlock.Text = text;
                }
                await Task.Delay(2000, token);
            }
        }
        catch (OperationCanceledException) { /* RHI closing */ }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CrashReporter.Log($"[BuildZeroHookRow] Running check failed — {ex.Message}");
        }
    }

    /// <summary>True when <paramref name="el"/>'s parents lead up to the window's content right now.</summary>
    private bool IsInWindow(DependencyObject el)
    {
        var root = _window.Content;
        if (root == null) return false;
        for (var cur = el; cur != null; cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, root)) return true;
        return false;
    }

    private static async Task ShowZeroHookMessageAsync(XamlRoot? xamlRoot, string title, string text)
    {
        if (xamlRoot == null) return;
        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }
}
