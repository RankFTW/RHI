// TabsTheme.cs -- colours, sizes and small control factories for the Tabs layout, taken from
// mockup A (docs/mockups/layouts-2.9.0/Main.dc.html; tokens listed in docs/layouts-plan-2.9.0.md).
// Keep every Tabs-only element on these so the layout matches the mockup in one place.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander;

internal static class TabsTheme
{
    // ── Colours ───────────────────────────────────────────────────────────────
    internal const string Page        = "#0B0F17";
    internal const string PanelBg     = "#0E131D";
    internal const string Card        = "#121824";
    internal const string CardBorder  = "#1F2737";
    internal const string RowHover    = "#151C29";
    internal const string TextPrimary = "#E6EAF2";
    internal const string TextSecondary = "#AEB7C7";
    internal const string TextMuted   = "#8D97AA";
    internal const string TextControl = "#C9D1DE";
    internal const string Accent      = "#6FA8FF";
    internal const string Link        = "#8DB8FF";
    internal const string Value       = "#7FE0A0";

    internal static SolidColorBrush B(string hex) => UIFactory.GetBrush(hex);

    // ── Button / pill looks ───────────────────────────────────────────────────

    /// <summary>A colour set for a button or pill: background, border, text, and background on hover.</summary>
    internal readonly record struct Look(string Bg, string Border, string Fg, string HoverBg, bool Bold);

    internal static readonly Look BlueButton    = new("#141F33", "#2A3D63", "#A9C6FF", "#1A2944", true);
    internal static readonly Look PurpleButton  = new("#2A2050", "#4A3A7A", "#D9C8FF", "#33285F", true);
    internal static readonly Look GreenButton   = new("#13261C", "#24563A", "#7FE0A0", "#183222", true);
    internal static readonly Look AmberButton   = new("#2A2110", "#5A4520", "#F0C35A", "#332814", true);
    internal static readonly Look IconButton    = new("#141A26", "#263042", TextControl, "#1E2738", false);
    internal static readonly Look DangerButton  = new("#1F1217", "#4A2630", "#FF9AA8", "#2A161D", false);

    internal static readonly Look GreenPill  = new("#13261C", "#24563A", "#7FE0A0", "#13261C", true);
    internal static readonly Look PurplePill = new("#2A2050", "#4A3A7A", "#D9C8FF", "#2A2050", true);
    internal static readonly Look GreyPill   = new("#151C29", "#263042", "#AEB7C7", "#151C29", true);
    internal static readonly Look AmberPill  = new("#2A2110", "#5A4520", "#F0C35A", "#2A2110", true);
    internal static readonly Look RedPill    = new("#1F1217", "#4A2630", "#FF9AA8", "#1F1217", true);

    /// <summary>Which kind of state a colour shows (from the rows' own status / button colours).</summary>
    internal enum Tone { Green, Purple, Amber, Red, Blue, Grey }

    /// <summary>
    /// Sorts a row colour into the mockup's tones, so the cards follow whatever the row decided
    /// (installed, update, warning, error) without repeating its rules.
    /// </summary>
    internal static Tone ToneOf(Brush? brush)
    {
        if (brush is not SolidColorBrush scb) return Tone.Grey;
        var c = scb.Color;
        double r = c.R, g = c.G, b = c.B;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max - min < 28) return Tone.Grey;                       // greys
        if (g >= r && g >= b && g - Math.Max(r, b) > 20) return Tone.Green;
        if (r > g && r > b && b > g + 10 && r - g > 40) return Tone.Red;     // pinks / reds
        if (r > b && g > b && r - b > 40) return Tone.Amber;         // yellows / oranges
        if (r > g + 10 && b > g + 10 && r > 90) return Tone.Purple;  // purples
        if (b >= r && b >= g) return Tone.Blue;
        return Tone.Grey;
    }

    internal static Look PillFor(Tone t) => t switch
    {
        Tone.Green  => GreenPill,
        Tone.Purple => PurplePill,
        Tone.Amber  => AmberPill,
        Tone.Red    => RedPill,
        _           => GreyPill,
    };

    internal static Look ButtonFor(Tone t) => t switch
    {
        Tone.Purple => PurpleButton,
        Tone.Green  => GreenButton,
        Tone.Amber  => AmberButton,
        Tone.Red    => DangerButton,
        _           => BlueButton,
    };

    /// <summary>
    /// Applies a look to a button, including its hover and pressed colours. WinUI reads the
    /// hover resources when the button's template loads, so call this before the button is shown;
    /// to change a shown button's look, create a new button (see OverviewCard).
    /// </summary>
    internal static void Apply(Button btn, Look look, double height = 38, double fontSize = 13)
    {
        btn.Height = height;
        btn.CornerRadius = new CornerRadius(8);
        btn.BorderThickness = new Thickness(1);
        btn.FontSize = fontSize;
        btn.FontWeight = look.Bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        btn.Background = B(look.Bg);
        btn.BorderBrush = B(look.Border);
        btn.Foreground = B(look.Fg);
        var r = btn.Resources;
        SetResource(r, "ButtonBackgroundPointerOver", B(look.HoverBg));
        SetResource(r, "ButtonBackgroundPressed", B(look.Bg));
        SetResource(r, "ButtonBorderBrushPointerOver", B(look.Border));
        SetResource(r, "ButtonBorderBrushPressed", B(look.Border));
        SetResource(r, "ButtonForegroundPointerOver", B(look.Fg));
        SetResource(r, "ButtonForegroundPressed", B(look.Fg));
        SetResource(r, "ButtonBackgroundDisabled", B(look.Bg));
        SetResource(r, "ButtonBorderBrushDisabled", B(look.Border));
        SetResource(r, "ButtonForegroundDisabled", B(TextMuted));
    }

    /// <summary>Sets a resource key, replacing an existing one (WinUI throws when setting a key that's already there).</summary>
    internal static void SetResource(ResourceDictionary r, string key, object value)
    {
        if (r.ContainsKey(key)) r.Remove(key);
        r[key] = value;
    }

    internal static void ApplyPill(Border pill, TextBlock text, Look look)
    {
        pill.Background = B(look.Bg);
        pill.BorderBrush = B(look.Border);
        text.Foreground = B(look.Fg);
    }

    /// <summary>A status badge: rounded rectangle (6 radius, like the buttons; user preference over the mockup's ovals), 3 10 padding, 12 px semibold.</summary>
    internal static Border MakePill(out TextBlock text)
    {
        text = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150,
        };
        return new Border
        {
            Padding = new Thickness(10, 3, 10, 3),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = text,
        };
    }

    // ── Hand cursor ───────────────────────────────────────────────────────────

    private static readonly Microsoft.UI.Input.InputCursor HandCursorObj =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    private static readonly Microsoft.UI.Input.InputCursor ArrowCursorObj =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);

    /// <summary>
    /// Hand cursor over a clickable element. Set again on every pointer move and after its tooltip
    /// opens, because WinUI drops a custom cursor when a tooltip appears (it went back to the arrow
    /// about a second into hovering).
    /// </summary>
    internal static void UseHandCursor(UIElement el)
    {
        void Hand() => DetailPanelBuilder.CursorProp?.SetValue(el, HandCursorObj);
        el.PointerEntered += (s, e) => Hand();
        el.PointerMoved += (s, e) => Hand();
        el.PointerExited += (s, e) => DetailPanelBuilder.CursorProp?.SetValue(el, ArrowCursorObj);
    }

    /// <summary>
    /// A plain-text tooltip for a clickable element, drawn inside RHI's window. WinUI's own tooltips
    /// open as a separate little window, and Windows resets the cursor to the arrow when that
    /// appears; drawing it in-window keeps the hand cursor. Same delay and look as a tooltip.
    /// Null or empty clears it.
    /// </summary>
    internal static void SetHandTip(UIElement el, string? text)
    {
        ToolTipService.SetToolTip(el, null);
        var state = HandTips.GetValue(el, AttachHandTip);
        state.Text = text;
        if (string.IsNullOrEmpty(text)) state.Close();
    }

    private sealed class HandTipState
    {
        internal string? Text;
        internal Microsoft.UI.Xaml.Controls.Primitives.Popup? Popup;
        internal Microsoft.UI.Dispatching.DispatcherQueueTimer? Timer;

        internal void Close()
        {
            Timer?.Stop();
            if (Popup != null) Popup.IsOpen = false;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, HandTipState> HandTips = new();

    private static HandTipState AttachHandTip(UIElement el)
    {
        var state = new HandTipState();
        el.PointerEntered += (s, e) =>
        {
            if (string.IsNullOrEmpty(state.Text) || el.DispatcherQueue == null) return;
            state.Timer ??= el.DispatcherQueue.CreateTimer();
            state.Timer.Interval = TimeSpan.FromMilliseconds(700);
            state.Timer.IsRepeating = false;
            state.Timer.Tick -= OnTick;
            state.Timer.Tick += OnTick;
            state.Timer.Start();
        };
        el.PointerExited += (s, e) => state.Close();
        el.PointerPressed += (s, e) => state.Close();
        el.PointerCanceled += (s, e) => state.Close();
        if (el is FrameworkElement fe) fe.Unloaded += (s, e) => state.Close();
        return state;

        void OnTick(Microsoft.UI.Dispatching.DispatcherQueueTimer t, object o) => ShowHandTip(el, state);
    }

    private static void ShowHandTip(UIElement el, HandTipState state)
    {
        if (string.IsNullOrEmpty(state.Text) || el.XamlRoot == null || el is not FrameworkElement fe) return;
        try
        {
            var text = new TextBlock
            {
                Text = state.Text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
                Foreground = B(TextPrimary),
            };
            var box = new Border
            {
                Background = B("#161D2B"),
                BorderBrush = B("#2A3446"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Child = text,
                IsHitTestVisible = false, // never under the pointer, so the element keeps hover
            };
            state.Popup ??= new Microsoft.UI.Xaml.Controls.Primitives.Popup { IsHitTestVisible = false };
            var popup = state.Popup;
            popup.XamlRoot = el.XamlRoot;
            popup.ShouldConstrainToRootBounds = true; // in-window: no separate tooltip window
            popup.Child = box;

            // Below the element, kept inside the window
            var root = el.XamlRoot.Size;
            box.Measure(new Windows.Foundation.Size(root.Width, root.Height));
            var size = box.DesiredSize;
            var origin = fe.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            double x = Math.Max(8, Math.Min(origin.X, root.Width - size.Width - 8));
            double y = origin.Y + fe.ActualHeight + 6;
            if (y + size.Height > root.Height - 8) y = origin.Y - size.Height - 6;
            popup.HorizontalOffset = x;
            popup.VerticalOffset = Math.Max(8, y);
            popup.IsOpen = true;
        }
        catch (Exception ex)
        {
            Services.CrashReporter.Log($"[TabsTheme.ShowHandTip] {ex.Message}");
        }
    }

    // ── Icons (Segoe Fluent Icons, falling back to MDL2 on Windows 10) ───────
    internal const string GlyphInfo = "";
    internal const string GlyphSettings = "";
    internal const string GlyphClose = "";

    internal static FontIcon Icon(string glyph, double size = 15) => new()
    {
        Glyph = glyph,
        FontSize = size,
        FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
    };

    /// <summary>A 38×38 icon button in the mockup's look.</summary>
    internal static Button MakeIconButton(string glyph, Look look, double iconSize = 15)
    {
        var btn = new Button { Width = 38, Padding = new Thickness(0), Content = Icon(glyph, iconSize) };
        Apply(btn, look);
        return btn;
    }

    /// <summary>Overview / section heading: 13 px, semibold, muted, uppercase, 0.08em letter spacing.</summary>
    internal static TextBlock SectionHeading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        CharacterSpacing = 80,
        Foreground = B(TextMuted),
    };

    /// <summary>The mockup's card: #121824, #1F2737 border, radius 12, padding 16.</summary>
    internal static Border MakeCard(UIElement child, double padding = 16) => new()
    {
        Background = B(Card),
        BorderBrush = B(CardBorder),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding),
        Child = child,
    };

    /// <summary>
    /// Column count for the mockup's "repeat(auto-fit, minmax(min, 1fr))" grids, and the tile width
    /// that fills the row.
    /// </summary>
    internal static (int Columns, double Width) AutoFit(double available, double minWidth, double gap)
    {
        if (available <= 0) available = 750;
        int n = Math.Max(1, (int)Math.Floor((available + gap) / (minWidth + gap)));
        double w = Math.Floor((available - (n - 1) * gap) / n) - 1;
        return (n, w);
    }

    // ── Tiles (Management, mockup A) ──────────────────────────────────────────

    /// <summary>A clickable tile: name (14/600) above a short description (12, secondary).</summary>
    internal static Button MakeTile(string name, string desc, bool danger, RoutedEventHandler onClick, string? tooltip = null)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = desc,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Foreground = B(TextSecondary),
        });
        var btn = new Button
        {
            Content = body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        Apply(btn, new Look(Card, danger ? "#4A2630" : CardBorder, danger ? "#FF9AA8" : TextPrimary, RowHover, true),
              height: double.NaN, fontSize: 14);
        btn.CornerRadius = new CornerRadius(12);
        btn.Padding = new Thickness(16, 14, 16, 14);
        btn.Click += onClick;
        if (tooltip != null) ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    // ── Forms (label above control, mockup A) ─────────────────────────────────

    /// <summary>A form field: 12 px secondary label above the control, 6 px apart (a StackPanel, so
    /// controls that swap themselves for an inline box in their parent keep working).</summary>
    internal static StackPanel Field(string? label, UIElement control)
    {
        var sp = new StackPanel { Spacing = 6 };
        if (!string.IsNullOrWhiteSpace(label))
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = B(TextSecondary),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        Detach(control);
        sp.Children.Add(control);
        return sp;
    }

    /// <summary>Card heading inside a section tab: 15 px semibold.</summary>
    internal static TextBlock CardTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = B(TextPrimary),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A card with a 15 px title and content, 12 px apart.</summary>
    internal static Border TitledCard(string? title, UIElement content, UIElement? headerRight = null)
    {
        var sp = new StackPanel { Spacing = 12 };
        if (title != null || headerRight != null)
        {
            var head = new Grid { ColumnSpacing = 10 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (title != null) head.Children.Add(CardTitle(title));
            if (headerRight != null)
            {
                Detach(headerRight);
                Grid.SetColumn((FrameworkElement)headerRight, 1);
                head.Children.Add(headerRight);
            }
            sp.Children.Add(head);
        }
        Detach(content);
        sp.Children.Add(content);
        return MakeCard(sp);
    }

    /// <summary>Removes an element from its current parent so it can be placed elsewhere.</summary>
    internal static void Detach(UIElement el)
    {
        var parent = (el as FrameworkElement)?.Parent ?? VisualTreeHelper.GetParent(el);
        switch (parent)
        {
            case Panel p: p.Children.Remove(el); break;
            case Border b when ReferenceEquals(b.Child, el): b.Child = null; break;
            case ContentControl cc when ReferenceEquals(cc.Content, el): cc.Content = null; break;
            case ScrollViewer sv when ReferenceEquals(sv.Content, el): sv.Content = null; break;
        }
    }

    // ── Restyle pass ──────────────────────────────────────────────────────────

    private static Windows.UI.Color? ResColor(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush b ? b.Color : null;

    private static readonly Lazy<(Windows.UI.Color? Primary, Windows.UI.Color? Secondary, Windows.UI.Color? Tertiary)> AppText =
        new(() => (ResColor(ResourceKeys.TextPrimaryBrush), ResColor(ResourceKeys.TextSecondaryBrush), ResColor(ResourceKeys.TextTertiaryBrush)));

    /// <summary>Dropdowns and text boxes: 36 high, radius 8, #0E131D, #263042 border, 13 px (mockup).</summary>
    internal static void StyleInput(Control c)
    {
        c.MinHeight = 36;
        c.CornerRadius = new CornerRadius(8);
        c.FontSize = 13;
        c.Background = B(PanelBg);
        c.BorderBrush = B("#263042");
        c.Foreground = B(TextPrimary);
        c.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (!double.IsNaN(c.Width)) c.Width = double.NaN; // cards are narrower than the Classic columns
        c.MinWidth = 0;
        if (c is ComboBox cb) cb.MaxWidth = double.PositiveInfinity;
    }

    /// <summary>
    /// Gives a built section's controls the mockup's look: inputs as above, buttons by the tone of
    /// their text colour (blue / purple / green / amber / red, grey ones as plain icon-style buttons),
    /// text in the mockup's text colours. Doesn't change behaviour or content.
    /// </summary>
    internal static void Restyle(DependencyObject root)
    {
        switch (root)
        {
            case ComboBox cb:
                StyleInput(cb);
                return; // never walk into dropdown items
            case TextBox tb:
                StyleInput(tb);
                return;
            case Button btn:
                RestyleButton(btn);
                return;
            case TextBlock text:
                RestyleText(text);
                return;
            case ToggleSwitch:
            case Slider:
            case ProgressBar:
                return;
        }

        if (root is Panel panel)
            foreach (var child in panel.Children) Restyle(child);
        else if (root is Border { Child: { } bc })
        {
            // Classic sub-panels (card / overlay colours) take the mockup's inset colour;
            // badges, pills and other coloured borders keep theirs
            if (root is Border bd && bd.Background is SolidColorBrush bg && IsAppSurface(bg.Color))
            {
                bd.Background = B(PanelBg);
                bd.BorderBrush = B(CardBorder);
            }
            Restyle(bc);
        }
        else if (root is ScrollViewer { Content: DependencyObject sc })
            Restyle(sc);
        else if (root is ContentControl { Content: DependencyObject cc })
            Restyle(cc);
    }

    private static readonly Lazy<Windows.UI.Color?[]> AppSurfaces = new(() => new[]
    {
        ResColor(ResourceKeys.SurfaceComponentTableBrush),
        ResColor(ResourceKeys.SurfaceOverlayBrush),
        ResColor("SurfaceCardBrush"),
        ResColor("SurfaceRaisedBrush"),
    });

    private static bool IsAppSurface(Windows.UI.Color c) => AppSurfaces.Value.Any(s => s == c);

    private static void RestyleButton(Button btn)
    {
        var tone = ToneOf(btn.Foreground);
        var look = tone == Tone.Grey ? IconButton : ButtonFor(tone);
        double h = Math.Max(btn.Height is double hh && !double.IsNaN(hh) ? hh : 0, 34);
        bool small = !double.IsNaN(btn.Width) && btn.Width <= 44;
        Apply(btn, look, height: small ? 36 : h, fontSize: 13);
        if (small) btn.Width = 36;
        if (btn.Content is TextBlock ct) ct.Foreground = B(look.Fg);
        btn.Padding = small ? new Thickness(0) : new Thickness(14, 0, 14, 0);
    }

    private static void RestyleText(TextBlock text)
    {
        if (text.Foreground is not SolidColorBrush fg) return;
        var (p, s, t) = AppText.Value;
        if (p is { } pc && fg.Color == pc) text.Foreground = B(TextPrimary);
        else if (s is { } sc && fg.Color == sc) text.Foreground = B(TextSecondary);
        else if (t is { } tc && fg.Color == tc) text.Foreground = B(TextMuted);
        if (text.FontSize < 12) text.FontSize = 12;
    }

    /// <summary>Segmented switch (the mockup's All / Installed / … and ReLimiter / Display Commander).</summary>
    internal static Border MakeSegmented(IReadOnlyList<string> labels, int selected, Action<int> onPick, string track = PanelBg)
    {
        var grid = new Grid { ColumnSpacing = 2 };
        for (int i = 0; i < labels.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bool on = i == selected;
            var label = new TextBlock
            {
                Text = labels[i],
                FontSize = 12,
                FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = B(on ? TextPrimary : TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var seg = new Border
            {
                Height = 30,
                CornerRadius = new CornerRadius(6),
                Background = on ? B("#22304A") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = label,
            };
            int idx = i;
            UseHandCursor(seg);
            seg.PointerEntered += (s, e) =>
            {
                if (!on) label.Foreground = B(TextPrimary);
            };
            seg.PointerExited += (s, e) => { if (!on) label.Foreground = B(TextSecondary); };
            seg.Tapped += (s, e) => { if (!on) onPick(idx); };
            Grid.SetColumn(seg, i);
            grid.Children.Add(seg);
        }
        return new Border
        {
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(9),
            Background = B(track),
            BorderBrush = B(CardBorder),
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }
}
