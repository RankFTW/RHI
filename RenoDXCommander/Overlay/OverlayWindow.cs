// OverlayWindow.cs -- the RHI overlay: a borderless, always-on-top RHI window shown over the game's monitor
// when the overlay hotkey is pressed (MainWindow.Overlay.cs). Nothing is loaded into the game. Created on
// first use, then hidden and reused so it opens instantly. Built in C# (no XAML).
// Closing: the hotkey again, Esc, Back to game, the game closing, or the overlay losing focus.
// Layout: top bar (game, clock, Back to game), left menu, the chosen panel (OverlayWindow.Panels.cs).
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

using Microsoft.UI.Xaml.Input;

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Overlay;

internal sealed partial class OverlayWindow : Window
{
    private readonly Func<string> _hotkeyText;
    private readonly Func<RemoteManifest?> _manifest;
    private readonly Func<RenoDXCommander.ViewModels.SettingsViewModel> _settings;
    private readonly Func<string, string?> _screenshotFolder; // RHI's screenshot folder for a game name
    private readonly IntPtr _hwnd;
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();
    private readonly TextBlock _clock = new();
    private readonly TextBlock _hint = new();
    private readonly Button _backButton;
    private readonly DispatcherQueueTimer _timer;
    private ForegroundGame? _game;
    private DateTime? _gameStart;
    private int _openCount;     // bumped on every open: background reads drop results meant for an earlier one
    private bool _closing;      // our own Close(): ignore the deactivation it causes
    private bool _allowDestroy; // only RHI shutting down really closes the window

    /// <summary>True while the overlay is shown.</summary>
    internal bool IsOpen { get; private set; }

    internal OverlayWindow(Func<string> hotkeyText, Func<RemoteManifest?> manifest,
        Func<RenoDXCommander.ViewModels.SettingsViewModel> settings, Func<string, string?> screenshotFolder)
    {
        _hotkeyText = hotkeyText;
        _manifest = manifest;
        _settings = settings;
        _screenshotFolder = screenshotFolder;
        Title = "RHI Overlay";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        try { SystemBackdrop = new OverlayBackdrop(); }
        catch (Exception ex) { CrashReporter.Log($"[OverlayWindow] Acrylic unavailable, solid backdrop — {ex.Message}"); }

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false; // not in Alt+Tab or the taskbar

        _backButton = new Button { Content = "Back to game" };
        TabsTheme.Apply(_backButton, TabsTheme.BlueButton, height: 44, fontSize: 15);
        _backButton.Click += (_, _) => Close(returnToGame: true);

        Content = BuildContent();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Tick();

        Activated += (_, e) =>
        {
            // Clicking another window or Alt+Tab hides the overlay without forcing the game back to the front
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen && !_closing)
                Close(returnToGame: false);
        };
        Closed += (_, e) =>
        {
            if (_allowDestroy) return;
            e.Handled = true; // Alt+F4 and the like: hide, keep the window for next time
            Close(returnToGame: true);
        };
    }

    private Grid BuildContent()
    {
        var root = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x0B, 0x0F, 0x17)), // light dim; the game stays visible
            Padding = new Thickness(56, 40, 56, 32),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; GoBack(); }
            else if (ViewerOpen && e.Key == Windows.System.VirtualKey.Left && FocusManager.GetFocusedElement(root.XamlRoot) is not TextBox)
            { e.Handled = true; OpenViewer(_viewerIndex - 1); }
            else if (ViewerOpen && e.Key == Windows.System.VirtualKey.Right && FocusManager.GetFocusedElement(root.XamlRoot) is not TextBox)
            { e.Handled = true; OpenViewer(_viewerIndex + 1); }
        };

        // Top: game name and store on the left, clock and Back to game on the right
        var top = new Grid { ColumnSpacing = 24 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 4 };
        titles.Children.Add(new TextBlock { Text = "RHI OVERLAY", FontSize = 12, CharacterSpacing = 120, Foreground = TabsTheme.B(TabsTheme.TextMuted) });
        _title.FontSize = 36;
        _title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _title.Foreground = TabsTheme.B(TabsTheme.TextPrimary);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        titles.Children.Add(_title);
        _subtitle.FontSize = 15;
        _subtitle.Foreground = TabsTheme.B(TabsTheme.TextSecondary);
        titles.Children.Add(_subtitle);
        top.Children.Add(titles);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, VerticalAlignment = VerticalAlignment.Top };
        _clock.FontSize = 30;
        _clock.Foreground = TabsTheme.B(TabsTheme.TextPrimary);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(_clock);
        right.Children.Add(_backButton);
        Grid.SetColumn(right, 1);
        top.Children.Add(right);
        root.Children.Add(top);

        // Body: the dashboard (columns that adapt to the width) and the screenshot viewer over it
        var body = BuildDashboardShell();
        body.Margin = new Thickness(0, 28, 0, 14);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        _hint.FontSize = 13;
        _hint.Foreground = TabsTheme.B(TabsTheme.TextMuted);
        Grid.SetRow(_hint, 2);
        root.Children.Add(_hint);
        return root;
    }

    /// <summary>Shows the overlay over <paramref name="game"/>'s monitor.</summary>
    internal void Open(ForegroundGame game)
    {
        _game = game;
        _openCount++;
        _gameStart = null;
        try { _gameStart = System.Diagnostics.Process.GetProcessById(game.ProcessId).StartTime; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CrashReporter.Log($"[OverlayWindow.Open] Can't read the game's start time — {ex.Message}");
        }

        var card = game.Card;
        _title.Text = card?.GameName ?? (game.ExePath != null ? Path.GetFileNameWithoutExtension(game.ExePath) : "Unknown game");
        _subtitle.Text = card?.Source ?? "";
        // Cover the game's whole monitor (not just the work area); its shape sizes the screenshot thumbnails
        var info = new OverlayNative.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<OverlayNative.MONITORINFO>() };
        var monitor = OverlayNative.MonitorFromWindow(game.Hwnd, OverlayNative.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero && OverlayNative.GetMonitorInfo(monitor, ref info))
        {
            var r = info.rcMonitor;
            AppWindow.MoveAndResize(new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            if (r.Right > r.Left && r.Bottom > r.Top) _thumbAspect = (double)(r.Bottom - r.Top) / (r.Right - r.Left);
        }

        UpdateHint();
        BuildDashboard();
        Tick();

        IsOpen = true;
        _closing = false;
        Activate();
        if (!OverlayNative.SetForegroundWindow(_hwnd))
        {
            // Opened from the controller, Windows may refuse (the last input went to the game): one Alt tap
            // lifts the foreground lock, as when RHI comes back after a game closes (MainWindow.ReturnAfterGame.cs)
            NativeInterop.keybd_event(NativeInterop.VK_MENU, 0, 0, UIntPtr.Zero);
            NativeInterop.keybd_event(NativeInterop.VK_MENU, 0, NativeInterop.KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (!OverlayNative.SetForegroundWindow(_hwnd)) CrashReporter.Log("[OverlayWindow.Open] Windows refused to bring the overlay to the front");
        }
        _backButton.Focus(FocusState.Programmatic);
        _timer.Start();
        // Optional: freeze the game so it doesn't react to the controller (Settings › RHI overlay, off by default)
        if (_settings().RhiOverlayPauseGame && card != null) GamePauser.Pause(game.ProcessId);
        CrashReporter.Log($"[OverlayWindow.Open] Over '{_title.Text}' (PID {game.ProcessId}, known: {card != null}, notification state {OverlayNative.QueryNotificationState()})");
    }

    /// <summary>Hides the overlay; <paramref name="returnToGame"/> puts the game back in front.</summary>
    internal void Close(bool returnToGame)
    {
        if (!IsOpen) return;
        SaveNotes();
        StopShotWatcher();
        IsOpen = false;
        _closing = true;
        _timer.Stop();
        _padUsed = false;
        try
        {
            GamePauser.Resume(); // before the game gets the focus back
            AppWindow.Hide();
            if (returnToGame && _game != null && OverlayNative.IsWindow(_game.Hwnd))
                OverlayNative.SetForegroundWindow(_game.Hwnd);
        }
        finally { _closing = false; }
        CrashReporter.Log($"[OverlayWindow.Close] Closed (back to game: {returnToGame})");
    }

    /// <summary>RHI is shutting down: really close the window.</summary>
    internal void Destroy()
    {
        _timer.Stop();
        _allowDestroy = true;
        GamePauser.Resume();
        try { base.Close(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            CrashReporter.Log($"[OverlayWindow.Destroy] {ex.Message}");
        }
    }

    private void Tick()
    {
        _clock.Text = DateTime.Now.ToString("t");
        if (_game != null && IsOpen && !OverlayNative.IsWindow(_game.Hwnd))
        {
            Close(returnToGame: false); // the game closed
            return;
        }
        UpdateSessionText();
    }

    /// <summary>The game the overlay is open over (null when closed).</summary>
    internal RenoDXCommander.ViewModels.GameCardViewModel? CurrentCard => IsOpen ? _game?.Card : null;

    /// <summary>Esc / B: out of the screenshot viewer first, then back to the game.</summary>
    private void GoBack()
    {
        if (ViewerOpen) { CloseViewer(); return; }
        Close(returnToGame: true);
    }

    private bool _padUsed;

    private void UpdateHint()
    {
        _hint.Text = _padUsed
            ? "D-pad: move    A: select    B: back    LB / RB: previous / next column (screenshot when one is open)    Right stick: scroll    Hold View + Menu: close"
            : $"Press {_hotkeyText()} or Esc to go back to the game";
    }

    /// <summary>A controller action while the overlay is open (MainWindow.Overlay.cs reads the controller).</summary>
    internal void HandlePad(PadAction action)
    {
        if (!IsOpen || Content?.XamlRoot is not { } xamlRoot) return;
        if (!_padUsed) { _padUsed = true; UpdateHint(); }
        var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        switch (action)
        {
            case PadAction.Up:
            case PadAction.Down:
            case PadAction.Left:
            case PadAction.Right:
                var dir = action switch
                {
                    PadAction.Up => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up,
                    PadAction.Down => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down,
                    PadAction.Left => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left,
                    _ => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right,
                };
                // The screenshot viewer: left / right step through screenshots unless a button row has focus
                if (focused is not Button && ViewerOpen && action is PadAction.Left or PadAction.Right)
                {
                    OpenViewer(_viewerIndex + (action == PadAction.Left ? -1 : 1));
                    break;
                }
                var options = new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = Content };
                if (focused == null) { FocusColumn(0); break; }
                if (Microsoft.UI.Xaml.Input.FocusManager.FindNextElement(dir, options) is Control next)
                    next.Focus(FocusState.Keyboard);
                break;
            case PadAction.Select:
                if (focused is Button b && b.IsEnabled
                    && new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(b) is { } peer)
                    peer.Invoke();
                else if (focused is TextBox tb) tb.Focus(FocusState.Keyboard);
                break;
            case PadAction.Back:
                GoBack();
                break;
            case PadAction.PrevGame:
                if (ViewerOpen) OpenViewer(_viewerIndex - 1, "Previous"); // a screenshot is open: previous one
                else StepColumn(focused, -1);
                break;
            case PadAction.NextGame:
                if (ViewerOpen) OpenViewer(_viewerIndex + 1, "Next");
                else StepColumn(focused, 1);
                break;
        }
    }

    /// <summary>Right stick: scroll the column with focus (or the screenshot viewer).</summary>
    internal void PadScroll(double pixels)
    {
        if (!IsOpen || pixels == 0 || Content?.XamlRoot is not { } xamlRoot) return;
        var sv = ViewerOpen ? _viewerScroller
            : ScrollerOf(FocusManager.GetFocusedElement(xamlRoot) as DependencyObject) ?? FirstVisibleScroller();
        sv?.ChangeView(null, Math.Max(0, sv.VerticalOffset + pixels), null, disableAnimation: true);
    }

    internal static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalMinutes < 1) return "less than a minute";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} min";
        return $"{(int)t.TotalHours} h {t.Minutes} min";
    }
}
