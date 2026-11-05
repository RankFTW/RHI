// MainWindow.Controller.cs -- controller support in the Spotlight layout.
// An Xbox-style controller (XInput, Services/ControllerInput.cs) is read only while RHI is the active
// window, so it never acts while you're in a game: launch one and RHI goes quiet by itself. Buttons
// held when RHI becomes active again are ignored until released.
//   D-pad / left stick  move focus        A  select           B  back (closes menus, the panel, Settings)
//   Y  Launch           X  game menu      LB / RB  previous / next game
//   Menu  Settings      View  Filters     Right stick  scroll
// Focus moves with WinUI's own XY search (FocusManager.FindNextElement), and is set as keyboard focus
// so the focus outline shows. Menus, dropdowns and dialogs (popups) get real Escape / Enter / arrow
// key presses instead, sent to RHI's own window and only while it's the active window.
// Setting: Settings › General › Windows and Admin Mode › Controller support (default on).

using System.Runtime.InteropServices;
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
    private Border? _padHints;
    private IntPtr _padHwnd;

    private const int PadFastMs = 16, PadSlowMs = 250, PadScanMs = 2000;
    private const int PadPageSize = 10; // LT / RT jump this many games
    private bool _padStepping;          // the controller is changing the selected cover (no ScrollIntoView)
    private ScrollViewer? _shelfScroller;
    private bool _shelfBringHooked;
    private long _shelfPadFocusUntil;

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
                ShowPadHints(false);
                return;
            }
            if (_padTimer == null)
            {
                _padHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _padTimer = DispatcherQueue.CreateTimer();
                _padTimer.Interval = TimeSpan.FromMilliseconds(PadSlowMs);
                _padTimer.Tick += (t, _) => PadTick();
                LifetimeToken.Register(() => DispatcherQueue.TryEnqueue(() => _padTimer?.Stop()));
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
                SetPadInterval(PadSlowMs);
                return;
            }

            long now = Environment.TickCount64;
            XInput.State state = default;
            if (_padSlot >= 0 && !XInput.TryGetState(_padSlot, out state))
            {
                CrashReporter.Log("[MainWindow.Controller] Controller disconnected");
                _padSlot = -1;
                _padActive = false;
            }
            if (_padSlot < 0)
            {
                SetPadInterval(PadSlowMs);
                if (now < _padNextScan) return;
                _padNextScan = now + PadScanMs;
                for (int i = 0; i < 4 && _padSlot < 0; i++)
                    if (XInput.TryGetState(i, out state)) _padSlot = i;
                if (_padSlot < 0) return;
                CrashReporter.Log($"[MainWindow.Controller] Controller found (slot {_padSlot})");
            }

            var g = state.Gamepad;
            ushort buttons = ControllerPoller.WithTriggers(g.Buttons, g.LeftTrigger, g.RightTrigger);
            if (!_padActive)
            {
                // RHI just became active (or the controller was just found): ignore what's held
                _padActive = true;
                _pad.Reset(buttons, g.ThumbLX, g.ThumbLY);
                SetPadInterval(PadFastMs);
                return;
            }
            SetPadInterval(PadFastMs);

            var actions = _pad.Update(buttons, g.ThumbLX, g.ThumbLY, g.ThumbRY, now, out var scroll);
            foreach (var a in actions) HandlePadAction(a);
            if (scroll != 0) PadScroll(scroll);
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
                PadBack(popup, xamlRoot);
                break;

            case PadAction.Launch:
                if (popup == null && onGames) layout.PadLaunch();
                break;

            case PadAction.GameMenu:
                if (popup == null && onGames) layout.PadGameMenu();
                break;

            case PadAction.PrevGame:
            case PadAction.NextGame:
                if (popup == null && onGames) StepGame(action == PadAction.NextGame ? 1 : -1, focused);
                break;

            case PadAction.PageBack:
            case PadAction.PageForward:
                // A page of games at a time, for big libraries
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
                if (ViewModel.CurrentPage == AppPage.Settings) ViewModel.NavigateToGameView();
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
            // Menus and dropdown lists move with the arrow keys; anything else in a popup (a dialog) by XY
            if (focused is SelectorItem or MenuFlyoutItemBase) { SendPadKey(ArrowKey(dir)); return; }
            if (popup.Child is UIElement child && (focused == null || !IsWithin(focused, child)))
            {
                if (FocusManager.FindFirstFocusableElement(child) is { } first) PadFocus(first);
                return;
            }
            PadFocusNext(dir, popup.Child);
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

        // The slide-in panel keeps focus inside it
        if (layout.IsPanelOpen && layout.PanelSheet is { } sheet)
        {
            if (!IsWithin(focused, sheet))
            {
                if (layout.FirstInPanel() is { } first) PadFocus(first);
                return;
            }
            PadFocusNext(dir, sheet);
            return;
        }

        if (ViewModel.CurrentPage == AppPage.GameView && _shelf != null)
        {
            if (IsWithin(focused, _shelf))
            {
                switch (dir)
                {
                    case FocusNavigationDirection.Left: StepGame(-1, focused); return;
                    case FocusNavigationDirection.Right: StepGame(1, focused); return;
                    case FocusNavigationDirection.Down:
                        if (layout.LaunchButton is { } launch) { PadFocus(launch); return; }
                        break;
                    case FocusNavigationDirection.Up:
                        // The filter tabs (All / Installed / …), whichever cover is selected
                        if (SelectedSegment() is { } seg) { PadFocus(seg); return; }
                        break;
                }
            }
            else if (dir == FocusNavigationDirection.Down && !(_filtersOpen && _chipsRow is { Visibility: Visibility.Visible })
                     && ((_searchRowStack != null && IsWithin(focused, _searchRowStack)) || (_countsRow != null && IsWithin(focused, _countsRow))))
            {
                // From the search / filter row straight back to the selected cover, not the nearest one
                FocusShelf();
                return;
            }
            else if (dir == FocusNavigationDirection.Up && layout.LaunchButton is { } launch
                     && (ReferenceEquals(focused, launch) || IsBesideLaunch(focused, launch)))
            {
                FocusShelf();
                return;
            }
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

    /// <summary>The Launch split button's other half (the menu chevron) also goes up to the shelf.</summary>
    private static bool IsBesideLaunch(DependencyObject focused, Button launch) =>
        focused is Button b && VisualTreeHelper.GetParent(b) is Grid g && ReferenceEquals(VisualTreeHelper.GetParent(launch), g);

    private void PadSelect(DependencyObject? focused, Popup? popup, XamlRoot xamlRoot)
    {
        var layout = _detailPanelBuilder.Layout;
        if (focused == null || (popup == null && _footer != null && IsWithin(focused, _footer))) { FocusHome(xamlRoot); return; }

        if (popup != null && focused is SelectorItem or MenuFlyoutItemBase)
        {
            SendPadKey(VkReturn);
            return;
        }

        // A cover in the shelf: go to its Launch button
        if (popup == null && _shelf != null && IsWithin(focused, _shelf))
        {
            if (layout.LaunchButton is { } launch) PadFocus(launch);
            return;
        }

        bool panelWasOpen = layout.IsPanelOpen, cogWasOpen = layout.IsCogInPanel;
        string? panelKeyBefore = layout.PanelKey;

        // All / Installed / Favourites / Hidden: picking one rebuilds the row, so refocus the same segment
        if (popup == null && _segmentHost != null && focused is Button segBtn && VisualTreeHelper.GetParent(segBtn) is Grid segGrid && IsWithin(focused, _segmentHost))
        {
            int segIndex = segGrid.Children.IndexOf(segBtn);
            var segPeer = FrameworkElementAutomationPeer.FromElement(segBtn) ?? FrameworkElementAutomationPeer.CreatePeerForElement(segBtn);
            if (segPeer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider segInvoke) segInvoke.Invoke();
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
                break;
            case ToggleSwitch toggle:
                toggle.IsOn = !toggle.IsOn;
                break;
            case TextBox:
                break; // typing needs a keyboard
            case UIElement element:
                var peer = FrameworkElementAutomationPeer.FromElement(element)
                           ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
                if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
                else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggleP) toggleP.Toggle();
                else if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand) expand.Expand();
                else SendPadKey(VkReturn);
                break;
            default:
                SendPadKey(VkReturn); // a Hyperlink in text
                break;
        }

        // Something opened in the panel (a section from a tile, or a cog's settings): focus its first control
        AfterLayout(() =>
        {
            if (TopPopup(xamlRoot, null) is { } opened)
            {
                if (focused is ComboBox) return; // an open dropdown puts focus on the current choice itself
                var now = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
                if ((now == null || opened.Child is not UIElement c || !IsWithin(now, c))
                    && opened.Child is UIElement child && FocusManager.FindFirstFocusableElement(child) is { } first)
                    PadFocus(first);
                return;
            }
            if (layout.IsPanelOpen && (!panelWasOpen || layout.PanelKey != panelKeyBefore || layout.IsCogInPanel != cogWasOpen))
                if (layout.FirstInPanel() is { } first) PadFocus(first);
        });
    }

    private void PadBack(Popup? popup, XamlRoot xamlRoot)
    {
        var layout = _detailPanelBuilder.Layout;
        if (popup != null)
        {
            // A menu when RHI isn't the active window (the dev pad script): keys can't be sent, so close it directly
            if (NativeInterop.GetForegroundWindow() != _padHwnd || _padHwnd == IntPtr.Zero)
            {
                if (popup.Child is MenuFlyoutPresenter) { popup.IsOpen = false; return; }
                if ((popup.Child as ContentDialog ?? FindChild<ContentDialog>(popup.Child)) is { } dialog) { dialog.Hide(); return; }
            }
            // Escape, as on the keyboard: a dropdown closes without changing the setting
            SendPadKey(VkEscape);
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

    /// <summary>LB / RB (and left / right on the shelf): the previous or next game.</summary>
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
        if (ViewModel.CurrentPage == AppPage.Settings) sv = FindChild<ScrollViewer>(SettingsPanel);
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
        DependencyObject? root = ViewModel.CurrentPage == AppPage.Settings ? SettingsPanel : xamlRoot.Content;
        if (root != null && FocusManager.FindFirstFocusableElement(root) is { } first) PadFocus(first);
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
    private static void PadFocusNext(FocusNavigationDirection dir, DependencyObject? searchRoot, DependencyObject? skip = null)
    {
        if (searchRoot == null) return;
        var options = new FindNextElementOptions { SearchRoot = searchRoot };
        if (FocusManager.FindNextElement(dir, options) is { } next && (skip == null || !IsWithin(next, skip)))
            PadFocus(next);
    }

    /// <summary>Keyboard focus, so the focus outline shows (and scroll areas bring it into view).</summary>
    private static void PadFocus(DependencyObject target)
    {
        try { _ = FocusManager.TryFocusAsync(target, FocusState.Keyboard); }
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

    // ── Button hints ──────────────────────────────────────────────────────────

    /// <summary>The "A Select · B Back …" bar: shown once the controller is used, hidden when the mouse moves.</summary>
    private void ShowPadHints(bool show)
    {
        try
        {
            if (!show)
            {
                if (_padHints != null) _padHints.Visibility = Visibility.Collapsed;
                return;
            }
            if (_padHints == null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
                foreach (var (key, label) in new[] { ("A", "Select"), ("B", "Back"), ("Y", "Launch"), ("X", "Game menu"),
                                                     ("LB / RB", "Game"), ("LT / RT", "Skip 10"), ("LS", "Filter"),
                                                     ("RS", "Home"), ("View", "Filters"), ("Menu", "Settings") })
                {
                    var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                    pair.Children.Add(new Border
                    {
                        Background = TabsTheme.B("#1E2738"),
                        BorderBrush = TabsTheme.B("#33405A"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(7, 1, 7, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = key, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = TabsTheme.B(TabsTheme.TextPrimary) },
                    });
                    pair.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = TabsTheme.B(TabsTheme.TextSecondary) });
                    row.Children.Add(pair);
                }
                _padHints = new Border
                {
                    Background = TabsTheme.B("#E6101520"),
                    BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 8, 16, 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 14),
                    IsHitTestVisible = false,
                    Child = row,
                    Visibility = Visibility.Collapsed,
                };
                Canvas.SetZIndex(_padHints, 50);
                Grid.SetColumnSpan(_padHints, Math.Max(1, GameViewPanel.ColumnDefinitions.Count));
                Grid.SetRowSpan(_padHints, Math.Max(1, GameViewPanel.RowDefinitions.Count));
                GameViewPanel.Children.Add(_padHints);
            }
            if (!_padPointerHooked && Content is UIElement root)
            {
                _padPointerHooked = true;
                root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((s, e) =>
                {
                    if (_padHints?.Visibility == Visibility.Visible
                        && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                        _padHints.Visibility = Visibility.Collapsed;
                }), handledEventsToo: true);
            }
            _padHints.Visibility = ViewModel.CurrentPage == AppPage.GameView ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowPadHints] {ex.Message}");
        }
    }

    // ── Key presses for popups ────────────────────────────────────────────────

    private const ushort VkReturn = 0x0D, VkEscape = 0x1B, VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;

    private static ushort ArrowKey(FocusNavigationDirection dir) => dir switch
    {
        FocusNavigationDirection.Up => VkUp,
        FocusNavigationDirection.Down => VkDown,
        FocusNavigationDirection.Left => VkLeft,
        _ => VkRight,
    };

    /// <summary>Presses a key in RHI's window. Only while RHI is the active window, so it can't type into anything else.</summary>
    private void SendPadKey(ushort vk)
    {
        if (NativeInterop.GetForegroundWindow() != _padHwnd || _padHwnd == IntPtr.Zero)
        {
            CrashReporter.Log($"[MainWindow.SendPadKey] RHI isn't the active window, key 0x{vk:X2} not sent");
            return;
        }
        uint flags = vk is VkLeft or VkUp or VkRight or VkDown ? KeyeventfExtendedKey : 0;
        var inputs = new[]
        {
            new PadInput { Type = InputKeyboard, Ki = new KeybdInput { Vk = vk, Flags = flags } },
            new PadInput { Type = InputKeyboard, Ki = new KeybdInput { Vk = vk, Flags = flags | KeyeventfKeyUp } },
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<PadInput>()) != inputs.Length)
            CrashReporter.Log($"[MainWindow.SendPadKey] SendInput failed ({Marshal.GetLastWin32Error()})");
    }

    private const uint InputKeyboard = 1, KeyeventfExtendedKey = 0x0001, KeyeventfKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT: the union is sized for its largest member (MOUSEINPUT, 32 bytes on x64), so pad to it
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct PadInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, PadInput[] inputs, int size);
}
