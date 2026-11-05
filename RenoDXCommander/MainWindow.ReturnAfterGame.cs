// MainWindow.ReturnAfterGame.cs — Spotlight + controller: bring RHI back to the front when a
// game launched from RHI closes, so the controller works without clicking the window first
// (the controller only listens while RHI is the foreground window).
namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>Called on the UI thread after a game started from RHI has exited.</summary>
    private async void RefocusAfterGameExit(string gameName)
    {
        try
        {
            if (IsShuttingDown) return;
            if (!_detailPanelBuilder.Layout.IsSpotlight || !ViewModel.Settings.ControllerSupport) return;
            // Hidden to the tray: the user put RHI away on purpose, leave it there.
            if (!AppWindow.IsVisible) return;

            // Let the game's window finish closing before asking for the foreground.
            await Task.Delay(800, LifetimeToken);
            if (IsShuttingDown) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (NativeInterop.GetForegroundWindow() == hwnd) return;

            if (NativeInterop.IsIconic(hwnd)) NativeInterop.ShowWindow(hwnd, NativeInterop.SW_RESTORE);
            bool ok = NativeInterop.SetForegroundWindow(hwnd);
            if (!ok)
            {
                // The last input went to the game, so Windows refuses the request. A tap of Alt
                // lifts that lock (same as a user pressing it), then the request is allowed.
                NativeInterop.keybd_event(NativeInterop.VK_MENU, 0, 0, UIntPtr.Zero);
                NativeInterop.keybd_event(NativeInterop.VK_MENU, 0, NativeInterop.KEYEVENTF_KEYUP, UIntPtr.Zero);
                ok = NativeInterop.SetForegroundWindow(hwnd);
            }
            if (ok) Activate();
            _crashReporter.Log($"[MainWindow.RefocusAfterGameExit] '{gameName}' closed — {(ok ? "RHI brought to front" : "Windows declined the focus request")}");
        }
        catch (OperationCanceledException) when (IsShuttingDown) { /* Expected: app is closing. */ }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.RefocusAfterGameExit] Failed — {ex.Message}");
        }
    }
}
