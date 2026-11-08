// OverlayWindow.Screenshots.cs -- the RHI overlay's Screenshots page: the game's ReShade screenshots
// (ScreenshotLibraryService) as a grid of thumbnails, newest first, and a large view with previous / next,
// copy, delete (to the Recycle Bin) and open. New screenshots appear while the page is open.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Overlay;

internal sealed partial class OverlayWindow
{
    private const int ShotPageSize = 24;
    private const double ThumbMinWidth = 300;   // thumbnails grow to fill the column from this width
    private double _thumbAspect = 9.0 / 16;     // height / width: the game monitor's shape (screenshots match it)
    private string? _shotFolder;
    private List<FileInfo> _shots = new();
    private int _shotsShown;
    private int _viewerIndex = -1;           // >= 0 while the large view is open
    private bool _shotsChanged;              // screenshots changed while the viewer was open: redraw the grid on the way out
    private FileSystemWatcher? _shotWatcher;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _shotRefreshTimer;

    private bool ViewerOpen => _viewerIndex >= 0;

    private void BuildScreenshotsPanel(GameCardViewModel card)
    {
        _viewerIndex = -1;
        var rhiFolder = _screenshotFolder(card.GameName);
        _shotFolder = ScreenshotLibraryService.FindFolder(card.InstallPath, rhiFolder);
        if (_shotFolder == null)
        {
            _shotBox.Children.Add(MakeCard("Screenshots", SmallText(
                "No screenshot folder found for this game. Take a screenshot with ReShade (Print Screen by default), "
              + "or set a folder in RHI's Settings › ReShade › Screenshot folder.")));
            StopShotWatcher();
            return;
        }

        bool own = (_settings().PerGameScreenshotFolders && rhiFolder != null && SamePath(_shotFolder, rhiFolder))
                   || SamePath(_shotFolder, card.InstallPath);
        _shots = ScreenshotLibraryService.List(_shotFolder, _game?.ExePath, own);
        _shotsShown = 0;
        StartShotWatcher(_shotFolder);
        RenderShotGrid();
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try { return Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private void RenderShotGrid()
    {
        _shotBox.Children.Clear();
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(BigText(_shots.Count == 0 ? "No screenshots yet"
            : _shots.Count == 1 ? "1 screenshot" : $"{_shots.Count} screenshots"));
        header.Children.Add(SmallText(_shotFolder ?? ""));
        var openFolder = new Button { Content = "Open folder" };
        TabsTheme.Apply(openFolder, TabsTheme.IconButton, height: 40, fontSize: 14);
        openFolder.HorizontalAlignment = HorizontalAlignment.Left;
        openFolder.Click += (_, _) => OpenInExplorer(_shotFolder!, select: null);
        header.Children.Add(openFolder);
        if (_shots.Count == 0)
            header.Children.Add(SmallText("Take one with ReShade's screenshot key (Print Screen by default). It shows up here straight away."));
        _shotBox.Children.Add(MakeCard("Screenshots", header, stretch: true));

        var grid = new Controls.AutoFitGrid { MinColumnWidth = ThumbMinWidth, ColumnSpacing = 14, RowSpacing = 14, StretchFewItems = false };
        _shotBox.Children.Add(grid);
        _shotsShown = 0;
        AddThumbs(grid);
    }

    private void AddThumbs(Panel grid)
    {
        int end = Math.Min(_shots.Count, _shotsShown + ShotPageSize);
        int decode = (int)Math.Ceiling(560 * Math.Max(1, Content?.XamlRoot?.RasterizationScale ?? 1));
        for (int i = _shotsShown; i < end; i++)
        {
            int index = i;
            var file = _shots[i];
            var image = new Image { Stretch = Stretch.UniformToFill };
            var fallback = new TextBlock
            {
                Text = Path.GetExtension(file.Name).ToUpperInvariant().TrimStart('.') + " screenshot",
                FontSize = 13,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            var bmp = new BitmapImage { DecodePixelWidth = decode };
            bmp.ImageFailed += (_, _) => fallback.Visibility = Visibility.Visible; // e.g. some HDR .jxr files
            try { bmp.UriSource = new Uri(file.FullName); image.Source = bmp; }
            catch (UriFormatException) { fallback.Visibility = Visibility.Visible; }
            var cell = new Grid { Height = ThumbMinWidth * _thumbAspect, Background = TabsTheme.B("#1A2130") };
            // Height follows the width the grid gives it, in the screen's shape
            cell.SizeChanged += (_, e) =>
            {
                double h = Math.Round(e.NewSize.Width * _thumbAspect);
                if (e.NewSize.Width > 0 && Math.Abs(cell.Height - h) > 0.5) cell.Height = h;
            };
            cell.Children.Add(image);
            cell.Children.Add(fallback);
            var btn = new Button
            {
                Content = cell,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(1),
                BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
                CornerRadius = new CornerRadius(8),
                Background = TabsTheme.B("#1A2130"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(btn, $"{file.Name}\n{file.LastWriteTime:g}");
            btn.Click += (_, _) => OpenViewer(index);
            grid.Children.Add(btn);
        }
        _shotsShown = end;

        // "Show more" sits after the grid, replaced each time
        if (_shotBox.Children.LastOrDefault() is Button oldMore && oldMore.Tag as string == "ShotMore") _shotBox.Children.Remove(oldMore);
        if (_shotsShown < _shots.Count)
        {
            var more = new Button { Content = $"Show more ({_shots.Count - _shotsShown} left)", Tag = "ShotMore" };
            TabsTheme.Apply(more, TabsTheme.IconButton, height: 42, fontSize: 15);
            more.Click += (_, _) => AddThumbs(grid);
            _shotBox.Children.Add(more);
        }
    }

    // ── Large view ────────────────────────────────────────────────────────────

    /// <summary>Shows screenshot <paramref name="index"/> large. <paramref name="focus"/>: the button to keep focus on
    /// (Previous / Next keep it, so the controller can press them again); the first button otherwise.</summary>
    private void OpenViewer(int index, string? focus = null)
    {
        if (index < 0 || index >= _shots.Count) return;
        _viewerIndex = index;
        var file = _shots[index];
        _viewerBox.Children.Clear();
        _dash.Visibility = Visibility.Collapsed;
        _viewerLayer.Visibility = Visibility.Visible;

        double maxHeight = Math.Max(300, (_body.ActualHeight > 0 ? _body.ActualHeight : 700) - 110);
        int decode = (int)Math.Ceiling(Math.Max(1280, _body.ActualWidth) * Math.Max(1, Content?.XamlRoot?.RasterizationScale ?? 1));
        var image = new Image { Stretch = Stretch.Uniform, MaxHeight = maxHeight, HorizontalAlignment = HorizontalAlignment.Left };
        var failed = SmallText("This screenshot can't be shown here (HDR .jxr screenshots often can't). Use Open to view it.");
        failed.Visibility = Visibility.Collapsed;
        var bmp = new BitmapImage { DecodePixelWidth = decode };
        bmp.ImageFailed += (_, _) => failed.Visibility = Visibility.Visible;
        try { bmp.UriSource = new Uri(file.FullName); image.Source = bmp; }
        catch (UriFormatException) { failed.Visibility = Visibility.Visible; }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        Button Make(string text, Action click, TabsTheme.Look? look = null)
        {
            var b = new Button { Content = text };
            TabsTheme.Apply(b, look ?? TabsTheme.IconButton, height: 42, fontSize: 15);
            b.Click += (_, _) => click();
            bar.Children.Add(b);
            return b;
        }
        Make("Back to screenshots", CloseViewer, TabsTheme.BlueButton);
        var prev = Make("Previous", () => OpenViewer(_viewerIndex - 1, "Previous"));
        var next = Make("Next", () => OpenViewer(_viewerIndex + 1, "Next"));
        prev.IsEnabled = index > 0;
        next.IsEnabled = index < _shots.Count - 1;
        Make("Copy", () => _ = CopyShotAsync(file.FullName));
        Button? delete = null;
        delete = Make("Delete", () =>
        {
            // Second click deletes (to the Recycle Bin)
            if (delete!.Tag as string != "Confirm")
            {
                delete.Tag = "Confirm";
                delete.Content = "Click again to delete";
                return;
            }
            DeleteShot(_viewerIndex);
        });
        Make("Open", () => OpenShot(file.FullName));
        Make("Show in folder", () => OpenInExplorer(file.DirectoryName ?? "", select: file.FullName));

        var info = SmallText($"{file.Name}  ·  {file.LastWriteTime:g}  ·  {index + 1} of {_shots.Count}");
        _viewerBox.Children.Add(bar);
        _viewerBox.Children.Add(info);
        _viewerBox.Children.Add(image);
        _viewerBox.Children.Add(failed);
        _viewerScroller.ChangeView(null, 0, null, disableAnimation: true);
        // Keep the controller where it was; at the first / last screenshot the other way is the one left
        Button? target = focus switch
        {
            "Previous" => prev.IsEnabled ? prev : next.IsEnabled ? next : null,
            "Next" => next.IsEnabled ? next : prev.IsEnabled ? prev : null,
            _ => null,
        };
        target ??= bar.Children[0] as Button;
        target?.Focus(_padUsed ? FocusState.Keyboard : FocusState.Programmatic);
    }

    private void CloseViewer()
    {
        _viewerIndex = -1;
        _viewerBox.Children.Clear();
        _viewerLayer.Visibility = Visibility.Collapsed;
        _dash.Visibility = Visibility.Visible;
        if (_shotsChanged) { _shotsChanged = false; RenderShotGrid(); }
    }

    private void DeleteShot(int index)
    {
        if (index < 0 || index >= _shots.Count) return;
        var path = _shots[index].FullName;
        bool ok;
        try { ok = OverlayNative.RecycleFile(path); }
        catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.DeleteShot] {ex.Message}"); ok = false; }
        CrashReporter.Log($"[OverlayWindow.DeleteShot] {(ok ? "Moved to the Recycle Bin" : "Couldn't delete")}: {Path.GetFileName(path)}");
        if (!ok) return;
        _shots.RemoveAt(index);
        _shotsChanged = true;
        if (_shots.Count == 0) { CloseViewer(); return; }
        OpenViewer(Math.Min(index, _shots.Count - 1));
    }

    private static async Task CopyShotAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            data.SetStorageItems(new[] { file });
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            CrashReporter.Log($"[OverlayWindow.CopyShot] Copied {Path.GetFileName(path)}");
        }
        catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.CopyShot] {ex.Message}"); }
    }

    private void OpenShot(string path)
    {
        Close(returnToGame: false); // the viewer app comes to the front
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashReporter.Log($"[OverlayWindow.OpenShot] {ex.Message}");
        }
    }

    private void OpenInExplorer(string folder, string? select)
    {
        if (string.IsNullOrEmpty(folder)) return;
        Close(returnToGame: false);
        try
        {
            var args = select != null ? $"/select,\"{select}\"" : $"\"{folder}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.OpenInExplorer] {ex.Message}"); }
    }

    // ── New screenshots while the page is open ────────────────────────────────

    private void StartShotWatcher(string folder)
    {
        if (_shotWatcher?.Path is string p && SamePath(p, folder)) return;
        StopShotWatcher();
        try
        {
            _shotWatcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
            _shotWatcher.Created += (_, _) => DispatcherQueue.TryEnqueue(QueueShotRefresh);
            _shotWatcher.Deleted += (_, _) => DispatcherQueue.TryEnqueue(QueueShotRefresh);
            _shotWatcher.Renamed += (_, _) => DispatcherQueue.TryEnqueue(QueueShotRefresh);
            _shotWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OverlayWindow.StartShotWatcher] {ex.Message}");
            _shotWatcher = null;
        }
    }

    private void StopShotWatcher()
    {
        if (_shotWatcher == null) return;
        try { _shotWatcher.EnableRaisingEvents = false; _shotWatcher.Dispose(); }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException) { }
        _shotWatcher = null;
    }

    private void QueueShotRefresh()
    {
        if (_shotRefreshTimer == null)
        {
            _shotRefreshTimer = DispatcherQueue.CreateTimer();
            _shotRefreshTimer.Interval = TimeSpan.FromMilliseconds(1200); // ReShade is still writing the file
            _shotRefreshTimer.IsRepeating = false;
            _shotRefreshTimer.Tick += (_, _) =>
            {
                // Only the grid refreshes; the large view keeps what you're looking at
                if (IsOpen && !ViewerOpen && _game?.Card is { } c)
                {
                    BuildScreenshotsPanel(c);
                    _layoutMode = -1; // the box may have been empty before (not in a column)
                    ArrangeDashboard(_body.ActualWidth);
                }
                else if (ViewerOpen) _shotsChanged = true;
            };
        }
        _shotRefreshTimer.Stop();
        _shotRefreshTimer.Start();
    }
}
