// MainWindow.Controller.Rows.cs -- controller up / down / left / right on the Spotlight page and in
// the slide-in panel. The focusable controls are grouped into rows by their position on screen
// (top bar, filter row, covers, the header's buttons, each line of tiles…). Up / Down always go to
// the row above / below, landing on the control straight above / below; Left / Right stay in the
// row. WinUI's nearest-control search picked a different control depending on where things
// happened to sit; rows don't.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>
    /// A place focus can go. <c>Bounds</c> is on screen (for left / right distances); rows are built
    /// from <c>Zone</c> + <c>Top</c>: zone 0 is fixed above the scrolling content (top bar, filters,
    /// covers, a panel's title), zone 1 is the scrolling content, measured inside it so scrolling
    /// doesn't reorder the rows, zone 2 is fixed below it (Spotlight covers at the bottom).
    /// </summary>
    private sealed record PadTarget(Control Element, Rect Bounds, int Zone, double Top, bool IsShelf);

    /// <summary>
    /// Moves by rows inside <paramref name="root"/>. False when the focused control isn't one of the
    /// row targets (the caller falls back to the nearest-control search).
    /// </summary>
    private bool PadRowMove(FocusNavigationDirection dir, DependencyObject focused, UIElement root, bool includeShelf, ScrollViewer? scroller)
    {
        var targets = CollectPadTargets(root, includeShelf, scroller);
        if (targets.Count == 0) return false;
        var rows = GroupIntoRows(targets);

        int rowIndex = -1, colIndex = -1;
        for (int r = 0; r < rows.Count && rowIndex < 0; r++)
            for (int c = 0; c < rows[r].Count; c++)
            {
                var t = rows[r][c];
                bool hit = t.IsShelf ? _shelf != null && IsWithin(focused, _shelf) : IsWithin(focused, t.Element);
                if (hit) { rowIndex = r; colIndex = c; break; }
            }
        if (rowIndex < 0) return false;

        var row = rows[rowIndex];
        var current = row[colIndex];
        switch (dir)
        {
            case FocusNavigationDirection.Left:
            case FocusNavigationDirection.Right:
                int next = colIndex + (dir == FocusNavigationDirection.Right ? 1 : -1);
                if (next >= 0 && next < row.Count) FocusPadTarget(row[next]);
                return true; // at the end of a row: stay put

            default:
                int target = rowIndex + (dir == FocusNavigationDirection.Down ? 1 : -1);
                if (target < 0 || target >= rows.Count) return true;
                FocusPadTarget(PickInRow(rows[target], current));
                return true;
        }
    }

    /// <summary>
    /// The control to land on in a row: Launch / the selected filter tab when the row has one, else
    /// the one straight above / below (its width covers the current control's middle), else the
    /// nearest across. No memory of earlier visits: that made Up / Down jump sideways in grids.
    /// </summary>
    private PadTarget PickInRow(List<PadTarget> row, PadTarget from)
    {
        var launch = _detailPanelBuilder.Layout.LaunchButton;
        // While a collection is shown no segment is selected (SelectedSegment falls back to the first): use the active chip
        var segment = SelectedSegment() is { Tag: TabsTheme.SegmentSelectedTag } selectedSegment ? selectedSegment : null;
        var chip = ActiveCollectionChip();
        foreach (var t in row)
            if (ReferenceEquals(t.Element, launch) || ReferenceEquals(t.Element, segment) || ReferenceEquals(t.Element, chip)) return t;

        double x = from.Bounds.X + from.Bounds.Width / 2;
        static double Gap(Rect r, double x) => x < r.Left ? r.Left - x : x > r.Right ? x - r.Right : 0;
        return row.OrderBy(t => Gap(t.Bounds, x))
                  .ThenBy(t => Math.Abs(t.Bounds.X + t.Bounds.Width / 2 - x))
                  .First();
    }

    private void FocusPadTarget(PadTarget t)
    {
        if (t.IsShelf) FocusShelf();
        else PadFocus(t.Element);
    }

    /// <summary>
    /// Every control a controller can land on under <paramref name="root"/>, with its bounds. Hidden,
    /// see-through, disabled and non-tab-stop controls are skipped, as is the footer; the cover shelf
    /// counts as one target (the selected cover).
    /// </summary>
    private List<PadTarget> CollectPadTargets(UIElement root, bool includeShelf, ScrollViewer? scroller)
    {
        var list = new List<PadTarget>();
        var scrollContent = scroller?.Content as UIElement;
        // Fixed controls below the scrolling area (Spotlight covers at the bottom) come after it: zone 2
        double scrollerBottom = scroller != null && BoundsIn(root, scroller) is { } sb ? sb.Bottom : double.MaxValue;
        void Walk(DependencyObject node, bool inScroll)
        {
            if (node is UIElement u && (u.Visibility != Visibility.Visible || u.Opacity <= 0.01 || !u.IsHitTestVisible)) return;
            if (_footer != null && ReferenceEquals(node, _footer)) return;
            if (scrollContent != null && ReferenceEquals(node, scrollContent)) inScroll = true;
            if (_shelf != null && ReferenceEquals(node, _shelf))
            {
                if (includeShelf && Add(_shelf, ShelfAnchor(), inScroll) is { } shelf) list.Add(shelf with { IsShelf = true });
                return;
            }
            if (node is Control c && c.IsTabStop && c.IsEnabled && c is not ListViewBase and not ScrollViewer
                && c.ActualWidth > 0 && c.ActualHeight > 0)
            {
                if (Add(c, c, inScroll) is { } t) list.Add(t);
                return; // a control's insides aren't separate stops
            }
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(node, i), inScroll);
        }
        PadTarget? Add(Control element, FrameworkElement measure, bool inScroll)
        {
            if (BoundsIn(root, measure) is not { } screen) return null;
            double top = screen.Y;
            if (inScroll && BoundsIn(scrollContent!, measure) is { } inside) top = inside.Y;
            int zone = inScroll ? 1 : screen.Y >= scrollerBottom - 1 ? 2 : 0;
            return new PadTarget(element, screen, zone, top, false);
        }
        Walk(root, false);
        return list;
    }

    /// <summary>The selected cover, or the whole shelf when it isn't built.</summary>
    private FrameworkElement ShelfAnchor() =>
        _shelf!.SelectedItem is { } item && _shelf.ContainerFromItem(item) is FrameworkElement container ? container : _shelf;

    private static Rect? BoundsIn(UIElement root, FrameworkElement element)
    {
        try { return element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (ArgumentException) { return null; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>
    /// Rows, top to bottom, each sorted left to right. A control joins a row when its middle is
    /// within the height of the row's first (topmost) control.
    /// </summary>
    private static List<List<PadTarget>> GroupIntoRows(List<PadTarget> targets)
    {
        var rows = new List<List<PadTarget>>();
        double top = 0, bottom = 0;
        int zone = -1;
        foreach (var t in targets.OrderBy(t => t.Zone).ThenBy(t => t.Top))
        {
            double middle = t.Top + t.Bounds.Height / 2;
            if (rows.Count > 0 && t.Zone == zone && middle >= top && middle <= bottom) rows[^1].Add(t);
            else
            {
                rows.Add(new List<PadTarget> { t });
                zone = t.Zone;
                top = t.Top;
                bottom = t.Top + t.Bounds.Height;
            }
        }
        foreach (var r in rows) r.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        return rows;
    }
}
