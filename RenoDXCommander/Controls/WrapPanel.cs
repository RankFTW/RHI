using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace RenoDXCommander.Controls;

public class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 4;

    // Pre-allocated arrays reused across layout passes — resized only when child count changes
    private (int start, double rowHeight)[] _rowStarts = [];
    private UIElement[] _visibleChildren = [];
    private int _lastChildCount;

    private void EnsureCapacity(int childCount)
    {
        if (childCount != _lastChildCount)
        {
            _lastChildCount = childCount;
            if (_visibleChildren.Length < childCount)
            {
                _visibleChildren = new UIElement[childCount];
            }
            if (_rowStarts.Length < childCount)
            {
                _rowStarts = new (int, double)[childCount];
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int childCount = Children.Count;
        double x = 0, rowHeight = 0, totalHeight = 0, maxWidth = 0;
        for (int i = 0; i < childCount; i++)
        {
            UIElement child = Children[i];
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(availableSize);
            var desired = child.DesiredSize;

            // Skip zero-size children to avoid invalid layout
            if (desired.Width <= 0 && desired.Height <= 0) continue;

            if (x > 0 && x + desired.Width > availableSize.Width)
            {
                maxWidth = Math.Max(maxWidth, x - HorizontalSpacing);
                totalHeight += rowHeight + VerticalSpacing;
                x = 0; rowHeight = 0;
            }
            x += desired.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }
        maxWidth = Math.Max(maxWidth, x > 0 ? x - HorizontalSpacing : 0);
        totalHeight += rowHeight;
        return new Size(maxWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int childCount = Children.Count;
        EnsureCapacity(childCount);

        // First pass: compute row heights using pre-allocated arrays
        int rowCount = 0;
        int visibleCount = 0;
        double x = 0, rowHeight = 0;
        int rowStart = 0;
        for (int i = 0; i < childCount; i++)
        {
            UIElement child = Children[i];
            if (child.Visibility == Visibility.Collapsed) continue;
            var desired = child.DesiredSize;

            // Skip zero-size children
            if (desired.Width <= 0 && desired.Height <= 0) continue;

            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                _rowStarts[rowCount++] = (rowStart, rowHeight);
                rowStart = visibleCount;
                x = 0; rowHeight = 0;
            }
            _visibleChildren[visibleCount++] = child;
            x += desired.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }
        if (visibleCount > rowStart)
            _rowStarts[rowCount++] = (rowStart, rowHeight);

        // Second pass: arrange with row height for stretch
        x = 0; double y = 0; int rowIdx = 0;
        int nextRowStart = rowCount > 1 ? _rowStarts[1].start : int.MaxValue;
        double currentRowHeight = rowCount > 0 ? _rowStarts[0].rowHeight : 0;
        for (int i = 0; i < visibleCount; i++)
        {
            if (i >= nextRowStart)
            {
                rowIdx++;
                y += currentRowHeight + VerticalSpacing;
                x = 0;
                currentRowHeight = _rowStarts[rowIdx].rowHeight;
                nextRowStart = rowIdx + 1 < rowCount ? _rowStarts[rowIdx + 1].start : int.MaxValue;
            }
            var child = _visibleChildren[i];
            var desired = child.DesiredSize;
            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                // shouldn't happen since we pre-computed, but safety
                y += currentRowHeight + VerticalSpacing;
                x = 0;
            }
            var h = (child is FrameworkElement fe && fe.VerticalAlignment == VerticalAlignment.Stretch)
                ? currentRowHeight : desired.Height;
            var arrangeY = (child is FrameworkElement fe2 && fe2.VerticalAlignment == VerticalAlignment.Stretch)
                ? y : y + (currentRowHeight - desired.Height) / 2;
            child.Arrange(new Rect(x, arrangeY, desired.Width, h));
            x += desired.Width + HorizontalSpacing;
        }
        return finalSize;
    }
}
