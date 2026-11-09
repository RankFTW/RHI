using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander.Controls;

/// <summary>
/// A thin horizontal strip that shows the up/down resize cursor and reports vertical drags
/// (Spotlight banner height, cover size, top bar size), or with <c>horizontal: true</c> a thin vertical
/// strip with the left/right cursor that reports sideways drags (game list width). Distances are measured against <see cref="Reference"/>, an element
/// that doesn't move while dragging and sits inside RHI's interface scaling, so they're in layout units and
/// the edge follows the pointer at any interface size. Double-click raises <see cref="Reset"/>.
/// </summary>
public sealed class ResizeGrip : Grid
{
    private static readonly SolidColorBrush Clear = new(Microsoft.UI.Colors.Transparent);
    private readonly Border _line;
    private uint? _pointerId;
    private double _startY;
    private readonly bool _horizontal;

    /// <summary>The element distances are measured in (window root under the scaling). Null = this grip's parent.</summary>
    public Func<UIElement?>? Reference { get; set; }

    public event Action? DragStarted;
    /// <summary>Total distance from where the drag started (down / right = positive).</summary>
    public event Action<double>? DragDelta;
    public event Action? DragCompleted;
    public event Action? Reset;

    public ResizeGrip(double thickness = 10, bool horizontal = false)
    {
        _horizontal = horizontal;
        if (horizontal) { Width = thickness; VerticalAlignment = VerticalAlignment.Stretch; }
        else { Height = thickness; HorizontalAlignment = HorizontalAlignment.Stretch; }
        Background = Clear; // hit-testable across the whole strip
        ProtectedCursor = InputSystemCursor.Create(horizontal ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
        _line = new Border
        {
            Height = horizontal ? 64 : 2,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = horizontal ? 2 : 64,
            Background = TabsTheme.B(TabsTheme.Accent),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        Children.Add(_line);

        PointerEntered += (s, e) => _line.Opacity = 0.8;
        PointerExited += (s, e) => { if (_pointerId == null) _line.Opacity = 0; };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += (s, e) => End(e);
        PointerCaptureLost += (s, e) => End(e);
        DoubleTapped += (s, e) => { e.Handled = true; Reset?.Invoke(); };
    }

    private double Along(Windows.Foundation.Point p) => _horizontal ? p.X : p.Y;

    private UIElement RefElement => Reference?.Invoke() ?? (Parent as UIElement) ?? this;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !pt.Properties.IsLeftButtonPressed) return;
        if (!CapturePointer(e.Pointer)) return;
        _pointerId = e.Pointer.PointerId;
        _startY = Along(e.GetCurrentPoint(RefElement).Position);
        _line.Opacity = 1;
        e.Handled = true;
        DragStarted?.Invoke();
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        e.Handled = true;
        DragDelta?.Invoke(Along(e.GetCurrentPoint(RefElement).Position) - _startY);
    }

    private void End(PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        _pointerId = null;
        ReleasePointerCaptures();
        _line.Opacity = 0;
        e.Handled = true;
        DragCompleted?.Invoke();
    }
}
