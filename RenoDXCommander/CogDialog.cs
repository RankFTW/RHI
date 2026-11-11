// CogDialog.cs — Shared layout for every component ⚙ settings dialog.
// One look for all cogs: "<Component> Settings" title, fixed width, scrolling body,
// a fixed label column with the control beside it and a short explanation under it,
// section cards, button rows and a status line inside the dialog.
// Styled like the Tabs layout (TabsTheme): each AddSection starts a card, settings added before
// the first section share an untitled card, intro / warning text sits between the cards.
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

    // The scrolling body: cards and loose text, top to bottom
    private readonly StackPanel _root;
    // The open section's card (AddSection), or the untitled card for settings before the first section
    private StackPanel? _section;
    private StackPanel? _loose;
    private readonly HashSet<StackPanel> _cardPanels = new();
    private readonly HashSet<UIElement> _headings = new();

    /// <summary>
    /// Where content goes now: the open section's card (or the dialog body before any section).
    /// Prefer the Add* helpers; add directly only for one-off content.
    /// </summary>
    public StackPanel Body => _section ?? _root;

    /// <summary>Everything added so far, top to bottom, with the cards opened up (section headings left out).</summary>
    public IEnumerable<FrameworkElement> Items
    {
        get
        {
            foreach (var child in _root.Children.OfType<FrameworkElement>())
            {
                if (child is Border { Child: StackPanel p } && _cardPanels.Contains(p))
                {
                    foreach (var item in p.Children.OfType<FrameworkElement>())
                        if (!_headings.Contains(item)) yield return item;
                }
                else yield return child;
            }
        }
    }

    /// <summary>The dialog, available after <see cref="Build"/> or <see cref="ShowAsync"/>.</summary>
    public ContentDialog? Dialog { get; private set; }

    /// <param name="componentName">Display name, e.g. "ReShade". The title becomes "ReShade Settings".</param>
    public CogDialog(string componentName, XamlRoot xamlRoot)
    {
        _title = componentName.EndsWith(" Settings", StringComparison.Ordinal) ? componentName : componentName + " Settings";
        _xamlRoot = xamlRoot;
        _root = new StackPanel { Spacing = 14, Width = BodyWidth };
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

    /// <summary>Starts a section: a card with a small uppercase heading. Everything added after it goes in the card.</summary>
    public TextBlock AddSection(string header)
    {
        _loose = null;
        _section = NewCard(header, out var heading);
        _hasContent = true;
        return heading!;
    }

    private StackPanel NewCard(string? header, out TextBlock? heading)
    {
        var panel = new StackPanel { Spacing = 10 };
        heading = null;
        if (header != null)
        {
            heading = TabsTheme.SectionHeading(header);
            heading.FontSize = 11;
            heading.Margin = new Thickness(0, 0, 0, 2);
            panel.Children.Add(heading);
            _headings.Add(heading);
        }
        var card = TabsTheme.MakeCard(panel);
        card.Padding = new Thickness(18, 16, 18, 16);
        _root.Children.Add(card);
        _cardPanels.Add(panel);
        return panel;
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

    // ── Compact two-per-row grid ────────────────────────────────────────────────
    // For dialogs with many small settings (user preference): label | control | gap | label | control,
    // with each setting's help as a tooltip on both the label and the control.

    /// <summary>One cell of <see cref="AddCompactGrid"/>.</summary>
    public sealed record CompactCell(string Label, string? Help, FrameworkElement Control);

    private const double CompactControlWidth = 160;

    /// <summary>
    /// A dropdown for the compact grid that calls <paramref name="onChanged"/> when the user picks an item
    /// (never during construction). Returning false, or throwing, reverts the pick; errors show in the status line.
    /// </summary>
    public CompactCell CompactCombo(string label, string? help, IList<string> items, int selectedIndex,
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
        return new CompactCell(label, help, combo);
    }

    /// <summary>Adds settings two per row. A null cell leaves that slot empty.</summary>
    public Grid AddCompactGrid(params CompactCell?[] cells)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CompactControlWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) }); // with ColumnSpacing: 24 px gap
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CompactControlWidth) });
        for (int r = 0; r < (cells.Length + 1) / 2; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell == null) continue;
            int row = i / 2, col = i % 2 == 0 ? 0 : 3;
            var label = new TextBlock
            {
                Text = cell.Label,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            };
            var control = cell.Control;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            control.VerticalAlignment = VerticalAlignment.Center;
            if (!string.IsNullOrWhiteSpace(cell.Help))
            {
                // Disabled controls don't show tooltips, so the label carries it too
                ToolTipService.SetToolTip(label, cell.Help);
                ToolTipService.SetToolTip(control, cell.Help);
            }
            Grid.SetRow(label, row); Grid.SetColumn(label, col);
            Grid.SetRow(control, row); Grid.SetColumn(control, col + 1);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }
        Add(grid);
        return grid;
    }

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

    private bool _prepared;
    private ScrollViewer? _dialogScroll;
    private FrameworkElement? _footer;
    private bool _footerStyled;

    /// <summary>
    /// Finishes the body once: the empty note if nothing was added, the status line, and the Tabs
    /// look for everything the caller built (inputs, buttons by tone, text colours). Runs before the
    /// content is shown, because button hover colours are read when their template loads.
    /// </summary>
    private void PrepareBody()
    {
        if (_prepared) return;
        _prepared = true;
        if (!_hasContent) AddEmpty();
        _root.Children.Add(_status);
        TabsTheme.Restyle(_root);
    }

    private void PrepareFooter(FrameworkElement? footer)
    {
        if (footer == null || _footerStyled) return;
        _footerStyled = true;
        TabsTheme.Restyle(footer);
    }

    /// <summary>Creates the ContentDialog. <paramref name="footer"/> (optional) stays pinned below the scrolling body.</summary>
    public ContentDialog Build(FrameworkElement? footer = null)
    {
        PrepareBody();
        _footer = footer ?? _footer;
        footer = _footer;

        FrameworkElement content = _dialogScroll = new ScrollViewer
        {
            Content = _root,
            MaxHeight = BodyMaxHeight,
            Padding = new Thickness(0, 0, 16, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        if (footer != null)
        {
            PrepareFooter(footer);
            var root = new Grid { RowSpacing = 12 };
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
            Title = new TextBlock
            {
                Text = _title,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TabsTheme.B(TabsTheme.TextPrimary),
            },
            Content = content,
            CloseButtonText = _closeText,
            // No default button for Close: Enter in a text box (hotkeys, FPS, nits) must not close the dialog
            DefaultButton = _primaryText != null ? ContentDialogButton.Primary : ContentDialogButton.None,
            XamlRoot = _xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        if (_primaryText != null) dlg.PrimaryButtonText = _primaryText;
        ApplyDialogLook(dlg);
        Dialog = dlg;
        return dlg;
    }

    /// <summary>
    /// A dialog heading in the new look: the title (20 semibold) and an optional one-line subtitle.
    /// Used by the dialogs that aren't cogs (patch notes, messages, donate).
    /// </summary>
    internal static UIElement MakeDialogTitle(string title, string? subtitle = null)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        if (!string.IsNullOrEmpty(subtitle))
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
                TextWrapping = TextWrapping.Wrap,
            });
        return text;
    }

    /// <summary>Tabs colours for the dialog frame: navy body, darker button bar, softer backdrop.</summary>
    internal static void ApplyDialogLook(ContentDialog dlg, double maxWidth = DialogMaxWidth)
    {
        var r = dlg.Resources;
        TabsTheme.SetResource(r, "ContentDialogMaxWidth", maxWidth);
        dlg.Background = TabsTheme.B(TabsTheme.PanelBg);
        dlg.BorderBrush = TabsTheme.B("#263042");
        dlg.Foreground = TabsTheme.B(TabsTheme.TextPrimary);
        dlg.CornerRadius = new CornerRadius(16);
        TabsTheme.SetResource(r, "ContentDialogBackground", TabsTheme.B("#0C1119"));    // button bar
        TabsTheme.SetResource(r, "ContentDialogTopOverlay", TabsTheme.B(TabsTheme.PanelBg));
        TabsTheme.SetResource(r, "ContentDialogBorderBrush", TabsTheme.B("#263042"));
        TabsTheme.SetResource(r, "ContentDialogSeparatorBorderBrush", TabsTheme.B(TabsTheme.CardBorder));
        TabsTheme.SetResource(r, "ContentDialogForeground", TabsTheme.B(TabsTheme.TextPrimary));
        TabsTheme.SetResource(r, "OverlayCornerRadius", new CornerRadius(16));
        // The dialog's own buttons: Close / secondary grey, primary blue
        dlg.CloseButtonStyle = DialogButtonStyle(TabsTheme.IconButton);
        dlg.SecondaryButtonStyle = DialogButtonStyle(TabsTheme.IconButton);
        dlg.PrimaryButtonStyle = DialogButtonStyle(TabsTheme.BlueButton);
        // Ticks and radio dots in the accent blue instead of the system accent
        var tick = TabsTheme.B("#3E6DC4");
        foreach (var key in new[]
        {
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeChecked",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
            "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver",
            "RadioButtonOuterEllipseCheckedFillPressed", "RadioButtonOuterEllipseCheckedStroke",
            "RadioButtonOuterEllipseCheckedStrokePointerOver", "RadioButtonOuterEllipseCheckedStrokePressed",
        })
            TabsTheme.SetResource(r, key, tick);
        TabsTheme.SetResource(r, "CheckBoxCheckGlyphForegroundChecked", TabsTheme.B("#FFFFFF"));
        TabsTheme.SetResource(r, "CheckBoxCheckGlyphForegroundCheckedPointerOver", TabsTheme.B("#FFFFFF"));
        TabsTheme.SetResource(r, "CheckBoxCheckGlyphForegroundCheckedPressed", TabsTheme.B("#FFFFFF"));
        TabsTheme.SetResource(r, DialogLook.MarkerKey, true);
        TabsTheme.SetResource(r, "ButtonBackgroundPointerOver", TabsTheme.B(TabsTheme.IconButton.HoverBg));
        TabsTheme.SetResource(r, "ButtonBackgroundPressed", TabsTheme.B(TabsTheme.IconButton.Bg));
        TabsTheme.SetResource(r, "ButtonBorderBrushPointerOver", TabsTheme.B(TabsTheme.IconButton.Border));
        TabsTheme.SetResource(r, "ButtonForegroundPointerOver", TabsTheme.B(TabsTheme.TextPrimary));
        TabsTheme.SetResource(r, "ButtonForegroundPressed", TabsTheme.B(TabsTheme.TextPrimary));
    }

    private static Style DialogButtonStyle(TabsTheme.Look look)
    {
        var s = new Style(typeof(Button));
        if (Application.Current.Resources.TryGetValue("DefaultButtonStyle", out var b) && b is Style basedOn)
            s.BasedOn = basedOn;
        s.Setters.Add(new Setter(Control.BackgroundProperty, TabsTheme.B(look.Bg)));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, TabsTheme.B(look.Border)));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TabsTheme.B(look.Fg)));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        s.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36.0));
        s.Setters.Add(new Setter(Control.FontWeightProperty, look.Bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal));
        s.Setters.Add(new Setter(Controls.ButtonFade.DisabledProperty, true)); // no hover fade (App.xaml)
        return s;
    }

    /// <summary>
    /// Shows the settings: in Spotlight's side panel or in place under the row in Tabs when the
    /// layout can host them (<see cref="EmbeddedShow"/>), otherwise as a dialog through
    /// <see cref="DialogService.ShowSafeAsync"/>. Completes when they're closed; Primary means Apply.
    /// </summary>
    public Task<ContentDialogResult> ShowAsync(FrameworkElement? footer = null)
    {
        _footer = footer ?? _footer;
        if (EmbeddedShow?.Invoke(this) is { } embedded) return embedded;
        return DialogService.ShowSafeAsync(Dialog ?? Build(_footer));
    }

    /// <summary>Closes the settings if they're open (e.g. before reopening them with fresh content).</summary>
    public void Hide()
    {
        if (_closeEmbedded is { } close) { close(); return; }
        Dialog?.Hide();
    }

    // ── Embedded hosts (Spotlight side panel, Tabs in place) ─────────────────────

    /// <summary>
    /// Set by the game page layout (DetailLayoutHost): shows a cog's content in the layout and returns
    /// a task that completes when it closes, or null to use the dialog.
    /// </summary>
    internal static Func<CogDialog, Task<ContentDialogResult>?>? EmbeddedShow;

    private Action? _closeEmbedded;

    /// <summary>"&lt;Component&gt; Settings".</summary>
    internal string Title => _title;
    /// <summary>Apply button text when <see cref="UseApplyButton"/> was called, else null.</summary>
    internal string? PrimaryText => _primaryText;
    internal string CloseText => _closeText;

    /// <summary>The finished body for a host to place (no scroll viewer; fills the host's width).</summary>
    internal StackPanel TakeBody()
    {
        PrepareBody();
        if (_dialogScroll != null && ReferenceEquals(_dialogScroll.Content, _root)) _dialogScroll.Content = null;
        _root.Width = double.NaN;
        _root.HorizontalAlignment = HorizontalAlignment.Stretch;
        return _root;
    }

    /// <summary>The caller's pinned footer (OptiScaler's buttons), if any, ready for a host to place.</summary>
    internal FrameworkElement? TakeFooter()
    {
        if (_footer == null) return null;
        PrepareFooter(_footer);
        TabsTheme.Detach(_footer);
        _footer.Width = double.NaN;
        _footer.HorizontalAlignment = HorizontalAlignment.Stretch;
        return _footer;
    }

    /// <summary>Called by the host that shows this cog, so <see cref="Hide"/> closes it there (null when it closes).</summary>
    internal void AttachEmbedded(Action? close) => _closeEmbedded = close;

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

    /// <summary>Adds custom content (e.g. a compact grid) and counts it as content for section dividers.</summary>
    public T AddContent<T>(T element) where T : UIElement
    {
        Add(element);
        return element;
    }

    // ── Internals ────────────────────────────────────────────────────────────────

    private void Add(UIElement e)
    {
        _hasContent = true;
        if (_section != null) { _section.Children.Add(e); return; }
        // Before the first section: text (intro, warnings, notes) between cards,
        // settings in one untitled card until the next text
        if (e is TextBlock)
        {
            _loose = null;
            _root.Children.Add(e);
            return;
        }
        _loose ??= NewCard(null, out _);
        _loose.Children.Add(e);
    }

    private static TextBlock Text(string text, double size, string brushKey) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UIFactory.Brush(brushKey),
    };
}
