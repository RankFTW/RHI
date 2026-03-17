using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander;

/// <summary>
/// Shared static helpers for creating styled WinUI 3 UI elements.
/// Consolidates duplicated Brush, ParseColor, MakeStatusDot, MakeSeparator,
/// MakeLabel, and MakeActionButton methods from CardBuilder, DetailPanelBuilder,
/// and DragDropHandler into a single location.
/// </summary>
public static class UIFactory
{
    /// <summary>
    /// Cache of <see cref="SolidColorBrush"/> instances keyed by normalised hex colour string.
    /// Avoids creating duplicate brush objects for the same colour across card builds.
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a cached <see cref="SolidColorBrush"/> for the given hex colour string.
    /// If the brush has not been created yet it is parsed, cached, and returned.
    /// </summary>
    /// <param name="hex">A hex colour string such as "#1C2848" or "#FF1C2848".</param>
    public static SolidColorBrush GetBrush(string hex)
    {
        if (_brushCache.TryGetValue(hex, out var cached))
            return cached;

        var brush = new SolidColorBrush(ParseColor(hex));
        _brushCache[hex] = brush;
        return brush;
    }

    /// <summary>
    /// Retrieves a <see cref="SolidColorBrush"/> from the application's merged resource dictionaries.
    /// </summary>
    /// <param name="resourceKey">The resource key defined in DarkTheme.xaml (e.g. "TextPrimaryBrush").</param>
    public static SolidColorBrush Brush(string resourceKey) =>
        (SolidColorBrush)Application.Current.Resources[resourceKey];

    /// <summary>
    /// Parses a hex colour string (e.g. "#1C2848" or "#FF1C2848") into a <see cref="Windows.UI.Color"/>.
    /// Supports both 6-digit (#RRGGBB) and 8-digit (#AARRGGBB) formats.
    /// </summary>
    public static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        int offset = 0;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[..2], 16);
            offset = 2;
        }
        byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
        byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// Creates a horizontal status-dot panel: a small coloured ellipse followed by a label.
    /// Used on game cards to show RDX / RS / DC / Luma status.
    /// </summary>
    public static StackPanel MakeStatusDot(string label, string colorHex)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = GetBrush(colorHex),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = GetBrush("#A0AABB"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    /// <summary>
    /// Creates a thin horizontal separator line using the "BorderSubtleBrush" theme resource.
    /// </summary>
    public static Border MakeSeparator() => new()
    {
        Height = 1,
        Background = (SolidColorBrush)Application.Current.Resources["BorderSubtleBrush"],
        Margin = new Thickness(0, 2, 0, 2),
    };

    /// <summary>
    /// Creates a styled <see cref="TextBlock"/> label.
    /// </summary>
    /// <param name="text">The display text.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="foregroundKey">A theme resource key for the foreground brush (e.g. "TextPrimaryBrush").</param>
    public static TextBlock MakeLabel(
        string text,
        double fontSize = 12,
        string foregroundKey = "TextPrimaryBrush")
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Brush(foregroundKey),
        };
    }

    /// <summary>
    /// Creates a styled <see cref="Button"/> with hex-based background, foreground, and border colours.
    /// </summary>
    /// <param name="content">Button content (typically a string label).</param>
    /// <param name="tag">Value stored in <see cref="FrameworkElement.Tag"/> (usually the card view-model).</param>
    /// <param name="bgHex">Background colour hex string (e.g. "#182840").</param>
    /// <param name="fgHex">Foreground colour hex string (e.g. "#7AACDD").</param>
    /// <param name="borderHex">Border colour hex string (e.g. "#2A4468").</param>
    public static Button MakeActionButton(
        string content,
        object tag,
        string bgHex = "#182840",
        string fgHex = "#7AACDD",
        string borderHex = "#2A4468")
    {
        return new Button
        {
            Content = content,
            Tag = tag,
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            MinWidth = 0,
            Background = GetBrush(bgHex),
            Foreground = GetBrush(fgHex),
            BorderBrush = GetBrush(borderHex),
            CornerRadius = new CornerRadius(6),
        };
    }
}
