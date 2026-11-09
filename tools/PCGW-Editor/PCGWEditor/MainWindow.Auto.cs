// MainWindow.Auto.cs -- automatic Fetch Updates on a timer (with push when something changed), the system tray
// icon, and the Auto fetch / Log toolbar buttons. Settings and log: AutoFetch.cs.
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace PCGWEditor;

public partial class MainWindow
{
    private AutoFetchSettings _autoSettings = AutoFetchSettings.Load();
    private readonly DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private bool _fetchRunning;
    private bool _exiting;
    private string? _lastAutoResult;
    private System.Windows.Forms.NotifyIcon? _tray;
    private FetchLogWindow? _logWindow;

    /// <summary>When the next automatic run is due. Never run yet: two minutes after start (the startup sync first).</summary>
    private DateTime NextAutoRunUtc =>
        _autoSettings.LastRunUtc is { } last
            ? last.AddMinutes(_autoSettings.IntervalMinutes)
            : _startedUtc.AddMinutes(2);

    /// <summary>Called from the constructor.</summary>
    private void InitAutoFetch()
    {
        _autoTimer.Tick += async (_, _) =>
        {
            if (_autoSettings.Enabled && !_fetchRunning && DateTime.UtcNow >= NextAutoRunUtc)
                await RunAutoFetchAsync("Automatic");
        };
        _autoTimer.Start();
        FetchLog.Added += e => Dispatcher.BeginInvoke(() =>
        {
            _lastAutoResult = e.Headline + (e.Pushed == true ? ", pushed" : e.Pushed == false ? ", push failed" : "");
            UpdateTray();
        });
        UpdateTray();
    }

    /// <summary>One automatic run: records the time first, so a failing run waits for the next interval.</summary>
    private async Task RunAutoFetchAsync(string trigger)
    {
        _autoSettings.LastRunUtc = DateTime.UtcNow;
        _autoSettings.Save();
        await RunFetchAsync(fullRefresh: false, autoTrigger: trigger);
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private void AutoFetch_Click(object sender, RoutedEventArgs e)
    {
        var win = new AutoFetchWindow(_autoSettings, _autoSettings.Enabled ? NextAutoRunUtc : null, _lastAutoResult) { Owner = this };
        if (win.ShowDialog() != true) return;
        _autoSettings = AutoFetchSettings.Load();
        UpdateTray();
        StatusBar.Text = _autoSettings.Enabled
            ? $"Auto fetch on. Next run {NextAutoRunUtc.ToLocalTime():HH:mm}."
            : "Auto fetch off.";
        if (win.RunNow) _ = RunAutoFetchAsync("Run now");
    }

    private void Log_Click(object sender, RoutedEventArgs e) => ShowLog();

    private void ShowLog()
    {
        if (_logWindow is { IsLoaded: true })
        {
            _logWindow.Activate();
            return;
        }
        _logWindow = new FetchLogWindow { Owner = IsVisible ? this : null };
        _logWindow.Closed += (_, _) => _logWindow = null;
        _logWindow.Show();
    }

    // ── System tray ───────────────────────────────────────────────────────────

    /// <summary>Shows or removes the tray icon to match the setting, and refreshes its tooltip.</summary>
    private void UpdateTray()
    {
        if (!_autoSettings.KeepInTray)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (!IsVisible && !_exiting) ShowFromTray(); // never leave the window hidden with no icon
            return;
        }
        if (_tray == null)
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
                Visible = true,
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open PCGW Editor", null, (_, _) => ShowFromTray());
            menu.Items.Add("Fetch updates now", null, (_, _) => _ = RunAutoFetchAsync("Run now"));
            menu.Items.Add("Fetch log", null, (_, _) => ShowLog());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitFromTray());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowFromTray();
        }
        var text = "PCGW Editor" + (_lastAutoResult != null ? $": {_lastAutoResult}" : _autoSettings.Enabled ? ": auto fetch on" : "");
        _tray.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon limit
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        ShowFromTray(); // so an unsaved-changes prompt has a window
        _exiting = true;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _autoSettings.KeepInTray) Hide();
    }

    /// <summary>Close with "keep in tray" on: hide instead (Exit is in the tray menu).</summary>
    private bool HideInsteadOfClosing(CancelEventArgs e)
    {
        if (_exiting || !_autoSettings.KeepInTray) return false;
        e.Cancel = true;
        Hide();
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoTimer.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        base.OnClosed(e);
        Application.Current.Shutdown(); // the log window may still be open
    }
}
