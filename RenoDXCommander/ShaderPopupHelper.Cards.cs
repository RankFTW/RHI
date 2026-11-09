// ShaderPopupHelper.Cards.cs -- the shader picker's pack cards: each pack (and custom folder) is a card in a
// two-column grid, highlighted while selected, with a "Downloaded" label and a "Show N shaders" link that
// opens its file list inside the card.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;

namespace RenoDXCommander;

public static partial class ShaderPopupHelper
{
    /// <summary>A category's card grid: two columns when there's room, one when narrow.</summary>
    private static AutoFitGrid NewCardGrid() => new()
    {
        MinColumnWidth = 330,
        MaxColumns = 2,
        ColumnSpacing = 10,
        RowSpacing = 10,
        Margin = new Thickness(0, 0, 0, 6),
    };

    /// <summary>
    /// Wraps a pack's check box (and its file list) in a card. The card hides with the check box (search) and
    /// is highlighted while the pack is selected (fully or partly).
    /// </summary>
    private static Border MakePackCard(CheckBox packCb, StackPanel? fileSub)
    {
        packCb.HorizontalAlignment = HorizontalAlignment.Stretch;
        packCb.HorizontalContentAlignment = HorizontalAlignment.Stretch; // so the tick reaches the card's right edge
        packCb.Margin = new Thickness(0);
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(packCb);
        if (fileSub != null)
        {
            fileSub.Margin = new Thickness(30, 0, 0, 0);
            // Long lists scroll inside the card so the neighbouring card doesn't grow with it
            body.Children.Add(new ScrollViewer
            {
                Content = fileSub,
                MaxHeight = 240,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Child = body,
        };
        StyleCard(card, packCb.IsChecked);
        packCb.Checked += (_, _) => StyleCard(card, true);
        packCb.Unchecked += (_, _) => StyleCard(card, false);
        packCb.Indeterminate += (_, _) => StyleCard(card, null);
        packCb.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => card.Visibility = packCb.Visibility);
        return card;
    }

    private static void StyleCard(Border card, bool? isChecked)
    {
        bool on = isChecked != false;
        card.Background = Brush(on ? ResourceKeys.AccentBlueBgBrush : ResourceKeys.SurfaceRaisedBrush);
        card.BorderBrush = Brush(on ? ResourceKeys.AccentBlueBorderBrush : ResourceKeys.BorderSubtleBrush);
    }

    /// <summary>A pack card's title row: the name, and a green tick in the top-right corner when it's on disk.</summary>
    private static Grid MakeCardTitle(string name, string tooltip, bool good)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (good)
        {
            var tick = new TextBlock
            {
                Text = "✓",
                FontSize = 14,
                Foreground = Brush(ResourceKeys.AccentGreenBrush),
                VerticalAlignment = VerticalAlignment.Top,
            };
            ToolTipService.SetToolTip(tick, tooltip);
            Grid.SetColumn(tick, 1);
            row.Children.Add(tick);
        }
        return row;
    }

    private static TextBlock MakeCardDescription(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush(ResourceKeys.TextSecondaryBrush),
    };

    /// <summary>"Show N shaders" / "Hide shaders" link; the count lives in Tag.</summary>
    private static Button MakeExpandButton(int count)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var b = new Button
        {
            Tag = count,
            FontSize = 12,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 0),
            MinHeight = 0,
            Background = transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(ResourceKeys.AccentBlueBrush),
        };
        // Link look: no button fill on hover / press
        b.Resources["ButtonBackgroundPointerOver"] = transparent;
        b.Resources["ButtonBackgroundPressed"] = transparent;
        b.Resources["ButtonForegroundPointerOver"] = Brush(ResourceKeys.TextPrimaryBrush);
        b.Resources["ButtonForegroundPressed"] = Brush(ResourceKeys.TextPrimaryBrush);
        SetExpanded(b, false);
        return b;
    }

    private static void SetExpanded(Button b, bool open)
    {
        int n = b.Tag is int c ? c : 0;
        b.Content = open ? "Hide shaders" : n == 1 ? "Show 1 shader" : $"Show {n} shaders";
    }

    /// <summary>Category heading text with how many of its packs are selected.</summary>
    private static string HeadingText(string category, int selected, int total)
        => selected > 0 ? $"{category}   {selected} of {total} selected" : category;
}
