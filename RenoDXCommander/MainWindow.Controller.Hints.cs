// MainWindow.Controller.Hints.cs -- the controller's button hint bar (bottom centre) and its short
// messages ("Launching …"). The bar lists only the buttons that do something where focus is now, and
// hides when the mouse moves. Both sit over the game page and Settings, and never take clicks.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private StackPanel? _padHintRow;
    private string? _padHintKey;
    private Border? _padToast;
    private TextBlock? _padToastText;
    private DispatcherQueueTimer? _padToastTimer;

    /// <summary>Shows (or hides) the hint bar; shown, it's refreshed for where focus is.</summary>
    private void ShowPadHints(bool show)
    {
        try
        {
            if (!show)
            {
                if (_padHints != null) _padHints.Visibility = Visibility.Collapsed;
                SetPadHintRoom(false);
                return;
            }
            if (!EnsurePadOverlay()) return;
            UpdatePadHints();
            _padHints!.Visibility = Visibility.Visible;
            SetPadHintRoom(true);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowPadHints] {ex.Message}");
        }
    }

    /// <summary>Rebuilds the hint bar for the current place (only when it changed).</summary>
    private void UpdatePadHints()
    {
        if (_padHintRow == null || Content?.XamlRoot is not { } xamlRoot) return;
        var hints = CurrentPadHints(xamlRoot);
        var key = string.Join("|", hints.Select(h => h.Key + h.Label));
        if (key == _padHintKey) return;
        _padHintKey = key;
        _padHintRow.Children.Clear();
        foreach (var (button, label) in hints)
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
                Child = new TextBlock { Text = button, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = TabsTheme.B(TabsTheme.TextPrimary) },
            });
            pair.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = TabsTheme.B(TabsTheme.TextSecondary) });
            _padHintRow.Children.Add(pair);
        }
    }

    private double? _padSavedBottomPadding;

    /// <summary>
    /// While the bar shows, the game page gets extra room at the bottom so its last row of tiles can
    /// scroll clear of the bar (and focus never sits underneath it).
    /// </summary>
    private void SetPadHintRoom(bool on)
    {
        var sv = DetailScrollViewer;
        if (sv == null) return;
        var p = sv.Padding;
        if (on && _padSavedBottomPadding == null)
        {
            _padSavedBottomPadding = p.Bottom;
            sv.Padding = new Thickness(p.Left, p.Top, p.Right, p.Bottom + 64);
        }
        else if (!on && _padSavedBottomPadding is { } saved)
        {
            _padSavedBottomPadding = null;
            sv.Padding = new Thickness(p.Left, p.Top, p.Right, saved);
        }
    }

    /// <summary>What each button does right now, in the order they're listed.</summary>
    private List<(string Key, string Label)> CurrentPadHints(XamlRoot xamlRoot)
    {
        var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        var popup = TopPopup(xamlRoot, focused);
        var layout = _detailPanelBuilder.Layout;

        if (popup != null)
        {
            if (focused is ComboBoxItem) return new() { ("A", "Choose"), ("B", "Cancel") };
            if (focused is MenuFlyoutSubItem) return new() { ("A", "Open"), ("B", _padSubmenus.Count > 0 ? "Back" : "Close") };
            if (focused is MenuFlyoutItemBase) return new() { ("A", "Choose"), ("B", _padSubmenus.Count > 0 ? "Back" : "Close") };
            var dialogHints = new List<(string, string)> { ("A", "Select"), ("B", "Close") };
            if (PopupScroller(popup, focused) != null) dialogHints.Add(("Right stick", "Scroll"));
            return dialogHints;
        }

        if (ViewModel.CurrentPage == AppPage.Settings)
            return new() { ("A", "Select"), ("B", "Back to games"), ("LB / RB", "Section"), ("Right stick", "Scroll") };

        var hints = new List<(string, string)>();
        string? select = focused switch
        {
            TextBox => null,                   // typing needs a keyboard
            ComboBox => "Open",
            ToggleSwitch => "Switch",
            _ when focused != null && _shelf != null && IsWithin(focused, _shelf) => "Select",
            _ => "Select",
        };
        if (select != null) hints.Add(("A", select));

        if (layout.IsPanelOpen)
        {
            hints.Add(("B", layout.IsCogInPanel ? "Back" : "Close"));
            hints.Add(("LB / RB", "Section"));
            hints.Add(("Y", "Launch"));
            hints.Add(("RS", "Covers"));
            return hints;
        }

        bool onShelf = focused != null && _shelf != null && IsWithin(focused, _shelf);
        if (!onShelf) hints.Add(("B", "Covers"));
        hints.Add(("Y", "Launch"));
        hints.Add(("X", "Game menu"));
        hints.Add(("LB / RB", "Game"));
        hints.Add(("LT / RT", "Skip 10"));
        hints.Add(("LS", "Filter"));
        hints.Add(("View", "Filters"));
        hints.Add(("Menu", "Settings"));
        return hints;
    }

    /// <summary>A short message above the hint bar, e.g. "Launching Witcher 3…".</summary>
    private void ShowPadToast(string text, int ms = 3500)
    {
        try
        {
            if (!EnsurePadOverlay()) return;
            _padToastText!.Text = text;
            _padToast!.Visibility = Visibility.Visible;
            if (_padToastTimer == null)
            {
                _padToastTimer = DispatcherQueue.CreateTimer();
                _padToastTimer.IsRepeating = false;
                _padToastTimer.Tick += (t, _) => { if (_padToast != null) _padToast.Visibility = Visibility.Collapsed; };
            }
            _padToastTimer.Stop();
            _padToastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _padToastTimer.Start();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowPadToast] {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the hint bar and message once, in the grid that holds both the game page and Settings
    /// (so they show on either), and hides the bar when the mouse moves.
    /// </summary>
    private bool EnsurePadOverlay()
    {
        if (_padHints != null) return true;
        if (VisualTreeHelper.GetParent(GameViewPanel) is not Grid host) return false;

        _padHintRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
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
            Child = _padHintRow,
            Visibility = Visibility.Collapsed,
        };
        _padToastText = new TextBlock { FontSize = 14, Foreground = TabsTheme.B(TabsTheme.TextPrimary) };
        _padToast = new Border
        {
            Background = TabsTheme.B("#F01A2336"),
            BorderBrush = TabsTheme.B(TabsTheme.Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 10, 18, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 66), // just above the hint bar
            IsHitTestVisible = false,
            Child = _padToastText,
            Visibility = Visibility.Collapsed,
        };
        foreach (var el in new FrameworkElement[] { _padHints, _padToast })
        {
            Canvas.SetZIndex(el, 60);
            Grid.SetRowSpan(el, Math.Max(1, host.RowDefinitions.Count));
            Grid.SetColumnSpan(el, Math.Max(1, host.ColumnDefinitions.Count));
            host.Children.Add(el);
        }

        if (!_padPointerHooked && Content is UIElement root)
        {
            _padPointerHooked = true;
            root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((s, e) =>
            {
                if (_padHints?.Visibility == Visibility.Visible
                    && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                {
                    _padHints.Visibility = Visibility.Collapsed;
                    SetPadHintRoom(false);
                }
            }), handledEventsToo: true);
        }
        return true;
    }
}
