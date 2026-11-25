// DetailLayoutHost.Screenshots.cs -- Spotlight's Screenshots panel (the picture button beside the game's title):
// the game's ReShade screenshots as thumbnails, newest first, in the slide-in panel. Click = open in the default
// viewer; right-click = open, set as banner, copy, show in folder, delete (Recycle Bin). Same folder rules as the
// RHI overlay (ScreenshotLibraryService). Choose folder sets a per-game folder (other screenshot tools); Use default
// clears it.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    internal const string ScreenshotsKey = "Screenshots";
    private const int ShotPage = 30;
    private const double ShotThumbWidth = 240;

    private StackPanel? _shotsRoot;
    private int _shotsSeq;

    /// <summary>The panel's content (OpenPanel). Fills in once the folder has been read (background).</summary>
    private FrameworkElement? BuildScreenshotsPanel()
    {
        if (_window.ViewModel.SelectedGame is not { } card) return null;
        _shotsRoot = new StackPanel { Spacing = 14 };
        LoadScreenshots(card);
        return _shotsRoot;
    }

    /// <summary>(Re)fills <see cref="_shotsRoot"/>: reads the folder in the background, then renders.</summary>
    private void LoadScreenshots(GameCardViewModel card)
    {
        if (_shotsRoot == null) return;
        _shotsRoot.Children.Clear();
        _shotsRoot.Children.Add(MutedText("Looking for screenshots…"));
        int seq = ++_shotsSeq;

        var rhiFolder = _window.ViewModel.BuildScreenshotSavePath(card.GameName);
        bool perGame = _window.ViewModel.Settings.PerGameScreenshotFolders;
        var custom = _window.ViewModel.GetScreenshotFolder(card.GameName, card.Source ?? "");
        var installPath = card.InstallPath;
        _ = Task.Run(() =>
        {
            string? folder = null;
            bool customMissing = false;
            List<FileInfo> shots = new();
            try
            {
                // A chosen folder that's gone is reported, not silently swapped for ReShade's
                customMissing = custom != null && !ScreenshotLibraryService.FolderExists(custom);
                folder = customMissing ? null : ScreenshotLibraryService.FindFolder(installPath, rhiFolder, custom);
                if (folder != null)
                {
                    // Other tools don't name files after the exe: show everything in the chosen folder
                    bool isCustom = custom != null && SameFolder(folder, custom);
                    bool own = isCustom || (perGame && rhiFolder != null && SameFolder(folder, rhiFolder)) || SameFolder(folder, installPath);
                    string? exe = isCustom || string.IsNullOrEmpty(installPath) ? null
                        : App.Services.GetRequiredService<IPeHeaderService>().FindGameExe(installPath);
                    shots = ScreenshotLibraryService.List(folder, exe, own);
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DetailLayoutHost.Screenshots] {card.GameName}: {ex.Message}"); }
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (seq == _shotsSeq && _panelKey == ScreenshotsKey) RenderShots(card, folder, shots, custom, customMissing);
            });
        });
    }

    /// <summary>Reloads the panel after the game's screenshot folder changed, if it's still showing that game.</summary>
    private void ReloadScreenshots(GameCardViewModel card)
    {
        if (_shotsRoot != null && _panelKey == ScreenshotsKey && ReferenceEquals(_window.ViewModel.SelectedGame, card))
            LoadScreenshots(card);
    }

    private async Task ChooseScreenshotFolderAsync(GameCardViewModel card, string? startAt)
    {
        try
        {
            var picked = await _window.PickFolderAsync(startAt);
            if (string.IsNullOrEmpty(picked)) return;
            _window.ViewModel.SetScreenshotFolder(card.GameName, picked, card.Source ?? "");
            ReloadScreenshots(card);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DetailLayoutHost.ScreenshotsChooseFolder] {card.GameName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void UseDefaultScreenshotFolder(GameCardViewModel card)
    {
        try
        {
            _window.ViewModel.SetScreenshotFolder(card.GameName, null, card.Source ?? "");
            ReloadScreenshots(card);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DetailLayoutHost.ScreenshotsUseDefault] {card.GameName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ReturnScreenshotsPanel()
    {
        _shotsSeq++;      // drop a load still running
        _shotsRoot = null;
    }

    private static bool SameFolder(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try { return Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Foreground = TabsTheme.B(TabsTheme.TextMuted),
    };

    private void RenderShots(GameCardViewModel card, string? folder, List<FileInfo> shots, string? custom, bool customMissing)
    {
        if (_shotsRoot == null) return;
        _shotsRoot.Children.Clear();
        bool usingCustom = custom != null && !customMissing && folder != null && SameFolder(folder, custom);

        // Summary card: how many, where, Open folder / Choose folder / Use default
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(new TextBlock
        {
            Text = folder == null ? "No screenshots yet"
                 : shots.Count == 0 ? "No screenshots yet"
                 : shots.Count == 1 ? "1 screenshot" : $"{shots.Count} screenshots",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        if (customMissing)
            head.Children.Add(MutedText($"Your folder for this game wasn't found: {custom}"));
        else if (folder != null)
        {
            if (usingCustom) head.Children.Add(MutedText("Your folder for this game"));
            head.Children.Add(MutedText(folder));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        Button AddButton(string text, string? tip, Action click)
        {
            var b = new Button { Content = text };
            TabsTheme.Apply(b, TabsTheme.IconButton, height: 34, fontSize: 12);
            if (tip != null) ToolTipService.SetToolTip(b, tip);
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }
        if (folder != null) AddButton("Open folder", null, () => ShowInExplorer(folder, select: null));
        AddButton("Choose folder…",
            "Pick the folder your screenshot tool saves to for this game (Steam, NVIDIA App, Game Bar…). RHI shows every picture in it.",
            () => _ = ChooseScreenshotFolderAsync(card, customMissing ? card.InstallPath : folder ?? card.InstallPath));
        if (custom != null)
            AddButton("Use default", "Go back to ReShade's screenshot folder", () => UseDefaultScreenshotFolder(card));
        head.Children.Add(buttons);

        const string otherTool = "Screenshots from another tool (Steam, NVIDIA App, Game Bar…) show up here once you pick its folder with Choose folder.";
        if (customMissing)
            head.Children.Add(MutedText("Choose the folder again, or use Use default to go back to ReShade's screenshot folder."));
        else if (shots.Count == 0)
            head.Children.Add(MutedText(usingCustom
                ? "No pictures in this folder yet. Screenshots saved to it show up here."
                : folder == null
                ? "Take a screenshot in the game with ReShade's screenshot key (Print Screen by default), or set a folder in Settings › ReShade › Screenshot folder. " + otherTool
                : "Take one in the game with ReShade's screenshot key (Print Screen by default). " + otherTool));
        else
            head.Children.Add(MutedText("Click a screenshot to open it. Right-click for more: set it as the banner, copy it or delete it."));
        _shotsRoot.Children.Add(TabsTheme.MakeCard(head, 16));
        if (shots.Count == 0) return;

        var grid = new Controls.AutoFitGrid { MinColumnWidth = ShotThumbWidth, ColumnSpacing = 10, RowSpacing = 10, StretchFewItems = false };
        _shotsRoot.Children.Add(grid);
        AddShotThumbs(card, grid, shots, 0);
    }

    /// <summary>Height / width of the game's monitor: screenshots have its shape.</summary>
    private double ScreenAspect()
    {
        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(_window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var b = area.OuterBounds;
            if (b.Width > 0 && b.Height > 0) return (double)b.Height / b.Width;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[DetailLayoutHost.ScreenAspect] {ex.Message}");
        }
        return 9.0 / 16;
    }

    private void AddShotThumbs(GameCardViewModel card, Controls.AutoFitGrid grid, List<FileInfo> shots, int start)
    {
        double aspect = ScreenAspect();
        int decode = (int)Math.Ceiling(440 * Math.Max(1, grid.XamlRoot?.RasterizationScale ?? 1));
        int end = Math.Min(shots.Count, start + ShotPage);
        for (int i = start; i < end; i++)
            grid.Children.Add(MakeShotThumb(card, shots[i], aspect, decode));

        // "Show more" after the grid, replaced each time
        if (_shotsRoot!.Children.LastOrDefault() is Button { Tag: "ShotsMore" } old) _shotsRoot.Children.Remove(old);
        if (end < shots.Count)
        {
            var more = new Button { Content = $"Show more ({shots.Count - end} left)", Tag = "ShotsMore", HorizontalAlignment = HorizontalAlignment.Left };
            TabsTheme.Apply(more, TabsTheme.IconButton, height: 36, fontSize: 13);
            more.Click += (_, _) => AddShotThumbs(card, grid, shots, end);
            _shotsRoot.Children.Add(more);
        }
    }

    private Button MakeShotThumb(GameCardViewModel card, FileInfo file, double aspect, int decode)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var fallback = new TextBlock
        {
            Text = Path.GetExtension(file.Name).ToUpperInvariant().TrimStart('.') + " screenshot",
            FontSize = 12,
            Foreground = TabsTheme.B(TabsTheme.TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var bmp = new BitmapImage { DecodePixelWidth = decode };
        bmp.ImageFailed += (_, _) => fallback.Visibility = Visibility.Visible; // e.g. some HDR .jxr files
        try { bmp.UriSource = new Uri(file.FullName); image.Source = bmp; }
        catch (UriFormatException) { fallback.Visibility = Visibility.Visible; }

        var cell = new Grid { Height = Math.Round(ShotThumbWidth * aspect), Background = TabsTheme.B("#1A2130") };
        cell.SizeChanged += (_, e) =>
        {
            double h = Math.Round(e.NewSize.Width * aspect);
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
        ToolTipService.SetToolTip(btn, $"{file.Name}\n{file.LastWriteTime:g}\nClick to open, right-click for more");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, $"Screenshot {file.LastWriteTime:g}");
        btn.Click += (_, _) => OpenShotFile(file.FullName);
        btn.ContextFlyout = MakeShotMenu(card, file, btn);
        return btn;
    }

    private MenuFlyout MakeShotMenu(GameCardViewModel card, FileInfo file, Button thumb)
    {
        var menu = new MenuFlyout();
        void Add(string text, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Open", () => OpenShotFile(file.FullName));
        // HDR .jxr screenshots can't be shown as the banner
        bool bannerOk = !file.Extension.Equals(".jxr", StringComparison.OrdinalIgnoreCase);
        Add(bannerOk ? "Set as banner" : "Set as banner (not for .jxr)", () => _ = _window.SetBannerFromFileAsync(card, file.FullName), bannerOk);
        Add("Copy to clipboard", () => _ = CopyShotAsync(file.FullName));
        Add("Show in folder", () => ShowInExplorer(file.DirectoryName ?? "", select: file.FullName));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Delete (to the Recycle Bin)", () =>
        {
            bool ok;
            try { ok = Overlay.OverlayNative.RecycleFile(file.FullName); }
            catch (Exception ex) { CrashReporter.Log($"[DetailLayoutHost.DeleteShot] {ex.Message}"); ok = false; }
            CrashReporter.Log($"[DetailLayoutHost.DeleteShot] {(ok ? "Moved to the Recycle Bin" : "Couldn't delete")}: {file.Name}");
            if (ok && thumb.Parent is Panel p) p.Children.Remove(thumb);
            else if (!ok) _window.ShowStatusBarMessage($"Couldn't delete {file.Name}", TimeSpan.FromSeconds(6));
        });
        return menu;
    }

    private static void OpenShotFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashReporter.Log($"[DetailLayoutHost.OpenShotFile] {ex.Message}");
        }
    }

    private static void ShowInExplorer(string folder, string? select)
    {
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            var args = select != null ? $"/select,\"{select}\"" : $"\"{folder}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashReporter.Log($"[DetailLayoutHost.ShowInExplorer] {ex.Message}");
        }
    }

    private async Task CopyShotAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            data.SetStorageItems(new[] { file });
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            _window.ShowStatusBarMessage($"Copied {Path.GetFileName(path)}", TimeSpan.FromSeconds(4));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[DetailLayoutHost.CopyShot] {ex.Message}");
            _window.ShowStatusBarMessage("Couldn't copy the screenshot", TimeSpan.FromSeconds(6));
        }
    }
}
