// OverlayHotkeyService.cs -- the RHI overlay's system-wide hotkey. RegisterHotKey on RHI's main window (which
// keeps receiving messages while hidden in the tray) and a window subclass that catches WM_HOTKEY.
// WM_HOTKEY lets RHI take the foreground, so the overlay can come to the front over a game.
using RenoDXCommander.Services;

namespace RenoDXCommander.Overlay;

internal sealed class OverlayHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5248; // "RH"
    private static readonly UIntPtr SubclassId = new(2); // 1 = ForegroundActivationTarget

    private readonly IntPtr _hwnd;
    private readonly Func<Action, bool> _enqueue;
    private readonly Action _pressed;
    private readonly NativeInterop.SubclassProc _procedure; // keep the native callback alive
    private bool _subclassed;
    private bool _disposed;

    /// <summary>True while the hotkey is registered.</summary>
    internal bool IsRegistered { get; private set; }

    internal OverlayHotkeyService(IntPtr hwnd, Func<Action, bool> enqueue, Action pressed)
    {
        _hwnd = hwnd;
        _enqueue = enqueue;
        _pressed = pressed;
        _procedure = WindowProcedure;
        _subclassed = NativeInterop.SetWindowSubclass(hwnd, _procedure, SubclassId, UIntPtr.Zero);
        if (!_subclassed) CrashReporter.Log("[OverlayHotkeyService] Could not hook the main window — hotkey unavailable");
    }

    /// <summary>
    /// Registers <paramref name="hotkey"/> ("vk,ctrl,shift,alt"), replacing any earlier one. Returns false when
    /// Windows refuses it (usually another program has the same key). Must run on the UI thread.
    /// </summary>
    internal bool Register(string hotkey)
    {
        Unregister();
        if (_disposed || !_subclassed) return false;
        var (vk, shift, ctrl, alt) = HotkeyManager.ParseHotkeyString(hotkey);
        if (vk <= 0) return false;
        uint mods = OverlayNative.MOD_NOREPEAT
                  | (alt ? OverlayNative.MOD_ALT : 0)
                  | (ctrl ? OverlayNative.MOD_CONTROL : 0)
                  | (shift ? OverlayNative.MOD_SHIFT : 0);
        IsRegistered = OverlayNative.RegisterHotKey(_hwnd, HotkeyId, mods, (uint)vk);
        if (IsRegistered)
            CrashReporter.Log($"[OverlayHotkeyService.Register] {HotkeyManager.FormatHotkeyDisplay(hotkey)} registered");
        else
            CrashReporter.Log($"[OverlayHotkeyService.Register] {HotkeyManager.FormatHotkeyDisplay(hotkey)} refused (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}) — likely used by another program");
        return IsRegistered;
    }

    internal void Unregister()
    {
        if (!IsRegistered) return;
        OverlayNative.UnregisterHotKey(_hwnd, HotkeyId);
        IsRegistered = false;
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        UIntPtr subclassId, UIntPtr referenceData)
    {
        if (message == 0x0082) // WM_NCDESTROY
            Dispose();
        else if (message == OverlayNative.WM_HOTKEY && wParam.ToInt64() == HotkeyId)
        {
            // Leave the native callback before touching WinUI
            try { _enqueue(() => { if (!_disposed) _pressed(); }); }
            catch (Exception ex) { CrashReporter.Log($"[OverlayHotkeyService] Could not queue the hotkey — {ex.Message}"); }
            return IntPtr.Zero;
        }
        return NativeInterop.DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _disposed = true;
        if (_subclassed) NativeInterop.RemoveWindowSubclass(_hwnd, _procedure, SubclassId);
        _subclassed = false;
    }
}
