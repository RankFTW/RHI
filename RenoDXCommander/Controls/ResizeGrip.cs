using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander.Controls;

/// <summary>
/// A thin horizontal strip that shows the up/down resize cursor and reports vertical drags
/// (Spotlight banner height, cover size). Distances are measured against <see cref="Reference"/>, an element
/// that doesn't move while dragging and sits inside RHI's interface scaling, so they're in layout units and
/// the edge follows the pointer at any interface size. Double-click raises <see cref="Reset"/>.
/// </summary>
public sealed class ResizeGrip : Grid
{
    private static readonly SolidColorBrush Clear = new(Microsoft.UI.Colors.Transparent);
    private readonly Border _line;
    private uint? _pointerId;
    private double _startY;

    /// <summary>The element distances are measured in (window root under the scaling). Null = this grip's parent.</summary>
    public Func<UIElement?>? Reference { get; set; }

    public event Action? DragStarted;
    /// <summary>Total vertical distance from where the drag started (down = positive).</summary>
    public event Action<double>? DragDelta;
    public event Action? DragCompleted;
    public event Action? Reset;

    public ResizeGrip(double thickness = 10)
    {
        Height = thickness;
        Background = Clear; // hit-testable across the whole strip
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        _line = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 64,
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

    private UIElement RefElement => Reference?.Invoke() ?? (Parent as UIElement) ?? this;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !pt.Properties.IsLeftButtonPressed) return;
        if (!CapturePointer(e.Pointer)) return;
        _pointerId = e.Pointer.PointerId;
        _startY = e.GetCurrentPoint(RefElement).Position.Y;
        _line.Opacity = 1;
        e.Handled = true;
        DragStarted?.Invoke();
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        e.Handled = true;
        DragDelta?.Invoke(e.GetCurrentPoint(RefElement).Position.Y - _startY);
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
