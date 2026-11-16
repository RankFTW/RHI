// SettingsHandler.ThemePreview.cs — a small mock of the game page shown beside the colour picker in
// Settings › General › Appearance (Custom), so a colour change can be judged without leaving Settings.
// Built from the same default-look colours and brushes the real page uses (UIFactory.Brush / GetBrush), which
// ThemeService recolours live while the picker is dragged, so the preview always matches the real result,
// Button text included. Nothing here is interactive.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private const double ThemePreviewWidth = 340;

    /// <summary>A new preview (one per picker: an element can only have one parent).</summary>
    private FrameworkElement BuildThemePreview()
    {
        var outer = new StackPanel { Spacing = 6, Width = ThemePreviewWidth, VerticalAlignment = VerticalAlignment.Top };
        outer.Children.Add(new TextBlock
        {
            Text = "Preview",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
        });

        // Window: game list | game page
        var window = new Grid
        {
            Background = UIFactory.Brush(ResourceKeys.SurfaceBaseBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            ColumnSpacing = 8,
            IsHitTestVisible = false,
        };
        window.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        window.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        window.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        window.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        window.RowSpacing = 8;
        outer.Children.Add(window);

        // ── Game list ──
        var list = new StackPanel
        {
            Spacing = 3,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(6),
            Background = UIFactory.Brush(ResourceKeys.SurfaceSidebarBrush),
        };
        list.Children.Add(new Border
        {
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 0, 6, 0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceInputBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 4),
            Child = new TextBlock { Text = "Search", FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                                    Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) },
        });
        list.Children.Add(MakePreviewListItem("Cyber Racer", selected: true));
        list.Children.Add(MakePreviewListItem("Dusk Valley", selected: false));
        list.Children.Add(MakePreviewListItem("Iron Coast", selected: false));
        list.Children.Add(MakePreviewListItem("Starfall", selected: false));
        window.Children.Add(list);

        // ── Game page ──
        var page = new StackPanel { Spacing = 6 };
        Grid.SetColumn(page, 1);
        page.Children.Add(new TextBlock
        {
            Text = "Cyber Racer",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        page.Children.Add(new TextBlock
        {
            Text = "Steam · DirectX 12 · Unreal Engine",
            FontSize = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });

        var card = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = UIFactory.Brush(ResourceKeys.SurfaceRaisedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
        };
        card.Children.Add(new TextBlock
        {
            Text = "Components",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        // Same colours as the real rows: installed / update / not installed
        card.Children.Add(MakePreviewRow("ReShade", "Installed", "#5ECB7D", "Reinstall", "#182840", "#7AACDD", "#2A4468"));
        card.Children.Add(MakePreviewRow("RenoDX", "Update", "#B898E8", "Update", "#201838", "#B898E8", "#3A2860"));
        card.Children.Add(MakePreviewRow("OptiScaler", "Ready", "#A0AABB", "Install", null, null, null));
        page.Children.Add(card);

        // Accent, warning and error text as they appear on the page
        page.Children.Add(new TextBlock
        {
            Text = "Extras",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush),
        });
        page.Children.Add(new TextBlock
        {
            Text = "Remove DXVK first",
            FontSize = 10,
            Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
        });
        page.Children.Add(new TextBlock
        {
            Text = "Install failed",
            FontSize = 10,
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
        });
        window.Children.Add(page);

        // ── Footer (status bar) ──
        var footer = new Grid
        {
            Margin = new Thickness(-8, 0, -8, -8),
            Padding = new Thickness(8, 4, 8, 4),
            Background = UIFactory.Brush(ResourceKeys.SurfaceStatusBarBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock { Text = "Last checked: 5m ago", FontSize = 9,
            Foreground = UIFactory.Brush(ResourceKeys.FooterTextBrush) });
        var links = new TextBlock { Text = "Patch Notes   MOTD   Updates", FontSize = 9,
            Foreground = UIFactory.Brush(ResourceKeys.FooterTextBrush) };
        Grid.SetColumn(links, 1);
        footer.Children.Add(links);
        Grid.SetRow(footer, 1);
        Grid.SetColumnSpan(footer, 2);
        window.Children.Add(footer);

        outer.Children.Add(new TextBlock
        {
            Text = "Changes show here and across RHI as you pick.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
        });
        return outer;
    }

    private static Border MakePreviewListItem(string name, bool selected) => new()
    {
        Height = 22,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 0, 6, 0),
        BorderThickness = new Thickness(1),
        Background = selected ? UIFactory.Brush(ResourceKeys.SidebarItemSelectedBrush) : null,
        BorderBrush = selected ? UIFactory.Brush(ResourceKeys.SidebarItemSelectedBorderBrush) : null,
        Child = new TextBlock
        {
            Text = name,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = UIFactory.Brush(selected ? ResourceKeys.SidebarItemSelectedTextBrush : ResourceKeys.SidebarItemTextBrush),
        },
    };

    /// <summary>label | status | button | Info; a null button colour = the accent-blue install style.</summary>
    private static Grid MakePreviewRow(string label, string status, string statusHex,
        string button, string? bgHex, string? fgHex, string? borderHex)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        var statusText = new TextBlock
        {
            Text = status,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            Foreground = UIFactory.GetBrush(statusHex),
        };
        Grid.SetColumn(statusText, 1);
        row.Children.Add(statusText);

        var btn = MakePreviewButton(button,
            bgHex != null ? UIFactory.GetBrush(bgHex) : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            fgHex != null ? UIFactory.GetBrush(fgHex) : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            borderHex != null ? UIFactory.GetBrush(borderHex) : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush));
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(btn, 2);
        row.Children.Add(btn);

        var info = MakePreviewButton("Info",
            UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush));
        Grid.SetColumn(info, 3);
        row.Children.Add(info);
        return row;
    }

    /// <summary>A Border that looks like an RHI button (a real Button would take focus and hover colours).</summary>
    private static Border MakePreviewButton(string text, Brush bg, Brush fg, Brush border) => new()
    {
        Height = 22,
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(8, 0, 8, 0),
        Background = bg,
        BorderBrush = border,
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = fg,
        },
    };
}
