using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RenoDXdbEditor;

/// <summary>
/// One-by-one review dialog for new Unity games found on the wiki.
/// Shows Name, Status, raw Notes, and parsed Upgrades/Comments.
/// </summary>
public partial class UnityReviewWindow : Window
{
    private readonly List<WikiUnityEntry> _pending;
    public List<UnrealEntry> AcceptedEntries { get; } = new();

    private int _index;
    private int _acceptedCount;

    public UnityReviewWindow(List<WikiUnityEntry> entries)
    {
        InitializeComponent();
        _pending = entries;
        TotalCountLabel.Text = entries.Count.ToString();
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (_index >= _pending.Count)
        {
            DialogResult = AcceptedEntries.Count > 0;
            Close();
            return;
        }

        var e = _pending[_index];
        ProgressLabel.Text      = $"Reviewing entry {_index + 1} of {_pending.Count}";
        PositionLabel.Text      = $"Entry {_index + 1} / {_pending.Count}";
        AcceptedCountLabel.Text = _acceptedCount.ToString();

        // Left panel
        WikiName.Text   = e.Name;
        WikiStatus.Text = e.Status;
        WikiNotes.Text  = string.IsNullOrWhiteSpace(e.Notes) ? "(none)" : e.Notes;

        var (upgrades, comments) = WikiUnityScrapeService.ParseNotes(e.Notes);
        WikiUpgradesParsed.Text  = upgrades  ?? "(none)";
        WikiCommentsParsed.Text  = comments  ?? "(none)";

        // Right panel defaults
        DbNameBox.Text = e.Name;
        DbStatusCombo.SelectedIndex = e.Status == "Done" ? 0 : 1;
        BuildUpgradeRows(new UnrealEntry { Upgrades = upgrades }.ParseUpgrades());
        DbCommentsBox.Text  = comments  ?? "";

        DbNameBox.Focus();
        DbNameBox.SelectAll();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        var name = DbNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name cannot be empty.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var statusItem = DbStatusCombo.SelectedItem as ComboBoxItem;

        AcceptedEntries.Add(new UnrealEntry
        {
            Name     = name,
            Status   = statusItem?.Content as string ?? "Done",
            Upgrades = CollectUpgrades(),
            Comments = NullIfEmpty(DbCommentsBox.Text),
        });

        _acceptedCount++;
        _index++;
        ShowCurrent();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) { _index++; ShowCurrent(); }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = AcceptedEntries.Count > 0;
        Close();
    }

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Upgrade rows (same Unity keys/values as the main editor) ──────────────

    private static readonly SolidColorBrush FieldBg     = new(Color.FromRgb(0x25, 0x25, 0x40));
    private static readonly SolidColorBrush FieldFg     = new(Color.FromRgb(0xE0, 0xE0, 0xF0));
    private static readonly SolidColorBrush FieldBorder = new(Color.FromRgb(0x44, 0x44, 0x66));

    private void BuildUpgradeRows(List<(string Key, string Value)> pairs)
    {
        UpgradesPanel.Children.Clear();
        if (pairs.Count == 0)
            AddUpgradeRow("", "");   // always show one blank row
        else
            foreach (var (key, value) in pairs)
                AddUpgradeRow(key, value);
    }

    private void AddUpgradeRow_Click(object sender, RoutedEventArgs e) => AddUpgradeRow("", "");

    private void AddUpgradeRow(string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

        var keyCombo = NewCombo();
        keyCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
        foreach (var k in UnrealEntry.UnityFormatValues)
            keyCombo.Items.Add(new ComboBoxItem { Content = k, Tag = k });
        SelectByTag(keyCombo, key);
        Grid.SetColumn(keyCombo, 0);

        var valueCombo = NewCombo();
        valueCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
        foreach (var (label, v) in UnrealEntry.UnitySizeValuePairs)
            valueCombo.Items.Add(new ComboBoxItem { Content = label, Tag = v });
        SelectByTag(valueCombo, value);
        Grid.SetColumn(valueCombo, 2);

        var removeBtn = new Button
        {
            Content = "✕", Padding = new Thickness(0), Width = 28, Height = 26,
            ToolTip = "Remove this row",
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x1A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0xAA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x33, 0x33)),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        removeBtn.Click += (_, _) => UpgradesPanel.Children.Remove(grid);
        Grid.SetColumn(removeBtn, 3);

        grid.Children.Add(keyCombo);
        grid.Children.Add(valueCombo);
        grid.Children.Add(removeBtn);
        UpgradesPanel.Children.Add(grid);
    }

    private static ComboBox NewCombo() => new()
    {
        Background = FieldBg, Foreground = FieldFg, BorderBrush = FieldBorder, FontSize = 12,
    };

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        // Unknown value from the wiki — keep it selectable rather than silently dropping it
        if (!string.IsNullOrEmpty(tag))
        {
            var custom = new ComboBoxItem { Content = tag, Tag = tag };
            combo.Items.Add(custom);
            combo.SelectedItem = custom;
            return;
        }
        combo.SelectedIndex = 0;
    }

    private string? CollectUpgrades()
    {
        var pairs = new List<(string Key, string Value)>();
        foreach (var row in UpgradesPanel.Children.OfType<Grid>())
        {
            var combos = row.Children.OfType<ComboBox>().ToList();
            if (combos.Count < 2) continue;
            var key   = (combos[0].SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var value = (combos[1].SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            pairs.Add((key, value));
        }
        return UnrealEntry.SerialiseUpgrades(pairs);
    }
}
