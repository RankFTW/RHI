// MainWindow.SpotlightShelfDrag.cs -- Spotlight: hold the left mouse button on the covers and drag left / right
// to scroll them (as well as the scroll bar and the wheel). A click without movement still selects a cover;
// once the pointer moves past a small threshold it scrolls, and letting go selects nothing.

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const double ShelfDragThreshold = 6;

    private uint? _shelfDragPointer;
    private bool _shelfDragging;
    private double _shelfDragStartX, _shelfDragStartOffset;
    private ScrollViewer? _shelfDragScroller;

    private void HookShelfDrag()
    {
        if (_shelf == null) return;
        // handledEventsToo: the ListViewItems handle the press themselves (selection)
        _shelf.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ShelfDrag_Pressed), true);
        _shelf.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(ShelfDrag_Moved), true);
        _shelf.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ShelfDrag_Released), true);
        _shelf.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ShelfDrag_CaptureLost), true);
    }

    private void ShelfDrag_Pressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_shelf == null || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return; // touch pans by itself
            if (!e.GetCurrentPoint(_shelf).Properties.IsLeftButtonPressed) return;
            if (IsInScrollBar(e.OriginalSource as DependencyObject)) return; // the scroll bar works as before
            _shelfDragScroller ??= FindChild<ScrollViewer>(_shelf);
            if (_shelfDragScroller == null) return;
            _shelfDragPointer = e.Pointer.PointerId;
            _shelfDragging = false;
            _shelfDragStartX = e.GetCurrentPoint(LayoutRoot).Position.X;
            _shelfDragStartOffset = _shelfDragScroller.HorizontalOffset;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShelfDrag] Press failed — {ex.Message}");
        }
    }

    private void ShelfDrag_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_shelfDragPointer != e.Pointer.PointerId || _shelf == null || _shelfDragScroller == null) return;
        try
        {
            double dx = e.GetCurrentPoint(LayoutRoot).Position.X - _shelfDragStartX;
            if (!_shelfDragging)
            {
                if (Math.Abs(dx) < ShelfDragThreshold) return;
                _shelfDragging = true;
                // Take the pointer from the cover, so letting go doesn't select it
                ReleaseCapturesUpTo(e.OriginalSource as DependencyObject, _shelf);
                _shelf.CapturePointer(e.Pointer);
            }
            _shelfDragScroller.ChangeView(Math.Max(0, _shelfDragStartOffset - dx), null, null, disableAnimation: true);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShelfDrag] Move failed — {ex.Message}");
        }
    }

    private void ShelfDrag_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_shelfDragPointer != e.Pointer.PointerId) return;
        _shelfDragPointer = null;
        if (!_shelfDragging) return; // a plain click: the cover selects as usual
        _shelfDragging = false;
        e.Handled = true;
        _shelf?.ReleasePointerCaptures();
    }

    // Only the strip losing the pointer ends a drag: taking it from the cover raises CaptureLost there too
    private void ShelfDrag_CaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _shelf)) return;
        ShelfDrag_Released(sender, e);
    }

    private static bool IsInScrollBar(DependencyObject? d)
    {
        for (; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollBar) return true;
        return false;
    }

    private static void ReleaseCapturesUpTo(DependencyObject? d, UIElement stop)
    {
        for (; d != null && !ReferenceEquals(d, stop); d = VisualTreeHelper.GetParent(d))
            if (d is UIElement el) el.ReleasePointerCaptures();
    }
}
