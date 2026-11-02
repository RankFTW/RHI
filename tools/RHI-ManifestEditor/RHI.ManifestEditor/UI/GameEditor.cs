using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RHI.ManifestEditor.Json;
using RHI.ManifestEditor.Services;

namespace RHI.ManifestEditor.UI;

/// <summary>Every manifest setting for one game, on one page.</summary>
public sealed class GameEditor
{
    private readonly IEditorHost _host;
    private readonly string _game;
    private JsonDoc Doc => _host.Doc;

    private readonly StackPanel _summary = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly List<(GameGroup Group, Border Pill, TextBlock PillText)> _groupPills = new();

    /// <summary>Groups the user opened or closed this session (applies to every game).</summary>
    private static readonly Dictionary<string, bool> GroupOpen = new();

    public GameEditor(IEditorHost host, string game)
    {
        _host = host;
        _game = game;
    }

    private string[] P(string field) => new[] { field };
    private string[] P(string field, string sub) => new[] { field, sub };

    // ── Is-set logic ─────────────────────────────────────────────────────────

    private bool InList(string key) => Doc.ArrayContains(P(key), _game);

    private bool IsSet(GameField f) => f.Kind switch
    {
        FieldKind.Flag => InList(f.Key),
        FieldKind.Bitness => Catalog.BitnessKeys.Any(InList),
        FieldKind.UeMode => InList("ueExtendedGames") || InList("nativeHdrGames") || InList("noUeExtendedGames")
                            || Doc.Get("ueExtendedCompatibility", _game) != null,
        _ => Doc.Get(f.Key, _game) != null,
    };

    public List<string> SetLabels() => Catalog.Groups
        .SelectMany(g => g.Fields.Select(f => (g, f)))
        .Where(x => IsSet(x.f))
        .Select(x => x.g.Title == "Info button text" ? $"{x.f.Label} info text"
                   : x.g.Title == "Install warnings" ? "Install warnings" : x.f.Label)
        .ToList();

    /// <summary>Refreshes the "set" counters without rebuilding the controls (keeps focus while typing).</summary>
    public void UpdateSummary()
    {
        _summary.Children.Clear();
        var labels = SetLabels();
        if (labels.Count == 0)
            _summary.Children.Add(Ui.T("Nothing set for this game yet.", 13, fg: Ui.Secondary));
        else
        {
            _summary.Children.Add(Ui.T("Set:", 13, fg: Ui.Secondary));
            foreach (var l in labels) _summary.Children.Add(Ui.Pill(l, Ui.AccentText));
        }
        foreach (var (group, pill, text) in _groupPills)
        {
            int n = group.Fields.Count(IsSet);
            pill.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            text.Text = $"{n} set";
        }
    }

    // ── Page ─────────────────────────────────────────────────────────────────

    public UIElement Build()
    {
        var page = new StackPanel { Spacing = 14, Padding = new Thickness(28, 20, 28, 40), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };

        // Header
        var title = Ui.T(_game, 26, bold: true);
        var actions = Ui.H(8,
            Ui.Btn("Rename...", () => _ = RenameAsync(), tooltip: "Change this game's name everywhere in the manifest"),
            Ui.Btn("Remove from manifest...", () => _ = RemoveAllAsync(), tooltip: "Delete every setting for this game"));
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(title);
        Grid.SetColumn(actions, 1);
        actions.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(actions);
        page.Children.Add(head);
        page.Children.Add(LibraryHint());
        _summary.Margin = new Thickness(0, 2, 0, 6);
        var summaryScroll = new ScrollViewer
        {
            Content = _summary,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
        };
        page.Children.Add(summaryScroll);

        foreach (var group in Catalog.Groups)
            page.Children.Add(GroupCard(group));

        UpdateSummary();
        return page;
    }

    private UIElement LibraryHint()
    {
        var lib = _host.LibraryGames;
        var exact = lib.FirstOrDefault(g => g.Name == _game);
        if (exact != null)
            return Ui.T($"In your RHI library ({(string.IsNullOrEmpty(exact.Source) ? "unknown store" : exact.Source)}).", 13, fg: Ui.Success);

        var panel = Ui.V(6);
        if (lib.Count == 0)
        {
            panel.Children.Add(Ui.T("Your RHI library wasn't found, so this name can't be checked. It must match the name RHI detects exactly.", 13, fg: Ui.Secondary));
            return panel;
        }
        panel.Children.Add(Ui.T("Not in your RHI library. The name must match what RHI detects exactly (Ubisoft games use folder names, e.g. Watch_Dogs2).", 13, fg: Ui.Caution));
        var loose = Library.Loose(_game);
        var similar = lib.Where(g => Library.Loose(g.Name) == loose
                                  || (loose.Length >= 5 && Library.Loose(g.Name).Contains(loose))
                                  || (Library.Loose(g.Name).Length >= 5 && loose.Contains(Library.Loose(g.Name))))
                         .Take(5).ToList();
        if (similar.Count > 0)
        {
            var row = Ui.H(6, Ui.T("Did you mean:", 13, fg: Ui.Secondary));
            foreach (var s in similar)
            {
                var name = s.Name;
                var b = new HyperlinkButton { Content = name, Padding = new Thickness(4, 0, 4, 0) };
                ToolTipService.SetToolTip(b, $"Rename this game's settings to \"{name}\"");
                b.Click += (_, _) => _ = RenameToAsync(name);
                row.Children.Add(b);
            }
            panel.Children.Add(row);
        }
        return panel;
    }

    private UIElement GroupCard(GameGroup group)
    {
        int count = group.Fields.Count(IsSet);
        bool open = GroupOpen.TryGetValue(group.Title, out var o) ? o : count > 0;

        var chevron = new FontIcon { Glyph = open ? "" : "", FontSize = 12, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var pillText = Ui.T($"{count} set", 11, fg: Ui.AccentText, wrap: false);
        var pill = new Border
        {
            Padding = new Thickness(8, 1, 8, 2), CornerRadius = new CornerRadius(10),
            BorderBrush = Ui.AccentText, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Child = pillText,
            Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        _groupPills.Add((group, pill, pillText));

        var headGrid = new Grid { ColumnSpacing = 12, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headGrid.Children.Add(chevron);
        var titles = Ui.V(2, Ui.T(group.Title, 16, bold: true), Ui.Help(group.Description));
        Grid.SetColumn(titles, 1);
        headGrid.Children.Add(titles);
        Grid.SetColumn(pill, 2);
        headGrid.Children.Add(pill);

        var body = new StackPanel { Spacing = 22, Margin = new Thickness(0, 18, 0, 4), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
        bool built = false;
        void EnsureBuilt()
        {
            if (built) return;
            built = true;
            foreach (var f in group.Fields) body.Children.Add(FieldRow(f));
        }
        if (open) EnsureBuilt();

        var headBtn = new Button
        {
            Content = headGrid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        headBtn.Click += (_, _) =>
        {
            bool nowOpen = body.Visibility != Visibility.Visible;
            if (nowOpen) EnsureBuilt();
            body.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
            chevron.Glyph = nowOpen ? "" : "";
            GroupOpen[group.Title] = nowOpen;
        };

        return Ui.Card(Ui.V(0, headBtn, body), 14);
    }

    private UIElement FieldRow(GameField f)
    {
        var control = ControlFor(f);
        if (f.Kind == FieldKind.Warnings) return control; // has its own labels
        return Ui.Row(f.Label, f.Help, control);
    }

    /// <summary>The editing control for one field of this game. <paramref name="compact"/> is for the Fields view (warnings show only the components that have one).</summary>
    public UIElement ControlFor(GameField f, bool compact = false) => f.Kind switch
    {
        FieldKind.Flag => FlagControl(f),
        FieldKind.Text => TextControl(f),
        FieldKind.Number => NumberControl(f),
        FieldKind.Choice => ChoiceControl(f),
        FieldKind.Note => NoteControl(f),
        FieldKind.Link => LinkControl(f),
        FieldKind.DllNames => DllControl(f),
        FieldKind.KeyValues => KeyValuesControl(f),
        FieldKind.Warnings => compact ? CompactWarningsControl(f) : WarningsControl(f),
        FieldKind.Split => SplitControl(f),
        FieldKind.Bitness => BitnessControl(),
        FieldKind.UeMode => UeModeControl(),
        FieldKind.Api => ApiControl(f),
        _ => Ui.T("?"),
    };

    // ── Controls ─────────────────────────────────────────────────────────────

    private UIElement FlagControl(GameField f)
    {
        var ts = new ToggleSwitch { IsOn = InList(f.Key), OnContent = "Yes", OffContent = "No" };
        ts.Toggled += (_, _) =>
        {
            bool on = ts.IsOn;
            _host.Apply(() => { if (on) Doc.AddToArray(P(f.Key), _game); else Doc.RemoveFromArray(P(f.Key), _game); });
        };
        return ts;
    }

    /// <summary>Text box bound to field[game] (string). Empty removes the entry.</summary>
    private TextBox StringBox(string field, string? placeholder, bool multiline = false)
    {
        var current = Doc.Get(field, _game);
        var box = Ui.Box(current is { IsString: true } ? Ui.ForTextBox(current.Str!) : "", placeholder, multiline);
        if (current != null && !current.IsString)
        {
            box.Text = Doc.Raw(current);
            box.IsEnabled = false;
            ToolTipService.SetToolTip(box, "This value isn't text in the file. Edit it under Global > Raw JSON.");
            return box;
        }
        box.TextChanged += (_, _) =>
        {
            var v = Ui.NormalizeNewlines(box.Text).Trim();
            _host.Defer($"{field}|{_game}", () =>
            {
                if (v.Length == 0) Doc.Remove(P(field), _game);
                else Doc.Set(P(field), _game, JsonOut.Str(v));
            });
        };
        return box;
    }

    private UIElement TextControl(GameField f) => StringBox(f.Key, f.Placeholder, f.Multiline);

    private UIElement NumberControl(GameField f)
    {
        var current = Doc.Get(f.Key, _game);
        var box = Ui.Box(current != null ? Doc.Raw(current).Trim('"') : "", f.Placeholder);
        box.MaxWidth = 260;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        var err = Ui.T("Numbers only.", 12, fg: Ui.Critical);
        err.Visibility = Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            var v = box.Text.Trim();
            bool ok = v.Length == 0 || long.TryParse(v, out _);
            err.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            if (!ok) return;
            _host.Defer($"{f.Key}|{_game}", () =>
            {
                if (v.Length == 0) Doc.Remove(P(f.Key), _game);
                else Doc.Set(P(f.Key), _game, JsonOut.Num(long.Parse(v)));
            });
        };
        return Ui.V(4, box, err);
    }

    private const string NotSet = "(not set)";

    private UIElement ChoiceControl(GameField f)
    {
        var current = Doc.GetString(f.Key, _game);
        var options = f.Options?.Invoke(Doc).ToList() ?? new();
        if (current != null && !options.Contains(current)) options.Insert(0, current);
        var combo = new ComboBox { MinWidth = 220 };
        combo.Items.Add(NotSet);
        foreach (var o in options) combo.Items.Add(o);
        combo.SelectedItem = current ?? NotSet;
        combo.SelectionChanged += (_, _) =>
        {
            var v = combo.SelectedItem as string;
            _host.Apply(() =>
            {
                if (v == null || v == NotSet) Doc.Remove(P(f.Key), _game);
                else Doc.Set(P(f.Key), _game, JsonOut.Str(v));
            });
        };
        return combo;
    }

    /// <summary>Text box bound to field[game][member]. Empty removes the member, and the entry once it's empty.</summary>
    private TextBox MemberBox(string field, string member, string placeholder, bool multiline = false)
    {
        var cur = Doc.Get(field, _game, member);
        var box = Ui.Box(cur is { IsString: true } ? Ui.ForTextBox(cur.Str!) : "", placeholder, multiline);
        box.TextChanged += (_, _) =>
        {
            var v = Ui.NormalizeNewlines(box.Text);
            v = multiline ? v.TrimEnd() : v.Trim();
            _host.Defer($"{field}|{_game}|{member}", () =>
            {
                if (v.Length == 0) Doc.RemoveAndPrune(P(field, _game), member);
                else Doc.Set(P(field, _game), member, JsonOut.Str(v));
            });
        };
        return box;
    }

    private UIElement NoteControl(GameField f)
    {
        var notes = MemberBox(f.Key, "notes", "Text shown in the Info window. Leave everything empty for none.", multiline: true);
        var url = MemberBox(f.Key, "notesUrl", "Link (optional)");
        var label = MemberBox(f.Key, "notesUrlLabel", "Link text (optional)");
        var linkRow = new Grid { ColumnSpacing = 8 };
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        linkRow.Children.Add(url);
        Grid.SetColumn(label, 1);
        linkRow.Children.Add(label);
        return Ui.V(8, notes, linkRow);
    }

    private UIElement LinkControl(GameField f)
    {
        var url = MemberBox(f.Key, "url", "Page to open, e.g. https://www.nexusmods.com/...");
        var label = MemberBox(f.Key, "label", "Button text, e.g. Get on Nexus");
        return Ui.V(8, url, label);
    }

    private UIElement DllControl(GameField f)
    {
        var entry = Doc.Get(f.Key, _game);
        var rs = Ui.Box(entry?.Get("reshade")?.Str ?? "", "ReShade, e.g. dxgi.dll");
        var dc = Ui.Box(entry?.Get("dc")?.Str ?? "", "Display Commander, e.g. winmm.dll");
        void Changed()
        {
            var r = rs.Text.Trim();
            var d = dc.Text.Trim();
            _host.Defer($"{f.Key}|{_game}", () =>
            {
                if (r.Length == 0 && d.Length == 0) { Doc.Remove(P(f.Key), _game); return; }
                // Both keys are always written, like the existing entries
                Doc.Set(P(f.Key, _game), "reshade", JsonOut.Str(r));
                Doc.Set(P(f.Key, _game), "dc", JsonOut.Str(d));
            });
        }
        rs.TextChanged += (_, _) => Changed();
        dc.TextChanged += (_, _) => Changed();
        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Ui.V(4, Ui.Help("ReShade"), rs));
        var dcCol = Ui.V(4, Ui.Help("Display Commander"), dc);
        Grid.SetColumn(dcCol, 1);
        g.Children.Add(dcCol);
        return g;
    }

    private UIElement KeyValuesControl(GameField f)
    {
        var rows = new StackPanel { Spacing = 6 };
        var pairs = new List<(TextBox K, TextBox V)>();
        void Commit()
        {
            var obj = new JsonObject();
            foreach (var (k, v) in pairs)
            {
                var key = k.Text.Trim();
                if (key.Length > 0 && !obj.ContainsKey(key)) obj[key] = JsonOut.Str(v.Text.Trim());
            }
            _host.Defer($"{f.Key}|{_game}", () =>
            {
                if (obj.Count == 0) Doc.Remove(P(f.Key), _game);
                else Doc.Set(P(f.Key), _game, obj.DeepClone());
            });
        }
        void AddRow(string k, string v)
        {
            var kb = Ui.Box(k, "Key, e.g. Upgrade_R8G8B8A8_TYPELESS");
            var vb = Ui.Box(v, "Value");
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(kb);
            Grid.SetColumn(vb, 1); g.Children.Add(vb);
            var pair = (kb, vb);
            var rm = Ui.RemoveBtn(() => { pairs.Remove(pair); rows.Children.Remove(g); Commit(); });
            Grid.SetColumn(rm, 2); g.Children.Add(rm);
            pairs.Add(pair);
            rows.Children.Add(g);
            kb.TextChanged += (_, _) => Commit();
            vb.TextChanged += (_, _) => Commit();
        }
        if (Doc.Get(f.Key, _game) is { IsObject: true } existing)
            foreach (var m in existing.Members!) AddRow(m.Key, m.Value.IsString ? m.Value.Str! : Doc.Raw(m.Value));
        var add = Ui.Btn("Add setting", () => AddRow("", ""));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.V(8, rows, add);
    }

    private UIElement WarningsControl(GameField f)
    {
        var panel = new StackPanel { Spacing = 18 };
        var known = Catalog.WarningComponents.ToList();
        if (Doc.Get(f.Key, _game) is { IsObject: true } existing)
            foreach (var m in existing.Members!.Where(m => known.All(k => k.Key != m.Key)))
                known.Add((m.Key, m.Key));
        foreach (var (key, label) in known)
            panel.Children.Add(Ui.Row(label, null, MemberBox(f.Key, key, $"Shown before installing {label}. Leave empty for no warning.", multiline: true)));
        return panel;
    }

    /// <summary>Only the components that have a warning, plus a picker to add one.</summary>
    private UIElement CompactWarningsControl(GameField f)
    {
        var rows = new StackPanel { Spacing = 8 };
        var known = Catalog.WarningComponents.ToList();
        var present = new List<string>();
        if (Doc.Get(f.Key, _game) is { IsObject: true } existing)
            foreach (var m in existing.Members!)
            {
                present.Add(m.Key);
                if (known.All(k => k.Key != m.Key)) known.Add((m.Key, m.Key));
            }
        void AddRow(string key, string label)
        {
            var box = MemberBox(f.Key, key, $"Shown before installing {label}. Leave empty for no warning.", multiline: true);
            var g = new Grid { ColumnSpacing = 10 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = Ui.T(label, 13, fg: Ui.Secondary);
            l.Margin = new Thickness(0, 6, 0, 0);
            g.Children.Add(l);
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            rows.Children.Add(g);
        }
        foreach (var (key, label) in known.Where(k => present.Contains(k.Key)))
            AddRow(key, label);

        var pick = new ComboBox { PlaceholderText = "Add a warning for...", MinWidth = 220 };
        foreach (var (key, label) in known.Where(k => !present.Contains(k.Key)))
            pick.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedItem is not ComboBoxItem { Tag: string key, Content: string label } item) return;
            AddRow(key, label);
            pick.Items.Remove(item);
            pick.SelectedIndex = -1;
            if (pick.Items.Count == 0) pick.Visibility = Visibility.Collapsed;
        };
        if (pick.Items.Count == 0) pick.Visibility = Visibility.Collapsed;
        return Ui.V(8, rows, pick);
    }

    private UIElement SplitControl(GameField f)
    {
        var rows = new StackPanel { Spacing = 6 };
        var items = new List<(TextBox Name, TextBox Sub)>();
        void Commit()
        {
            var arr = new JsonArray();
            foreach (var (n, s) in items)
            {
                if (n.Text.Trim().Length == 0) continue;
                arr.Add(new JsonObject { ["name"] = JsonOut.Str(n.Text.Trim()), ["subPath"] = JsonOut.Str(s.Text.Trim()) });
            }
            _host.Defer($"{f.Key}|{_game}", () =>
            {
                if (arr.Count == 0) Doc.Remove(P(f.Key), _game);
                else Doc.Set(P(f.Key), _game, arr.DeepClone());
            });
        }
        void AddRow(string name, string sub)
        {
            var nb = Ui.Box(name, "Game name for the card");
            var sb = Ui.Box(sub, @"Subfolder, e.g. Game1\Binaries (may be empty)");
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(nb);
            Grid.SetColumn(sb, 1); g.Children.Add(sb);
            var item = (nb, sb);
            var rm = Ui.RemoveBtn(() => { items.Remove(item); rows.Children.Remove(g); Commit(); });
            Grid.SetColumn(rm, 2); g.Children.Add(rm);
            items.Add(item);
            rows.Children.Add(g);
            nb.TextChanged += (_, _) => Commit();
            sb.TextChanged += (_, _) => Commit();
        }
        if (Doc.Get(f.Key, _game) is { IsArray: true } arr)
            foreach (var it in arr.Items!.Where(i => i.IsObject))
                AddRow(it.Get("name")?.Str ?? "", it.Get("subPath")?.Str ?? "");
        var add = Ui.Btn("Add game", () => AddRow("", ""));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.V(8, rows, add);
    }

    private UIElement BitnessControl()
    {
        var combo = new ComboBox { MinWidth = 220 };
        combo.Items.Add("Detect automatically");
        combo.Items.Add("32-bit");
        combo.Items.Add("64-bit");
        bool is32 = InList("thirtyTwoBitGames"), is64 = InList("sixtyFourBitGames");
        combo.SelectedIndex = is32 ? 1 : is64 ? 2 : 0;
        var warn = Ui.T("This game is in both the 32-bit and 64-bit lists. Pick one to fix it.", 12, fg: Ui.Caution);
        warn.Visibility = is32 && is64 ? Visibility.Visible : Visibility.Collapsed;
        combo.SelectionChanged += (_, _) =>
        {
            int i = combo.SelectedIndex;
            warn.Visibility = Visibility.Collapsed;
            _host.Apply(() => Doc.Batch(() =>
            {
                Doc.RemoveFromArray(P("thirtyTwoBitGames"), _game);
                Doc.RemoveFromArray(P("sixtyFourBitGames"), _game);
                if (i == 1) Doc.AddToArray(P("thirtyTwoBitGames"), _game);
                if (i == 2) Doc.AddToArray(P("sixtyFourBitGames"), _game);
            }));
        };
        return Ui.V(6, combo, warn);
    }

    private static readonly (string Label, string? List, string Help)[] UeModes =
    {
        ("Automatic", null, "RHI decides: UE-Extended stays off unless the user turns it on (no named RenoDX mod required)."),
        ("UE-Extended (SDR to HDR)", "ueExtendedGames", "Uses the UE-Extended addon to upgrade SDR to HDR (Set_Path=1). A named RenoDX mod for the game still wins."),
        ("UE-Extended, native HDR", "nativeHdrGames", "For games with their own HDR: forces UE-Extended (Set_Path=0) even if the game has a named RenoDX mod, and hides the toggle."),
        ("UE-Extended with Engine.ini choices", "ueExtendedCompatibility", "Forces UE-Extended like native HDR, and lets you choose which Engine.ini keys RHI writes."),
        ("Never use UE-Extended", "noUeExtendedGames", "Blocks UE-Extended completely, even if the user turns it on."),
    };

    private UIElement UeModeControl()
    {
        // Native HDR + Engine.ini choices together is one mode (the app adds Engine.ini-choice games to native HDR anyway)
        var present = new List<int>();
        if (Doc.Get("ueExtendedCompatibility", _game) != null) present.Add(3);
        else if (InList("nativeHdrGames")) present.Add(2);
        if (InList("ueExtendedGames")) present.Add(1);
        if (InList("noUeExtendedGames")) present.Add(4);

        var combo = new ComboBox { MinWidth = 300 };
        foreach (var m in UeModes) combo.Items.Add(m.Label);
        combo.SelectedIndex = present.Count > 0 ? present[0] : 0;
        var help = Ui.Help(UeModes[combo.SelectedIndex].Help);
        var warn = Ui.T($"This game is in several UE-Extended lists ({string.Join(", ", present.Select(i => UeModes[i].Label))}). Pick one to fix it.", 12, fg: Ui.Caution);
        warn.Visibility = present.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Engine.ini choices (ueExtendedCompatibility)
        var (compatPanel, hdr, lut) = EngineIniChoices();
        compatPanel.Margin = new Thickness(0, 4, 0, 0);
        compatPanel.Visibility = combo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

        combo.SelectionChanged += (_, _) =>
        {
            int i = combo.SelectedIndex;
            help.Text = UeModes[i].Help;
            warn.Visibility = Visibility.Collapsed;
            compatPanel.Visibility = i == 3 ? Visibility.Visible : Visibility.Collapsed;
            _host.Apply(() => Doc.Batch(() =>
            {
                Doc.RemoveFromArray(P("ueExtendedGames"), _game);
                Doc.RemoveFromArray(P("noUeExtendedGames"), _game);
                if (i != 2 && i != 3) Doc.RemoveFromArray(P("nativeHdrGames"), _game);
                if (i != 3) Doc.Remove(P("ueExtendedCompatibility"), _game);
                if (i == 3)
                {
                    // Also listed as native HDR, like the existing entries
                    Doc.AddToArray(P("nativeHdrGames"), _game);
                    if (Doc.Get("ueExtendedCompatibility", _game) == null)
                        Doc.Set(P("ueExtendedCompatibility"), _game, new JsonObject());
                }
                else if (UeModes[i].List is string list) Doc.AddToArray(P(list), _game);
            }));
            if (i == 3) { hdr.SelectedIndex = 0; lut.SelectedIndex = 0; }
        };
        return Ui.V(6, combo, help, warn, compatPanel);
    }

    /// <summary>The HDR and LUT choices of this game's ueExtendedCompatibility entry.</summary>
    public (StackPanel Panel, ComboBox Hdr, ComboBox Lut) EngineIniChoices()
    {
        var compat = Doc.Get("ueExtendedCompatibility", _game);
        ComboBox TriCombo(string member, string defaultLabel)
        {
            var c = new ComboBox { MinWidth = 260 };
            c.Items.Add(defaultLabel);
            c.Items.Add("Write");
            c.Items.Add("Don't write");
            var cur = compat?.Get(member)?.AsBool;
            c.SelectedIndex = cur == true ? 1 : cur == false ? 2 : 0;
            c.SelectionChanged += (_, _) =>
            {
                int i = c.SelectedIndex;
                _host.Apply(() =>
                {
                    var path = P("ueExtendedCompatibility", _game);
                    if (i == 0) Doc.Remove(path, member); // keep the (possibly empty) entry: presence forces UE-Extended
                    else Doc.Set(path, member, JsonOut.Bool(i == 1));
                });
            };
            return c;
        }
        var hdr = TriCombo("hdr", "Default (UE5 yes, UE4 no)");
        var lut = TriCombo("lut", "Default (yes)");
        var label1 = Ui.T("Engine.ini HDR keys", 13, fg: Ui.Secondary);
        var label2 = Ui.T("Engine.ini LUT key", 13, fg: Ui.Secondary);
        label1.Width = label2.Width = 140;
        label1.VerticalAlignment = label2.VerticalAlignment = VerticalAlignment.Center;
        var panel = Ui.V(8,
            Ui.H(10, label1, hdr),
            Ui.H(10, label2, lut),
            Ui.Help("Choose \"Don't write\" for HDR when the game has its own HDR setting."));
        return (panel, hdr, lut);
    }

    private UIElement ApiControl(GameField f)
    {
        var current = Doc.GetString(f.Key, _game) ?? "";
        var tokens = current.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToUpperInvariant() switch { "VULKAN" => "VLK", "OPENGL" => "OGL", var x => x })
            .ToList();
        var unknown = tokens.Where(t => Catalog.ApiTokens.All(a => a.Token != t)).ToList();
        var boxes = new List<(string Token, CheckBox Box)>();
        var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 130 };
        foreach (var (token, label) in Catalog.ApiTokens)
        {
            var cb = new CheckBox { Content = label, IsChecked = tokens.Contains(token), MinWidth = 120 };
            boxes.Add((token, cb));
            wrap.Children.Add(cb);
            cb.Click += (_, _) =>
            {
                var picked = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Token).Concat(unknown).ToList();
                _host.Apply(() =>
                {
                    if (picked.Count == 0) Doc.Remove(P(f.Key), _game);
                    else Doc.Set(P(f.Key), _game, JsonOut.Str(string.Join(", ", picked)));
                });
            };
        }
        var panel = Ui.V(4, wrap, Ui.Help("None ticked = detect automatically."));
        if (unknown.Count > 0) panel.Children.Add(Ui.Help($"Also kept from the file: {string.Join(", ", unknown)}"));
        return panel;
    }

    // ── Whole-game actions ───────────────────────────────────────────────────

    private async Task RenameAsync()
    {
        var name = await _host.Prompt("Rename game", "New name. It must match exactly what RHI detects.", _game, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == _game) return;
        await RenameToAsync(name.Trim());
    }

    private async Task RenameToAsync(string newName)
    {
        var existing = Catalog.GameEntries(Doc).Where(e => e.Game == newName).Select(e => Catalog.LabelFor(e.Field)).Distinct().ToList();
        if (existing.Count > 0 && !await _host.Confirm("Name already has settings",
                $"\"{newName}\" already has: {string.Join(", ", existing)}.\n\nWhere both have the same setting, this game's value replaces it. Continue?", "Rename"))
            return;
        _host.Apply(() => Doc.Batch(() =>
        {
            foreach (var (field, _) in Catalog.GameEntries(Doc).Where(e => e.Game == _game).ToList())
            {
                if (Catalog.ListKeys.Contains(field)) Doc.RenameInArray(P(field), _game, newName);
                else Doc.RenameKey(P(field), _game, newName);
            }
        }));
        _host.OpenGame(newName);
    }

    private async Task RemoveAllAsync()
    {
        var labels = SetLabels();
        if (labels.Count == 0) return;
        if (!await _host.Confirm("Remove from manifest",
                $"Delete every setting for \"{_game}\"?\n\n{string.Join(", ", labels)}\n\nYou can undo this until you close the editor.", "Remove", danger: true))
            return;
        _host.Apply(() => Doc.Batch(() =>
        {
            foreach (var (field, _) in Catalog.GameEntries(Doc).Where(e => e.Game == _game).ToList())
            {
                if (Catalog.ListKeys.Contains(field)) Doc.RemoveFromArray(P(field), _game);
                else Doc.Remove(P(field), _game);
            }
        }));
        _host.OpenGame(_game);
    }
}
