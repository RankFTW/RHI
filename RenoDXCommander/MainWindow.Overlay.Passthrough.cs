// MainWindow.Overlay.Passthrough.cs -- lets the overlay hotkey reach games that have the RHI overlay switched off
// (Spotlight › Game settings). RegisterHotKey takes the key from every program, so while such a game is in front
// the hotkey is let go, and taken back when anything else comes to the front. Foreground changes come from a
// WinEvent hook, installed only while the overlay is on and at least one game has it switched off.
using RenoDXCommander.Overlay;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private OverlayNative.WinEventDelegate? _foregroundHookProc; // keep the native callback alive
    private IntPtr _foregroundHook;
    private bool _hotkeyPassthrough;    // hotkey let go because a game with the overlay off is in front
    private bool _overlayHotkeyRefused; // Windows refused the key: leave registration alone
    private uint _passthroughPid;       // last foreground process looked at (0 = none)

    /// <summary>
    /// Installs or removes the foreground hook to match Settings and the per-game switches, and lets go of or
    /// takes back the hotkey for the window in front now. Call after either changes. UI thread.
    /// </summary>
    internal void RefreshOverlayPassthrough()
    {
        try
        {
            _passthroughPid = 0; // switches may have changed: look at the window in front again
            bool want = _overlayHotkey != null && !IsShuttingDown && !_overlayHotkeyRefused
                        && ViewModel.Settings.RhiOverlayEnabled && ViewModel.AnyOverlayDisabled;
            if (!want)
            {
                RemoveForegroundHook();
                if (_hotkeyPassthrough) TakeBackHotkey("no game has the overlay off");
                return;
            }
            if (_foregroundHook == IntPtr.Zero)
            {
                _foregroundHookProc ??= ForegroundChanged;
                _foregroundHook = OverlayNative.SetWinEventHook(OverlayNative.EVENT_SYSTEM_FOREGROUND, OverlayNative.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _foregroundHookProc, 0, 0, OverlayNative.WINEVENT_OUTOFCONTEXT | OverlayNative.WINEVENT_SKIPOWNPROCESS);
                if (_foregroundHook == IntPtr.Zero)
                    CrashReporter.Log("[MainWindow.RefreshOverlayPassthrough] Could not watch the window in front — the hotkey stays registered");
                else
                    CrashReporter.Log("[MainWindow.RefreshOverlayPassthrough] Watching the window in front (games with the overlay off)");
            }
            CheckForegroundForPassthrough(OverlayNative.GetForegroundWindow());
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RefreshOverlayPassthrough] {ex.Message}");
        }
    }

    private void RemoveForegroundHook()
    {
        if (_foregroundHook == IntPtr.Zero) return;
        OverlayNative.UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
        _passthroughPid = 0;
    }

    private void ForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint eventThread, uint eventTime)
    {
        // Leave the native callback before doing any work
        try { DispatcherQueue?.TryEnqueue(() => CheckForegroundForPassthrough(hwnd)); }
        catch (Exception ex) { CrashReporter.Log($"[MainWindow.ForegroundChanged] Could not queue — {ex.Message}"); }
    }

    private void CheckForegroundForPassthrough(IntPtr hwnd)
    {
        try
        {
            if (_foregroundHook == IntPtr.Zero || _overlayHotkey == null || IsShuttingDown || hwnd == IntPtr.Zero) return;
            OverlayNative.GetWindowThreadProcessId(hwnd, out var pid);
            // RHI's own windows (main window, overlay) don't change anything: opening the overlay keeps the state
            if (pid == 0 || pid == (uint)Environment.ProcessId || pid == _passthroughPid) return;
            _passthroughPid = pid;

            var offGame = OverlayOffGameFor((int)pid);
            if (offGame != null && !_hotkeyPassthrough)
            {
                _overlayHotkey.Unregister();
                _hotkeyPassthrough = true;
                CrashReporter.Log($"[MainWindow.CheckForegroundForPassthrough] '{offGame}' in front has the overlay off — hotkey passed to the game");
            }
            else if (offGame == null && _hotkeyPassthrough)
                TakeBackHotkey("another window is in front");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.CheckForegroundForPassthrough] {ex.Message}");
        }
    }

    private void TakeBackHotkey(string why)
    {
        _hotkeyPassthrough = false;
        if (_overlayHotkey == null || IsShuttingDown || !ViewModel.Settings.RhiOverlayEnabled) return;
        CrashReporter.Log($"[MainWindow.TakeBackHotkey] Hotkey back — {why}");
        _overlayHotkey.Register(ViewModel.Settings.RhiOverlayHotkey);
    }

    /// <summary>The name of the library game process <paramref name="pid"/> belongs to when it has the overlay off, else null.</summary>
    private string? OverlayOffGameFor(int pid)
    {
        var cards = ViewModel.AllCards;
        var offCards = cards.Where(c => ViewModel.IsOverlayDisabled(c.GameName, c.Source ?? "")).ToList();
        if (offCards.Count == 0) return null;
        var exe = GetProcessImagePath(pid);
        if (exe == null) return null;
        // Cheap test against the switched-off games first; then make sure no other game's folder fits better
        if (ForegroundGameResolver.MatchCard(offCards, exe) == null) return null;
        var best = ForegroundGameResolver.MatchCard(cards, exe);
        return best != null && ViewModel.IsOverlayDisabled(best.GameName, best.Source ?? "") ? best.GameName : null;
    }
}
