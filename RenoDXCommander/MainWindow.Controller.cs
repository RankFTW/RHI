// MainWindow.Controller.cs -- controller support in the Spotlight layout.
// An Xbox-style controller (XInput, Services/ControllerInput.cs), a PlayStation pad (Services/PlayStationPad.cs)
// or a Steam Controller (Services/SteamControllerPad.cs) is read only while RHI is the active window, so it never acts while you're in a game: launch one and RHI goes quiet by itself. Buttons
// held when RHI becomes active again are ignored until released.
//   D-pad / left stick  move              A  select            B  back (closes menus, the panel, Settings)
//   Y  Launch           X  game menu      LB / RB  previous / next game (in the panel: section;
//   LT / RT  10 games (hold to keep going)          on Settings: section)
//   Left stick click  All / Installed / Favourites  Right stick click  back to the covers
//   Menu  Settings      View  Filters     Right stick  scroll
// Moving goes by rows (MainWindow.Controller.Rows.cs); menus, dropdowns and dialogs are worked through
// their controls (MainWindow.Controller.Popups.cs); hints and messages: MainWindow.Controller.Hints.cs.
// Setting: Settings › General › Windows and Admin Mode › Controller support (default on).

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _padTimer;
    private readonly ControllerPoller _pad = new();
    private int _padSlot = -1;
    private long _padNextScan;
    private bool _padActive;
    private bool _padUnavailable;
    private bool _padPointerHooked;
    private Border? _padHints; // in the footer's centre cell (MainWindow.Controller.Hints.cs)
    private IntPtr _padHwnd;

    private const int PadFastMs = 16, PadSlowMs = 250, PadScanMs = 2000, PsScanMs = 4000;
    private readonly PlayStationPadReader _psPad = new();
    private readonly SteamControllerReader _steamPad = new();
    private volatile bool _psScanRunning;
    private long _psNextScan;
    private enum PadSource { XInput, PlayStation, Steam }
    private PadSource _padSource;
    private bool _padIsPlayStation => _padSource == PadSource.PlayStation;
    private const int PadPageSize = 10; // LT / RT jump this many games
    private bool _padStepping;          // the controller is changing the selected cover (no ScrollIntoView)
    private ScrollViewer? _shelfScroller;
    private bool _shelfBringHooked;
    private long _shelfPadFocusUntil;

    // Launching with Y: a second press soon after is ignored, and coming back to RHI focuses the cover
    private string? _padLaunchedGame;
    private long _padLaunchedAt;
    private bool _padReturnToCover;
    private const long PadRelaunchGuardMs = 8000;

    private static readonly SolidColorBrush PadFocusBrush = TabsTheme.B(TabsTheme.Accent);
    private static readonly SolidColorBrush PadClearBrush = new(Microsoft.UI.Colors.Transparent);

    /// <summary>Starts or stops reading the controller: Spotlight layout and the setting on.</summary>
    internal void UpdateControllerTimer()
    {
        try
        {
            bool want = ViewModel.Settings.ControllerSupport && !_padUnavailable && !IsShuttingDown
                && _detailPanelBuilder?.Layout.IsSpotlight == true;
            if (!want)
            {
                _padTimer?.Stop();
                _padActive = false;
                _psPad.Dispose(); // let go of a PlayStation pad / Steam Controller until controller support is back on
                _steamPad.Dispose();
                ShowPadHints(false);
                return;
            }
            if (_padTimer == null)
            {
                _padHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _padTimer = DispatcherQueue.CreateTimer();
                _padTimer.Interval = TimeSpan.FromMilliseconds(PadSlowMs);
                _padTimer.Tick += (t, _) => PadTick();
                LifetimeToken.Register(() =>
                {
                    _psPad.Dispose();
                    _steamPad.Dispose();
                    DispatcherQueue.TryEnqueue(() => _padTimer?.Stop());
                });
                CrashReporter.Log("[MainWindow.Controller] Controller support on");
            }
            if (!_padTimer.IsRunning) _padTimer.Start();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.UpdateControllerTimer] {ex.Message}");
        }
    }

    private void SetPadInterval(int ms)
    {
        if (_padTimer != null && _padTimer.Interval.TotalMilliseconds != ms)
            _padTimer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    private void PadTick()
    {
        try
        {
            if (IsShuttingDown) { _padTimer?.Stop(); return; }
            // Only while RHI is the active window (a game in front gets the controller, not RHI)
            if (NativeInterop.GetForegroundWindow() != _padHwnd)
            {
                _padActive = false;
                RestorePadPointer(); // never leave the pointer hidden over another app's turn
                SetPadInterval(PadSlowMs);
                return;
            }
            // The mouse is back in use: show the pointer and drop the hints
            if (PadPointerUsed()) ShowPadHints(false);

            long now = Environment.TickCount64;
            if (ReadPad(now) is not { } g)
            {
                SetPadInterval(PadSlowMs);
                return;
            }

            ushort buttons = ControllerPoller.WithTriggers(g.Buttons, g.LeftTrigger, g.RightTrigger);
            if (!_padActive)
            {
                // RHI just became active (or the controller was just found / changed): ignore what's held
                _padActive = true;
                _pad.Reset(buttons, g.LX, g.LY);
                SetPadInterval(PadFastMs);
                if (_padReturnToCover) ReturnToCoverAfterGame();
                return;
            }
            SetPadInterval(PadFastMs);

            var actions = _pad.Update(buttons, g.LX, g.LY, g.RY, now, out var scroll);
            foreach (var a in actions) HandlePadAction(a);
            if (scroll != 0) PadScroll(scroll);
            WatchForNewPopup();
        }
        catch (DllNotFoundException ex)
        {
            _padUnavailable = true;
            _padTimer?.Stop();
            CrashReporter.Log($"[MainWindow.Controller] XInput not available, controller support off — {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            _padUnavailable = true;
            _padTimer?.Stop();
            CrashReporter.Log($"[MainWindow.Controller] XInput not available, controller support off — {ex.Message}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PadTick] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The controller's state: an Xbox-style pad (XInput) first, else a PlayStation pad or a Steam
    /// Controller read directly (Services/PlayStationPad.cs, SteamControllerPad.cs). Null when none is
    /// connected. Preferring XInput means a pad that DS4Windows or Steam also shows as an Xbox pad isn't read twice.
    /// </summary>
    private PadState? ReadPad(long now)
    {
        if (_padSlot >= 0)
        {
            if (XInput.TryGetState(_padSlot, out var x)) return UsePadSource(PadSource.XInput, FromXInput(x));
            CrashReporter.Log("[MainWindow.Controller] Controller disconnected");
            _padSlot = -1;
            _padActive = false;
        }
        if (now >= _padNextScan)
        {
            _padNextScan = now + PadScanMs;
            for (int i = 0; i < 4; i++)
                if (XInput.TryGetState(i, out var x))
                {
                    _padSlot = i;
                    CrashReporter.Log($"[MainWindow.Controller] Controller found (slot {i})");
                    return UsePadSource(PadSource.XInput, FromXInput(x));
                }
        }

        if (_psPad.TryGetState(out var ps)) return UsePadSource(PadSource.PlayStation, ps);
        if (_steamPad.TryGetState(out var sc)) return UsePadSource(PadSource.Steam, sc);
        // A Steam dongle stays open with its controller off, so each kind is looked for on its own
        if (!_psPad.Connected || !_steamPad.Connected) StartNativePadScan(now);
        _padActive = false;
        return null;
    }

    private static PadState FromXInput(XInput.State s)
    {
        var g = s.Gamepad;
        return new PadState(g.Buttons, g.LeftTrigger, g.RightTrigger, g.ThumbLX, g.ThumbLY, g.ThumbRX, g.ThumbRY);
    }

    /// <summary>Notes which kind of pad is in use (for the hints); switching kinds starts fresh like a reconnect.</summary>
    private PadState UsePadSource(PadSource source, PadState state)
    {
        if (source != _padSource)
        {
            _padSource = source;
            _padActive = false;
            _padHintKey = null; // relabel the hints
        }
        return state;
    }

    /// <summary>Looks for a PlayStation pad and a Steam Controller in the background (device enumeration is slow), every few seconds.</summary>
    private void StartNativePadScan(long now)
    {
        if (_psScanRunning || now < _psNextScan) return;
        _psScanRunning = true;
        _psNextScan = now + PsScanMs;
        _ = Task.Run(() =>
        {
            try
            {
                if (!_psPad.Connected) _psPad.TryConnect();
                if (!_steamPad.Connected) _steamPad.TryConnect();
            }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.StartNativePadScan] {ex.GetType().Name}: {ex.Message}"); }
            finally { _psScanRunning = false; }
        });
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    internal void HandlePadAction(PadAction action)
    {
        var xamlRoot = Content?.XamlRoot;
        if (xamlRoot == null) return;
        ShowPadHints(true);

        var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        var popup = TopPopup(xamlRoot, focused);
        var layout = _detailPanelBuilder.Layout;
        bool onGames = ViewModel.CurrentPage == AppPage.GameView;
        bool onSettings = ViewModel.CurrentPage == AppPage.Settings;

        switch (action)
        {
            case PadAction.Up:
            case PadAction.Down:
            case PadAction.Left:
            case PadAction.Right:
                PadMove(action, focused, popup, xamlRoot);
                break;

            case PadAction.Select:
                PadSelect(focused, popup, xamlRoot);
                break;

            case PadAction.Back:
                PadBack(focused, popup, xamlRoot);
                break;

            case PadAction.Launch:
                if (popup == null && onGames) PadLaunch();
                break;

            case PadAction.GameMenu:
                if (popup == null && onGames) layout.PadGameMenu();
                break;

            case PadAction.PrevGame:
            case PadAction.NextGame:
                if (popup != null) break;
                int step = action == PadAction.NextGame ? 1 : -1;
                if (onSettings)
                {
                    // Settings: previous / next section, focus its first setting
                    if (_settingsHandler.PadJumpSection(step) is { } section)
                        AfterLayout(() => { if (FocusManager.FindFirstFocusableElement(section) is { } first) PadFocus(first); });
                }
                else if (onGames && layout.IsPanelOpen)
                {
                    // In the side panel: previous / next section
                    if (layout.StepPanel(step))
                        AfterLayout(() => { if (layout.FirstInPanel() is { } first) PadFocus(first); });
                }
                else if (onGames) StepGame(step, focused);
                break;

            case PadAction.PageBack:
            case PadAction.PageForward:
                // A page of games at a time, for big libraries (hold to keep going)
                if (popup == null && onGames) StepGame(action == PadAction.PageForward ? PadPageSize : -PadPageSize, focused);
                break;

            case PadAction.CycleFilter:
                if (popup == null && onGames) CycleMainFilter(focused);
                break;

            case PadAction.Home:
                // From anywhere back to the selected game's cover (closes the panel, leaves Settings)
                if (popup != null) break;
                if (!onGames) ViewModel.NavigateToGameView();
                if (layout.IsPanelOpen) layout.ClosePanel();
                AfterLayout(FocusShelf);
                break;

            case PadAction.Settings:
                if (popup != null) break;
                if (onSettings) ViewModel.NavigateToGameView();
                else SettingsButton_Click(SettingsBtn, new RoutedEventArgs());
                AfterLayout(() => FocusHome(xamlRoot));
                break;

            case PadAction.Filters:
                if (popup != null || !onGames || _filtersBtn == null) break;
                _filtersOpen = !_filtersOpen;
                RefreshTabsChrome();
                AfterLayout(() =>
                {
                    if (_filtersOpen && _chipsRow != null && FocusManager.FindFirstFocusableElement(_chipsRow) is { } chip)
                        PadFocus(chip);
                    else FocusHome(xamlRoot);
                });
                break;
        }

        // The hints follow focus; let focus and popups settle first
        AfterLayout(() => AfterLayout(UpdatePadHints));
    }

    private void PadMove(PadAction action, DependencyObject? focused, Popup? popup, XamlRoot xamlRoot)
    {
        var dir = action switch
        {
            PadAction.Up => FocusNavigationDirection.Up,
            PadAction.Down => FocusNavigationDirection.Down,
            PadAction.Left => FocusNavigationDirection.Left,
            _ => FocusNavigationDirection.Right,
        };
        var layout = _detailPanelBuilder.Layout;

        if (popup != null)
        {
            PopupMove(dir, focused, popup);
            return;
        }

        // Nothing focused, or focus left somewhere odd (the footer, after a panel closed under it): start at the shelf
        if (focused == null || focused is FrameworkElement { IsLoaded: false } || (_footer != null && IsWithin(focused, _footer)))
        {
            FocusHome(xamlRoot);
            return;
        }

        // Sliders: left / right change the value, up / down move on
        if (focused is Slider slider && dir is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
        {
            double step = slider.SmallChange > 0 ? slider.SmallChange : slider.StepFrequency > 0 ? slider.StepFrequency : 1;
            slider.Value = Math.Clamp(slider.Value + (dir == FocusNavigationDirection.Right ? step : -step), slider.Minimum, slider.Maximum);
            return;
        }

        // The slide-in panel keeps focus inside it, row by row
        if (layout.IsPanelOpen && layout.PanelSheet is { } sheet)
        {
            if (!IsWithin(focused, sheet))
            {
                if (layout.FirstInPanel() is { } first) PadFocus(first);
                return;
            }
            if (!PadRowMove(dir, focused, sheet, includeShelf: false, FindVisibleScrollViewer(sheet)))
                PadFocusNext(dir, sheet);
            return;
        }

        if (ViewModel.CurrentPage == AppPage.GameView && _shelf != null && Content is UIElement root)
        {
            // On the covers: left / right change the game, towards the page goes to Launch
            // (Down with the covers on top, Up with them at the bottom)
            if (IsWithin(focused, _shelf))
            {
                var towardsPage = ViewModel.Settings.SpotlightShelfAtBottom ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
                switch (dir)
                {
                    case FocusNavigationDirection.Left: StepGame(-1, focused); return;
                    case FocusNavigationDirection.Right: StepGame(1, focused); return;
                    case var d when d == towardsPage:
                        if (layout.LaunchButton is { IsLoaded: true } launch) { PadFocus(launch); return; }
                        break;
                }
            }
            if (PadRowMove(dir, focused, root, includeShelf: true, DetailScrollViewer)) return;
        }

        PadFocusNext(dir, xamlRoot.Content, skip: _footer);
    }

    /// <summary>The selected button in the All / Installed / Favourites / Hidden switch.</summary>
    private Button? SelectedSegment()
    {
        if ((_segmentHost?.Child as Border)?.Child is not Grid g) return null;
        Button? first = null;
        foreach (var child in g.Children)
            if (child is Button b)
            {
                if (b.Tag as string == TabsTheme.SegmentSelectedTag) return b;
                first ??= b;
            }
        return first;
    }

    private void PadSelect(DependencyObject? focused, Popup? popup, XamlRoot xamlRoot)
    {
        var layout = _detailPanelBuilder.Layout;
        if (focused == null || (popup == null && _footer != null && IsWithin(focused, _footer))) { FocusHome(xamlRoot); return; }

        // A popup opened by itself (What's New at startup) while focus is still behind it: go into it, don't press what's behind
        if (popup?.Child is UIElement popupChild && !IsWithin(focused, popupChild))
        {
            if (FirstInPopup(popupChild) is { } first) PadFocus(first);
            return;
        }
        if (popup != null && PopupSelect(focused)) return;

        if (popup == null)
        {
            // A cover: go to its Launch button
            if (_shelf != null && IsWithin(focused, _shelf))
            {
                if (layout.LaunchButton is { } launch) PadFocus(launch);
                return;
            }
            // The Launch button itself: launch with the controller's double-press guard and message
            if (ReferenceEquals(focused, layout.LaunchButton))
            {
                PadLaunch();
                return;
            }
        }

        bool panelWasOpen = layout.IsPanelOpen, cogWasOpen = layout.IsCogInPanel;
        string? panelKeyBefore = layout.PanelKey;

        // All / Installed / Favourites / Hidden: picking one rebuilds the row, so refocus the same segment
        if (popup == null && _segmentHost != null && focused is Button segBtn && VisualTreeHelper.GetParent(segBtn) is Grid segGrid && IsWithin(focused, _segmentHost))
        {
            int segIndex = segGrid.Children.IndexOf(segBtn);
            Press(segBtn);
            AfterLayout(() =>
            {
                if ((_segmentHost.Child as Border)?.Child is Grid g && segIndex >= 0 && segIndex < g.Children.Count)
                    PadFocus(g.Children[segIndex]);
            });
            return;
        }

        switch (focused)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = true;
                AfterLayout(() => FocusOpenCombo(combo));
                return;
            case ToggleSwitch toggle:
                toggle.IsOn = !toggle.IsOn;
                break;
            case TextBox:
                break; // typing needs a keyboard
            case UIElement element:
                Press(element);
                break;
        }

        // Something opened: a popup (menu, dialog) or the panel (a section from a tile, a cog's settings): focus its first control
        AfterLayout(() =>
        {
            if (TopPopup(xamlRoot, null) is { } opened)
            {
                var now = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
                if (opened.Child is UIElement child
                    && (now == null || !IsWithin(now, child) || (now is TextBox && FindPadStart(child) != null))
                    && FirstInPopup(child) is { } first)
                    PadFocus(first);
                return;
            }
            if (layout.IsPanelOpen && (!panelWasOpen || layout.PanelKey != panelKeyBefore || layout.IsCogInPanel != cogWasOpen))
                if (layout.FirstInPanel() is { } first) PadFocus(first);
        });
    }

    /// <summary>Presses a control as a click would (button, toggle, expander), through its automation peer.</summary>
    private static void Press(UIElement element)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand) expand.Expand();
        else CrashReporter.Log($"[MainWindow.Controller] A: nothing to press on {element.GetType().Name}");
    }

    private void PadBack(DependencyObject? focused, Popup? popup, XamlRoot xamlRoot)
    {
        var layout = _detailPanelBuilder.Layout;
        if (popup != null)
        {
            PopupBack(focused, popup, xamlRoot);
            return;
        }

        if (ViewModel.CurrentPage != AppPage.GameView)
        {
            ViewModel.NavigateToGameView();
            AfterLayout(() => FocusHome(xamlRoot));
            return;
        }

        if (layout.IsPanelOpen)
        {
            if (layout.IsCogInPanel)
            {
                layout.CloseCog(ContentDialogResult.None);
                AfterLayout(() => { if (layout.FirstInPanel() is { } first) PadFocus(first); });
                return;
            }
            var key = layout.PanelKey;
            layout.ClosePanel();
            AfterLayout(() => { if (layout.SpotTileFor(key) is { } tile) PadFocus(tile); else FocusHome(xamlRoot); });
            return;
        }

        FocusShelf();
    }

    private Popup? _padLastPopup;

    /// <summary>
    /// While the controller is in use (hint bar showing): when a dialog appears (it opens a moment
    /// after the press that asked for it, or by itself), move focus into it, to its "PadStart"
    /// control when it has one rather than a search box.
    /// </summary>
    private void WatchForNewPopup()
    {
        if (_padHints?.Visibility != Visibility.Visible || Content?.XamlRoot is not { } xamlRoot) return;
        var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        var popup = TopPopup(xamlRoot, focused);
        if (ReferenceEquals(popup, _padLastPopup)) return;
        _padLastPopup = popup;
        if (popup?.Child is not UIElement child || popup.Child is MenuFlyoutPresenter) return; // menus focus their first item themselves
        AfterLayout(() =>
        {
            var now = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
            bool outside = now == null || !IsWithin(now, child);
            if ((outside || (now is TextBox && FindPadStart(child) != null)) && FirstInPopup(child) is { } first)
                PadFocus(first);
            AfterLayout(UpdatePadHints);
        });
    }

    // ── Launch ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Y (or A on Launch): launches the selected game with a message, and ignores a second press for
    /// the same game for a few seconds (or while it's running). When RHI is active again afterwards,
    /// focus goes back to the game's cover.
    /// </summary>
    private void PadLaunch()
    {
        var layout = _detailPanelBuilder.Layout;
        if (ViewModel.SelectedGame is not { } game) return;
        string name = game.GameName;
        if (game.IsRunning)
        {
            ShowPadToast($"{name} is already running");
            return;
        }
        long now = Environment.TickCount64;
        if (string.Equals(_padLaunchedGame, name, StringComparison.OrdinalIgnoreCase) && now - _padLaunchedAt < PadRelaunchGuardMs)
        {
            ShowPadToast($"Already launching {name}…");
            return;
        }
        if (layout.LaunchButton is not { IsEnabled: true, IsLoaded: true })
        {
            ShowPadToast($"{name} can't be launched from here");
            return;
        }
        _padLaunchedGame = name;
        _padLaunchedAt = now;
        _padReturnToCover = true;
        CrashReporter.Log($"[MainWindow.PadLaunch] '{name}'");
        ShowPadToast($"Launching {name}…", 5000);
        layout.PadLaunch();
    }

    /// <summary>Back in RHI after launching: close the panel and focus the game's cover.</summary>
    private void ReturnToCoverAfterGame()
    {
        _padReturnToCover = false;
        if (ViewModel.CurrentPage != AppPage.GameView) return;
        if (Content?.XamlRoot is { } root && TopPopup(root, null) != null) return;
        var layout = _detailPanelBuilder.Layout;
        if (layout.IsPanelOpen) layout.ClosePanel();
        AfterLayout(FocusShelf);
    }

    // ── Games ─────────────────────────────────────────────────────────────────

    /// <summary>Left stick click: All → Installed → Favourites → All (from Hidden: All).</summary>
    private void CycleMainFilter(DependencyObject? focused)
    {
        string[] cycle = { "Detected", "Installed", "Favourites" };
        var active = ViewModel.ActiveFilters;
        int current = Array.FindIndex(cycle, active.Contains);
        string next = cycle[(current + 1) % cycle.Length];
        CrashReporter.Log($"[MainWindow.CycleMainFilter] {next}");
        Filter_Click(MainFilterButton(next), new RoutedEventArgs());
        // The shelf now shows another list: keep focus on its selected cover
        bool inShelf = focused != null && _shelf != null && IsWithin(focused, _shelf);
        if (inShelf) AfterLayout(FocusShelf);
    }

    /// <summary>LB / RB, LT / RT (and left / right on the shelf): another game.</summary>
    private void StepGame(int delta, DependencyObject? focused)
    {
        if (_shelf == null || ViewModel.DisplayedGames.Count == 0) return;
        var layout = _detailPanelBuilder.Layout;
        if (layout.IsPanelOpen) layout.ClosePanel();
        int count = ViewModel.DisplayedGames.Count;
        int index = Math.Clamp(_shelf.SelectedIndex + delta, 0, count - 1);
        if (index == _shelf.SelectedIndex && _shelf.SelectedIndex >= 0) return;
        _padStepping = true;
        try { _shelf.SelectedIndex = index; }
        finally { _padStepping = false; }
        CenterShelf(animate: true);
        bool inShelf = focused != null && IsWithin(focused, _shelf);
        if (inShelf) AfterLayout(FocusShelf);
    }

    /// <summary>
    /// Scrolls the shelf so the selected cover sits in the middle. Near either end the row can't
    /// scroll further, so the selection moves towards the edge instead.
    /// </summary>
    private void CenterShelf(bool animate)
    {
        if (_shelf == null || _shelf.SelectedIndex < 0) return;
        _shelfScroller ??= FindChild<ScrollViewer>(_shelf);
        if (_shelfScroller is not { ViewportWidth: > 0 } sv) return;
        HookShelfBringIntoView();
        // Covers are all the same width: measure one that's on screen, else use the tile size
        double slot = (_shelf.ContainerFromIndex(_shelf.SelectedIndex) as FrameworkElement)?.ActualWidth is > 0 and var w
            ? w + 8 : TileWidth + 8; // + item margin 4,0,4,0
        double centre = _shelf.Padding.Left + _shelf.SelectedIndex * slot + slot / 2;
        double offset = Math.Clamp(centre - sv.ViewportWidth / 2, 0, sv.ScrollableWidth);
        _shelfPadFocusUntil = Environment.TickCount64 + 400;
        sv.ChangeView(offset, null, null, disableAnimation: !animate);
    }

    /// <summary>
    /// Focusing a cover normally scrolls it just into view, which would fight the centring: while the
    /// controller is moving, the items panel swallows that request before the ScrollViewer sees it.
    /// </summary>
    private void HookShelfBringIntoView()
    {
        if (_shelfBringHooked || _shelf?.ItemsPanelRoot is not { } panel) return;
        _shelfBringHooked = true;
        panel.BringIntoViewRequested += (s, e) =>
        {
            if (Environment.TickCount64 < _shelfPadFocusUntil) e.Handled = true;
        };
    }

    private void PadScroll(double pixels)
    {
        ScrollViewer? sv;
        var layout = _detailPanelBuilder.Layout;
        var xamlRoot = Content?.XamlRoot;
        var focused = xamlRoot == null ? null : FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        if (xamlRoot != null && TopPopup(xamlRoot, focused) is { } popup) sv = PopupScroller(popup, focused);
        else if (ViewModel.CurrentPage == AppPage.Settings) sv = FindChild<ScrollViewer>(SettingsPanel);
        else if (layout.IsPanelOpen && layout.PanelSheet != null)
            sv = FindVisibleScrollViewer(layout.PanelSheet);
        else sv = DetailScrollViewer;
        if (sv == null) return;
        ShowPadHints(true);
        sv.ChangeView(null, Math.Clamp(sv.VerticalOffset + pixels, 0, sv.ScrollableHeight), null, disableAnimation: true);
    }

    // ── Focus helpers ─────────────────────────────────────────────────────────

    /// <summary>Where focus goes when nothing (or nothing visible) has it: the selected game's cover.</summary>
    private void FocusHome(XamlRoot xamlRoot)
    {
        if (ViewModel.CurrentPage == AppPage.GameView && _shelf != null && _shelf.Visibility == Visibility.Visible)
        {
            if (_detailPanelBuilder.Layout.IsPanelOpen && _detailPanelBuilder.Layout.FirstInPanel() is { } inPanel)
            {
                PadFocus(inPanel);
                return;
            }
            FocusShelf();
            return;
        }
        if (ViewModel.CurrentPage == AppPage.Settings && _settingsHandler.PadStartButton() is { } start)
        {
            PadFocus(start);
            return;
        }
        DependencyObject? root = ViewModel.CurrentPage == AppPage.Settings ? SettingsPanel : xamlRoot.Content;
        if (root != null && FocusManager.FindFirstFocusableElement(root) is { } first) PadFocus(first);
    }

    /// <summary>
    /// Settings has just opened: if the controller is in use (its hints are showing), focus the
    /// shown section's button on the left, so Down / Up and A work straight away. Mouse users
    /// (hints hidden) keep normal focus.
    /// </summary>
    internal void PadFocusSettingsStart()
    {
        try
        {
            if (_padHints?.Visibility != Visibility.Visible || ViewModel.CurrentPage != AppPage.Settings) return;
            if (Content?.XamlRoot is { } root && TopPopup(root, null) != null) return;
            if (_settingsHandler.PadStartButton() is { } start) PadFocus(start);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PadFocusSettingsStart] {ex.Message}");
        }
    }

    private void FocusShelf()
    {
        if (_shelf == null) return;
        if (_shelf.SelectedItem is { } item)
        {
            if (_shelf.ContainerFromItem(item) is ListViewItem container)
            {
                CenterShelf(animate: true);
                PadFocus(container);
                return;
            }
            // Far off screen (not built yet): jump there, then focus it once it's built
            CenterShelf(animate: false);
            AfterLayout(() =>
            {
                if (_shelf.ContainerFromItem(item) is ListViewItem built) PadFocus(built);
                else PadFocus(_shelf);
            });
            return;
        }
        PadFocus(_shelf);
    }

    /// <summary>Moves focus to the nearest control in that direction, never into <paramref name="skip"/> (the footer).</summary>
    private void PadFocusNext(FocusNavigationDirection dir, DependencyObject? searchRoot, DependencyObject? skip = null)
    {
        if (searchRoot == null) return;
        var options = new FindNextElementOptions { SearchRoot = searchRoot };
        // WinUI's search skips controls covered by something hit-testable, and the mouse shield
        // (Controller.Pointer.cs) covers the whole window: let it through for the search only.
        // Synchronous, so no pointer hit-test runs in between.
        var shield = _padPointerShield;
        bool lowered = shield is { IsHitTestVisible: true, Visibility: Visibility.Visible };
        if (lowered) shield!.IsHitTestVisible = false;
        DependencyObject? next;
        try { next = FocusManager.FindNextElement(dir, options); }
        finally { if (lowered) shield!.IsHitTestVisible = true; }
        if (next != null && (skip == null || !IsWithin(next, skip)))
            PadFocus(next);
    }

    /// <summary>
    /// Keyboard focus, so the focus outline shows (and scroll areas bring it into view). The outline is
    /// RHI's blue, matching the selected cover's ring, instead of Windows' white and black one.
    /// </summary>
    private static void PadFocus(DependencyObject target)
    {
        try
        {
            if (target is FrameworkElement fe && !ReferenceEquals(fe.FocusVisualPrimaryBrush, PadFocusBrush))
            {
                fe.FocusVisualPrimaryBrush = PadFocusBrush;
                fe.FocusVisualPrimaryThickness = new Thickness(2);
                fe.FocusVisualSecondaryBrush = PadClearBrush;
                fe.FocusVisualSecondaryThickness = new Thickness(0);
            }
            _ = FocusManager.TryFocusAsync(target, FocusState.Keyboard);
        }
        catch (Exception ex) { CrashReporter.Log($"[MainWindow.PadFocus] {ex.Message}"); }
    }

    /// <summary>Runs after the current change has been laid out (panels open, tiles rebuilt).</summary>
    private void AfterLayout(Action action) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try { action(); }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.Controller] {ex.Message}"); }
        });

    /// <summary>An open menu, dropdown, flyout or dialog (not tooltips), preferring the one with focus.</summary>
    private static Popup? TopPopup(XamlRoot xamlRoot, DependencyObject? focused)
    {
        Popup? any = null;
        foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            if (!p.IsHitTestVisible || p.Child is null or ToolTip || !p.Child.IsHitTestVisible) continue;
            if (focused != null && IsWithin(focused, p.Child)) return p;
            any ??= p;
        }
        return any;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (var cur = element; cur != null; cur = VisualTreeHelper.GetParent(cur) ?? (cur as FrameworkElement)?.Parent)
            if (ReferenceEquals(cur, ancestor)) return true;
        return false;
    }

    private static ScrollViewer? FindVisibleScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { Visibility: Visibility.Collapsed }) continue;
            if (child is ScrollViewer sv) return sv;
            if (FindVisibleScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
