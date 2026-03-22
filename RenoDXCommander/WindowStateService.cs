using System.Runtime.InteropServices;

namespace RenoDXCommander;

/// <summary>
/// Service class responsible for window bounds persistence and WndProc subclassing.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class WindowStateService
{
    private readonly IntPtr _hwnd;
    private readonly IntPtr _origWndProc;
    private readonly NativeInterop.WndProcDelegate _wndProcDelegate; // prevent GC

    // ── Window persistence (JSON-based, works for unpackaged WinUI 3 apps) ────────
    private static readonly string _windowSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UPST", "window_main.json");

    // In-memory cache of window bounds (populated from file on first restore)
    private (int X, int Y, int W, int H)? _windowBounds;

    public WindowStateService(IntPtr hwnd, IntPtr origWndProc)
    {
        _hwnd = hwnd;
        _origWndProc = origWndProc;
        _wndProcDelegate = new NativeInterop.WndProcDelegate(WndProc);

        // Install the subclass
        NativeInterop.SetWindowLongPtr(_hwnd, NativeInterop.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    /// <summary>The WndProc delegate reference (must be kept alive to prevent GC).</summary>
    internal NativeInterop.WndProcDelegate WndProcDelegateRef => _wndProcDelegate;

    public IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
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
        return NativeInterop.CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    public void TryRestoreWindowBounds()
    {
        try
        {
            if (!File.Exists(_windowSettingsPath)) return;
            var json = File.ReadAllText(_windowSettingsPath);
            var doc  = System.Text.Json.JsonDocument.Parse(json).RootElement;

            // Try unified bounds first (new format)
            if (doc.TryGetProperty("X", out var ux) && doc.TryGetProperty("Y", out var uy) &&
                doc.TryGetProperty("W", out var uw) && doc.TryGetProperty("H", out var uh))
                _windowBounds = (ux.GetInt32(), uy.GetInt32(), uw.GetInt32(), uh.GetInt32());

            // Legacy migration: old format stored FullX/FullY or CompactX/CompactY
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

            Directory.CreateDirectory(Path.GetDirectoryName(_windowSettingsPath)!);
            var data = new Dictionary<string, int>();
            if (_windowBounds is var (x, y, w, h))
            {
                data["X"] = x; data["Y"] = y; data["W"] = w; data["H"] = h;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            File.WriteAllText(_windowSettingsPath, json);
        }
        catch { }
    }
}
