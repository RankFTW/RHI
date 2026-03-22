using Microsoft.UI.Xaml;
using RenoDXCommander.Services;
using System.Runtime.InteropServices;

namespace RenoDXCommander;

/// <summary>
/// Encapsulates Win32 interop for window bounds persistence, WndProc subclassing
/// (minimum size enforcement), and WM_DROPFILES routing.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class WindowStateManager
{
    private readonly Window _window;
    private readonly DragDropHandler _dragDropHandler;
    private readonly ICrashReporter _crashReporter;
    private IntPtr _hwnd;
    private IntPtr _origWndProc;
    private NativeInterop.WndProcDelegate? _wndProcDelegate; // prevent GC

    private static readonly string _windowSettingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UPST", "window_main.json");

    // In-memory cache of window bounds (populated from file on first restore)
    private (int X, int Y, int W, int H)? _windowBounds;

    public WindowStateManager(Window window, IntPtr hwnd, DragDropHandler dragDropHandler, ICrashReporter crashReporter)
    {
        _window = window;
        _hwnd = hwnd;
        _dragDropHandler = dragDropHandler;
        _crashReporter = crashReporter;
    }

    /// <summary>
    /// Installs the WndProc subclass to enforce minimum window size via WM_GETMINMAXINFO
    /// and to intercept WM_DROPFILES for Win32 drag-and-drop.
    /// </summary>
    public void InstallWndProcSubclass()
    {
        _origWndProc = NativeInterop.GetWindowLongPtr(_hwnd, NativeInterop.GWLP_WNDPROC);
        _wndProcDelegate = new NativeInterop.WndProcDelegate(WndProc);
        NativeInterop.SetWindowLongPtr(_hwnd, NativeInterop.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    /// <summary>
    /// Enables Win32 drag-and-drop (WM_DROPFILES) and allows drag messages
    /// through UIPI when running as admin.
    /// </summary>
    public void EnableDragAccept()
    {
        NativeInterop.DragAcceptFiles(_hwnd, true);
        // Allow drag messages through UIPI when running as admin
        NativeInterop.ChangeWindowMessageFilterEx(_hwnd, NativeInterop.WM_DROPFILES, NativeInterop.MSGFLT_ALLOW, IntPtr.Zero);
        NativeInterop.ChangeWindowMessageFilterEx(_hwnd, NativeInterop.WM_COPYGLOBALDATA, NativeInterop.MSGFLT_ALLOW, IntPtr.Zero);
    }

    internal IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeInterop.WM_GETMINMAXINFO)
        {
            var dpi = NativeInterop.GetDpiForWindow(hWnd);
            var scale = dpi / 96.0;
            var mmi = Marshal.PtrToStructure<NativeInterop.MINMAXINFO>(lParam);
            mmi.ptMinTrackSize = new System.Drawing.Point(
                (int)(NativeInterop.MinWindowWidth * scale),
                (int)(NativeInterop.MinWindowHeight * scale));
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        if (msg == NativeInterop.WM_DROPFILES)
        {
            HandleWin32Drop(wParam);
            return IntPtr.Zero;
        }

        return NativeInterop.CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Handles WM_DROPFILES from Win32 shell drag-and-drop.
    /// Extracts file paths and routes them to the DragDropHandler for processing.
    /// </summary>
    internal void HandleWin32Drop(IntPtr hDrop)
    {
        try
        {
            uint fileCount = NativeInterop.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var paths = new List<string>();

            for (uint i = 0; i < fileCount; i++)
            {
                uint size = NativeInterop.DragQueryFile(hDrop, i, null, 0) + 1;
                var buffer = new char[size];
                NativeInterop.DragQueryFile(hDrop, i, buffer, size);
                paths.Add(new string(buffer, 0, (int)(size - 1)));
            }

            NativeInterop.DragFinish(hDrop);

            // Process on the UI thread via DragDropHandler
            _window.DispatcherQueue.TryEnqueue(async () =>
            {
                foreach (var path in paths)
                {
                    var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";

                    if (ext is ".addon64" or ".addon32"
                        && Path.GetFileName(path).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
                    {
                        try { await _dragDropHandler.ProcessDroppedAddon(path); }
                        catch (Exception ex) { _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] Addon error — {ex.Message}"); }
                        continue;
                    }

                    if (DragDropHandler.AllowedExtensions.Contains(ext) && ext != ".exe"
                        && ext is not ".addon64" and not ".addon32")
                    {
                        try { await _dragDropHandler.ProcessDroppedArchive(path); }
                        catch (Exception ex) { _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] Archive error — {ex.Message}"); }
                        continue;
                    }

                    if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        try { await _dragDropHandler.ProcessDroppedExe(path); }
                        catch (Exception ex) { _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] Exe error — {ex.Message}"); }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] Failed — {ex.Message}");
        }
    }

    // ── Window persistence (JSON-based, works for unpackaged WinUI 3 apps) ────────

    public void TryRestoreWindowBounds()
    {
        try
        {
            if (!System.IO.File.Exists(_windowSettingsPath)) return;
            var json = System.IO.File.ReadAllText(_windowSettingsPath);
            var doc  = System.Text.Json.JsonDocument.Parse(json).RootElement;

            // Try unified bounds first (new format)
            if (doc.TryGetProperty("X", out var ux) && doc.TryGetProperty("Y", out var uy) &&
                doc.TryGetProperty("W", out var uw) && doc.TryGetProperty("H", out var uh))
                _windowBounds = (ux.GetInt32(), uy.GetInt32(), uw.GetInt32(), uh.GetInt32());

            // Legacy migration: old format stored FullX/FullY or CompactX/CompactY — prefer Compact (closest to new layout)
            if (_windowBounds == null &&
                doc.TryGetProperty("CompactX", out var cx) && doc.TryGetProperty("CompactY", out var cy) &&
                doc.TryGetProperty("CompactW", out var cw) && doc.TryGetProperty("CompactH", out var ch))
                _windowBounds = (cx.GetInt32(), cy.GetInt32(), cw.GetInt32(), ch.GetInt32());

            if (_windowBounds == null &&
                doc.TryGetProperty("FullX", out var fx) && doc.TryGetProperty("FullY", out var fy) &&
                doc.TryGetProperty("FullW", out var fw) && doc.TryGetProperty("FullH", out var fh))
                _windowBounds = (fx.GetInt32(), fy.GetInt32(), fw.GetInt32(), fh.GetInt32());

            // Apply the bounds
            if (_windowBounds is var (x, y, w, h) && w >= 400 && h >= 300 && w <= 7680 && h <= 4320)
            {
                NativeInterop.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, 0x0040 /* SWP_NOZORDER */);
            }
        }
        catch { }
    }

    /// <summary>Captures the current window rect into the in-memory cache.</summary>
    public void CaptureCurrentBounds()
    {
        try
        {
            if (!NativeInterop.GetWindowRect(_hwnd, out var r)) return;
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            if (w < 100 || h < 100) return;
            _windowBounds = (r.Left, r.Top, w, h);
        }
        catch { }
    }

    /// <summary>Restores the cached window bounds, if available.</summary>
    public void RestoreWindowBounds()
    {
        try
        {
            if (_windowBounds is var (x, y, w, h) && w >= 400 && h >= 300 && w <= 7680 && h <= 4320)
            {
                NativeInterop.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, 0x0040 /* SWP_NOZORDER */);
            }
        }
        catch { }
    }

    public void SaveWindowBounds()
    {
        try
        {
            // Capture final bounds
            CaptureCurrentBounds();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_windowSettingsPath)!);
            var data = new Dictionary<string, int>();
            if (_windowBounds is var (x, y, w, h))
            {
                data["X"] = x; data["Y"] = y; data["W"] = w; data["H"] = h;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            System.IO.File.WriteAllText(_windowSettingsPath, json);
        }
        catch { }
    }
}
