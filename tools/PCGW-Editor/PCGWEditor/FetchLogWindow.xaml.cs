using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PCGWEditor;

/// <summary>Every fetch (automatic or manual) and push: when, what came in, whether the push worked. Updates live.</summary>
public partial class FetchLogWindow : Window
{
    public FetchLogWindow()
    {
        InitializeComponent();
        Reload();
        FetchLog.Added += OnAdded;
        Closed += (_, _) => FetchLog.Added -= OnAdded;
    }

    private void OnAdded(FetchLogEntry _) => Dispatcher.BeginInvoke(Reload);

    private void Reload()
    {
        var entries = FetchLog.LoadNewestFirst();
        EntryList.ItemsSource = entries;
        if (entries.Count == 0)
        {
            SummaryText.Text = "No fetches logged yet. Each automatic or manual Fetch Updates, and each push, is listed here.";
            return;
        }
        var lastPush = entries.FirstOrDefault(e => e.Pushed == true);
        int failed = entries.Count(e => e.Outcome == "Failed" || e.Pushed == false);
        SummaryText.Text = $"{entries.Count} entries, newest first. "
            + (lastPush != null ? $"Last successful push: {lastPush.When}. " : "No successful push yet. ")
            + (failed > 0 ? $"{failed} with an error." : "No errors.");
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(FetchLog.FilePath)) return;
        try { Process.Start(new ProcessStartInfo(FetchLog.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Couldn't open the log file:\n{ex.Message}", "Fetch log", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Delete every entry in the fetch log?", "Clear log",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        FetchLog.Clear();
        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
