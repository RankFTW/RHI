// MainWindow.StatusBar.cs — bottom-left status bar text.
// Priority: short-lived message > loading/refresh progress > running background activity
// > offline/error notice > idle "Last checked … · next check in …".

using Microsoft.UI.Dispatching;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _statusBarTimer;
    private string? _statusBarMessage;
    private DateTime _statusBarMessageUntilUtc;

    /// <summary>Starts the 15-second refresh that keeps the idle countdown current.</summary>
    private void StartStatusBarTimer()
    {
        if (_statusBarTimer != null) return;
        _statusBarTimer = DispatcherQueue.CreateTimer();
        _statusBarTimer.Interval = TimeSpan.FromSeconds(15);
        _statusBarTimer.IsRepeating = true;
        _statusBarTimer.Tick += (_, _) =>
        {
            if (IsShuttingDown) { _statusBarTimer?.Stop(); return; }
            if (!AppWindow.IsVisible) return; // closed to tray
            RefreshStatusBar();
        };
        _statusBarTimer.Start();
        RefreshStatusBar();
    }

    private void StopStatusBarTimer() => _statusBarTimer?.Stop();

    /// <summary>Shows a one-off message (e.g. "Shortcut created") for a while, then goes back to normal.</summary>
    internal void ShowStatusBarMessage(string message, TimeSpan duration)
    {
        _statusBarMessage = message;
        _statusBarMessageUntilUtc = DateTime.UtcNow + duration;
        RefreshStatusBar();
        _ = Task.Delay(duration + TimeSpan.FromMilliseconds(100)).ContinueWith(_ =>
            DispatcherQueue?.TryEnqueue(RefreshStatusBar), TaskScheduler.Default);
    }

    /// <summary>Recomputes the status bar text. UI thread only.</summary>
    internal void RefreshStatusBar()
    {
        try
        {
            var text = GetStatusBarText(DateTime.UtcNow);
            StatusBarText.Text = text;
            // The text trims on narrow windows; the tooltip shows it in full
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(StatusBarText, string.IsNullOrEmpty(text) ? null : text);
        }
        catch (Exception ex) { CrashReporter.Log($"[MainWindow.RefreshStatusBar] {ex.Message}"); }
    }

    private string GetStatusBarText(DateTime nowUtc)
    {
        if (_statusBarMessage != null)
        {
            if (nowUtc < _statusBarMessageUntilUtc) return _statusBarMessage;
            _statusBarMessage = null;
        }

        var vm = ViewModel;
        if (vm.IsLoading)
            return vm.StatusText + (string.IsNullOrEmpty(vm.SubStatusText) ? "" : $"  —  {vm.SubStatusText}");

        var activity = vm.CurrentActivityText;
        if (!string.IsNullOrEmpty(activity)) return activity;

        // Problems stay visible until the next successful scan
        if (vm.StatusText.Contains("offline mode", StringComparison.OrdinalIgnoreCase)
            || vm.StatusText.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            return vm.StatusText + (string.IsNullOrEmpty(vm.SubStatusText) ? "" : $"  —  {vm.SubStatusText}");

        return FormatIdleStatus(vm.Settings.BackgroundUpdateChecks, vm.Settings.LastUpdateCheckUtc,
            vm.NextUpdateCheckUtc, nowUtc);
    }

    /// <summary>"Last checked 18m ago · next check in 3h 41m" (Minimal mode: "Last full check … · next data refresh in …").</summary>
    internal static string FormatIdleStatus(string? backgroundChecks, string? lastUtcText, DateTime? nextUtc, DateTime nowUtc)
    {
        bool minimal = string.Equals(backgroundChecks, "Minimal", StringComparison.OrdinalIgnoreCase);
        var last = SettingsHandler.FormatLastUpdateCheck(lastUtcText, nowUtc);
        var text = (minimal ? "Last full check: " : "Last checked: ") + (last == "Just now" ? "just now" : last == "Not yet" ? "not yet" : last);
        if (nextUtc != null)
        {
            var remaining = nextUtc.Value - nowUtc;
            var next = remaining <= TimeSpan.FromMinutes(1)
                ? "under a minute"
                : $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m";
            text += minimal ? $"  ·  Next data refresh in {next}" : $"  ·  Next check in {next}";
        }
        return text;
    }
}
