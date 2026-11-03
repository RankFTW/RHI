// MainWindow.DevScreenshot.cs -- developer-only screenshot hook (needs unlock.txt).
// Lets tooling capture the window when RHI runs in Admin Mode (a normal process can't capture an
// elevated window). Write %LocalAppData%\RHI\rhi_screenshot_requested with lines:
//   <output .png path>
//   tab=<Tabs layout tab key>      (optional, e.g. Overview, Extras, NvidiaProfileDlss)
//   target=detail                  (optional: render the whole game page instead of the window)
// Checked by the same 2 s timer as the installer shutdown signal.

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _devScreenshotBusy;
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
            // Same path as Settings › General › Game page › Layout
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
        if (int.TryParse(Opt("wait"), out var w) && w > 0) waitMs = w;
        bool detail = string.Equals(Opt("target"), "detail", StringComparison.OrdinalIgnoreCase);

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
}
