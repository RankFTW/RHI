// MainWindow.DevScreenshot.cs -- developer-only screenshot hook (needs unlock.txt).
// Lets tooling capture the window when RHI runs in Admin Mode (a normal process can't capture an
// elevated window). Write %LocalAppData%\RHI\rhi_screenshot_requested with lines:
//   <output .png path>
//   tab=<Tabs layout tab key>      (optional, e.g. Overview, Extras, NvidiaProfileDlss)
//   target=detail                  (optional: render the whole game page instead of the window)
//   target=popup                   (optional: render the open dialog / flyout instead of the window)
//   page=settings[:Section]        (optional: open the Settings page, e.g. page=settings:Display)
//   open=displaymodes              (optional: open the selected game's Display mode dialog)
//   close=dialogs                  (optional: close open dialogs first)
//   dmswitch=<mode name>           (optional: switch the selected game's display mode)
//   target=popupcontent / settings (optional: a dialog's whole scrolling body / the whole Settings column)
// Checked by the same 2 s timer as the installer shutdown signal.

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _devScreenshotBusy;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _padScriptTimer;

    /// <summary>Dev: the first text inside an element (a button's label), for the pad script's log.</summary>
    private static string FirstText(DependencyObject root)
    {
        if (root is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FirstText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)) is { Length: > 0 } t) return t;
        return "";
    }

    /// <summary>Dev: runs controller actions in order (after <paramref name="startMs"/>), logging where focus lands.</summary>
    private void RunPadScript(List<string> actions, int startMs)
    {
        if (actions.Count == 0) return;
        int next = 0;
        var timer = _padScriptTimer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(600, startMs));
        timer.Tick += (t, _) =>
        {
            timer.Interval = TimeSpan.FromMilliseconds(600);
            if (next >= actions.Count) { timer.Stop(); return; }
            WatchForNewPopup(); // as the controller's poll does between presses
            var action = actions[next++];
            // ScrollDown / ScrollUp: the right stick (one big flick); anything else is a PadAction
            if (action.Equals("ScrollDown", StringComparison.OrdinalIgnoreCase)) PadScroll(400);
            else if (action.Equals("ScrollUp", StringComparison.OrdinalIgnoreCase)) PadScroll(-400);
            else if (Enum.TryParse<PadAction>(action, true, out var pa)) HandlePadAction(pa);
            else return;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var f = Content?.XamlRoot is { } xr ? FocusManager.GetFocusedElement(xr) : null;
                string what = f switch
                {
                    ContentControl { Content: string text } cc => $"{cc.GetType().Name} '{text}'",
                    FrameworkElement fe => $"{fe.GetType().Name} '{fe.Name}' tag={fe.Tag} tip={ToolTipService.GetToolTip(fe)} text='{FirstText(fe)}'",
                    null => "nothing",
                    _ => f.GetType().Name,
                };
                string scrolled = "";
                if (action.StartsWith("Scroll", StringComparison.OrdinalIgnoreCase) && Content?.XamlRoot is { } sr
                    && TopPopup(sr, f as DependencyObject) is { } sp && PopupScroller(sp, f as DependencyObject) is { } psv)
                    scrolled = $" (popup scroll {psv.VerticalOffset:0} of {psv.ScrollableHeight:0})";
                _crashReporter.Log($"[MainWindow.DevScreenshot] pad {action} -> focus: {what}{scrolled}");
            });
        };
        timer.Start();
    }
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _devScreenshotTimer;

    private void CheckDevScreenshotRequest()
    {
        try { CheckDevScreenshotRequestCore(); }
        catch (Exception ex)
        {
            // Without this an error left the hook "busy" for the rest of the session
            _devScreenshotBusy = false;
            _crashReporter.Log($"[MainWindow.DevScreenshot] Request failed — {ex.GetType().Name}: {ex.Message} | {ex.StackTrace}");
        }
    }

    private void CheckDevScreenshotRequestCore()
    {
        if (_devScreenshotBusy || !DevUnlockService.IsUnlocked) return;
        var request = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rhi_screenshot_requested");
        if (!File.Exists(request)) return;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(request);
            File.Delete(request);
        }
        catch (IOException ex) { _crashReporter.Log($"[MainWindow.DevScreenshot] Request unreadable — {ex.Message}"); return; }
        catch (UnauthorizedAccessException ex) { _crashReporter.Log($"[MainWindow.DevScreenshot] Request unreadable — {ex.Message}"); return; }

        var outPath = lines.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(outPath)) return;
        string? Opt(string name) => lines.Skip(1)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];

        _devScreenshotBusy = true;
        _crashReporter.Log($"[MainWindow.DevScreenshot] Request: {string.Join(" | ", lines.Skip(1))}");
        int waitMs = 1500;
        if (Opt("layout") is { Length: > 0 } layout
            && Enum.TryParse<MainLayout>(layout, true, out var ml)
            && !string.Equals(ViewModel.Settings.MainLayout, ml.ToString(), StringComparison.Ordinal))
        {
            // Same path as the top bar layout switch
            ViewModel.Settings.MainLayout = ml.ToString();
            ViewModel.SaveSettingsPublic();
            ApplyMainLayout();
            waitMs = 4000;
        }
        if (Opt("game") is { Length: > 0 } game)
        {
            // Select a game like a click in the list would (sections build in the background)
            var match = ViewModel.DisplayedGames.FirstOrDefault(c =>
                c.GameName.Equals(game, StringComparison.OrdinalIgnoreCase));
            if (match != null && !ReferenceEquals(GameList.SelectedItem, match))
            {
                GameList.SelectedItem = match;
                GameList.ScrollIntoView(match);
                waitMs = 5000;
            }
        }
        // tab=none closes the Spotlight panel; in Spotlight any other tab opens it
        if (Opt("tab") is { Length: > 0 } tab)
        {
            if (tab.Equals("none", StringComparison.OrdinalIgnoreCase)) _detailPanelBuilder.Layout.ClosePanel();
            else _detailPanelBuilder.Layout.SelectTab(tab);
        }
        // pad=Down,Select,Back: controller actions, one every 600 ms, before the screenshot
        if (Opt("pad") is { Length: > 0 } pad)
        {
            var actions = pad.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            RunPadScript(actions, waitMs);
            waitMs += actions.Count * 600 + 800;
        }
        if (Opt("page") is { Length: > 0 } page && page.StartsWith("settings", StringComparison.OrdinalIgnoreCase))
        {
            _settingsHandler.SettingsButton_Click(this, new RoutedEventArgs());
            var section = page.Contains(':') ? page[(page.IndexOf(':') + 1)..] : null;
            if (!string.IsNullOrEmpty(section)) _settingsHandler.DevJumpToSection(section);
            waitMs = Math.Max(waitMs, 3000);
        }
        // close=dialogs: hide open dialogs first (one left open by an earlier open=...)
        if (string.Equals(Opt("close"), "dialogs", StringComparison.OrdinalIgnoreCase) && Content?.XamlRoot is { } cr)
            foreach (var p in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(cr))
                if (p.Child is ContentDialog cd) cd.Hide();
        // dmswitch=<mode name>: switch the selected game's display mode (same path as its mode button)
        if (Opt("dmswitch") is { Length: > 0 } dmName && ViewModel.SelectedGame is { } dmGame
            && ViewModel.DisplayModeStore.Modes.FirstOrDefault(m => m.Name.Equals(dmName, StringComparison.OrdinalIgnoreCase)) is { } dmMode)
        {
            _ = SwitchDisplayModeFromUiAsync(dmGame, dmMode.Id);
            waitMs = Math.Max(waitMs, 3000);
        }
        if (string.Equals(Opt("open"), "displaymodes", StringComparison.OrdinalIgnoreCase) && ViewModel.SelectedGame is { } dmCard)
        {
            _ = ShowDisplayModeGameDialogAsync(dmCard);
            waitMs = Math.Max(waitMs, 3000);
        }
        if (int.TryParse(Opt("wait"), out var w) && w > 0) waitMs = w;
        bool detail = string.Equals(Opt("target"), "detail", StringComparison.OrdinalIgnoreCase);
        bool popupContent = string.Equals(Opt("target"), "popupcontent", StringComparison.OrdinalIgnoreCase);
        bool popup = popupContent || string.Equals(Opt("target"), "popup", StringComparison.OrdinalIgnoreCase);
        bool settingsColumn = string.Equals(Opt("target"), "settings", StringComparison.OrdinalIgnoreCase);

        // Give layout (and any background section build) a moment before rendering
        // Kept in a field: a timer only held by a local can be garbage-collected before it fires
        var delay = _devScreenshotTimer = DispatcherQueue.CreateTimer();
        delay.Interval = TimeSpan.FromMilliseconds(waitMs);
        delay.IsRepeating = false;
        delay.Tick += async (t, _) =>
        {
            try
            {
                UIElement target = detail ? DetailPanel : (UIElement)Content;
                if (popup && Content?.XamlRoot is { } pr)
                {
                    // The dialog itself, not its dim layer (a separate popup)
                    var popups = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(pr);
                    var pick = popups.FirstOrDefault(p => p.Child is ContentDialog)
                            ?? popups.LastOrDefault(p => p.Child is FrameworkElement { ActualWidth: > 0 } and not Microsoft.UI.Xaml.Shapes.Rectangle);
                    if (pick?.Child is { } popupChild) target = popupChild;
                    // popupcontent: the dialog's whole scrolling body, not just what fits on screen
                    if (popupContent)
                    {
                        // A cog dialog's body is the StackPanel CogDialog makes BodyWidth wide
                        if (FindDescendant<StackPanel>(target, sp => sp.Width == CogDialog.BodyWidth) is { } body) target = body;
                    }
                }
                if (settingsColumn) target = SettingsCardsPanel;
                var rtb = new RenderTargetBitmap();
                _crashReporter.Log("[MainWindow.DevScreenshot] Rendering");
                await rtb.RenderAsync(target);
                var pixels = (await rtb.GetPixelsAsync()).ToArray();
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
                await encoder.FlushAsync();
                stream.Seek(0);
                var bytes = new byte[stream.Size];
                await stream.AsStreamForRead().ReadAsync(bytes, 0, bytes.Length);
                await File.WriteAllBytesAsync(outPath, bytes);
                _crashReporter.Log($"[MainWindow.DevScreenshot] Saved {rtb.PixelWidth}x{rtb.PixelHeight} to {outPath}");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainWindow.DevScreenshot] Failed — {ex.Message}");
            }
            finally { _devScreenshotBusy = false; }
        };
        delay.Start();
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t && (match == null || match(t))) return t;
            if (FindDescendant(c, match) is { } found) return found;
        }
        return null;
    }
}
