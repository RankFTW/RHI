// MainWindow.Controller.Popups.cs -- the controller in menus, dropdowns and dialogs. Everything is done
// through the controls themselves (move focus between items, pick an item, close the popup), never by
// pressing keys, so it works the same whichever window Windows thinks is active.
//   Dropdown (ComboBox):  up / down move, A picks, B closes without changing the setting
//   Menu (MenuFlyout):    up / down move (wrapping), right / A open a submenu, left / B close it,
//                         A runs an item, B closes the menu
//   Dialog / other popup: D-pad moves between its controls, A presses, B closes (as Cancel)

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // Submenus opened with the controller, innermost last: left / B closes the last one
    private readonly List<MenuFlyoutSubItem> _padSubmenus = new();

    private void PopupMove(FocusNavigationDirection dir, DependencyObject? focused, Popup popup)
    {
        if (focused is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox combo)
        {
            if (dir is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
                FocusComboItem(combo, combo.IndexFromContainer(item), dir == FocusNavigationDirection.Down ? 1 : -1);
            return;
        }

        if (focused is MenuFlyoutItemBase menuItem)
        {
            switch (dir)
            {
                case FocusNavigationDirection.Up:
                case FocusNavigationDirection.Down:
                    var items = MenuItemsBeside(menuItem);
                    int i = items.IndexOf(menuItem);
                    if (items.Count > 0 && i >= 0)
                        PadFocus(items[((i + (dir == FocusNavigationDirection.Down ? 1 : -1)) % items.Count + items.Count) % items.Count]);
                    break;
                case FocusNavigationDirection.Right:
                    if (menuItem is MenuFlyoutSubItem sub) OpenSubmenu(sub);
                    break;
                case FocusNavigationDirection.Left:
                    CloseInnermostSubmenu();
                    break;
            }
            return;
        }

        // A dialog or other popup: its own controls, nearest first
        if (popup.Child is UIElement child && (focused == null || !IsWithin(focused, child)))
        {
            if (FirstInPopup(child) is { } first) PadFocus(first);
            return;
        }
        // In a search box (typing needs a keyboard): go to where the dialog wants a controller to start
        if (focused is TextBox && popup.Child is UIElement c && FindPadStart(c) is { } start)
        {
            PadFocus(start);
            return;
        }
        PadFocusNext(dir, popup.Child);
    }

    /// <summary>
    /// Where a controller starts in a popup: a control the dialog tagged "PadStart" (e.g. the
    /// selected version in What's New), else its first control.
    /// </summary>
    internal static DependencyObject? FirstInPopup(UIElement child) =>
        FindPadStart(child) ?? FocusManager.FindFirstFocusableElement(child);

    private static Control? FindPadStart(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is UIElement { Visibility: Visibility.Collapsed }) continue;
            if (c is Control { Tag: "PadStart", IsEnabled: true } found) return found;
            if (FindPadStart(c) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>
    /// The right stick in a popup: scrolls the biggest scrolling area that doesn't hold focus (the
    /// notes beside a list you're moving through), else the one that does.
    /// </summary>
    private static ScrollViewer? PopupScroller(Popup popup, DependencyObject? focused)
    {
        ScrollViewer? best = null, withFocus = null;
        void Walk(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var c = VisualTreeHelper.GetChild(node, i);
                if (c is UIElement { Visibility: Visibility.Collapsed }) continue;
                if (c is ScrollViewer { ScrollableHeight: > 0 } sv)
                {
                    if (focused != null && IsWithin(focused, sv)) withFocus ??= sv;
                    else if (best == null || sv.ScrollableHeight > best.ScrollableHeight) best = sv;
                }
                Walk(c);
            }
        }
        if (popup.Child != null) Walk(popup.Child);
        return best ?? withFocus;
    }

    /// <summary>A in a popup. True when handled here; false for an ordinary control (the caller presses it).</summary>
    private bool PopupSelect(DependencyObject? focused)
    {
        switch (focused)
        {
            case ComboBoxItem item when ItemsControl.ItemsControlFromItemContainer(item) is ComboBox combo:
                // As a click on the item: the setting changes and the dropdown closes
                int index = combo.IndexFromContainer(item);
                if (index >= 0) combo.SelectedIndex = index;
                combo.IsDropDownOpen = false;
                AfterLayout(() => PadFocus(combo));
                return true;

            case MenuFlyoutSubItem sub:
                OpenSubmenu(sub);
                return true;

            case MenuFlyoutItemBase menuItem:
                var peer = FrameworkElementAutomationPeer.FromElement(menuItem) ?? FrameworkElementAutomationPeer.CreatePeerForElement(menuItem);
                if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
                else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
                {
                    toggle.Toggle(); // a tick item: toggle it and close the menu, as a click does
                    CloseMenus();
                }
                _padSubmenus.Clear();
                return true;
        }
        return false;
    }

    /// <summary>B in a popup: dropdowns close unchanged, submenus close one level, menus and dialogs close (Cancel).</summary>
    private void PopupBack(DependencyObject? focused, Popup popup, XamlRoot xamlRoot)
    {
        if (focused is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox combo)
        {
            combo.IsDropDownOpen = false;
            AfterLayout(() => PadFocus(combo));
            return;
        }
        if (focused is MenuFlyoutItemBase && _padSubmenus.Count > 0)
        {
            CloseInnermostSubmenu();
            return;
        }
        if (popup.Child is MenuFlyoutPresenter || focused is MenuFlyoutItemBase)
        {
            CloseMenus();
            AfterLayout(() => RefocusAfterPopup(xamlRoot));
            return;
        }
        if ((popup.Child as ContentDialog ?? FindChild<ContentDialog>(popup.Child)) is { } dialog)
        {
            dialog.Hide(); // same as Escape: ContentDialogResult.None
            AfterLayout(() => RefocusAfterPopup(xamlRoot));
            return;
        }
        popup.IsOpen = false; // a flyout
        AfterLayout(() => RefocusAfterPopup(xamlRoot));
    }

    /// <summary>After A opened a dropdown: focus its current choice (or the first one).</summary>
    private void FocusOpenCombo(ComboBox combo)
    {
        if (!combo.IsDropDownOpen) return;
        int index = Math.Max(0, combo.SelectedIndex);
        if (combo.ContainerFromIndex(index) is ComboBoxItem { IsEnabled: true } current) PadFocus(current);
        else FocusComboItem(combo, -1, 1);
    }

    /// <summary>Focuses the next enabled dropdown item from <paramref name="from"/> in <paramref name="step"/>'s direction.</summary>
    private static void FocusComboItem(ComboBox combo, int from, int step)
    {
        for (int j = from + step; j >= 0 && j < combo.Items.Count; j += step)
            if (combo.ContainerFromIndex(j) is ComboBoxItem { IsEnabled: true, Visibility: Visibility.Visible } next)
            {
                PadFocus(next);
                return;
            }
    }

    /// <summary>The menu items in the same menu as <paramref name="item"/> that can be focused (no separators).</summary>
    private static List<MenuFlyoutItemBase> MenuItemsBeside(MenuFlyoutItemBase item)
    {
        var list = new List<MenuFlyoutItemBase>();
        DependencyObject? cur = item;
        while (cur != null && cur is not MenuFlyoutPresenter) cur = VisualTreeHelper.GetParent(cur);
        if (cur is not MenuFlyoutPresenter presenter) return list;
        foreach (var o in presenter.Items)
            if (o is MenuFlyoutItemBase m and not MenuFlyoutSeparator && m.Visibility == Visibility.Visible && m.IsEnabled)
                list.Add(m);
        return list;
    }

    private void OpenSubmenu(MenuFlyoutSubItem sub)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(sub) ?? FrameworkElementAutomationPeer.CreatePeerForElement(sub);
        if (peer?.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expand) return;
        expand.Expand();
        if (!_padSubmenus.Contains(sub)) _padSubmenus.Add(sub);
        AfterLayout(() =>
        {
            // The submenu's own presenter is the newest open menu popup
            if (sub.XamlRoot == null) return;
            foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(sub.XamlRoot))
                if (p.Child is MenuFlyoutPresenter mp && !IsWithin(sub, mp)
                    && mp.Items.OfType<MenuFlyoutItemBase>().FirstOrDefault(m => m is not MenuFlyoutSeparator && m.IsEnabled) is { } first)
                {
                    PadFocus(first);
                    return;
                }
        });
    }

    private void CloseInnermostSubmenu()
    {
        if (_padSubmenus.Count == 0) return;
        var sub = _padSubmenus[^1];
        _padSubmenus.RemoveAt(_padSubmenus.Count - 1);
        var peer = FrameworkElementAutomationPeer.FromElement(sub) ?? FrameworkElementAutomationPeer.CreatePeerForElement(sub);
        if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand) expand.Collapse();
        AfterLayout(() => PadFocus(sub));
    }

    /// <summary>Closes every open menu (a submenu's popup and its parent's).</summary>
    private void CloseMenus()
    {
        _padSubmenus.Clear();
        var root = Content?.XamlRoot;
        if (root == null) return;
        foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (p.Child is MenuFlyoutPresenter) p.IsOpen = false;
    }

    /// <summary>When a popup closes, focus normally returns to what opened it; if it went nowhere, start again at home.</summary>
    private void RefocusAfterPopup(XamlRoot xamlRoot)
    {
        if (FocusManager.GetFocusedElement(xamlRoot) is not FrameworkElement { IsLoaded: true } now
            || (_footer != null && IsWithin(now, _footer)))
            FocusHome(xamlRoot);
    }
}
