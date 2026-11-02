using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RHI.ManifestEditor.UI;

/// <summary>Small factory helpers so pages read as layout, not boilerplate.</summary>
public static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    public static Brush Secondary => Res("TextFillColorSecondaryBrush");
    public static Brush Tertiary => Res("TextFillColorTertiaryBrush");
    public static Brush AccentText => Res("AccentTextFillColorPrimaryBrush");
    public static Brush CardBg => Res("CardBackgroundFillColorDefaultBrush");
    public static Brush CardStroke => Res("CardStrokeColorDefaultBrush");
    public static Brush Caution => Res("SystemFillColorCautionBrush");
    public static Brush Critical => Res("SystemFillColorCriticalBrush");
    public static Brush Success => Res("SystemFillColorSuccessBrush");
    public static Brush SubtleFill => Res("SubtleFillColorSecondaryBrush");
    public static Brush LayerBg => Res("LayerFillColorDefaultBrush");

    public static TextBlock T(string text, double size = 14, bool bold = false, Brush? fg = null, bool wrap = true)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        if (fg != null) t.Foreground = fg;
        return t;
    }

    public static TextBlock Help(string text) => T(text, 12, fg: Secondary);

    public static StackPanel V(double spacing, params UIElement?[] children)
    {
        var p = new StackPanel { Spacing = spacing };
        foreach (var c in children) if (c != null) p.Children.Add(c);
        return p;
    }

    public static StackPanel H(double spacing, params UIElement?[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) if (c != null) p.Children.Add(c);
        return p;
    }

    public static Border Card(UIElement content, double padding = 16) => new()
    {
        Background = CardBg,
        BorderBrush = CardStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(padding),
        Child = content,
    };

    public static Button Btn(string text, Action onClick, bool accent = false, string? tooltip = null)
    {
        var b = new Button { Content = text };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => onClick();
        if (tooltip != null) ToolTipService.SetToolTip(b, tooltip);
        return b;
    }

    /// <summary>Small borderless "x" button for removing a row.</summary>
    public static Button RemoveBtn(Action onClick, string tooltip = "Remove")
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(b, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Border Pill(string text, Brush fg)
    {
        return new Border
        {
            Padding = new Thickness(8, 1, 8, 2),
            CornerRadius = new CornerRadius(10),
            BorderBrush = fg,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = T(text, 11, fg: fg, wrap: false),
        };
    }

    public static TextBox Box(string text, string? placeholder = null, bool multiline = false)
    {
        var tb = new TextBox
        {
            Text = text,
            PlaceholderText = placeholder ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (multiline)
        {
            tb.AcceptsReturn = true;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.MinHeight = 76;
            tb.MaxHeight = 320;
            ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Auto);
        }
        return tb;
    }

    public static TextBox Mono(string text, double minHeight = 200)
    {
        var tb = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            MinHeight = minHeight,
            IsSpellCheckEnabled = false,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(tb, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Auto);
        return tb;
    }

    /// <summary>Label + help on the left, control on the right. Stacks on narrow widths are not needed for this tool.</summary>
    public static Grid Row(string label, string? help, UIElement control, bool isSet = false)
    {
        var g = new Grid { ColumnSpacing = 24 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var title = T(label, 14, bold: true);
        var left = V(2, isSet ? H(6, new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 7, Height = 7, Fill = AccentText, VerticalAlignment = VerticalAlignment.Center,
        }, title) : title);
        if (!string.IsNullOrEmpty(help)) left.Children.Add(Help(help));
        Grid.SetColumn(left, 0);
        if (control is FrameworkElement fe && fe.VerticalAlignment == VerticalAlignment.Stretch)
            fe.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn((FrameworkElement)control, 1);
        g.Children.Add(left);
        g.Children.Add(control);
        return g;
    }

    public static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
    public static string ForTextBox(string s) => s.Replace("\r\n", "\n").Replace('\n', '\r');
}
