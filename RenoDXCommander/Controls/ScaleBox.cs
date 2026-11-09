using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace RenoDXCommander.Controls;

/// <summary>
/// Shows one child at <see cref="Factor"/> and takes up the scaled size in the layout (WinUI has no
/// LayoutTransform): the child is laid out at the available size ÷ scale, then drawn scaled.
/// Used for the top bar and game list sizes, on top of the window-wide interface size.
/// </summary>
public sealed class ScaleBox : Panel
{
    private readonly ScaleTransform _transform = new() { ScaleX = 1, ScaleY = 1 };
    private double _scale = 1.0;

    public double Factor
    {
        get => _scale;
        set
        {
            value = Math.Clamp(value, 0.25, 4.0);
            if (Math.Abs(value - _scale) < 0.0001) return;
            _scale = value;
            _transform.ScaleX = value;
            _transform.ScaleY = value;
            InvalidateMeasure();
        }
    }

    public UIElement? Child
    {
        get => Children.Count > 0 ? Children[0] : null;
        set
        {
            Children.Clear(); // a control can only have one parent
            if (value == null) return;
            value.RenderTransform = _transform;
            Children.Add(value);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return new Size(0, 0);
        child.Measure(new Size(availableSize.Width / _scale, availableSize.Height / _scale));
        return new Size(child.DesiredSize.Width * _scale, child.DesiredSize.Height * _scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width / _scale, finalSize.Height / _scale));
        return finalSize;
    }
}
