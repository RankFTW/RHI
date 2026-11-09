using System.Windows;

namespace PCGWEditor;

/// <summary>Auto fetch settings: on/off, interval, push when changed, keep in the tray.</summary>
public partial class AutoFetchWindow : Window
{
    private readonly AutoFetchSettings _settings;

    /// <summary>True when the user chose "Save and run now".</summary>
    public bool RunNow { get; private set; }

    public AutoFetchWindow(AutoFetchSettings settings, DateTime? nextRunUtc, string? lastResult)
    {
        InitializeComponent();
        _settings = settings;
        EnabledBox.IsChecked = settings.Enabled;
        PushBox.IsChecked = settings.PushWhenChanged;
        TrayBox.IsChecked = settings.KeepInTray;

        foreach (var (minutes, label) in AutoFetchSettings.Intervals)
            IntervalBox.Items.Add(label);
        int index = Array.FindIndex(AutoFetchSettings.Intervals, i => i.Minutes == settings.IntervalMinutes);
        IntervalBox.SelectedIndex = index >= 0 ? index : 4; // every 3 hours

        LastRunText.Text = settings.LastRunUtc is { } last
            ? $"Last run: {last.ToLocalTime():ddd d MMM, HH:mm}" + (lastResult != null ? $" ({lastResult})" : "")
            : "Last run: never";
        NextRunText.Text = settings.Enabled && nextRunUtc is { } next
            ? $"Next run: {next.ToLocalTime():ddd d MMM, HH:mm}"
            : "Next run: automatic fetching is off";
    }

    private void Apply()
    {
        _settings.Enabled = EnabledBox.IsChecked == true;
        _settings.PushWhenChanged = PushBox.IsChecked == true;
        _settings.KeepInTray = TrayBox.IsChecked == true;
        if (IntervalBox.SelectedIndex >= 0)
            _settings.IntervalMinutes = AutoFetchSettings.Intervals[IntervalBox.SelectedIndex].Minutes;
        _settings.Save();
    }

    private void Save_Click(object sender, RoutedEventArgs e) { Apply(); DialogResult = true; Close(); }
    private void RunNow_Click(object sender, RoutedEventArgs e) { Apply(); RunNow = true; DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
