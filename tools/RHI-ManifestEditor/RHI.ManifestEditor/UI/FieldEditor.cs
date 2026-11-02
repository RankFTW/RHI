using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RHI.ManifestEditor.Json;
using RHI.ManifestEditor.Services;

namespace RHI.ManifestEditor.UI;

/// <summary>One manifest field and every game in it: add, edit and remove games in place.</summary>
public sealed class FieldEditor
{
    private readonly IEditorHost _host;
    private readonly FieldView _view;
    private JsonDoc Doc => _host.Doc;

    private readonly Panel _rows;
    private readonly List<(string Game, FrameworkElement Row)> _entries = new();
    private readonly TextBlock _countText = Ui.T("", 13, fg: Ui.Secondary, wrap: false);
    private readonly TextBox _filter = new() { PlaceholderText = "Filter games in this list", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _empty = Ui.T("No games in this list yet. Add one above.", 14, fg: Ui.Secondary);
    private readonly HashSet<string> _library;

    public FieldEditor(IEditorHost host, FieldView view)
    {
        _host = host;
        _view = view;
        _library = new HashSet<string>(host.LibraryGames.Select(g => g.Name), StringComparer.Ordinal);
        _rows = view.IsList
            ? new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 340, ItemHeight = 44 }
            : new StackPanel();
    }

    private string[] P() => new[] { _view.Key };

    public UIElement Build()
    {
        var page = new StackPanel { Spacing = 14, Padding = new Thickness(28, 20, 28, 40), MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch };

        // Header
        var key = Ui.T(_view.Key, 12, fg: Ui.Tertiary, wrap: false);
        key.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        key.IsTextSelectionEnabled = true;
        page.Children.Add(Ui.V(4,
            Ui.T(_view.Title, 26, bold: true),
            Ui.H(10, _countText, key)));
        if (!string.IsNullOrEmpty(_view.Help)) page.Children.Add(Ui.T(_view.Help, 14, fg: Ui.Secondary));

        // Add
        page.Children.Add(Ui.Card(AddPanel(), 14));

        // Games
        _filter.TextChanged += (_, _) => ApplyFilter();
        page.Children.Add(_filter);
        page.Children.Add(_empty);
        page.Children.Add(_rows);

        foreach (var game in Catalog.GamesIn(Doc, _view.Key).OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
            AddRow(game, atTop: false);
        UpdateCount();
        return page;
    }

    // ── Add ──────────────────────────────────────────────────────────────────

    private UIElement AddPanel()
    {
        var box = new AutoSuggestBox
        {
            PlaceholderText = "Game name (games from your RHI library and the manifest are suggested)",
            QueryIcon = null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            s.ItemsSource = Suggestions(s.Text);
        };
        box.SuggestionChosen += (s, e) => s.Text = e.SelectedItem as string ?? s.Text;
        box.QuerySubmitted += (s, e) =>
        {
            var name = (e.ChosenSuggestion as string ?? e.QueryText).Trim();
            if (Add(name)) s.Text = "";
        };
        var addBtn = Ui.Btn("Add", () => { if (Add(box.Text.Trim())) box.Text = ""; }, accent: true);

        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(box);
        Grid.SetColumn(addBtn, 1);
        g.Children.Add(addBtn);

        var hint = _view.IsList || _view.IsCompat
            ? "The name must match what RHI detects exactly. Pick it from the suggestions to be sure."
            : "The name must match what RHI detects exactly. The game is saved to the list once you give it a value.";
        return Ui.V(8, Ui.T("Add a game", 14, bold: true), g, Ui.Help(hint));
    }

    private List<string> Suggestions(string text)
    {
        var q = text.Trim();
        if (q.Length == 0) return new();
        var inField = new HashSet<string>(_entries.Select(e => e.Game), StringComparer.Ordinal);
        var loose = Library.Loose(q);
        var names = _host.LibraryGames.Select(g => g.Name)
            .Concat(Catalog.GameEntries(Doc).Select(e => e.Game))
            .Distinct(StringComparer.Ordinal)
            .Where(n => !inField.Contains(n))
            .Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase) || (loose.Length > 0 && Library.Loose(n).Contains(loose)))
            .OrderByDescending(n => n.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();
        return names;
    }

    private bool Add(string name)
    {
        if (name.Length == 0) return false;
        if (_entries.Any(e => e.Game == name))
        {
            _filter.Text = name;
            _host.SetStatus($"{name} is already in this list.");
            return true;
        }
        if (_view.IsList)
            _host.Apply(() => Doc.AddToArray(P(), name));
        else if (_view.IsCompat)
            _host.Apply(() => Doc.Batch(() =>
            {
                Doc.Set(P(), name, new JsonObject());
                Doc.AddToArray(new[] { "nativeHdrGames" }, name);
            }));
        // Other fields: the row's control writes the entry once it has a value

        _filter.Text = "";
        var row = AddRow(name, atTop: true);
        UpdateCount();
        row.StartBringIntoView();
        if (!_view.IsList && !_view.IsCompat)
        {
            if (FindFirst<TextBox>(row) is { } tb) tb.Loaded += (_, _) => tb.Focus(FocusState.Programmatic);
            _host.SetStatus($"Added {name}. Enter a value to save it to the list.");
        }
        else _host.SetStatus($"Added {name}.");
        return true;
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    private FrameworkElement AddRow(string game, bool atTop)
    {
        var row = _view.IsList ? ListChip(game) : DictRow(game);
        if (atTop) { _rows.Children.Insert(0, row); _entries.Insert(0, (game, row)); }
        else { _rows.Children.Add(row); _entries.Add((game, row)); }
        return row;
    }

    private FrameworkElement NameLink(string game, bool trim)
    {
        bool known = _library.Count == 0 || _library.Contains(game);
        var text = Ui.T(game, 14, wrap: !trim);
        if (!known) text.Foreground = Ui.Caution;
        var link = new HyperlinkButton { Content = text, Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTipService.SetToolTip(link, known
            ? $"{game}\nOpen every setting for this game"
            : $"{game}\nNot in your RHI library: check the name matches what RHI detects.\nOpen every setting for this game");
        link.Click += (_, _) => _host.OpenGame(game);
        return link;
    }

    private FrameworkElement ListChip(string game)
    {
        var g = new Grid { ColumnSpacing = 2, VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = NameLink(game, trim: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(name);
        Border? chip = null;
        var rm = Ui.RemoveBtn(() => Remove(game, chip!), $"Remove {game} from this list");
        Grid.SetColumn(rm, 1);
        g.Children.Add(rm);
        chip = new Border
        {
            Child = g,
            Background = Ui.CardBg,
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 0, 2, 0),
            Margin = new Thickness(0, 0, 8, 6),
        };
        return chip;
    }

    private FrameworkElement DictRow(string game)
    {
        var editor = new GameEditor(_host, game);
        UIElement control = _view.IsCompat ? editor.EngineIniChoices().Panel : editor.ControlFor(_view.Field, compact: true);

        var g = new Grid { ColumnSpacing = 20 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = NameLink(game, trim: false);
        name.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(name);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn((FrameworkElement)control, 1);
        g.Children.Add(control);
        Border? row = null;
        var tip = _view.IsCompat ? $"Remove {game}'s Engine.ini choices (it stays UE-Extended, native HDR)" : $"Remove {game} from this list";
        var rm = Ui.RemoveBtn(() => Remove(game, row!), tip);
        rm.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(rm, 2);
        g.Children.Add(rm);
        row = new Border
        {
            Child = g,
            Padding = new Thickness(0, 12, 0, 12),
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        return row;
    }

    private void Remove(string game, FrameworkElement row)
    {
        _host.Flush();
        if (_view.IsList) _host.Apply(() => Doc.RemoveFromArray(P(), game));
        else _host.Apply(() => Doc.Remove(P(), game));
        _rows.Children.Remove(row);
        _entries.RemoveAll(e => e.Row == row);
        UpdateCount();
        _host.SetStatus($"Removed {game} from {_view.Title}. Undo brings it back.");
    }

    private void ApplyFilter()
    {
        // Rebuild the children rather than collapsing rows: the wrap grid would leave gaps
        var q = _filter.Text.Trim();
        var loose = Library.Loose(q);
        _rows.Children.Clear();
        foreach (var (game, row) in _entries)
            if (q.Length == 0 || game.Contains(q, StringComparison.OrdinalIgnoreCase) || (loose.Length > 0 && Library.Loose(game).Contains(loose)))
                _rows.Children.Add(row);
    }

    private void UpdateCount()
    {
        int n = _entries.Count;
        _countText.Text = n == 1 ? "1 game" : $"{n} games";
        _empty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        _filter.Visibility = n > 8 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T? FindFirst<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T t) return t;
        // Not yet in the visual tree: walk logical children of the panels we build
        IEnumerable<DependencyObject> kids = root switch
        {
            Panel p => p.Children,
            Border b when b.Child != null => new[] { (DependencyObject)b.Child },
            _ => Array.Empty<DependencyObject>(),
        };
        foreach (var k in kids)
            if (FindFirst<T>(k) is { } found) return found;
        return null;
    }
}
