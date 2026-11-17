// SingleKeyBox.cs — a one-key hotkey box with a ↺ button: click the box and press a key (no modifiers; Backspace =
// None; Tab / Enter / Esc keep their normal job). Used for HSF's keys in Settings › Components and the HSF ⚙.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

internal static class SingleKeyBox
{
    /// <summary>A virtual-key code as RHI shows it ("None" for 0).</summary>
    public static string Text(int vk) => vk <= 0 ? "None" : HotkeyManager.FormatHotkeyDisplay($"{vk},0,0,0");

    /// <summary>
    /// The box and its ↺ button in a Grid [box | ↺]. <paramref name="onKey"/> gets the new key (0 = None) and returns
    /// the key to show (it may refuse a change); <paramref name="onReset"/> returns the key ↺ put back.
    /// </summary>
    public static Grid Create(int vk, Func<int, int> onKey, Func<int> onReset, string tooltip, string resetTooltip,
        Style? boxStyle = null, Style? buttonStyle = null)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            IsReadOnly = true,
            PlaceholderText = "Press a key...",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Text = Text(vk),
        };
        if (boxStyle != null) box.Style = boxStyle;
        ToolTipService.SetToolTip(box, tooltip);
        box.PreviewKeyDown += (s, e) =>
        {
            var key = e.Key;
            if (key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.Menu
                    or Windows.System.VirtualKey.LeftControl or Windows.System.VirtualKey.RightControl
                    or Windows.System.VirtualKey.LeftShift or Windows.System.VirtualKey.RightShift
                    or Windows.System.VirtualKey.LeftMenu or Windows.System.VirtualKey.RightMenu
                    or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Escape
                || (int)key is 91 or 92)
                return;
            int pressed = key == Windows.System.VirtualKey.Back ? 0 : (int)key;
            if (pressed > 255) return;
            box.Text = Text(onKey(pressed));
            e.Handled = true;
        };
        Grid.SetColumn(box, 0);
        grid.Children.Add(box);

        var reset = new Button { Content = "↺" };
        if (buttonStyle != null) reset.Style = buttonStyle;
        else reset.Padding = new Thickness(10, 4, 10, 4);
        reset.Foreground = UIFactory.Brush(ResourceKeys.SettingsLabelBrush);
        ToolTipService.SetToolTip(reset, resetTooltip);
        reset.Click += (s, e) => box.Text = Text(onReset());
        Grid.SetColumn(reset, 1);
        grid.Children.Add(reset);
        return grid;
    }
}
