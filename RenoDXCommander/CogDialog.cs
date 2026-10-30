// CogDialog.cs — Shared layout for every component ⚙ settings dialog.
// One look for all cogs: "<Component> Settings" title, fixed width, scrolling body,
// a fixed label column with the control beside it and a short explanation under it,
// section headers, button rows and a status line inside the dialog.
// Settings apply as soon as they change; the dialog only has a Close button
// (slider dialogs can opt into Apply / Cancel via UseApplyButton).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>Visual style for buttons inside a cog dialog.</summary>
internal enum CogButtonKind
{
    /// <summary>Main action, e.g. "Deploy X.ini". Blue.</summary>
    Primary,
    /// <summary>Secondary action, e.g. "Open X.ini", "Open log". Grey.</summary>
    Secondary,
    /// <summary>Destructive action, e.g. "Delete folder". Red.</summary>
    Danger,
}

/// <summary>
/// Builds a component ⚙ settings dialog with the shared layout.
/// Add content top to bottom, then call <see cref="ShowAsync"/>.
/// </summary>
internal sealed class CogDialog
{
    public const double DialogMaxWidth = 720;
    public const double BodyWidth = 600;
    public const double BodyMaxHeight = 620;
    public const double LabelColumnWidth = 170;
    public const double ColumnGap = 12;
    /// <summary>Opacity for rows that can't be used yet (e.g. component not installed).</summary>
    public const double DisabledOpacity = 0.45;

    private readonly string _title;
    private readonly XamlRoot _xamlRoot;
    private readonly TextBlock _status;
    private bool _hasContent;
    private string? _primaryText;
    private string _closeText = "Close";

    /// <summary>The scrolling body. Prefer the Add* helpers; add directly only for one-off content.</summary>
    public StackPanel Body { get; }

    /// <summary>The dialog, available after <see cref="Build"/> or <see cref="ShowAsync"/>.</summary>
    public ContentDialog? Dialog { get; private set; }

    /// <param name="componentName">Display name, e.g. "ReShade". The title becomes "ReShade Settings".</param>
    public CogDialog(string componentName, XamlRoot xamlRoot)
    {
        _title = componentName.EndsWith(" Settings", StringComparison.Ordinal) ? componentName : componentName + " Settings";
        _xamlRoot = xamlRoot;
        Body = new StackPanel { Spacing = 10, Width = BodyWidth };
        _status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
    }

    // ── Text ─────────────────────────────────────────────────────────────────────

    /// <summary>Short explanation at the top of the dialog.</summary>
    public TextBlock AddIntro(string text)
    {
        var tb = Text(text, 12, ResourceKeys.TextSecondaryBrush);
        Add(tb);
        return tb;
    }

    /// <summary>Amber notice, e.g. "Install X on this game to change these settings."</summary>
    public TextBlock AddWarning(string text)
    {
        var tb = Text(text, 12, ResourceKeys.AccentAmberBrush);
        Add(tb);
        return tb;
    }

    /// <summary>Small grey note (full width).</summary>
    public TextBlock AddNote(string text)
    {
        var tb = Text(text, 11, ResourceKeys.TextTertiaryBrush);
        Add(tb);
        return tb;
    }

    /// <summary>Body text for components with nothing to configure.</summary>
    public TextBlock AddEmpty(string? text = null)
    {
        var tb = Text(text ?? "This component has no settings.", 12, ResourceKeys.TextSecondaryBrush);
        Add(tb);
        return tb;
    }

    /// <summary>Section header, with a divider above it unless it's the first thing in the dialog.</summary>
    public TextBlock AddSection(string header)
    {
        if (_hasContent)
            Body.Children.Add(new Border
            {
                Height = 1,
                Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                Margin = new Thickness(0, 6, 0, 0),
            });
        var tb = new TextBlock
        {
            Text = header,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        Add(tb);
        return tb;
    }

    // ── Setting rows ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A labelled row: fixed-width label on the left, <paramref name="control"/> stretched on the right,
    /// and <paramref name="help"/> (if any) in small grey text underneath. Help is also the tooltip.
    /// Returns the row container (label grid + help) so callers can dim or hide the whole row.
    /// </summary>
    public FrameworkElement AddRow(string label, FrameworkElement control, string? help = null)
    {
        var row = new StackPanel { Spacing = 3 };
        var grid = new Grid { ColumnSpacing = ColumnGap };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        if (control.HorizontalAlignment != HorizontalAlignment.Left)
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(lbl);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        row.Children.Add(grid);

        if (!string.IsNullOrWhiteSpace(help))
        {
            ToolTipService.SetToolTip(lbl, help);
            row.Children.Add(new TextBlock
            {
                Text = help,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(LabelColumnWidth + ColumnGap, 0, 0, 2),
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
        }
        Add(row);
        return row;
    }

    /// <summary>
    /// A labelled dropdown that calls <paramref name="onChanged"/> with the new index when the user
    /// picks an item (never during construction). Return false from the callback to revert the pick.
    /// </summary>
    public ComboBox AddCombo(string label, string? help, IList<string> items, int selectedIndex,
        Func<int, bool> onChanged, bool isEnabled = true)
    {
        var combo = MakeCombo(items, selectedIndex, isEnabled);
        int last = combo.SelectedIndex;
        bool reverting = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (reverting || combo.SelectedIndex < 0 || combo.SelectedIndex == last) return;
            bool keep;
            try { keep = onChanged(combo.SelectedIndex); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[CogDialog] '{_title}' / '{label}' change failed — {ex.Message}");
                SetError($"Couldn't change {label}: {ex.Message}");
                keep = false;
            }
            if (keep) { last = combo.SelectedIndex; return; }
            reverting = true;
            combo.SelectedIndex = last;
            reverting = false;
        };
        AddRow(label, combo, help);
        return combo;
    }

    /// <summary>Same as the Func overload, for callbacks that always keep the pick.</summary>
    public ComboBox AddCombo(string label, string? help, IList<string> items, int selectedIndex,
        Action<int> onChanged, bool isEnabled = true)
        => AddCombo(label, help, items, selectedIndex, i => { onChanged(i); return true; }, isEnabled);

    /// <summary>A dropdown with the shared styling and no handler attached.</summary>
    public static ComboBox MakeCombo(IList<string> items, int selectedIndex, bool isEnabled = true) => new()
    {
        ItemsSource = items,
        SelectedIndex = selectedIndex,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        FontSize = 12,
        IsEnabled = isEnabled,
    };

    /// <summary>A text box with the shared styling.</summary>
    public static TextBox MakeTextBox(string text = "", string? placeholder = null) => new()
    {
        Text = text,
        PlaceholderText = placeholder ?? "",
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Lays out a main control (stretched) with small buttons after it, for rows like
    /// [TextBox][Set] or [ComboBox][Auto]. Pass the result to <see cref="AddRow"/>.
    /// </summary>
    public static Grid Inline(FrameworkElement main, params FrameworkElement[] after)
    {
        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(main);
        main.HorizontalAlignment = HorizontalAlignment.Stretch;
        for (int i = 0; i < after.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(after[i], i + 1);
            g.Children.Add(after[i]);
        }
        return g;
    }

    // ── Buttons ──────────────────────────────────────────────────────────────────

    /// <summary>A button in the shared style. <paramref name="onClick"/> errors are logged and shown in the status line.</summary>
    public Button MakeButton(string text, CogButtonKind kind, Func<Task> onClick, string? tooltip = null)
    {
        var b = StyledButton(text, kind);
        if (tooltip != null) ToolTipService.SetToolTip(b, tooltip);
        b.Click += async (_, _) =>
        {
            try { await onClick(); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[CogDialog] '{_title}' / '{text}' failed — {ex}");
                SetError($"{text} failed: {ex.Message}");
            }
        };
        return b;
    }

    /// <summary>Synchronous overload of <see cref="MakeButton(string, CogButtonKind, Func{Task}, string?)"/>.</summary>
    public Button MakeButton(string text, CogButtonKind kind, Action onClick, string? tooltip = null)
        => MakeButton(text, kind, () => { onClick(); return Task.CompletedTask; }, tooltip);

    /// <summary>A grey button that opens a file (ini, log) in its default app. Disabled if the file doesn't exist.</summary>
    public Button MakeOpenFileButton(string text, string? path)
    {
        var b = MakeButton(text, CogButtonKind.Secondary, async () =>
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { SetError($"{Path.GetFileName(path)} wasn't found."); return; }
            await Windows.System.Launcher.LaunchUriAsync(new Uri(path));
        });
        b.IsEnabled = !string.IsNullOrEmpty(path) && File.Exists(path);
        return b;
    }

    /// <summary>A grey button that copies a text file (e.g. a log) to the clipboard.</summary>
    public Button MakeCopyFileButton(string text, string? path)
    {
        var b = MakeButton(text, CogButtonKind.Secondary, async () =>
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { SetError($"{Path.GetFileName(path)} wasn't found."); return; }
            string content;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                content = await sr.ReadToEndAsync();
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(content);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            SetSuccess($"Copied {Path.GetFileName(path)} to the clipboard.");
        });
        b.IsEnabled = !string.IsNullOrEmpty(path) && File.Exists(path);
        return b;
    }

    /// <summary>A row of buttons, left-aligned, sized to their text. Wraps onto a second line if needed.</summary>
    public WrapPanel AddButtons(params Button[] buttons)
    {
        var row = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        foreach (var b in buttons) row.Children.Add(b);
        Add(row);
        return row;
    }

    /// <summary>Button styling without a click handler (for buttons wired up by the caller).</summary>
    public static Button StyledButton(string text, CogButtonKind kind, bool compact = false)
    {
        var (bg, fg, border) = kind switch
        {
            CogButtonKind.Primary => (ResourceKeys.AccentBlueBgBrush, ResourceKeys.AccentBlueBrush, ResourceKeys.AccentBlueBorderBrush),
            CogButtonKind.Danger => (ResourceKeys.AccentRedBgBrush, ResourceKeys.AccentRedBrush, ResourceKeys.AccentRedBrush),
            _ => (ResourceKeys.SurfaceOverlayBrush, ResourceKeys.TextSecondaryBrush, ResourceKeys.BorderStrongBrush),
        };
        return new Button
        {
            Content = text,
            FontSize = 12,
            Padding = compact ? new Thickness(10, 5, 10, 5) : new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            Background = UIFactory.Brush(bg),
            Foreground = UIFactory.Brush(fg),
            BorderBrush = UIFactory.Brush(border),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ── Status line ──────────────────────────────────────────────────────────────

    /// <summary>Neutral status message at the bottom of the dialog (plain text, no emoji).</summary>
    public void SetStatus(string text) => ShowStatus(text, ResourceKeys.TextSecondaryBrush);
    /// <summary>Green status message.</summary>
    public void SetSuccess(string text) => ShowStatus(text, ResourceKeys.AccentGreenBrush);
    /// <summary>Red status message.</summary>
    public void SetError(string text) => ShowStatus(text, ResourceKeys.AccentRedBrush);
    /// <summary>Hides the status line.</summary>
    public void ClearStatus() => _status.Visibility = Visibility.Collapsed;

    private void ShowStatus(string text, string brushKey)
    {
        _status.Text = text;
        _status.Foreground = UIFactory.Brush(brushKey);
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Dialog ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply / Cancel instead of Close. Only for dialogs where applying on every change would be
    /// too expensive (sliders that write to the driver).
    /// </summary>
    public void UseApplyButton(string applyText = "Apply", string cancelText = "Cancel")
    {
        _primaryText = applyText;
        _closeText = cancelText;
    }

    /// <summary>Creates the ContentDialog. <paramref name="footer"/> (optional) stays pinned below the scrolling body.</summary>
    public ContentDialog Build(FrameworkElement? footer = null)
    {
        if (!_hasContent) AddEmpty();
        Body.Children.Add(_status);

        FrameworkElement content = new ScrollViewer
        {
            Content = Body,
            MaxHeight = BodyMaxHeight,
            Padding = new Thickness(0, 0, 16, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        if (footer != null)
        {
            var root = new Grid { RowSpacing = 10 };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.Width = BodyWidth;
            footer.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(content);
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);
            content = root;
        }

        var dlg = new ContentDialog
        {
            Title = _title,
            Content = content,
            CloseButtonText = _closeText,
            // No default button for Close: Enter in a text box (hotkeys, FPS, nits) must not close the dialog
            DefaultButton = _primaryText != null ? ContentDialogButton.Primary : ContentDialogButton.None,
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        if (_primaryText != null) dlg.PrimaryButtonText = _primaryText;
        dlg.Resources["ContentDialogMaxWidth"] = DialogMaxWidth;
        Dialog = dlg;
        return dlg;
    }

    /// <summary>Builds (if needed) and shows the dialog through <see cref="DialogService.ShowSafeAsync"/>.</summary>
    public Task<ContentDialogResult> ShowAsync(FrameworkElement? footer = null)
        => DialogService.ShowSafeAsync(Dialog ?? Build(footer));

    /// <summary>Hides the dialog if it's open (e.g. before reopening it with fresh content).</summary>
    public void Hide() => Dialog?.Hide();

    // ── Cog button ───────────────────────────────────────────────────────────────

    /// <summary>The ⚙ button used on component rows (32 px high, 36 wide).</summary>
    public static Button MakeCogButton(string tooltip)
    {
        var b = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
        };
        ToolTipService.SetToolTip(b, tooltip);
        return b;
    }

    // ── Internals ────────────────────────────────────────────────────────────────

    private void Add(UIElement e)
    {
        Body.Children.Add(e);
        _hasContent = true;
    }

    private static TextBlock Text(string text, double size, string brushKey) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UIFactory.Brush(brushKey),
    };
}
