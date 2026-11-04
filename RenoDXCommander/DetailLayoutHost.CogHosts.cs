// DetailLayoutHost.CogHosts.cs -- where a component's ⚙ settings open in the new layouts
// (CogDialog.EmbeddedShow). Classic+ keeps the dialog.
//   Spotlight: inside the slide-in panel the cog was clicked in. The panel's section is swapped for
//              the settings, with a back button to the section.
//   Tabs:      in place, right under the row (or the Overview cards) whose ⚙ was clicked; the same
//              ⚙ again closes them. If that part of the page is rebuilt while they're open, they move
//              under the rebuilt row (found by the ⚙'s tooltip and Tag), or close if it's gone.
// Either closes when the game or the layout changes. Anything else (no ⚙ focused, a ⚙ outside the
// game page) falls back to the dialog.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    private CogDialog? _cog;
    private TaskCompletionSource<ContentDialogResult>? _cogResult;
    private string? _cogGame;
    private MainLayout _cogLayout;

    // Spotlight: the settings view laid over the panel's section
    private ScrollViewer? _panelCogView;
    private StackPanel? _panelCogContent;
    private Button? _panelBack;
    private string? _panelTitleBefore;

    // Tabs: the settings box inserted under a row
    private Border? _inlineCog;
    private string? _inlineAnchorTip;
    private object? _inlineAnchorTag;
    private Button? _inlineAnchor;

    // A cog closed by its own code (cog.Hide) and opened again with fresh content, e.g. OptiScaler after
    // a preset or version change: the click that reopens it isn't on a ⚙, so remember where it was
    private string? _reopenTitle;
    private DateTime _reopenAt;
    private Button? _reopenAnchor;

    private string? CurrentGameName => _window.ViewModel.SelectedGame?.GameName;

    /// <summary>CogDialog.EmbeddedShow: shows the cog in the layout, or returns null for the dialog.</summary>
    internal Task<ContentDialogResult>? TryShowCog(CogDialog cog)
    {
        try
        {
            if (_window.ViewModel.CurrentPage != AppPage.GameView) return null;
            bool reopening = _reopenTitle == cog.Title && DateTime.UtcNow - _reopenAt < TimeSpan.FromSeconds(3)
                && string.Equals(_cogGame, CurrentGameName, StringComparison.OrdinalIgnoreCase);
            var reopenAnchor = _reopenAnchor;
            _reopenTitle = null;
            _reopenAnchor = null;
            if (reopening)
            {
                if (IsSpotlight && _panelOverlay?.Visibility == Visibility.Visible)
                    return ShowCogInPanel(cog, anchor: null);
                if (Layout == MainLayout.Tabs
                    && (reopenAnchor is { IsLoaded: true } ? reopenAnchor : FindAnchorAgain()) is { } again)
                    return ShowCogInPlace(cog, again);
            }
            var anchor = FocusedButton();
            if (anchor == null) return null;
            if (IsSpotlight) return ShowCogInPanel(cog, anchor);
            if (Layout == MainLayout.Tabs) return ShowCogInPlace(cog, anchor);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DetailLayoutHost.TryShowCog] '{cog.Title}' — {ex.Message}; using the dialog");
            CloseCog(ContentDialogResult.None);
        }
        return null;
    }

    /// <summary>Closes the open cog (if any) and completes its ShowAsync.</summary>
    internal void CloseCog(ContentDialogResult result)
    {
        var cog = _cog;
        if (cog == null) return;
        _cog = null;
        cog.AttachEmbedded(null);

        if (_panelCogView != null && _panelCogView.Visibility == Visibility.Visible)
        {
            _panelCogView.Visibility = Visibility.Collapsed;
            _panelCogContent?.Children.Clear();
            if (_panelScroll != null) _panelScroll.Visibility = Visibility.Visible;
            if (_panelBack != null) _panelBack.Visibility = Visibility.Collapsed;
            if (_panelTitle != null && _panelTitleBefore != null) _panelTitle.Text = _panelTitleBefore;
        }
        if (_inlineCog != null)
        {
            TabsTheme.Detach(_inlineCog);
            _inlineCog = null;
            _inlineAnchor = null;
        }
        var tcs = _cogResult;
        _cogResult = null;
        tcs?.TrySetResult(result);
    }

    /// <summary>Called when the page is laid out again: a different game or layout closes the cog.</summary>
    private void CloseCogIfStale()
    {
        if (_cog == null) return;
        if (Layout != _cogLayout || !string.Equals(CurrentGameName, _cogGame, StringComparison.OrdinalIgnoreCase))
            CloseCog(ContentDialogResult.None);
    }

    private Task<ContentDialogResult> BeginCog(CogDialog cog)
    {
        _cog = cog;
        _cogGame = CurrentGameName;
        _cogLayout = Layout;
        _cogResult = new TaskCompletionSource<ContentDialogResult>();
        cog.AttachEmbedded(() =>
        {
            // cog.Hide(): usually followed by the same cog opening again
            _reopenTitle = cog.Title;
            _reopenAt = DateTime.UtcNow;
            _reopenAnchor = _inlineAnchor;
            CloseCog(ContentDialogResult.None);
        });
        return _cogResult.Task;
    }

    // ── Spotlight: in the side panel ──────────────────────────────────────────

    private Task<ContentDialogResult>? ShowCogInPanel(CogDialog cog, Button? anchor)
    {
        if (_panelOverlay == null || _panelOverlay.Visibility != Visibility.Visible || _panelSheet == null) return null;
        if (anchor != null && !IsWithin(anchor, _panelSheet)) return null; // a ⚙ outside the panel: dialog
        bool replacing = _cog != null;
        string? titleBefore = replacing ? _panelTitleBefore : _panelTitle?.Text;
        CloseCog(ContentDialogResult.None);
        EnsurePanelCogView();

        _panelTitleBefore = titleBefore ?? _panelTitle!.Text;
        _panelCogContent!.Children.Clear();
        _panelCogContent.Children.Add(cog.TakeBody());
        if (cog.TakeFooter() is { } footer) _panelCogContent.Children.Add(footer);
        if (cog.PrimaryText != null) _panelCogContent.Children.Add(MakeApplyBar(cog));

        _panelScroll!.Visibility = Visibility.Collapsed;
        _panelCogView!.Visibility = Visibility.Visible;
        _panelCogView.ChangeView(null, 0, null, disableAnimation: true);
        _panelBack!.Content = BackLabel(_panelTitleBefore);
        _panelBack.Visibility = Visibility.Visible;
        _panelTitle!.Text = cog.Title;
        CrashReporter.Log($"[DetailLayoutHost.ShowCogInPanel] {cog.Title}");
        return BeginCog(cog);
    }

    private void EnsurePanelCogView()
    {
        if (_panelCogView != null) return;
        _panelCogContent = new StackPanel { Spacing = 14 };
        _panelCogView = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(24, 4, 24, 32), Child = _panelCogContent },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Visibility = Visibility.Collapsed,
        };
        if (_panelScroll?.Parent is Grid sheetGrid)
        {
            Grid.SetRow(_panelCogView, 1);
            sheetGrid.Children.Add(_panelCogView);
        }

        // Back button in the panel header, left of the title
        _panelBack = new Button { Padding = new Thickness(8, 0, 12, 0), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0) };
        TabsTheme.Apply(_panelBack, TabsTheme.IconButton, height: 34, fontSize: 13);
        _panelBack.Click += (s, e) => CloseCog(ContentDialogResult.None);
        ToolTipService.SetToolTip(_panelBack, "Back");
        if (_panelTitle?.Parent is Grid head)
        {
            head.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
            foreach (var child in head.Children.OfType<FrameworkElement>())
                Grid.SetColumn(child, Grid.GetColumn(child) + 1);
            head.Children.Add(_panelBack);
        }
    }

    private static StackPanel BackLabel(string? to)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(TabsTheme.Icon("", 11)); // chevron left
        sp.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(to) ? "Back" : to, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    // ── Tabs: in place ────────────────────────────────────────────────────────

    private Task<ContentDialogResult>? ShowCogInPlace(CogDialog cog, Button anchor)
    {
        if (!IsWithin(anchor, SectionsPanel)) return null;
        // The same ⚙ again closes it
        if (_cog != null && _inlineCog != null && ReferenceEquals(_inlineAnchor, anchor))
        {
            CloseCog(ContentDialogResult.None);
            return Task.FromResult(ContentDialogResult.None);
        }
        var row = FindRow(anchor);
        if (row?.Parent is not Panel list) return null;
        CloseCog(ContentDialogResult.None);

        var box = BuildInlineBox(cog);
        list.Children.Insert(list.Children.IndexOf(row) + 1, box);
        _inlineCog = box;
        _inlineAnchor = anchor;
        _inlineAnchorTip = ToolTipService.GetToolTip(anchor) as string;
        _inlineAnchorTag = anchor.Tag;
        box.Unloaded += (s, e) => OnInlineUnloaded(box);
        CrashReporter.Log($"[DetailLayoutHost.ShowCogInPlace] {cog.Title}");
        var task = BeginCog(cog);
        box.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.1 });
        return task;
    }

    /// <summary>The settings box: title and ✕, then the cog's cards two across (text above them full width).</summary>
    private Border BuildInlineBox(CogDialog cog)
    {
        var title = TabsTheme.CardTitle(cog.Title);
        title.VerticalAlignment = VerticalAlignment.Center;
        var close = TabsTheme.MakeIconButton(TabsTheme.GlyphClose, TabsTheme.IconButton, 12);
        close.Width = 34; close.Height = 34;
        close.Click += (s, e) => CloseCog(ContentDialogResult.None);
        ToolTipService.SetToolTip(close, "Close");
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(title);
        Grid.SetColumn(close, 1);
        head.Children.Add(close);

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(head);

        // Lay the cog's body out wide: leading text full width, then cards in two columns
        var body = cog.TakeBody();
        var items = body.Children.ToList();
        body.Children.Clear();
        var leading = new StackPanel { Spacing = 10 };
        // Two columns that stack on their own (a short card isn't pushed down by a tall one beside it);
        // one column when there isn't room for two.
        var leftCol = new StackPanel { Spacing = 14 };
        var rightCol = new StackPanel { Spacing = 14 };
        var cards = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.Children.Add(leftCol);
        cards.Children.Add(rightCol);
        var trailing = new StackPanel { Spacing = 10 };
        bool seenCard = false;
        int cardCount = 0;
        foreach (var item in items)
        {
            if (item is Border)
            {
                seenCard = true;
                (cardCount++ % 2 == 0 ? leftCol : rightCol).Children.Add(item);
            }
            else if (!seenCard) leading.Children.Add(item);
            else trailing.Children.Add(item); // text after the cards (notes, the status line)
        }
        bool? twoCols = null;
        void Fit(double width)
        {
            bool two = cardCount > 1 && width >= 2 * 460 + 14;
            if (twoCols == two) return;
            twoCols = two;
            cards.ColumnDefinitions[1].Width = two ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            cards.ColumnSpacing = two ? 14 : 0;
            cards.RowSpacing = two || rightCol.Children.Count == 0 ? 0 : 14;
            Grid.SetColumn(rightCol, two ? 1 : 0);
            Grid.SetRow(rightCol, two ? 0 : 1);
        }
        Fit(double.MaxValue);
        cards.SizeChanged += (s, e) => Fit(e.NewSize.Width);
        if (leading.Children.Count > 0) content.Children.Add(leading);
        if (cardCount > 0) content.Children.Add(cards);
        if (trailing.Children.Count > 0) content.Children.Add(trailing);
        if (cog.TakeFooter() is { } footer) content.Children.Add(footer);
        if (cog.PrimaryText != null) content.Children.Add(MakeApplyBar(cog));

        return new Border
        {
            Background = TabsTheme.B(TabsTheme.PanelBg),
            BorderBrush = TabsTheme.B("#2A3D63"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 14, 18, 18),
            Margin = new Thickness(0, 6, 0, 10),
            Child = content,
        };
    }

    /// <summary>
    /// The box left the page (its row was rebuilt): put it under the rebuilt row, or close the cog
    /// if that row is gone. Checked after the rebuild has finished.
    /// </summary>
    private void OnInlineUnloaded(Border box)
    {
        _window.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (_cog == null || !ReferenceEquals(_inlineCog, box) || box.IsLoaded) return;
                if (Layout == MainLayout.Tabs && FindAnchorAgain() is { } anchor && FindRow(anchor) is { Parent: Panel list } row)
                {
                    TabsTheme.Detach(box);
                    list.Children.Insert(list.Children.IndexOf(row) + 1, box);
                    _inlineAnchor = anchor;
                    return;
                }
                CloseCog(ContentDialogResult.None);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailLayoutHost.OnInlineUnloaded] {ex.Message}");
                CloseCog(ContentDialogResult.None);
            }
        });
    }

    private Button? FindAnchorAgain()
    {
        if (_inlineAnchorTip is not string tip) return null;
        foreach (var b in Descendants<Button>(SectionsPanel))
            if (ToolTipService.GetToolTip(b) is string t && t == tip
                && (Equals(b.Tag, _inlineAnchorTag) || (b.Tag is GameCardViewModel c && _inlineAnchorTag is GameCardViewModel o && c.GameName == o.GameName))
                && b.Visibility == Visibility.Visible && b.IsLoaded)
                return b;
        return null;
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    /// <summary>Apply / Cancel for cogs that apply on demand (RTX HDR, Auto HDR sliders).</summary>
    private Grid MakeApplyBar(CogDialog cog)
    {
        var cancel = new Button { Content = cog.CloseText, Padding = new Thickness(16, 0, 16, 0) };
        TabsTheme.Apply(cancel, TabsTheme.IconButton, height: 36);
        cancel.Click += (s, e) => CloseCog(ContentDialogResult.None);
        var apply = new Button { Content = cog.PrimaryText, Padding = new Thickness(18, 0, 18, 0) };
        TabsTheme.Apply(apply, TabsTheme.BlueButton, height: 36);
        apply.Click += (s, e) => CloseCog(ContentDialogResult.Primary);
        var bar = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(cancel, 1); bar.Children.Add(cancel);
        Grid.SetColumn(apply, 2); bar.Children.Add(apply);
        return bar;
    }

    /// <summary>The ⚙ that was just clicked (buttons keep focus after a click).</summary>
    private Button? FocusedButton()
    {
        var root = _window.Content?.XamlRoot;
        if (root == null) return null;
        DependencyObject? cur = FocusManager.GetFocusedElement(root) as DependencyObject;
        while (cur != null && cur is not Button) cur = VisualTreeHelper.GetParent(cur);
        return cur as Button;
    }

    private static bool IsWithin(DependencyObject el, DependencyObject ancestor)
    {
        for (var cur = el; cur != null; cur = VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// The row a ⚙ belongs to: its nearest ancestor that sits in a vertical list and is at least
    /// 400 px wide (a component row, or the Overview card grid).
    /// </summary>
    private static FrameworkElement? FindRow(Button anchor)
    {
        DependencyObject? cur = anchor;
        while (cur != null)
        {
            var parent = VisualTreeHelper.GetParent(cur);
            if (cur is FrameworkElement fe && parent is StackPanel { Orientation: Orientation.Vertical } && fe.ActualWidth >= 400)
                return fe;
            cur = parent;
        }
        return null;
    }
}
