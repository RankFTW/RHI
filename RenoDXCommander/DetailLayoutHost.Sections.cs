// DetailLayoutHost.Sections.cs -- Tabs layout (mockup A): how each section tab is laid out.
// The section builders still create every control, with its handlers and state, exactly as in
// Classic. In Tabs they hand their finished (not yet shown) content to an Arrange* method here,
// which moves those same controls into the mockup's arrangement and restyles them. Nothing is
// copied, so a setting added to a builder shows up in both layouts.
// Arranging happens before the content is added to the window, so it adds no layout passes
// (see "UI Freeze Investigation" in rhi-knowledge.md).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    /// <summary>Tabs or Spotlight: both use the mockup look and the Arrange* section layouts.</summary>
    internal bool IsTabs => Layout is MainLayout.Tabs or MainLayout.Spotlight;

    internal bool IsSpotlight => Layout == MainLayout.Spotlight;

    // ── Section frames ────────────────────────────────────────────────────────

    private readonly Dictionary<Border, (Brush? Bg, Brush? Border, Thickness Thick, Thickness Pad)> _classicChrome = new();

    /// <summary>
    /// Tabs: section containers lose their Classic card frame, because the mockup's tab content
    /// draws its own cards. Classic: the original frame is put back.
    /// </summary>
    private void ApplyContainerChrome(bool tabs)
    {
        foreach (var key in SectionKeys)
        {
            if (GetContainer(key) is not { } c) continue;
            if (!_classicChrome.ContainsKey(c))
                _classicChrome[c] = (c.Background, c.BorderBrush, c.BorderThickness, c.Padding);
            if (tabs)
            {
                c.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                c.BorderThickness = new Thickness(0);
                c.Padding = new Thickness(0);
            }
            else
            {
                var o = _classicChrome[c];
                c.Background = o.Bg;
                c.BorderBrush = o.Border;
                c.BorderThickness = o.Thick;
                c.Padding = o.Pad;
            }
        }
    }

    // ── Extras: one card per group, one list row per component ───────────────

    // Fixed slot widths so pills, buttons and icons line up down the whole tab
    private const double ExtrasPillCol = 120, ExtrasButtonCol = 112, ExtrasIconCol = 38;

    /// <summary>
    /// Tabs Extras (mockup A): a titled card per group (Classic's separators become the titles;
    /// the rows before the first separator are "Loaders and upscalers"), with a row per component:
    /// name and description, status pill, main button, Info / ⚙ / ✕ in fixed columns. Uses the
    /// controls the Extras row builders made (their 6-column rows: label, status, Info, button, ⚙, ✕).
    /// </summary>
    internal void ArrangeExtras(StackPanel body)
    {
        var items = body.Children.ToList();
        body.Children.Clear();
        body.Spacing = 20;

        string title = "Loaders and upscalers";
        var list = new StackPanel();
        var rows = new List<Border>();

        void Flush()
        {
            if (list.Children.Count == 0) return;
            if (list.Children[^1] is Border last && rows.Contains(last)) last.BorderThickness = new Thickness(0);
            var group = new StackPanel { Spacing = 10 };
            group.Children.Add(TabsTheme.SectionHeading(title));
            var card = TabsTheme.MakeCard(list);
            card.Padding = new Thickness(16, 4, 16, 4);
            group.Children.Add(card);
            body.Children.Add(group);
            list = new StackPanel();
        }

        foreach (var item in items)
        {
            if (item is not Grid g) { list.Children.Add(item); continue; }
            var byCol = g.Children.OfType<FrameworkElement>()
                .GroupBy(Grid.GetColumn).ToDictionary(x => x.Key, x => x.ToList());
            bool isSeparator = !byCol.ContainsKey(0) && byCol.TryGetValue(3, out var c3) && c3.Count == 1 && c3[0] is TextBlock;
            if (isSeparator)
            {
                Flush();
                title = ((TextBlock)byCol[3][0]).Text.Trim('—', ' ');
                continue;
            }
            if (byCol.TryGetValue(0, out var c0) && c0.FirstOrDefault() is TextBlock)
                g.Children.Clear(); // free the controls for their new row (WinUI allows one parent)
            var row = MakeListRow(byCol);
            if (row == null) { list.Children.Add(g); continue; }
            rows.Add(row);
            list.Children.Add(row);
        }
        Flush();
    }

    /// <summary>One mockup list row from a Classic 6-column component row.</summary>
    private static Border? MakeListRow(Dictionary<int, List<FrameworkElement>> byCol)
    {
        FrameworkElement? Col(int i) => byCol.TryGetValue(i, out var l) ? l.FirstOrDefault() : null;
        if (Col(0) is not TextBlock label) return null;

        // Name (600) with its tooltip text as the description underneath
        var nameCol = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        label.FontSize = 14;
        label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        label.Foreground = TabsTheme.B(TabsTheme.TextPrimary);
        label.TextWrapping = TextWrapping.Wrap;
        nameCol.Children.Add(label);
        if (TooltipText(label) is { Length: > 0 } tip)
            nameCol.Children.Add(new TextBlock
            {
                Text = FirstSentence(tip),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TabsTheme.B(TabsTheme.TextSecondary),
            });

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExtrasPillCol) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExtrasButtonCol) });
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExtrasIconCol) });
        grid.Children.Add(nameCol);

        void Put(FrameworkElement el, int col, HorizontalAlignment align = HorizontalAlignment.Stretch)
        {
            Grid.SetColumn(el, col);
            el.VerticalAlignment = VerticalAlignment.Center;
            el.HorizontalAlignment = align;
            grid.Children.Add(el);
        }

        // A disabled main button whose text is a reason ("Installed via OptiScaler", "⚠ ReShade
        // required") becomes a plain disabled "Install" plus the reason in words under the description
        var mains = byCol.TryGetValue(3, out var m) ? m : new();
        string? blockedReason = null;
        foreach (var main in mains)
            if (main is Button bb && (!bb.IsEnabled || !bb.IsHitTestVisible)
                && ContentText(bb.Content) is { Length: > 0 } bt && !IsActionText(bt))
                blockedReason = ShortAction(bt);

        if (Col(1) is TextBlock statusText && !string.IsNullOrWhiteSpace(statusText.Text))
        {
            bool unavailable = false;
            if (string.Equals(statusText.Text.Trim(), "Ready", StringComparison.OrdinalIgnoreCase))
            {
                unavailable = blockedReason != null;
                statusText.Text = unavailable ? "Unavailable" : "Not installed";
            }
            Put(MakeStatusPill(statusText, unavailable), 1, HorizontalAlignment.Right);
        }

        if (blockedReason != null)
            nameCol.Children.Add(new TextBlock
            {
                Text = BlockedSentence(blockedReason),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TabsTheme.B("#F0C35A"),
                Margin = new Thickness(0, 2, 0, 0),
            });

        foreach (var main in mains)
        {
            TabsTheme.Restyle(main);
            if (main is Button mb)
            {
                StyleMainButton(mb);
                mb.HorizontalAlignment = HorizontalAlignment.Stretch;
                if (blockedReason != null) mb.Content = "Install";
            }
            Put(main, 2);
        }
        if (Col(2) is Button info) { IconOf(info, TabsTheme.GlyphInfo); Put(info, 3); }
        if (Col(4) is Button cog) { IconOf(cog, TabsTheme.GlyphSettings); Put(cog, 4); }
        if (Col(5) is Button del) { IconOf(del, TabsTheme.GlyphClose, danger: true); Put(del, 5); }

        return new Border
        {
            Padding = new Thickness(0, 12, 0, 12),
            BorderBrush = TabsTheme.B("#1A2130"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    /// <summary>True for a normal button label ("Install", "↺ Reinstall X", "Installing…").</summary>
    private static bool IsActionText(string text)
    {
        var first = ShortAction(text).Split(' ', 2)[0].TrimEnd('…', '.');
        if (first.Length == 0) return true;
        return ActionVerbs.Any(v => first.Equals(v, StringComparison.OrdinalIgnoreCase)
                                 || first.Equals(v.TrimEnd('e') + "ing", StringComparison.OrdinalIgnoreCase))
            || first.Equals("Redeploy", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"Installed via OptiScaler" → a short sentence saying why the button is off.</summary>
    private static string BlockedSentence(string reason)
    {
        var r = reason.Trim().TrimEnd('.');
        if (r.EndsWith("via OptiScaler", StringComparison.OrdinalIgnoreCase))
            return "Not needed: OptiScaler already provides this for the game.";
        if (r.EndsWith(" required", StringComparison.OrdinalIgnoreCase))
            return $"Needs {r[..^" required".Length]} installed first.";
        if (r.EndsWith(" installed", StringComparison.OrdinalIgnoreCase))
            return $"Can't be used while {r[..^" installed".Length]} is installed.";
        return r + ".";
    }

    private static string FirstSentence(string text)
    {
        var t = text.Replace("\r", "").Split('\n')[0].Trim();
        var dash = t.IndexOf(" — ", StringComparison.Ordinal);
        if (dash > 0 && dash < t.Length - 3) t = t[(dash + 3)..];
        int dot = t.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 20) t = t[..(dot + 1)];
        return t.Length > 0 ? char.ToUpperInvariant(t[0]) + t[1..] : t;
    }

    /// <summary>Wraps a row's status TextBlock in a mockup pill, keeping its click and tooltip.</summary>
    private static Border MakeStatusPill(TextBlock status, bool amber = false)
    {
        var tone = amber ? TabsTheme.Tone.Amber : TabsTheme.ToneOf(status.Foreground);
        var look = TabsTheme.PillFor(tone);
        status.Text = (status.Text ?? "").Replace("\r", "").Replace('\n', ' ');
        status.FontSize = 12;
        status.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        status.Foreground = TabsTheme.B(look.Fg);
        status.TextDecorations = Windows.UI.Text.TextDecorations.None;
        status.HorizontalTextAlignment = TextAlignment.Center;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.MaxWidth = ExtrasPillCol - 22;
        status.Margin = new Thickness(0);
        return new Border
        {
            Padding = new Thickness(10, 3, 10, 3),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = TabsTheme.B(look.Bg),
            BorderBrush = TabsTheme.B(look.Border),
            Child = status,
        };
    }

    /// <summary>Main list button: 34 high, 16 px side padding, glyph prefixes dropped (mockup "Install").</summary>
    private static void StyleMainButton(Button b)
    {
        b.Height = 34;
        b.Padding = new Thickness(16, 0, 16, 0);
        b.HorizontalAlignment = HorizontalAlignment.Right;
        b.Width = double.NaN;
        if (b.Content is string s) b.Content = ShortAction(s);
        else if (b.Content is TextBlock tb) tb.Text = ShortAction(tb.Text);
    }


    /// <summary>Turns a row's small text button (Info / ⚙ / ✕) into the mockup's 38×38 icon button.</summary>
    private static void IconOf(Button b, string glyph, bool danger = false)
    {
        bool hidden = b.Opacity < 0.05 || b.Visibility != Visibility.Visible;
        var tone = TabsTheme.ToneOf(b.Foreground);
        var look = danger ? TabsTheme.DangerButton
                 : tone == TabsTheme.Tone.Blue ? TabsTheme.IconButton with { Border = "#2A3D63" }
                 : TabsTheme.IconButton;
        TabsTheme.Apply(b, look);
        b.Width = 38;
        b.Padding = new Thickness(0);
        b.Content = TabsTheme.Icon(glyph, danger ? 12 : 15);
        // Classic hides ✕ with Opacity 0 to keep its column; here it can just go
        if (hidden && b.IsHitTestVisible == false) b.Visibility = Visibility.Collapsed;
    }

    // ── Column sections (Driver Settings, DLSS / Streamline) ──────────────────

    /// <summary>A label-above-control field taken from a Classic column.</summary>
    private sealed record ColumnField(string Group, string? Label, object? Tip, Brush? LabelColour, UIElement Control);

    /// <summary>
    /// Reads Classic's column layout (a Grid of StackPanel columns: an 11 px title, then 10 px labels
    /// each followed by its control) into fields, in order. Dividers are dropped.
    /// </summary>
    private static List<ColumnField> ReadColumns(Grid columns)
    {
        var fields = new List<ColumnField>();
        var cols = columns.Children.OfType<FrameworkElement>()
            .OrderBy(Grid.GetColumn).ToList();
        foreach (var col in cols)
        {
            if (col is not Panel panel) continue; // dividers
            string group = "";
            string? pending = null;
            object? pendingTip = null;
            Brush? pendingColour = null;
            bool first = true;
            var children = panel.Children.ToList();
            panel.Children.Clear(); // free the controls for their new fields (WinUI allows one parent)
            foreach (var child in children)
            {
                if (child is TextBlock tb)
                {
                    if (first) { group = tb.Text.Trim(); first = false; continue; }
                    pending = tb.Text;
                    pendingTip = ToolTipService.GetToolTip(tb);
                    pendingColour = tb.Foreground;
                    continue;
                }
                first = false;
                if (child.Visibility != Visibility.Visible) { pending = null; continue; }
                fields.Add(new ColumnField(group, string.IsNullOrWhiteSpace(pending) ? null : pending.Trim(),
                    pendingTip, pendingColour, child));
                pending = null; pendingTip = null; pendingColour = null;
            }
        }
        return fields;
    }

    private static readonly HashSet<string> GenericLabels = new(StringComparer.OrdinalIgnoreCase)
        { "Mode", "Enable", "Version", "DLL", "Type" };

    /// <summary>The mockup's field labels: a group's first generic field is named after the group.</summary>
    private static List<(string Label, ColumnField Field)> NameFields(List<ColumnField> fields, Func<string, string> shortGroup)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupsNamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, ColumnField)>();
        foreach (var f in fields)
        {
            string label;
            if (f.Label == null && groupsNamed.Contains(f.Group))
            {
                // A button under the group's other controls (Multi Frame Gen, Deploy DLL): no label of its own
                result.Add(("", f));
                continue;
            }
            if (f.Label == null || GenericLabels.Contains(f.Label))
                label = groupsNamed.Add(f.Group) ? f.Group : $"{f.Group} {(f.Label ?? "").ToLowerInvariant()}".Trim();
            else
                label = f.Label;
            groupsNamed.Add(f.Group);
            if (!used.Add(label)) { label = $"{shortGroup(f.Group)} {label.ToLowerInvariant()}"; used.Add(label); }
            result.Add((label, f));
        }
        return result;
    }

    private static StackPanel MakeField(string label, ColumnField f)
    {
        var field = TabsTheme.Field(label, f.Control);
        if (string.IsNullOrEmpty(label))
            field.Children.Insert(0, new TextBlock { Text = " ", FontSize = 12 }); // keeps it in line with labelled fields
        if (field.Children.Count > 1 && field.Children[0] is TextBlock lt)
        {
            if (f.Tip != null) ToolTipService.SetToolTip(lt, f.Tip is string s ? s : TooltipTextOf(f.Tip));
            // Keep a label's warning colour (e.g. a greyed-out reason)
            if (f.LabelColour is SolidColorBrush c && TabsTheme.ToneOf(c) is TabsTheme.Tone.Amber or TabsTheme.Tone.Red)
                lt.Foreground = TabsTheme.B(TabsTheme.PillFor(TabsTheme.ToneOf(c)).Fg);
        }
        TabsTheme.Restyle(f.Control);
        StretchControl(f.Control);
        return field;
    }

    private static string? TooltipTextOf(object tip) => tip switch
    {
        string s => s,
        TextBlock tb => tb.Text,
        ToolTip { Content: string s2 } => s2,
        _ => null,
    };

    /// <summary>Lets a moved control fill its field (Classic gave many of them fixed column widths).</summary>
    private static void StretchControl(UIElement el)
    {
        if (el is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (fe is not Button { Content: FontIcon }) fe.Width = double.NaN;
            fe.MinWidth = 0;
        }
        if (el is Grid g)
            foreach (var cd in g.ColumnDefinitions)
                if (cd.Width.IsAbsolute && cd.Width.Value > 60)
                    cd.Width = new GridLength(1, GridUnitType.Star);
    }

    /// <summary>
    /// Tabs Driver Settings (mockup A): one card, "Driver Settings" with the driver version on the
    /// right, every setting as a label-above-dropdown field in an auto-fit grid (180 px columns),
    /// then the admin note and Restore Defaults.
    /// </summary>
    internal UIElement ArrangeDriver(Grid columns, Grid footer, string? driverVersion)
    {
        // Grouped like Classic's columns (VSync, Smooth Motion, Other, ReBAR): one column per
        // group with a small heading, its fields stacked underneath
        var read = ReadColumns(columns);
        var fields = NameFields(read, g => g);
        var grid = new AutoFitGrid { MinColumnWidth = 200, MaxColumns = 4, ColumnSpacing = 24, RowSpacing = 20 };
        foreach (var group in fields.GroupBy(x => x.Field.Group))
        {
            var col = new StackPanel { Spacing = 10 };
            var head = TabsTheme.SectionHeading(DriverGroupTitle(group.Key));
            head.FontSize = 12;
            col.Children.Add(head);
            foreach (var (label, f) in group) col.Children.Add(MakeField(label, f));
            grid.Children.Add(col);
        }

        TextBlock? version = null;
        if (!string.IsNullOrEmpty(driverVersion))
        {
            version = new TextBlock { FontSize = 12, Foreground = TabsTheme.B(TabsTheme.TextSecondary), VerticalAlignment = VerticalAlignment.Center };
            version.Inlines.Add(new Run { Text = "Driver " });
            version.Inlines.Add(new Run { Text = driverVersion, Foreground = TabsTheme.B(TabsTheme.Value) });
        }

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(grid);

        // Footer: admin note (left) and Restore Defaults (right)
        var footerItems = footer.Children.OfType<FrameworkElement>().ToList();
        footer.Children.Clear();
        var foot = new Grid { ColumnSpacing = 10 };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var item in footerItems)
        {
            TabsTheme.Detach(item);
            TabsTheme.Restyle(item);
            // The version is already in the card's header
            if (item is TextBlock note && !string.IsNullOrEmpty(driverVersion)
                && note.Text.StartsWith("Driver " + driverVersion, StringComparison.Ordinal))
                note.Text = note.Text[("Driver " + driverVersion).Length..].TrimStart(' ', '·').TrimStart();
            if (item is Button b)
            {
                b.Width = double.NaN;
                b.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(b, 1);
            }
            else Grid.SetColumn(item, 0);
            item.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(item);
        }
        if (foot.Children.Count > 0) content.Children.Add(foot);

        return TabsTheme.TitledCard("Driver Settings", content, version);
    }

    /// <summary>Classic's column titles, with "Other" named after what it holds.</summary>
    private static string DriverGroupTitle(string group) => group switch
    {
        _ when group.Equals("VSync", StringComparison.OrdinalIgnoreCase) => "VSync and latency",
        _ when group.Equals("Other", StringComparison.OrdinalIgnoreCase) => "Power, G-Sync and FPS",
        _ => group,
    };

    /// <summary>
    /// Tabs DLSS / Streamline (mockup A): one card, "DLSS / Streamline" with the Streamline column's
    /// action buttons (apply defaults, restore) on the right, every setting as a field in an
    /// auto-fit grid (200 px columns). Anything else the builder added (notes) follows the grid.
    /// </summary>
    internal void ArrangeDlss(StackPanel body)
    {
        var items = body.Children.ToList();
        body.Children.Clear();
        var columns = items.OfType<Grid>().FirstOrDefault(g => g.Children.OfType<StackPanel>().Count() >= 2);
        if (columns == null)
        {
            // Nothing to arrange (no DLSS files): just restyle what's there in a card
            var plain = new StackPanel { Spacing = 10 };
            foreach (var i in items) { TabsTheme.Restyle(i); plain.Children.Add(i); }
            if (!items.Any(i => i.Visibility == Visibility.Visible && i is not StackPanel { Children.Count: 0 }))
                plain.Children.Add(new TextBlock
                {
                    Text = "No DLSS or Streamline files found in this game.",
                    FontSize = 13,
                    Foreground = TabsTheme.B(TabsTheme.TextSecondary),
                });
            body.Children.Add(TabsTheme.TitledCard("DLSS / Streamline", plain));
            return;
        }

        static string Short(string g) => g switch
        {
            "DLSS Super Resolution" => "SR",
            "Ray Reconstruction"    => "RR",
            "Frame Generation"      => "FG",
            "Neural Rendering"      => "NR",
            "Streamline"            => "SL",
            _                       => g,
        };

        var read = ReadColumns(columns)
            .Select(f => f with { Group = f.Group == "DLSS Super Resolution" ? "Super Resolution" : f.Group })
            .ToList();

        // The Streamline column's button-only items are the section's actions (mockup header)
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var fieldItems = new List<ColumnField>();
        foreach (var f in read)
        {
            bool buttonOnly = f.Group == "Streamline" && IsButtonOnly(f.Control);
            if (buttonOnly)
            {
                TabsTheme.Detach(f.Control);
                TabsTheme.Restyle(f.Control);
                foreach (var b in Descendants<Button>(f.Control)) { b.Width = double.NaN; b.Height = 34; b.FontSize = 12; }
                if (f.Control is FrameworkElement fe) { fe.Width = double.NaN; fe.HorizontalAlignment = HorizontalAlignment.Right; }
                actions.Children.Add(f.Control);
            }
            else fieldItems.Add(f);
        }

        var fields = NameFields(fieldItems, g => Short(g == "Super Resolution" ? "DLSS Super Resolution" : g));
        // One row per component, three slots each: SR / RR (version, preset, render scale),
        // FG (version, preset, Multi Frame Gen), NR (version, preset, Deploy DLL), then Streamline.
        // Short rows are padded with empty slots so the columns line up.
        string[] order = { "Super Resolution", "Ray Reconstruction", "Frame Generation", "Neural Rendering", "Streamline" };
        const int Slots = 3;
        var grid = new AutoFitGrid { MinColumnWidth = 180, MaxColumns = Slots, ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var group in fields.GroupBy(x => x.Field.Group)
                     .OrderBy(g => Array.IndexOf(order, g.Key) is var i && i >= 0 ? i : order.Length))
        {
            int n = 0;
            foreach (var (label, f) in group) { grid.Children.Add(MakeField(label, f)); n++; }
            for (; n % Slots != 0; n++) grid.Children.Add(new Border());
        }

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(grid);
        foreach (var other in items)
        {
            if (ReferenceEquals(other, columns)) continue;
            TabsTheme.Restyle(other);
            content.Children.Add(other);
        }
        body.Children.Add(TabsTheme.TitledCard("DLSS / Streamline", content, actions.Children.Count > 0 ? actions : null));
    }

    // ── Game Overrides: grouped cards ─────────────────────────────────────────

    /// <summary>
    /// Tabs Game Overrides (mockup A): the section's six groups (Classic's three rows of two
    /// columns) as the mockup's four cards: Names and matching, ReShade (bitness, API, channel,
    /// shaders, addons), DLL names, Updates and launch. Two cards per row at most, so the groups'
    /// own side-by-side controls keep enough room.
    /// </summary>
    internal void ArrangeOverrides(StackPanel panel)
    {
        var items = panel.Children.ToList();
        panel.Children.Clear();

        static (UIElement? Left, UIElement? Right) Split(Grid g)
        {
            var kids = g.Children.OfType<FrameworkElement>().ToList();
            var left = kids.FirstOrDefault(k => Grid.GetColumn(k) == 0 && k is Panel);
            var right = kids.FirstOrDefault(k => Grid.GetColumn(k) == 2 && k is Panel);
            g.Children.Clear();
            return (left, right);
        }

        var rows = items.OfType<Grid>()
            .Where(g => g.Children.OfType<FrameworkElement>().Any(k => Grid.GetColumn(k) == 2 && k is Panel))
            .ToList();
        var rest = items.Where(i => !rows.Contains(i) && !(i is Border { Child: null })).ToList();

        var cards = new AutoFitGrid { MinColumnWidth = 320, MaxColumns = 2 };
        Border Card(string title, params UIElement?[] parts)
        {
            var content = new StackPanel { Spacing = 14 };
            foreach (var p in parts)
                if (p != null) { TabsTheme.Restyle(p); content.Children.Add(p); }
            return TabsTheme.TitledCard(title, content);
        }

        if (rows.Count == 3)
        {
            var top = Split(rows[0]);
            var middle = Split(rows[1]);
            var bottom = Split(rows[2]);
            DropHeading(top.Right, "DLL naming overrides");
            cards.Children.Add(Card("Names and matching", top.Left));
            cards.Children.Add(Card("ReShade", middle.Left, bottom.Left));
            cards.Children.Add(Card("DLL names", top.Right));
            cards.Children.Add(Card("Updates and launch", middle.Right, bottom.Right));
        }
        else
        {
            // Unexpected shape (a builder changed): one untitled card per group, nothing lost
            foreach (var g in rows)
            {
                var (l, r) = Split(g);
                if (l != null) cards.Children.Add(Card("", l));
                if (r != null) cards.Children.Add(Card("", r));
            }
        }

        panel.Children.Add(cards);
        foreach (var r in rest) { TabsTheme.Restyle(r); panel.Children.Add(r); }
    }

    /// <summary>Removes a group's own heading when the card title already says it.</summary>
    private static void DropHeading(UIElement? group, string text)
    {
        if (group is Panel p && p.Children.FirstOrDefault() is TextBlock tb
            && string.Equals(tb.Text?.Trim(), text, StringComparison.OrdinalIgnoreCase))
            p.Children.Remove(tb);
    }

    // ── Neural Rendering: method cards + setup ────────────────────────────────

    private static string NrMethodDescription(string name) => name switch
    {
        "ShortFuse DLSS Tool"      => "For most games with native DLSS (DX9, DX11, DX12, Vulkan).",
        "DLSS5 Tool"               => "For DX12 games with native DLSS.",
        "DLSS5 Tool + DX11 Bridge" => "For DX11 and Vulkan games with native DLSS.",
        "DLSS5 Feeder"             => "For games without DLSS, including OpenGL, DX9 and 32-bit games.",
        _                          => "",
    };

    /// <summary>
    /// Tabs Neural Rendering (mockup A): a card per method (name, what it's for, Select), then a
    /// "Setup" card with everything else the section has: the method and version dropdowns as
    /// fields, status, description, install buttons, Cost Scaler and links. Selecting a method card
    /// changes the Method dropdown, so the section's own handler does the work.
    /// </summary>
    internal void ArrangeNeuralRendering(StackPanel body)
    {
        var items = body.Children.ToList();
        body.Children.Clear();

        // Row 1 (Method / versions) is the first Grid of label+dropdown columns
        var row1 = items.OfType<Grid>().FirstOrDefault(g => g.Children.OfType<StackPanel>().Any(sp => sp.Children.OfType<ComboBox>().Any()));
        ComboBox? method = null;
        var setup = new StackPanel { Spacing = 14 };
        if (row1 != null)
        {
            method = Descendants<ComboBox>(row1).FirstOrDefault(c => c.Items.OfType<ComboBoxItem>()
                .Any(i => i.Content as string == "DLSS5 Feeder"));
            // Each column panel (label + dropdown) is kept whole and only restyled: the builder
            // shows/hides it and renames its label when the method changes (Bridge / Feeder Version)
            var grid = new AutoFitGrid { MinColumnWidth = 200, ColumnSpacing = 10, RowSpacing = 10 };
            var cols = row1.Children.OfType<FrameworkElement>().OrderBy(Grid.GetColumn).ToList();
            row1.Children.Clear();
            foreach (var col in cols)
            {
                if (col is not StackPanel sp) continue; // dividers
                sp.Spacing = 6;
                sp.Margin = new Thickness(0);
                foreach (var child in sp.Children)
                {
                    if (child is TextBlock lt)
                    {
                        lt.FontSize = 12;
                        if (TabsTheme.ToneOf(lt.Foreground) is not (TabsTheme.Tone.Amber or TabsTheme.Tone.Red))
                            lt.Foreground = TabsTheme.B(TabsTheme.TextSecondary);
                        lt.TextTrimming = TextTrimming.CharacterEllipsis;
                    }
                    else
                    {
                        TabsTheme.Restyle(child);
                        StretchControl(child);
                    }
                }
                StretchControl(sp);
                grid.Children.Add(sp);
            }
            setup.Children.Add(grid);
        }
        foreach (var item in items)
        {
            if (ReferenceEquals(item, row1)) continue;
            TabsTheme.Restyle(item);
            setup.Children.Add(item);
        }

        var outer = new StackPanel { Spacing = 12 };
        if (method != null)
        {
            var methods = new AutoFitGrid { MinColumnWidth = 240 };
            var options = method.Items.OfType<ComboBoxItem>().ToList();
            var greyedTip = TooltipText(method);
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                var opt = options[i];
                var name = opt.Content as string ?? "";
                bool selected = method.SelectedIndex == i;
                var title = new TextBlock
                {
                    Text = name,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = TabsTheme.B(TabsTheme.TextPrimary),
                    TextWrapping = TextWrapping.Wrap,
                };
                var desc = new TextBlock
                {
                    Text = NrMethodDescription(name),
                    FontSize = 13,
                    LineHeight = 19.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TabsTheme.B(TabsTheme.TextSecondary),
                };
                var pick = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Content = selected ? "Selected" : "Select" };
                TabsTheme.Apply(pick, selected ? TabsTheme.GreenButton : TabsTheme.BlueButton, height: 36);
                pick.IsEnabled = opt.IsEnabled && !selected;
                pick.Click += (s, e) => method.SelectedIndex = idx;

                var cardBody = new Grid { RowSpacing = 10 };
                cardBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cardBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cardBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                cardBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cardBody.Children.Add(title);
                Grid.SetRow(desc, 1); cardBody.Children.Add(desc);
                Grid.SetRow(pick, 3); cardBody.Children.Add(pick);
                var card = TabsTheme.MakeCard(cardBody);
                if (selected) card.BorderBrush = TabsTheme.B(TabsTheme.Accent);
                if (!opt.IsEnabled)
                {
                    card.Opacity = 0.5;
                    if (greyedTip != null) ToolTipService.SetToolTip(card, greyedTip);
                }
                methods.Children.Add(card);
            }
            outer.Children.Add(methods);
        }
        outer.Children.Add(TabsTheme.TitledCard("Setup", setup));
        body.Children.Add(outer);
    }

    private static bool IsButtonOnly(UIElement el) =>
        el is Button || (Descendants<Button>(el).Any() && !Descendants<ComboBox>(el).Any() && !Descendants<TextBox>(el).Any());

    private static IEnumerable<T> Descendants<T>(UIElement root) where T : class
    {
        if (root is T t) yield return t;
        IEnumerable<UIElement> kids = root switch
        {
            Panel p => p.Children,
            Border { Child: { } c } => new[] { c },
            ContentControl { Content: UIElement cc } => new[] { cc },
            _ => Array.Empty<UIElement>(),
        };
        foreach (var k in kids)
            foreach (var d in Descendants<T>(k)) yield return d;
    }
}
