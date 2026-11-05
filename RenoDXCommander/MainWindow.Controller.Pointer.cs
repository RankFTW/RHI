// MainWindow.Controller.Pointer.cs -- while the controller is in use (hint bar showing) a
// see-through layer over the window catches the mouse, so a pointer left resting on the window
// can't hover buttons or pop tooltips as the controller moves things underneath it. Moving the
// mouse or pressing a mouse button removes the layer, as does RHI losing the foreground.
// Notes:
// - Don't turn hit-testing off on the page instead: the controller's row navigation skips anything
//   that isn't hit-test visible, so that breaks the D-pad.
// - Hiding the cursor itself was tried (ShowCursor(false) returned -1 and SetCursor(null) on the UI
//   thread that owns the window under the pointer) and Windows still drew the arrow; WinUI resets it.

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _padPointerHidden;
    private POINT _padPointerAt;
    private Border? _padPointerShield;
    private const int PadPointerMoveSlack = 3; // px: ignore tiny jitter

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;

    /// <summary>Puts the shield over the window (no hover, no tooltips) and closes any open tooltip.</summary>
    private void HidePadPointer()
    {
        try
        {
            if (_padPointerHidden || !EnsurePointerShield()) return;
            if (!GetCursorPos(out _padPointerAt)) return;
            _padPointerHidden = true;
            _padPointerShield!.Visibility = Visibility.Visible;
            CloseOpenToolTips();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.HidePadPointer] {ex.Message}");
        }
    }

    /// <summary>Gives the mouse back to the page.</summary>
    private void RestorePadPointer()
    {
        try
        {
            if (!_padPointerHidden) return;
            _padPointerHidden = false;
            if (_padPointerShield != null) _padPointerShield.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RestorePadPointer] {ex.Message}");
        }
    }

    /// <summary>
    /// Each controller poll: true if the mouse was moved or clicked since the shield went up.
    /// (The shield swallows PointerMoved, and its arrival raises one without any movement.)
    /// </summary>
    private bool PadPointerUsed()
    {
        if (!_padPointerHidden) return false;
        if (GetCursorPos(out var p)
            && (Math.Abs(p.X - _padPointerAt.X) > PadPointerMoveSlack || Math.Abs(p.Y - _padPointerAt.Y) > PadPointerMoveSlack))
            return true;
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
            || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
            || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
    }

    /// <summary>A transparent layer over the whole window; it takes the pointer, never focus.</summary>
    private bool EnsurePointerShield()
    {
        if (_padPointerShield != null) return true;
        // At an interface size other than 100% the root Grid sits inside a scaling Canvas (UiScale.cs)
        if ((_uiScaledRoot ?? Content) is not Grid root)
        {
            CrashReporter.Log("[MainWindow.EnsurePointerShield] Window root isn't a Grid — mouse shield not added");
            return false;
        }
        _padPointerShield = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), // hit-testable
            Visibility = Visibility.Collapsed,
            IsTabStop = false,
        };
        Canvas.SetZIndex(_padPointerShield, 1000);
        Grid.SetRowSpan(_padPointerShield, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(_padPointerShield, Math.Max(1, root.ColumnDefinitions.Count));
        root.Children.Add(_padPointerShield);
        return true;
    }

    /// <summary>Closes a tooltip the resting pointer had already opened.</summary>
    private void CloseOpenToolTips()
    {
        if (Content?.XamlRoot is not { } xamlRoot) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
            if (popup.Child is ToolTip tip) tip.IsOpen = false;
    }
}
