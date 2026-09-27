using System.Windows;
using System.Windows.Controls;

namespace RenoDXdbEditor;

/// <summary>
/// One-by-one review dialog for new UE-Extended games found on the wiki.
/// Shows only the fields relevant to UnrealEntry: Name, Status, Method, Comments.
/// </summary>
public partial class UeExtReviewWindow : Window
{
    private readonly List<WikiUeExtEntry> _pending;

    public List<UnrealEntry> AcceptedEntries { get; } = new();

    private int _index;
    private int _acceptedCount;

    public UeExtReviewWindow(List<WikiUeExtEntry> entries)
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
        ProgressLabel.Text   = $"Reviewing entry {_index + 1} of {_pending.Count}";
        PositionLabel.Text   = $"Entry {_index + 1} / {_pending.Count}";
        AcceptedCountLabel.Text = _acceptedCount.ToString();

        // Left panel (wiki read-only)
        WikiName.Text   = e.Name;
        WikiStatus.Text = e.Status;
        WikiNotes.Text  = string.IsNullOrWhiteSpace(e.Notes) ? "(none)" : e.Notes;

        // Right panel (editable defaults)
        DbNameBox.Text = e.Name;
        DbStatusCombo.SelectedIndex = e.Status == "Done" ? 0 : 1;
        DbMethodCombo.SelectedIndex = 0; // (none)
        DbCommentsBox.Text = e.Notes ?? "";

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
        var methodItem = DbMethodCombo.SelectedItem as ComboBoxItem;
        var method     = (methodItem?.Tag as string ?? "").Trim();

        AcceptedEntries.Add(new UnrealEntry
        {
            Name     = name,
            Status   = statusItem?.Content as string ?? "Done",
            Method   = string.IsNullOrEmpty(method) ? null : method,
            Comments = NullIfEmpty(DbCommentsBox.Text),
        });

        _acceptedCount++;
        _index++;
        ShowCurrent();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _index++;
        ShowCurrent();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = AcceptedEntries.Count > 0;
        Close();
    }

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
