// SetupWindow.xaml.cs — First-launch setup window, shown before MainWindow when there are no settings yet.
// One page in the Tabs / Spotlight look (TabsTheme colours), built in C#:
//   1. How RHI handles ReShade (two choice tiles, "Manage it for me" selected)
//   2. GitHub token (optional, recommended): link to make one, box + Check, saved through GitHubTokenSetup
//   3. Restore from a backup (optional): picks a .rhibackup; then the ReShade choice comes from the backup
// A footer button finishes: it saves a typed-but-unchecked token first, then calls OnRestore(path) or
// OnComplete(manageReShade). App.OnLaunched does the rest.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using WinRT.Interop;

namespace RenoDXCommander;

/// <summary>
/// First-launch setup window: ReShade management choice, optional GitHub token, optional restore from a backup.
/// </summary>
public sealed partial class SetupWindow : Window
{
    private ForegroundActivationTarget? _foregroundTarget;

    /// <summary>
    /// Called when the user finishes without a backup.
    /// <c>true</c> = "Manage it for me", <c>false</c> = "I'll manage it myself".
    /// </summary>
    public Action<bool>? OnComplete { get; set; }

    /// <summary>
    /// Called with a backup file the user chose to restore from (instead of OnComplete). Applies RHI's data and
    /// starts the main window; throws when the backup can't be restored (shown in this window).
    /// </summary>
    public Action<string>? OnRestore { get; set; }

    // Colours: the Tabs / Spotlight look (TabsTheme)
    private const string SelectedBg = "#16213A";
    private static readonly TabsTheme.Look PrimaryLook = new("#22304A", TabsTheme.Accent, TabsTheme.TextPrimary, "#2A3A58", true);

    private bool _manageReShade = true;
    private Grid? _choiceGrid;
    private Border? _reShadeCard;
    private TextBlock? _reShadeFromBackup;

    private PasswordBox? _tokenBox;
    private Button? _tokenCheckBtn;
    private TextBlock? _tokenStatus;
    private bool _tokenSaved;

    private string? _restorePath;
    private TextBlock? _restoreStatus;
    private Button? _restorePickBtn;
    private HyperlinkButton? _restoreClearBtn;

    private Button? _finishBtn;
    private TextBlock? _finishNote;
    private bool _finishing;

    public SetupWindow()
    {
        InitializeComponent();

        Title = "RHI Setup";

        // Size and position — scale by display DPI so window is correct at any Windows scaling setting
        var hwndForDpi = WindowNative.GetWindowHandle(this);
        uint dpi = NativeInterop.GetDpiForWindow(hwndForDpi);
        double dpiScale = dpi / 96.0;
        int logicalW = 700, logicalH = 920;
        // Never taller than the screen (small laptops): the page scrolls, the footer stays
        try
        {
            var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            logicalH = Math.Min(logicalH, (int)(work.Height / dpiScale) - 40);
        }
        catch (Exception ex) { CrashReporter.Log($"[SetupWindow] Work area unavailable — {ex.Message}"); }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(logicalW * dpiScale),
            (int)(logicalH * dpiScale)));
        CenterOnPrimaryDisplay();

        // Dark title bar — same as MainWindow
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeInterop.EnableDarkTitleBar(hwnd);

        if (AppWindow.TitleBar is { } titleBar)
        {
            var res = Application.Current.Resources;
            titleBar.BackgroundColor               = (Windows.UI.Color)res["TitleBarBackground"];
            titleBar.ForegroundColor               = (Windows.UI.Color)res["TitleBarForeground"];
            titleBar.InactiveBackgroundColor       = (Windows.UI.Color)res["TitleBarInactiveBackground"];
            titleBar.InactiveForegroundColor       = (Windows.UI.Color)res["TitleBarInactiveForeground"];
            titleBar.ButtonBackgroundColor         = (Windows.UI.Color)res["TitleBarButtonBackground"];
            titleBar.ButtonForegroundColor         = (Windows.UI.Color)res["TitleBarButtonForeground"];
            titleBar.ButtonHoverBackgroundColor    = (Windows.UI.Color)res["TitleBarButtonHoverBackground"];
            titleBar.ButtonHoverForegroundColor    = (Windows.UI.Color)res["TitleBarButtonHoverForeground"];
            titleBar.ButtonPressedBackgroundColor  = (Windows.UI.Color)res["TitleBarButtonPressedBackground"];
            titleBar.ButtonPressedForegroundColor  = (Windows.UI.Color)res["TitleBarButtonPressedForeground"];
            titleBar.ButtonInactiveBackgroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveBackground"];
            titleBar.ButtonInactiveForegroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveForeground"];
        }

        // Prevent resizing
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
        {
            overlapped.IsResizable  = false;
            overlapped.IsMaximizable = false;
        }

        BuildContent();

        Activated += (_, _) =>
        {
            _foregroundTarget ??= new ForegroundActivationTarget(hwnd,
                action => DispatcherQueue.TryEnqueue(() => action()), () =>
                {
                    AppWindow.Show();
                    Activate();
                    ForegroundActivation.Request(hwnd);
                });
        };
        Closed += (_, _) => _foregroundTarget?.Dispose();
    }

    private void CenterOnPrimaryDisplay()
    {
        try
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            var workArea = displayArea.WorkArea;
            var winSize = AppWindow.Size;
            int x = workArea.X + (workArea.Width  - winSize.Width)  / 2;
            int y = workArea.Y + (workArea.Height - winSize.Height) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
        catch (Exception ex) { CrashReporter.Log($"[SetupWindow.CenterOnPrimaryDisplay] {ex.Message}"); }
    }

    // ── Building blocks ──────────────────────────────────────────────────────────

    private static TextBlock Text(string text, double size, string colour, bool semiBold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = TabsTheme.B(colour),
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size + 6,
        FontWeight = semiBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    private static Border Pill(string text, TabsTheme.Look look) => new()
    {
        Background = TabsTheme.B(look.Bg),
        BorderBrush = TabsTheme.B(look.Border),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 1, 8, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = TabsTheme.B(look.Fg) },
    };

    /// <summary>A card: title (+ optional pill), a one-line description, then the body.</summary>
    private static (Border Card, StackPanel Body) Card(string title, string? description, Border? pill = null)
    {
        var body = new StackPanel { Spacing = 12 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(Text(title, 16, TabsTheme.TextPrimary, semiBold: true));
        if (pill != null) head.Children.Add(pill);
        body.Children.Add(head);
        if (description != null) body.Children.Add(Text(description, 13, TabsTheme.TextSecondary));
        var card = new Border
        {
            Background = TabsTheme.B(TabsTheme.Card),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 22),
            Child = body,
        };
        return (card, body);
    }

    private static Button MakeButton(string text, TabsTheme.Look look, double height = 36)
    {
        var b = new Button { Content = text, Padding = new Thickness(16, 0, 16, 0) };
        TabsTheme.Apply(b, look, height, 13);
        return b;
    }

    private void BuildContent()
    {
        var page = new StackPanel { Spacing = 18, Padding = new Thickness(40, 34, 40, 28) };

        // ── Header ──
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(Text("Welcome to RHI", 28, TabsTheme.TextPrimary, semiBold: true));
        header.Children.Add(Text(
            "RHI installs and keeps up to date ReShade, RenoDX, frame limiters and more for every game on your PC. "
          + "Three quick choices and you're in.",
            14, TabsTheme.TextSecondary));
        page.Children.Add(header);

        page.Children.Add(BuildReShadeCard());
        page.Children.Add(BuildTokenCard());
        page.Children.Add(BuildRestoreCard());

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = page,
        };

        // ── Footer: always visible ──
        var footerGrid = new Grid { ColumnSpacing = 16 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _finishNote = Text("You can change all of this later in Settings.", 12, TabsTheme.TextMuted);
        _finishNote.VerticalAlignment = VerticalAlignment.Center;
        footerGrid.Children.Add(_finishNote);
        _finishBtn = MakeButton("Get started", PrimaryLook, 40);
        _finishBtn.MinWidth = 170;
        _finishBtn.Click += async (_, _) =>
        {
            try { await FinishAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[SetupWindow.Finish] {ex.GetType().Name}: {ex.Message}"); }
        };
        Grid.SetColumn(_finishBtn, 1);
        footerGrid.Children.Add(_finishBtn);
        var footer = new Border
        {
            Background = TabsTheme.B(TabsTheme.PanelBg),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(40, 14, 40, 14),
            Child = footerGrid,
        };

        var root = new Grid { Background = TabsTheme.B(TabsTheme.Page) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(scroll);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        RootContent.Content = root;
    }

    // ── 1. ReShade ───────────────────────────────────────────────────────────────

    private Border BuildReShadeCard()
    {
        var (card, body) = Card("How should RHI handle ReShade?", null);
        _choiceGrid = new Grid { ColumnSpacing = 12 };
        _choiceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _choiceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(_choiceGrid);
        RefreshChoices();
        _reShadeFromBackup = Text("Your backup brings back the choice you made before.", 12, TabsTheme.TextMuted);
        _reShadeFromBackup.Visibility = Visibility.Collapsed;
        body.Children.Add(_reShadeFromBackup);
        _reShadeCard = card;
        return card;
    }

    /// <summary>The two choice tiles. Rebuilt on a change (a button's hover colours are fixed when it loads).</summary>
    private void RefreshChoices()
    {
        if (_choiceGrid == null) return;
        _choiceGrid.Children.Clear();
        var manage = ChoiceTile("Manage it for me", recommended: true,
            "Installs and updates ReShade in each game with the right DLL name, and keeps your shader packs in every game. "
          + "Shaders already in a game folder are backed up first.",
            _manageReShade, () => { _manageReShade = true; RefreshChoices(); });
        var self = ChoiceTile("I'll manage it myself", recommended: false,
            "For a hand-tuned ReShade setup. RHI won't install, update or change ReShade or shaders in your games.",
            !_manageReShade, () => { _manageReShade = false; RefreshChoices(); });
        Grid.SetColumn(self, 1);
        _choiceGrid.Children.Add(manage);
        _choiceGrid.Children.Add(self);
    }

    private static Button ChoiceTile(string title, bool recommended, string description, bool selected, Action pick)
    {
        var content = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Text(title, 14, selected ? TabsTheme.TextPrimary : TabsTheme.TextControl, semiBold: true));
        // Selection mark: a filled or empty ring, like a radio button
        var ring = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(selected ? 5 : 1.5),
            BorderBrush = TabsTheme.B(selected ? TabsTheme.Accent : "#3A4558"),
            Background = TabsTheme.B(selected ? "#0B0F17" : "#0E131D"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(ring, 1);
        head.Children.Add(ring);
        content.Children.Add(head);
        if (recommended) content.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = Pill("Recommended", TabsTheme.GreenPill) });
        content.Children.Add(Text(description, 12, TabsTheme.TextSecondary));

        var look = selected
            ? new TabsTheme.Look(SelectedBg, TabsTheme.Accent, TabsTheme.TextPrimary, SelectedBg, false)
            : new TabsTheme.Look("#0E131D", TabsTheme.CardBorder, TabsTheme.TextControl, "#151C29", false);
        var tile = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(16, 14, 16, 16),
        };
        TabsTheme.Apply(tile, look, double.NaN, 13);
        tile.CornerRadius = new CornerRadius(10);
        tile.BorderThickness = new Thickness(selected ? 1.5 : 1);
        tile.Click += (_, _) => pick();
        return tile;
    }

    // ── 2. GitHub token ──────────────────────────────────────────────────────────

    private Border BuildTokenCard()
    {
        var (card, body) = Card("Add a GitHub token",
            "RHI downloads from GitHub. Without a token it can only ask GitHub 60 times an hour, and with a big library "
          + "installs and update checks can fail until the hour is up. A free token raises that to 5,000.",
            Pill("Recommended", TabsTheme.GreenPill));

        var steps = new StackPanel { Spacing = 4 };
        steps.Children.Add(Text("1.  Open GitHub and choose Generate token. Any name and expiry; leave every box unticked.", 13, TabsTheme.TextControl));
        steps.Children.Add(Text("2.  Copy the token and paste it below.", 13, TabsTheme.TextControl));
        body.Children.Add(steps);

        var link = new HyperlinkButton
        {
            Content = "Make a token on GitHub",
            NavigateUri = new Uri(GitHubTokenSetup.NewTokenUrl),
            Foreground = TabsTheme.B(TabsTheme.Link),
            Padding = new Thickness(0),
            FontSize = 13,
        };
        body.Children.Add(link);

        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tokenBox = new PasswordBox
        {
            PlaceholderText = "ghp_...",
            Height = 36,
            PasswordRevealMode = PasswordRevealMode.Peek,
            Background = TabsTheme.B("#0B0F17"),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            CornerRadius = new CornerRadius(8),
        };
        _tokenBox.PasswordChanged += (_, _) =>
        {
            _tokenSaved = false;
            ShowStatus(_tokenStatus, "", error: false);
        };
        row.Children.Add(_tokenBox);
        _tokenCheckBtn = MakeButton("Check and save", TabsTheme.BlueButton);
        _tokenCheckBtn.Click += async (_, _) =>
        {
            try { await SaveTokenAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[SetupWindow.TokenCheck] {ex.GetType().Name}: {ex.Message}"); }
        };
        Grid.SetColumn(_tokenCheckBtn, 1);
        row.Children.Add(_tokenCheckBtn);
        body.Children.Add(row);

        _tokenStatus = Text("", 12, TabsTheme.TextSecondary);
        _tokenStatus.Visibility = Visibility.Collapsed;
        body.Children.Add(_tokenStatus);
        body.Children.Add(Text("Optional. Kept on this PC only (not in backups). Add or change it later in Settings › Accounts & Data.", 12, TabsTheme.TextMuted));
        return card;
    }

    /// <summary>Checks and saves the typed token. True when there's nothing to save or it was saved.</summary>
    private async Task<bool> SaveTokenAsync()
    {
        var pat = _tokenBox?.Password?.Trim() ?? "";
        if (string.IsNullOrEmpty(pat) || _tokenSaved) return true;
        if (_tokenCheckBtn != null) { _tokenCheckBtn.IsEnabled = false; _tokenCheckBtn.Content = "Checking..."; }
        try
        {
            if (!GitHubTokenSetup.LooksLikeToken(pat))
                ShowStatus(_tokenStatus, "That doesn't look like a GitHub token (they start with ghp_ or github_pat_). Checking it anyway...", error: false);
            var http = App.Services.GetRequiredService<HttpClient>();
            var (ok, username, error) = await GitHubTokenSetup.ValidateAndSaveAsync(http, pat);
            if (!ok)
            {
                ShowStatus(_tokenStatus, (error ?? "The token couldn't be saved.") + " Clear the box to carry on without one.", error: true);
                return false;
            }
            _tokenSaved = true;
            ShowStatus(_tokenStatus, username != null ? $"Saved. Signed in to GitHub as {username}." : "Saved.", error: false, success: true);
            return true;
        }
        finally
        {
            if (_tokenCheckBtn != null) { _tokenCheckBtn.IsEnabled = true; _tokenCheckBtn.Content = "Check and save"; }
        }
    }

    // ── 3. Restore ───────────────────────────────────────────────────────────────

    private Border BuildRestoreCard()
    {
        var (card, body) = Card("Coming from another PC, or a reformat?",
            "Restore a backup made in Settings › Tools & Maintenance › Backup and restore. Your settings, collections and "
          + "per-game choices come back, and once your games are found RHI offers to install everything again.");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        _restorePickBtn = MakeButton("Choose a backup...", TabsTheme.IconButton);
        _restorePickBtn.Click += async (_, _) =>
        {
            try { await PickRestoreAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[SetupWindow.RestoreClick] {ex.GetType().Name}: {ex.Message}"); }
        };
        row.Children.Add(_restorePickBtn);
        _restoreClearBtn = new HyperlinkButton
        {
            Content = "Don't restore",
            Foreground = TabsTheme.B(TabsTheme.Link),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _restoreClearBtn.Click += (_, _) => SetRestore(null, null);
        row.Children.Add(_restoreClearBtn);
        body.Children.Add(row);

        _restoreStatus = Text("", 12, TabsTheme.TextSecondary);
        _restoreStatus.Visibility = Visibility.Collapsed;
        body.Children.Add(_restoreStatus);
        return card;
    }

    private async Task PickRestoreAsync()
    {
        var path = await BackupRestoreHelper.PickBackupFileAsync(WindowNative.GetWindowHandle(this), null);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var manifest = await Task.Run(() => BackupArchive.ReadManifest(path));
            SetRestore(path, Path.GetFileName(path) + ". " + BackupRestoreHelper.Describe(manifest));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetRestore(null, null);
            ShowStatus(_restoreStatus, ex is InvalidDataException ? ex.Message : $"The file couldn't be read: {ex.Message}", error: true);
        }
    }

    /// <summary>Chooses (or clears) the backup to restore; the ReShade choice and the finish button follow it.</summary>
    private void SetRestore(string? path, string? description)
    {
        _restorePath = path;
        bool on = path != null;
        ShowStatus(_restoreStatus, description ?? "", error: false, success: on);
        if (_restoreClearBtn != null) _restoreClearBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_restorePickBtn != null) _restorePickBtn.Content = on ? "Choose another..." : "Choose a backup...";
        if (_reShadeCard != null)
        {
            _reShadeCard.Opacity = on ? 0.45 : 1;
            _reShadeCard.IsHitTestVisible = !on;
        }
        if (_reShadeFromBackup != null) _reShadeFromBackup.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_finishBtn != null) _finishBtn.Content = on ? "Restore and get started" : "Get started";
        if (_finishNote != null)
            _finishNote.Text = on
                ? "RHI restores your settings, then lists what to reinstall once your games are found."
                : "You can change all of this later in Settings.";
    }

    // ── Finish ───────────────────────────────────────────────────────────────────

    private async Task FinishAsync()
    {
        if (_finishing) return;
        _finishing = true;
        if (_finishBtn != null) _finishBtn.IsEnabled = false;
        try
        {
            // A token typed but not checked yet is saved first, so RHI's first requests already use it
            if (!await SaveTokenAsync()) return;

            var path = _restorePath;
            // Deferred off the Click stack: the callbacks close this window
            var done = new TaskCompletionSource();
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (path != null) OnRestore?.Invoke(path);
                    else OnComplete?.Invoke(_manageReShade);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    CrashReporter.Log($"[SetupWindow.Finish] Restore failed — {ex.Message}");
                    ShowStatus(_restoreStatus, $"The backup couldn't be restored: {ex.Message}", error: true);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[SetupWindow.Finish] Callback threw — {ex.Message}");
                }
                done.TrySetResult();
            });
            await done.Task;
        }
        finally
        {
            _finishing = false;
            if (_finishBtn != null) _finishBtn.IsEnabled = true;
        }
    }

    private static void ShowStatus(TextBlock? block, string text, bool error, bool success = false)
    {
        if (block == null) return;
        block.Text = text;
        block.Foreground = TabsTheme.B(error ? "#FF9AA8" : success ? TabsTheme.Value : TabsTheme.TextSecondary);
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
