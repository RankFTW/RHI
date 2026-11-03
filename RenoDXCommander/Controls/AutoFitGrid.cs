using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace RenoDXCommander.Controls;

/// <summary>
/// CSS "grid-template-columns: repeat(auto-fit, minmax(MinColumnWidth, 1fr))": as many equal
/// columns as fit at MinColumnWidth or wider, filling the width; items in a row share its height.
/// Used by the Tabs layout (mockup A). Needs a finite width (the detail panel sets one).
/// </summary>
public class AutoFitGrid : Panel
{
    public double MinColumnWidth { get; set; } = 220;
    public double ColumnSpacing { get; set; } = 12;
    public double RowSpacing { get; set; } = 12;
    public int MaxColumns { get; set; } = int.MaxValue;

    private (int Columns, double ColumnWidth) Columns(double width, int itemCount)
    {
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0) width = MinColumnWidth;
        int n = (int)Math.Floor((width + ColumnSpacing) / (MinColumnWidth + ColumnSpacing));
        n = Math.Clamp(n, 1, Math.Max(1, MaxColumns));
        // auto-fit (not auto-fill): with fewer items than columns, the items stretch to fill the row
        if (itemCount > 0) n = Math.Min(n, itemCount);
        double colW = Math.Max(0, (width - (n - 1) * ColumnSpacing) / n);
        return (n, colW);
    }

    private List<UIElement> Visible()
    {
        var list = new List<UIElement>(Children.Count);
        foreach (var c in Children)
            if (c.Visibility == Visibility.Visible) list.Add(c);
        return list;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Visible();
        var (n, colW) = Columns(availableSize.Width, items.Count);
        double total = 0;
        for (int start = 0; start < items.Count; start += n)
        {
            double rowH = 0;
            for (int i = start; i < Math.Min(start + n, items.Count); i++)
            {
                items[i].Measure(new Size(colW, double.PositiveInfinity));
                rowH = Math.Max(rowH, items[i].DesiredSize.Height);
            }
            total += rowH + (start > 0 ? RowSpacing : 0);
        }
        double width = double.IsInfinity(availableSize.Width) ? n * colW + (n - 1) * ColumnSpacing : availableSize.Width;
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Visible();
        var (n, colW) = Columns(finalSize.Width, items.Count);
        double y = 0;
        for (int start = 0; start < items.Count; start += n)
        {
            int end = Math.Min(start + n, items.Count);
            double rowH = 0;
            for (int i = start; i < end; i++) rowH = Math.Max(rowH, items[i].DesiredSize.Height);
            for (int i = start; i < end; i++)
            {
                int col = i - start;
                items[i].Arrange(new Rect(col * (colW + ColumnSpacing), y, colW, rowH));
            }
            y += rowH + RowSpacing;
        }
        return finalSize;
    }
}
