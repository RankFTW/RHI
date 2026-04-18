using Microsoft.UI.Xaml;
using RenoDXCommander.Services;
using System.Runtime.InteropServices;
using static RenoDXCommander.NativeInterop;

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
    private OleDropTarget? _oleDropTarget; // prevent GC of COM drop target

    private static readonly string _windowSettingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "window_main.json");

    // In-memory cache of window bounds (populated from file on first restore)
    private (int X, int Y, int W, int H)? _windowBounds;

    // Compact-mode size locking
    private bool _sizeLocked;
    private (int W, int H) _compactSize = (1050, 750); // Fixed compact dimensions

    public WindowStateManager(Window window, IntPtr hwnd, DragDropHandler dragDropHandler, ICrashReporter crashReporter)
    {
        _window = window;
        _hwnd = hwnd;
        _dragDropHandler = dragDropHandler;
        _crashReporter = crashReporter;
    }

    /// <summary>
    /// Enables or disables window size locking for Compact mode.
    /// When locked, WM_GETMINMAXINFO enforces both min and max track size
    /// to the fixed compact dimensions, preventing user resizing.
    /// </summary>
    public void SetSizeLocked(bool locked) => _sizeLocked = locked;

    /// <summary>
    /// Resizes the window to the fixed compact dimensions, scaled for the current DPI.
    /// Preserves the current window position (top-left corner).
    /// </summary>
    public void ApplyCompactSize()
    {
        var dpi = NativeInterop.GetDpiForWindow(_hwnd);
        var scale = dpi / 96.0;
        var w = (int)(_compactSize.W * scale);
        var h = (int)(_compactSize.H * scale);

        NativeInterop.GetWindowRect(_hwnd, out var rect);
        NativeInterop.SetWindowPos(_hwnd, IntPtr.Zero,
            rect.Left, rect.Top, w, h, 0x0040 /* SWP_NOZORDER */);
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
    /// through UIPI when running as admin. Also registers an OLE IDropTarget
    /// to receive URL text drops from browsers and Discord.
    /// </summary>
    public void EnableDragAccept()
    {
        // Allow drag messages through UIPI when running as admin
        NativeInterop.ChangeWindowMessageFilterEx(_hwnd, NativeInterop.WM_DROPFILES, NativeInterop.MSGFLT_ALLOW, IntPtr.Zero);
        NativeInterop.ChangeWindowMessageFilterEx(_hwnd, NativeInterop.WM_COPYGLOBALDATA, NativeInterop.MSGFLT_ALLOW, IntPtr.Zero);

        // Try OLE IDropTarget first — this handles BOTH file drops (CF_HDROP) and
        // URL text drops (CF_UNICODETEXT) from browsers/Discord. We must NOT call
        // DragAcceptFiles before RegisterDragDrop because DragAcceptFiles internally
        // registers its own drop target, causing RegisterDragDrop to fail with
        // DRAGDROP_E_ALREADYREGISTERED (0x80040101).
        try
        {
            int oleHr = NativeInterop.OleInitialize(IntPtr.Zero);
            // S_OK (0) or S_FALSE (1, already initialized) are both acceptable
            if (oleHr < 0)
            {
                _crashReporter.Log($"[WindowStateManager.EnableDragAccept] OleInitialize failed with HRESULT 0x{oleHr:X8} — falling back to WM_DROPFILES only");
                NativeInterop.DragAcceptFiles(_hwnd, true);
                return;
            }

            _oleDropTarget = new OleDropTarget(this);
            int regHr = NativeInterop.RegisterDragDrop(_hwnd, _oleDropTarget);
            if (regHr != 0)
            {
                _crashReporter.Log($"[WindowStateManager.EnableDragAccept] RegisterDragDrop failed with HRESULT 0x{regHr:X8} — falling back to WM_DROPFILES only");
                _oleDropTarget = null;
                NativeInterop.DragAcceptFiles(_hwnd, true);
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[WindowStateManager.EnableDragAccept] OLE registration failed — {ex.Message}. Falling back to WM_DROPFILES only");
            _oleDropTarget = null;
            NativeInterop.DragAcceptFiles(_hwnd, true);
        }
    }

    internal IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeInterop.WM_GETMINMAXINFO)
        {
            var dpi = NativeInterop.GetDpiForWindow(hWnd);
            var scale = dpi / 96.0;
            var mmi = Marshal.PtrToStructure<NativeInterop.MINMAXINFO>(lParam);

            if (_sizeLocked)
            {
                // Lock both min and max to the compact size, preventing user resizing
                var w = (int)(_compactSize.W * scale);
                var h = (int)(_compactSize.H * scale);
                mmi.ptMinTrackSize = new System.Drawing.Point(w, h);
                mmi.ptMaxTrackSize = new System.Drawing.Point(w, h);
            }
            else
            {
                mmi.ptMinTrackSize = new System.Drawing.Point(
                    (int)(NativeInterop.MinWindowWidth * scale),
                    (int)(NativeInterop.MinWindowHeight * scale));
            }

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

                    // .url shortcut files — parse the URL inside and route to ProcessDroppedUrl
                    if (ext == ".url")
                    {
                        try
                        {
                            var url = DragDropHandler.ParseUrlFromShortcutFile(path);
                            if (!string.IsNullOrEmpty(url))
                            {
                                _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] Parsed URL from .url file '{Path.GetFileName(path)}': {url}");
                                await _dragDropHandler.ProcessDroppedUrl(url);
                            }
                            else
                            {
                                _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] No URL found in .url file '{Path.GetFileName(path)}' — skipping");
                            }
                        }
                        catch (Exception ex)
                        {
                            _crashReporter.Log($"[WindowStateManager.HandleWin32Drop] Error processing .url file '{Path.GetFileName(path)}' — {ex.Message}");
                        }
                        continue;
                    }

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

    public void TryRestoreWindowBounds(bool positionOnly = false)
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
                if (positionOnly)
                {
                    // Restore position only — size will be set by ApplyCompactSize
                    NativeInterop.GetWindowRect(_hwnd, out var current);
                    var curW = current.Right - current.Left;
                    var curH = current.Bottom - current.Top;
                    NativeInterop.SetWindowPos(_hwnd, IntPtr.Zero, x, y, curW, curH, 0x0040 /* SWP_NOZORDER */);
                }
                else
                {
                    NativeInterop.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, 0x0040 /* SWP_NOZORDER */);
                }
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

    /// <summary>
    /// Revokes the OLE drop target registration for this window.
    /// Should be called during window close/cleanup. Logs a warning if revoke fails
    /// but does not throw, allowing shutdown to continue.
    /// </summary>
    public void CleanupOleDragDrop()
    {
        if (_oleDropTarget == null)
            return;

        try
        {
            int hr = NativeInterop.RevokeDragDrop(_hwnd);
            if (hr != 0)
            {
                _crashReporter.Log($"[WindowStateManager.CleanupOleDragDrop] RevokeDragDrop failed with HRESULT 0x{hr:X8} — continuing shutdown");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[WindowStateManager.CleanupOleDragDrop] RevokeDragDrop threw — {ex.Message}. Continuing shutdown");
        }
        finally
        {
            _oleDropTarget = null;
        }
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

    // ── OLE IDropTarget implementation ──────────────────────────────────────────

    /// <summary>
    /// COM-visible implementation of <see cref="NativeInterop.IDropTarget"/> that receives
    /// OLE drag-and-drop data (URLs from browsers/Discord via CF_UNICODETEXT, or file paths
    /// via CF_HDROP). Local file paths take priority over URL text per Requirement 1.3.
    /// </summary>
    [ComVisible(true)]
    internal class OleDropTarget : NativeInterop.IDropTarget
    {
        private readonly WindowStateManager _owner;
        private bool _acceptedDrag;

        public OleDropTarget(WindowStateManager owner)
        {
            _owner = owner;
        }

        public int DragEnter(NativeInterop.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            _acceptedDrag = false;

            // Check for CF_HDROP (local file paths)
            if (HasFormat(pDataObj, CF_HDROP))
            {
                _acceptedDrag = true;
                pdwEffect = DROPEFFECT_COPY;
                return 0; // S_OK
            }

            // Check for CF_UNICODETEXT (URL text from browsers/Discord)
            if (HasFormat(pDataObj, CF_UNICODETEXT))
            {
                _acceptedDrag = true;
                pdwEffect = DROPEFFECT_COPY;
                return 0;
            }

            pdwEffect = DROPEFFECT_NONE;
            return 0;
        }

        public int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            pdwEffect = _acceptedDrag ? DROPEFFECT_COPY : DROPEFFECT_NONE;
            return 0; // S_OK
        }

        public int DragLeave()
        {
            _acceptedDrag = false;
            return 0; // S_OK
        }

        public int Drop(NativeInterop.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            pdwEffect = DROPEFFECT_NONE;

            if (!_acceptedDrag)
                return 0;

            try
            {
                // CF_HDROP takes priority — local files always win (Req 1.3)
                if (HasFormat(pDataObj, CF_HDROP))
                {
                    var filePaths = ExtractHdrop(pDataObj);
                    if (filePaths != null && filePaths.Count > 0)
                    {
                        pdwEffect = DROPEFFECT_COPY;
                        _owner._crashReporter.Log($"[OleDropTarget.Drop] Received {filePaths.Count} file(s) via CF_HDROP — routing to HandleWin32Drop logic");

                        _owner._window.DispatcherQueue.TryEnqueue(async () =>
                        {
                            foreach (var path in filePaths)
                            {
                                var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";

                                if (ext is ".addon64" or ".addon32"
                                    && Path.GetFileName(path).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
                                {
                                    try { await _owner._dragDropHandler.ProcessDroppedAddon(path); }
                                    catch (Exception ex) { _owner._crashReporter.Log($"[OleDropTarget.Drop] Addon error — {ex.Message}"); }
                                    continue;
                                }

                                if (DragDropHandler.AllowedExtensions.Contains(ext) && ext != ".exe"
                                    && ext is not ".addon64" and not ".addon32")
                                {
                                    try { await _owner._dragDropHandler.ProcessDroppedArchive(path); }
                                    catch (Exception ex) { _owner._crashReporter.Log($"[OleDropTarget.Drop] Archive error — {ex.Message}"); }
                                    continue;
                                }

                                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    try { await _owner._dragDropHandler.ProcessDroppedExe(path); }
                                    catch (Exception ex) { _owner._crashReporter.Log($"[OleDropTarget.Drop] Exe error — {ex.Message}"); }
                                }
                            }
                        });

                        return 0;
                    }
                }

                // Fall back to CF_UNICODETEXT — extract URL text
                var text = ExtractUnicodeText(pDataObj);
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Try CF_TEXT as last resort
                    text = ExtractAnsiText(pDataObj);
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Discord and browsers may include extra text, newlines, or whitespace.
                    // Try each line to find a valid HTTP(S) URL with an addon extension.
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var url = line.Trim();
                        if (string.IsNullOrEmpty(url))
                            continue;

                        _owner._crashReporter.Log($"[OleDropTarget.Drop] Checking text line as URL: '{url}'");

                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            var filename = DragDropHandler.ExtractFileNameFromUrl(url);
                            if (filename != null)
                            {
                                var ext = Path.GetExtension(filename);
                                if (ext != null && (ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                                                 || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)))
                                {
                                    pdwEffect = DROPEFFECT_COPY;
                                    _owner._window.DispatcherQueue.TryEnqueue(async () =>
                                    {
                                        try
                                        {
                                            await _owner._dragDropHandler.ProcessDroppedUrl(url);
                                        }
                                        catch (Exception ex)
                                        {
                                            _owner._crashReporter.Log($"[OleDropTarget.Drop] ProcessDroppedUrl error — {ex.Message}");
                                        }
                                    });
                                    return 0;
                                }
                            }
                        }
                    }

                    _owner._crashReporter.Log($"[OleDropTarget.Drop] Dropped text does not contain a valid addon URL — ignored");
                }
            }
            catch (Exception ex)
            {
                _owner._crashReporter.Log($"[OleDropTarget.Drop] Unexpected error — {ex.Message}");
            }

            return 0;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>Returns true if the data object supports the given clipboard format.</summary>
        private static bool HasFormat(NativeInterop.IDataObject dataObj, ushort cfFormat)
        {
            var fmt = new FORMATETC
            {
                cfFormat = cfFormat,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED_HGLOBAL,
            };
            return dataObj.QueryGetData(ref fmt) == 0; // S_OK
        }

        /// <summary>Extracts file paths from CF_HDROP data.</summary>
        private static List<string>? ExtractHdrop(NativeInterop.IDataObject dataObj)
        {
            var fmt = new FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED_HGLOBAL,
            };

            if (dataObj.GetData(ref fmt, out var medium) != 0)
                return null;

            try
            {
                var hDrop = medium.unionmember;
                uint fileCount = NativeInterop.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                var paths = new List<string>((int)fileCount);

                for (uint i = 0; i < fileCount; i++)
                {
                    uint size = NativeInterop.DragQueryFile(hDrop, i, null, 0) + 1;
                    var buffer = new char[size];
                    NativeInterop.DragQueryFile(hDrop, i, buffer, size);
                    paths.Add(new string(buffer, 0, (int)(size - 1)));
                }

                return paths;
            }
            finally
            {
                NativeInterop.ReleaseStgMedium(ref medium);
            }
        }

        /// <summary>Extracts a Unicode string from CF_UNICODETEXT data.</summary>
        private static string? ExtractUnicodeText(NativeInterop.IDataObject dataObj)
        {
            var fmt = new FORMATETC
            {
                cfFormat = CF_UNICODETEXT,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED_HGLOBAL,
            };

            if (dataObj.GetData(ref fmt, out var medium) != 0)
                return null;

            try
            {
                var ptr = GlobalLock(medium.unionmember);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringUni(ptr);
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                NativeInterop.ReleaseStgMedium(ref medium);
            }
        }

        /// <summary>Extracts an ANSI string from CF_TEXT data.</summary>
        private static string? ExtractAnsiText(NativeInterop.IDataObject dataObj)
        {
            var fmt = new FORMATETC
            {
                cfFormat = CF_TEXT,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED_HGLOBAL,
            };

            if (dataObj.GetData(ref fmt, out var medium) != 0)
                return null;

            try
            {
                var ptr = GlobalLock(medium.unionmember);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringAnsi(ptr);
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                NativeInterop.ReleaseStgMedium(ref medium);
            }
        }
    }
}
