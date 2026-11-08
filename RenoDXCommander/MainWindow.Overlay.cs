// MainWindow.Overlay.cs -- wires the RHI overlay (Overlay/) into the main window: the system-wide hotkey,
// the controller (hold View + Menu to open or close; navigation while it's open), finding the game in front,
// and RHI's own playtime count. See docs/rhi-overlay-plan.md.
using Microsoft.UI.Dispatching;

using RenoDXCommander.Models;
using RenoDXCommander.Overlay;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private OverlayHotkeyService? _overlayHotkey;
    private OverlayWindow? _overlayWindow;

    // Controller: read on its own timer, separate from the Spotlight controller code (which only runs while
    // RHI's main window is in front). Holding View + Menu toggles the overlay; while it's open the pad drives it.
    private DispatcherQueueTimer? _overlayPadTimer;
    private readonly ControllerPoller _overlayPoller = new();
    private bool _overlayPadActive;
    private int _overlayPadSlot = -1;
    private long _overlayPadNextScan;
    private long _overlayNativeNextScan;
    private long _overlayComboSince = -1;
    private bool _overlayComboUsed;
    private const ushort OverlayCombo = ControllerPoller.Start | ControllerPoller.BackBtn;
    private const long OverlayComboHoldMs = 500;
    private const int OverlayPadIdleMs = 100, OverlayPadFastMs = 16, OverlayPadNoneMs = 500;

    // Playtime: once a minute, the game in front gets a minute
    private DispatcherQueueTimer? _playtimeTimer;
    private int _playtimeTicks;

    /// <summary>Starts the overlay (once, after startup). Runs whether RHI starts shown or in the tray.</summary>
    private void InitRhiOverlay()
    {
        if (IsShuttingDown || _overlayHotkey != null) return;
        try
        {
            _overlayHotkey = new OverlayHotkeyService(_windowStateManager.Hwnd,
                action => DispatcherQueue.TryEnqueue(() => action()), ToggleRhiOverlay);
            ApplyRhiOverlaySetting();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.InitRhiOverlay] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Registers or removes the hotkey (and starts / stops the controller and playtime timers) to match
    /// Settings. Returns false when the overlay is on but Windows refused the key (another program has it).
    /// </summary>
    internal bool ApplyRhiOverlaySetting()
    {
        if (_overlayHotkey == null) return true;
        UpdateOverlayTimers();
        if (!ViewModel.Settings.RhiOverlayEnabled)
        {
            _overlayHotkey.Unregister();
            _overlayWindow?.Close(returnToGame: true);
            CrashReporter.Log("[MainWindow.ApplyRhiOverlaySetting] Overlay off");
            return true;
        }
        return _overlayHotkey.Register(ViewModel.Settings.RhiOverlayHotkey);
    }

    /// <summary>True when the overlay is on and its hotkey is registered.</summary>
    internal bool RhiOverlayHotkeyActive => _overlayHotkey?.IsRegistered == true;

    private string RhiOverlayHotkeyText() => HotkeyManager.FormatHotkeyDisplay(ViewModel.Settings.RhiOverlayHotkey);

    private void ToggleRhiOverlay()
    {
        if (IsShuttingDown || !ViewModel.Settings.RhiOverlayEnabled) return;
        try
        {
            if (_overlayWindow?.IsOpen == true)
            {
                _overlayWindow.Close(returnToGame: true);
                return;
            }
            var game = ForegroundGameResolver.Resolve(ViewModel.AllCards);
            if (game == null)
            {
                CrashReporter.Log("[MainWindow.ToggleRhiOverlay] RHI (or nothing) in front — not opened");
                return;
            }
            _overlayWindow ??= new OverlayWindow(RhiOverlayHotkeyText, () => ViewModel.Manifest,
                () => ViewModel.Settings, BuildScreenshotSavePath);
            _overlayWindow.Open(game);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ToggleRhiOverlay] Failed — {ex}");
        }
    }

    private void UpdateOverlayTimers()
    {
        bool on = ViewModel.Settings.RhiOverlayEnabled && !IsShuttingDown;

        bool pad = on && ViewModel.Settings.RhiOverlayController;
        if (pad && _overlayPadTimer == null)
        {
            _overlayPadTimer = DispatcherQueue.CreateTimer();
            _overlayPadTimer.Interval = TimeSpan.FromMilliseconds(OverlayPadIdleMs);
            _overlayPadTimer.Tick += (_, _) => OverlayPadTick();
        }
        if (pad) _overlayPadTimer!.Start();
        else _overlayPadTimer?.Stop();

        if (on && _playtimeTimer == null)
        {
            _playtimeTimer = DispatcherQueue.CreateTimer();
            _playtimeTimer.Interval = TimeSpan.FromMinutes(1);
            _playtimeTimer.Tick += (_, _) => PlaytimeTick();
        }
        if (on) _playtimeTimer!.Start();
        else _playtimeTimer?.Stop();
    }

    // ── Controller ────────────────────────────────────────────────────────────

    private void OverlayPadTick()
    {
        try
        {
            if (IsShuttingDown) { _overlayPadTimer?.Stop(); return; }
            bool open = _overlayWindow?.IsOpen == true;
            // RHI's own window in front: the Spotlight controller code has the pad
            if (!open && NativeInterop.GetForegroundWindow() == _windowStateManager.Hwnd)
            {
                ResetOverlayCombo();
                SetOverlayPadInterval(OverlayPadIdleMs);
                return;
            }

            long now = Environment.TickCount64;
            if (ReadOverlayPad(now) is not { } g)
            {
                ResetOverlayCombo();
                SetOverlayPadInterval(OverlayPadNoneMs);
                return;
            }
            ushort buttons = ControllerPoller.WithTriggers(g.Buttons, g.LeftTrigger, g.RightTrigger);

            // Hold View + Menu: open / close
            if ((buttons & OverlayCombo) == OverlayCombo)
            {
                if (_overlayComboSince < 0) _overlayComboSince = now;
                else if (!_overlayComboUsed && now - _overlayComboSince >= OverlayComboHoldMs)
                {
                    _overlayComboUsed = true;
                    _overlayPadActive = false; // ignore what's held when the overlay appears
                    ToggleRhiOverlay();
                    open = _overlayWindow?.IsOpen == true;
                }
            }
            else ResetOverlayCombo();

            if (!open)
            {
                _overlayPadActive = false;
                SetOverlayPadInterval(OverlayPadIdleMs);
                return;
            }
            SetOverlayPadInterval(OverlayPadFastMs);
            if (!_overlayPadActive)
            {
                _overlayPadActive = true;
                _overlayPoller.Reset(buttons, g.LX, g.LY);
                return;
            }
            var actions = _overlayPoller.Update(buttons, g.LX, g.LY, g.RY, now, out var scroll);
            foreach (var a in actions)
                if (a is not (PadAction.Settings or PadAction.Filters)) // Menu / View are the close combo
                    _overlayWindow!.HandlePad(a);
            if (scroll != 0) _overlayWindow!.PadScroll(scroll);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _overlayPadTimer?.Stop();
            CrashReporter.Log($"[MainWindow.OverlayPadTick] XInput not available — {ex.Message}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.OverlayPadTick] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ResetOverlayCombo()
    {
        _overlayComboSince = -1;
        _overlayComboUsed = false;
    }

    private void SetOverlayPadInterval(int ms)
    {
        if (_overlayPadTimer != null && _overlayPadTimer.Interval.TotalMilliseconds != ms)
            _overlayPadTimer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>An Xbox-style pad first, else a PlayStation pad or Steam Controller (shared readers). Null when none.</summary>
    private PadState? ReadOverlayPad(long now)
    {
        if (_overlayPadSlot >= 0)
        {
            if (XInput.TryGetState(_overlayPadSlot, out var x)) return FromXInput(x);
            _overlayPadSlot = -1;
        }
        if (now >= _overlayPadNextScan)
        {
            _overlayPadNextScan = now + 3000;
            for (int i = 0; i < 4; i++)
                if (XInput.TryGetState(i, out var x)) { _overlayPadSlot = i; return FromXInput(x); }
        }
        if (_psPad.TryGetState(out var ps)) return ps;
        if (_steamPad.TryGetState(out var sc)) return sc;
        // Device enumeration is slow: look for PlayStation / Steam pads now and then
        if ((!_psPad.Connected || !_steamPad.Connected) && now >= _overlayNativeNextScan)
        {
            _overlayNativeNextScan = now + 10000;
            StartNativePadScan(now);
        }
        return null;
    }

    // ── Playtime ──────────────────────────────────────────────────────────────

    private void PlaytimeTick()
    {
        try
        {
            if (IsShuttingDown) return;
            var card = _overlayWindow?.CurrentCard ?? ForegroundGameResolver.Resolve(ViewModel.AllCards)?.Card;
            if (card != null)
                GameJournalStore.AddPlaytime(GameKey.FromCard(card.GameName, card.Source).ToKey(), TimeSpan.FromMinutes(1));
            if (++_playtimeTicks % 5 == 0)
                _ = Task.Run(GameJournalStore.Save);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PlaytimeTick] {ex.Message}");
        }
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    private void DisposeRhiOverlay()
    {
        try
        {
            _overlayPadTimer?.Stop();
            _playtimeTimer?.Stop();
            GameJournalStore.Save();
            _overlayHotkey?.Dispose();
            _overlayHotkey = null;
            _overlayWindow?.Destroy();
            _overlayWindow = null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DisposeRhiOverlay] {ex.Message}");
        }
    }
}
