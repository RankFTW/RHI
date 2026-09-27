using System.Windows;

namespace PCGWEditor;

public partial class FetchSummaryWindow : Window
{
    public FetchSummaryWindow(List<string> added, List<string> updated)
    {
        InitializeComponent();

        TitleLabel.Text = $"Fetch complete — {added.Count} new, {updated.Count} updated";

        if (added.Count > 0)
        {
            NewSection.Visibility = Visibility.Visible;
            NewCountLabel.Text    = $"New games ({added.Count})";
            NewList.ItemsSource   = added.OrderBy(s => s).ToList();
        }

        if (updated.Count > 0)
        {
            UpdatedSection.Visibility = Visibility.Visible;
            UpdatedCountLabel.Text    = $"Updated games ({updated.Count})";
            UpdatedList.ItemsSource   = updated.OrderBy(s => s).ToList();
        }

        if (added.Count > 0 && updated.Count > 0)
            SectionSep.Visibility = Visibility.Visible;

        if (added.Count == 0 && updated.Count == 0)
            NoChangesLabel.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
