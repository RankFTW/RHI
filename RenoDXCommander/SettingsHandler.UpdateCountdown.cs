// SettingsHandler.UpdateCountdown.cs -- "Next check in / Last checked" panel above the
// Check For Updates button (General › Updates). Built in C#; ticks once per second only while
// the Settings page is open.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private bool _countdownSetupDone;
    private DispatcherQueueTimer? _countdownTimer;
    private TextBlock? _nextCheckLabel;
    private TextBlock? _nextCheckValue;
    private TextBlock? _lastCheckLabel;
    private TextBlock? _lastCheckValue;

    /// <summary>One-time: inserts the countdown panel directly above the Check For Updates button.</summary>
    private void EnsureUpdateCountdownPanel()
    {
        if (_countdownSetupDone) return;
        _countdownSetupDone = true;
        try
        {
            // CheckForUpdatesBtn is a private generated field: find it by Name, searching down from
            // SettingsCardsPanel (FrameworkElement.Parent can be null here, so don't climb up).
            var (column, button) = FindButtonAndPanel(_window.SettingsCardsPanel, "CheckForUpdatesBtn");
            if (column == null || button == null)
            {
                CrashReporter.Log("[SettingsHandler.EnsureUpdateCountdownPanel] Check For Updates button not found — countdown not added");
                return;
            }

            _nextCheckLabel = MakeCountdownLabel();
            _nextCheckValue = new TextBlock
            {
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            };
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_nextCheckValue, FontNumeralAlignment.Tabular);
            _lastCheckLabel = MakeCountdownLabel();
            _lastCheckValue = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            };
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_lastCheckValue, FontNumeralAlignment.Tabular);

            var grid = new Grid { ColumnSpacing = 20 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Spacing = 2 };
            left.Children.Add(_nextCheckLabel);
            left.Children.Add(_nextCheckValue);
            var right = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
            right.Children.Add(_lastCheckLabel);
            right.Children.Add(_lastCheckValue);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            var panel = new Border
            {
                Background = UIFactory.Brush(ResourceKeys.SurfaceInputBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Child = grid,
            };
            ToolTipService.SetToolTip(panel,
                "On: every 4 hours RHI refreshes mod data and checks all components and RHI itself for updates. " +
                "Minimal: every 4 hours RHI refreshes mod data, DLSS versions and checks RHI itself; component checks only run when you check now, Refresh or Update All.");

            // Above the "RHI checks every 4 hours" note when it sits right before the button
            int index = column.Children.IndexOf(button);
            if (index > 0 && column.Children[index - 1] is TextBlock) index--;
            column.Children.Insert(index, panel);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureUpdateCountdownPanel] Failed — {ex.Message}");
        }
    }

    /// <summary>Searches the element tree under <paramref name="root"/> (Panel children, Border child,
    /// ContentControl content) for a Button with the given Name; returns it and the Panel holding it.</summary>
    private static (Panel? Panel, Button? Button) FindButtonAndPanel(DependencyObject? root, string name)
    {
        switch (root)
        {
            case Panel p:
                foreach (var c in p.Children)
                {
                    if (c is Button b && string.Equals(b.Name, name, StringComparison.Ordinal)) return (p, b);
                    var found = FindButtonAndPanel(c, name);
                    if (found.Button != null) return found;
                }
                break;
            case Border bd:
                return FindButtonAndPanel(bd.Child, name);
            case ContentControl cc when cc.Content is DependencyObject d:
                return FindButtonAndPanel(d, name);
        }
        return (null, null);
    }

    private static TextBlock MakeCountdownLabel() => new()
    {
        FontSize = 11,
        Foreground = UIFactory.Brush(ResourceKeys.SettingsHelpBrush),
    };

    /// <summary>Starts the once-per-second refresh (Settings page opened).</summary>
    private void StartUpdateCountdown()
    {
        if (_window.IsShuttingDown) return;
        EnsureUpdateCountdownPanel();
        if (_nextCheckValue == null) return;
        if (_countdownTimer == null)
        {
            _countdownTimer = _window.DispatcherQueue.CreateTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1);
            _countdownTimer.IsRepeating = true;
            _countdownTimer.Tick += (_, _) =>
            {
                if (_window.IsShuttingDown) { _countdownTimer?.Stop(); return; }
                if (!_window.AppWindow.IsVisible) return; // closed to tray
                UpdateCountdownText();
            };
        }
        UpdateCountdownText();
        if (!_countdownTimer.IsRunning) _countdownTimer.Start();
    }

    /// <summary>Stops the refresh (Settings page left or window closing).</summary>
    private void StopUpdateCountdown() => _countdownTimer?.Stop();

    private void UpdateCountdownText()
    {
        if (_nextCheckLabel == null || _nextCheckValue == null || _lastCheckLabel == null || _lastCheckValue == null) return;
        try
        {
            var now = DateTime.UtcNow;
            // Minimal mode: the 4-hour run refreshes data (manifests, mod lists, DLSS versions, RHI's own
            // version) but skips component update checks, and LastUpdateCheckUtc only moves on a full check.
            bool minimal = string.Equals(ViewModel.Settings.BackgroundUpdateChecks, "Minimal", StringComparison.OrdinalIgnoreCase);
            _nextCheckLabel.Text = minimal ? "Next data refresh in" : "Next check in";
            _nextCheckValue.Text = FormatNextUpdateCheck(ViewModel.NextUpdateCheckUtc, now);
            _lastCheckLabel.Text = minimal ? "Last full check" : "Last checked";
            _lastCheckValue.Text = FormatLastUpdateCheck(ViewModel.Settings.LastUpdateCheckUtc, now);
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.UpdateCountdownText] {ex.Message}"); }
    }

    /// <summary>"2h 41m 20s"; "After startup" before the periodic timer has started.</summary>
    internal static string FormatNextUpdateCheck(DateTime? nextUtc, DateTime nowUtc)
    {
        if (nextUtc == null) return "After startup";
        var remaining = nextUtc.Value - nowUtc;
        if (remaining <= TimeSpan.Zero) return "Any moment";
        // Round up so the display reaches 0s exactly when the check is due
        var secs = (long)Math.Ceiling(remaining.TotalSeconds);
        return $"{secs / 3600}h {secs % 3600 / 60:00}m {secs % 60:00}s";
    }

    /// <summary>"Just now" / "18m ago" / "1h 18m ago" / "2d 3h ago"; "Not yet" when empty or unparsable.</summary>
    internal static string FormatLastUpdateCheck(string? lastUtcText, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(lastUtcText)
            || !DateTime.TryParse(lastUtcText, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.RoundtripKind, out var last))
            return "Not yet";
        var lastUtc = last.Kind == DateTimeKind.Local ? last.ToUniversalTime() : last;
        var ago = nowUtc - lastUtc;
        if (ago < TimeSpan.FromMinutes(1)) return "Just now"; // includes clock skew (future timestamps)
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes}m ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours}h {ago.Minutes:00}m ago";
        return $"{(int)ago.TotalDays}d {ago.Hours}h ago";
    }
}
