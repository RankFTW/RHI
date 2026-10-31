// SettingsHandler.Navigation.cs -- Settings page left column: "Find a setting" search box, section
// jump list that highlights the section in view, last-section memory and the narrow-window layout.
// Everything here is built in C# (XAML constraint: no new x:Name / controls in MainWindow.xaml).

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    // ── Layout constants ──────────────────────────────────────────────────────
    // The nav column and the sections sit side by side as one block of at most
    // NavColumnWidth + SettingsContentMaxWidth, centred in the window (as in the mock-up).
    private const double NavColumnWidth = 244;          // 24 left padding + 220 list
    private const double SettingsContentMaxWidth = 996; // 28 gap + 944 cards + 24 right padding
    /// <summary>Below this content width (window minus nav column) the nav column is hidden.</summary>
    private const double MinSettingsContentWidth = 560;
    private const string SectionTagPrefix = "Section:";
    private const int MaxSearchResults = 60;

    private sealed class SettingsSection
    {
        public SettingsSection(string key, StackPanel panel, string title, Button button)
        { Key = key; Panel = panel; Title = title; Button = button; }
        public string Key { get; }
        public StackPanel Panel { get; }
        public string Title { get; }
        public Button Button { get; }
    }

    private sealed record SearchEntry(string Label, string? Tooltip, FrameworkElement Element,
                                      Border? Card, string SectionKey, string Context);

    private sealed class IndexContext
    {
        public IndexContext(SettingsSection section) => Section = section;
        public SettingsSection Section { get; }
        public Border? Card;
        public string? CardTitle;
        public string? SubHeading;
        public string? LastLabel;
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _navSetupDone;
    private ScrollViewer? _settingsScroll;
    private ColumnDefinition? _navColumn;
    private Border? _navBorder;
    private StackPanel? _navList;
    private StackPanel? _searchPanel;
    private Border? _narrowSearchHost;
    private TextBox? _settingsSearchBox;
    private StackPanel? _searchResultsPanel;
    private readonly List<SettingsSection> _sections = new();
    private string? _highlightedSectionKey;
    private bool _isNarrowLayout;

    private DispatcherQueueTimer? _scrollSpyTimer;
    private DispatcherQueueTimer? _searchDebounceTimer;
    private DispatcherQueueTimer? _cardFlashTimer;
    private Border? _flashCard;
    private Brush? _flashOriginalBrush;

    // Highlight pinned to a section after a jump (so a section that can't reach the top stays highlighted)
    private string? _pinnedSectionKey;
    private double _pinnedOffset = double.NaN;
    private bool _navAnimating;

    // Search mode
    private string _appliedSearchQuery = "";
    private readonly Dictionary<UIElement, Visibility> _searchHiddenElements = new();
    private string? _preSearchHighlightKey;
    private double _preSearchOffset;

    // Last-section restore on open (re-applied once the page has finished populating)
    private string? _restoreKey;
    private double _restoreOffset = double.NaN;

    // Created on first use (UI thread), not in a static initializer, so the pure static helpers
    // (Excerpt, countdown formatting) stay callable from unit tests without the WinUI runtime.
    private static SolidColorBrush? s_transparent;
    private static SolidColorBrush s_transparentBrush => s_transparent ??= new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private bool IsSettingsSearchActive => _appliedSearchQuery.Length > 0;

    // ── Setup (once) ──────────────────────────────────────────────────────────

    /// <summary>
    /// One-time setup on the first Settings open: wraps the settings ScrollViewer in a two-column Grid
    /// with the nav column (search + jump list) on the left, outside the ScrollViewer. Idempotent.
    /// </summary>
    private void EnsureSettingsNavigation()
    {
        if (_navSetupDone) return;
        _navSetupDone = true;
        try
        {
            // Found by searching down from SettingsPanel: FrameworkElement.Parent can be null here
            // (it was on the first open), so don't climb up from SettingsCardsPanel.
            var cardsPanel = _window.SettingsCardsPanel;
            var host = _window.SettingsPanel;
            var sv = host.Children.OfType<ScrollViewer>()
                .FirstOrDefault(s => ReferenceEquals(s.Content, cardsPanel));
            if (sv == null)
            {
                CrashReporter.Log("[SettingsHandler.EnsureSettingsNavigation] Unexpected Settings layout — navigation not added");
                return;
            }

            // 1. Discover sections and build their jump buttons
            var buttons = new List<Button>();
            foreach (var child in cardsPanel.Children)
            {
                if (child is not StackPanel sp || sp.Tag is not string tag
                    || !tag.StartsWith(SectionTagPrefix, StringComparison.Ordinal)) continue;
                var key = tag.Substring(SectionTagPrefix.Length);
                var title = GetSectionTitle(sp) ?? key;
                var btn = CreateNavButton(title, key);
                _sections.Add(new SettingsSection(key, sp, title, btn));
                buttons.Add(btn);
            }
            if (_sections.Count == 0)
            {
                CrashReporter.Log("[SettingsHandler.EnsureSettingsNavigation] No Section:* panels found — navigation not added");
                return;
            }

            // 2. Search box + label
            _settingsSearchBox = new TextBox
            {
                PlaceholderText = "e.g. hotkey, HDR, ReBAR",
                FontSize = 13,
                MinHeight = 34,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.SurfaceInputBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_settingsSearchBox, "Find a setting");
            _settingsSearchBox.TextChanged += SettingsSearchBox_TextChanged;
            _settingsSearchBox.KeyDown += SettingsSearchBox_KeyDown;
            _searchPanel = new StackPanel { Spacing = 4 };
            _searchPanel.Children.Add(new TextBlock
            {
                Text = "Find a setting",
                FontSize = 11,
                Margin = new Thickness(4, 0, 4, 0),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });
            _searchPanel.Children.Add(_settingsSearchBox);

            // 3. Nav column: search on top, then the jump list. Fixed width, right-aligned in its
            //    column so it stays next to the sections when the window is wider than the block.
            _navList = new StackPanel { Spacing = 2, Padding = new Thickness(24, 24, 0, 24) };
            _searchPanel.Margin = new Thickness(0, 0, 0, 10);
            _navList.Children.Add(_searchPanel);
            foreach (var b in buttons) _navList.Children.Add(b);
            _navBorder = new Border
            {
                Width = NavColumnWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _navList,
                },
            };
            Grid.SetColumn(_navBorder, 0);
            Grid.SetRow(_navBorder, 0);
            Grid.SetRowSpan(_navBorder, 2);

            // Narrow-window host for the search box (above the sections, outside the ScrollViewer)
            _narrowSearchHost = new Border
            {
                Padding = new Thickness(24, 16, 24, 0),
                MaxWidth = SettingsContentMaxWidth,
                Visibility = Visibility.Collapsed,
            };
            Grid.SetColumn(_narrowSearchHost, 1);
            Grid.SetRow(_narrowSearchHost, 0);

            // 4. Search results panel (first child of SettingsCardsPanel, hidden until a query is typed)
            _searchResultsPanel = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };

            // 5. Timers
            var dq = _window.DispatcherQueue;
            _scrollSpyTimer = dq.CreateTimer();
            _scrollSpyTimer.Interval = TimeSpan.FromMilliseconds(100);
            _scrollSpyTimer.IsRepeating = false;
            _scrollSpyTimer.Tick += (_, _) => UpdateSectionHighlight();
            _searchDebounceTimer = dq.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
            _searchDebounceTimer.IsRepeating = false;
            _searchDebounceTimer.Tick += (_, _) => ApplySettingsSearch(_settingsSearchBox?.Text ?? "");
            _cardFlashTimer = dq.CreateTimer();
            _cardFlashTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _cardFlashTimer.IsRepeating = false;
            _cardFlashTimer.Tick += (_, _) => EndCardFlash();

            // 6. Re-parent: host Grid row → new root Grid [nav | ScrollViewer]. Everything above is built
            //    first so a failure can't leave the page without its ScrollViewer.
            var root = new Grid();
            _navColumn = new ColumnDefinition { Width = new GridLength(NavColumnWidth) };
            root.ColumnDefinitions.Add(_navColumn);
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(root, Grid.GetRow(sv));

            int index = host.Children.IndexOf(sv);
            host.Children.RemoveAt(index);
            Grid.SetColumn(sv, 1);
            Grid.SetRow(sv, 1);
            root.Children.Add(_navBorder);
            root.Children.Add(_narrowSearchHost);
            root.Children.Add(sv);
            host.Children.Insert(index, root);
            _settingsScroll = sv;

            cardsPanel.Children.Insert(0, _searchResultsPanel);

            // Sections sit directly right of the nav column (centring is done by widening that column)
            cardsPanel.HorizontalAlignment = HorizontalAlignment.Left;
            cardsPanel.MaxWidth = SettingsContentMaxWidth;
            cardsPanel.Padding = new Thickness(28, 24, 24, 40);

            // 7. Events (attached once)
            sv.ViewChanged += SettingsScroll_ViewChanged;
            root.SizeChanged += (_, e) => UpdateSettingsNarrowLayout(e.NewSize.Width);

            CrashReporter.Log($"[SettingsHandler.EnsureSettingsNavigation] Settings navigation added ({_sections.Count} sections)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureSettingsNavigation] Failed — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static string? GetSectionTitle(StackPanel section)
    {
        if (section.Children.Count > 0 && section.Children[0] is StackPanel header)
            foreach (var c in header.Children)
                if (c is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                    return tb.Text.Trim();
        return null;
    }

    private Button CreateNavButton(string title, string key)
    {
        var btn = new Button
        {
            Content = title,
            Tag = key,
            FontSize = 14,
            MinHeight = 40,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        ApplyNavButtonStyle(btn, active: false);
        btn.Click += (_, _) =>
        {
            try { JumpToSection(key, animate: true, saveAsLast: true); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.NavButton_Click] '{key}' failed — {ex.Message}"); }
        };
        return btn;
    }

    private static void ApplyNavButtonStyle(Button btn, bool active)
    {
        btn.Background = active ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : s_transparentBrush;
        btn.BorderBrush = active ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : s_transparentBrush;
        btn.Foreground = UIFactory.Brush(active ? ResourceKeys.TextPrimaryBrush : ResourceKeys.TextSecondaryBrush);
    }

    // ── Open / leave / shutdown ───────────────────────────────────────────────

    /// <summary>Called from SettingsButton_Click right after the page is made visible.</summary>
    private void OnSettingsPageOpened(bool wasAlreadyOpen)
    {
        EnsureSettingsPageHooks();
        EnsureSettingsNavigation();
        StartUpdateCountdown();
        if (wasAlreadyOpen || _settingsScroll == null) return; // keep the current position

        // Always start with the search cleared
        ExitSettingsSearch(clearBox: true, restoreScroll: false);
        _pinnedSectionKey = null;
        _navAnimating = false;
        _restoreOffset = double.NaN;
        _restoreKey = ViewModel.Settings.SettingsLastSection;
        if (string.IsNullOrEmpty(_restoreKey) || FindSection(_restoreKey) == null)
            _restoreKey = _sections.Count > 0 ? _sections[0].Key : null;
        SetHighlightedSection(_restoreKey);
        ScheduleRestoreScroll(0);
    }

    private bool _pageHooksDone;

    /// <summary>
    /// Leave/shutdown hooks, registered once and independently of the navigation setup, so the
    /// countdown timer still stops when leaving Settings even if the jump list couldn't be built.
    /// </summary>
    private void EnsureSettingsPageHooks()
    {
        if (_pageHooksDone) return;
        _pageHooksDone = true;
        try
        {
            _window.SettingsPanel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
            {
                if (_window.SettingsPanel.Visibility == Visibility.Collapsed) OnSettingsPageLeft();
            });
            _window.LifetimeToken.Register(OnSettingsWindowShuttingDown);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureSettingsPageHooks] Failed — {ex.Message}");
        }
    }

    /// <summary>Called at the end of SettingsButton_Click once every combo (some async) is populated.
    /// Re-applies the last-section scroll if the user hasn't scrolled since, because populating can
    /// change the height of content above the target section.</summary>
    private void OnSettingsPagePopulated()
    {
        _window.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (_settingsScroll == null || _restoreKey == null || double.IsNaN(_restoreOffset)) return;
                if (IsSettingsSearchActive || _window.SettingsPanel.Visibility != Visibility.Visible) return;
                if (Math.Abs(_settingsScroll.VerticalOffset - _restoreOffset) < 2)
                    JumpToSection(_restoreKey, animate: false, saveAsLast: false);
                _restoreKey = null;
                _restoreOffset = double.NaN;
            }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.OnSettingsPagePopulated] {ex.Message}"); }
        });
    }

    private void ScheduleRestoreScroll(int attempt)
    {
        _window.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try
            {
                var sv = _settingsScroll;
                var key = _restoreKey;
                if (sv == null || key == null || !double.IsNaN(_restoreOffset)) return;
                if (_window.SettingsPanel.Visibility != Visibility.Visible || IsSettingsSearchActive) return;
                if (sv.ViewportHeight <= 0 || sv.ExtentHeight <= 0)
                {
                    if (attempt < 10) ScheduleRestoreScroll(attempt + 1); // layout not ready yet
                    return;
                }
                JumpToSection(key, animate: false, saveAsLast: false);
                _restoreOffset = _pinnedOffset;
            }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.ScheduleRestoreScroll] {ex.Message}"); }
        });
    }

    /// <summary>Settings page hidden (Back, About/FAQ, or any page change).</summary>
    private void OnSettingsPageLeft()
    {
        try
        {
            StopUpdateCountdown();
            ExitSettingsSearch(clearBox: true, restoreScroll: false);
            EndCardFlash();
            _restoreKey = null;
            _restoreOffset = double.NaN;
            if (_highlightedSectionKey != null) PersistLastSection(_highlightedSectionKey);
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.OnSettingsPageLeft] {ex.Message}"); }
    }

    /// <summary>Window closing: stop timers; remember the section if Settings is open (saved by the shutdown flush).</summary>
    private void OnSettingsWindowShuttingDown()
    {
        try
        {
            StopUpdateCountdown();
            _scrollSpyTimer?.Stop();
            _searchDebounceTimer?.Stop();
            _cardFlashTimer?.Stop();
            if (_window.SettingsPanel.Visibility == Visibility.Visible && !IsSettingsSearchActive
                && _highlightedSectionKey != null)
                ViewModel.Settings.SettingsLastSection = _highlightedSectionKey;
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.OnSettingsWindowShuttingDown] {ex.Message}"); }
    }

    private void PersistLastSection(string key)
    {
        if (string.Equals(ViewModel.Settings.SettingsLastSection, key, StringComparison.Ordinal)) return;
        ViewModel.Settings.SettingsLastSection = key;
        ViewModel.SaveSettingsPublic();
    }

    // ── Narrow layout ─────────────────────────────────────────────────────────

    private void UpdateSettingsNarrowLayout(double width)
    {
        if (_navColumn == null || _navBorder == null || _narrowSearchHost == null
            || _searchPanel == null || _navList == null || width <= 0) return;
        bool narrow = width < NavColumnWidth + MinSettingsContentWidth;

        // Wide window: widen the nav column by half the spare width so nav + sections are centred
        var cardsPanel = _window.SettingsCardsPanel;
        if (!narrow)
        {
            double spare = Math.Max(0, width - NavColumnWidth - SettingsContentMaxWidth);
            _navColumn.Width = new GridLength(NavColumnWidth + Math.Floor(spare / 2));
        }
        cardsPanel.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        cardsPanel.Padding = narrow ? new Thickness(24, 16, 24, 40) : new Thickness(28, 24, 24, 40);

        if (narrow == _isNarrowLayout) return;
        _isNarrowLayout = narrow;

        bool hadFocus = _settingsSearchBox != null && _settingsSearchBox.FocusState != FocusState.Unfocused;
        DetachFromParent(_searchPanel);
        if (narrow)
        {
            _searchPanel.Margin = new Thickness(0);
            _narrowSearchHost.Child = _searchPanel;
            _narrowSearchHost.Visibility = Visibility.Visible;
            _navBorder.Visibility = Visibility.Collapsed;
            _navColumn.Width = new GridLength(0);
        }
        else
        {
            _narrowSearchHost.Visibility = Visibility.Collapsed;
            _searchPanel.Margin = new Thickness(0, 0, 0, 10);
            _navList.Children.Insert(0, _searchPanel);
            _navBorder.Visibility = Visibility.Visible;
        }
        if (hadFocus && _settingsSearchBox != null)
        {
            var box = _settingsSearchBox;
            _window.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                box.Focus(FocusState.Programmatic);
                box.SelectionStart = box.Text.Length;
            });
        }
    }

    private static void DetachFromParent(FrameworkElement el)
    {
        switch (el.Parent)
        {
            case Panel p: p.Children.Remove(el); break;
            case Border b: b.Child = null; break;
        }
    }

    // ── Jump list + scroll spy ────────────────────────────────────────────────

    private SettingsSection? FindSection(string? key)
        => key == null ? null : _sections.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Y of <paramref name="el"/> inside the ScrollViewer content, or null if it isn't laid out.</summary>
    private double? GetTopInSettingsContent(UIElement el)
    {
        try
        {
            return el.TransformToVisual(_window.SettingsCardsPanel).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        }
        catch (ArgumentException) { return null; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    private void JumpToSection(string key, bool animate, bool saveAsLast)
    {
        var sv = _settingsScroll;
        var section = FindSection(key);
        if (sv == null || section == null) return;

        if (IsSettingsSearchActive || (_settingsSearchBox?.Text.Length ?? 0) > 0)
        {
            ExitSettingsSearch(clearBox: true, restoreScroll: false);
            _window.SettingsCardsPanel.UpdateLayout();
        }
        if (saveAsLast)
        {
            // A user jump cancels any pending open-time restore
            _restoreKey = null;
            _restoreOffset = double.NaN;
        }

        var firstVisible = _sections.FirstOrDefault(s => s.Panel.Visibility == Visibility.Visible);
        double target = ReferenceEquals(section, firstVisible) ? 0 : (GetTopInSettingsContent(section.Panel) ?? 0) - 16;
        target = Math.Clamp(target, 0, Math.Max(0, sv.ScrollableHeight));

        _pinnedSectionKey = section.Key;
        _pinnedOffset = target;
        _navAnimating = animate && Math.Abs(sv.VerticalOffset - target) >= 1;
        if (!sv.ChangeView(null, target, null, !animate)) _navAnimating = false;
        SetHighlightedSection(section.Key);
        if (saveAsLast) PersistLastSection(section.Key);
    }

    private void SettingsScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            _navAnimating = false;
            _scrollSpyTimer?.Stop();
            UpdateSectionHighlight();
            return;
        }
        if (_scrollSpyTimer is { IsRunning: false }) _scrollSpyTimer.Start();
    }

    private void UpdateSectionHighlight()
    {
        try
        {
            var sv = _settingsScroll;
            if (sv == null || _sections.Count == 0 || IsSettingsSearchActive) return;
            if (_window.SettingsPanel.Visibility != Visibility.Visible) return;

            if (_pinnedSectionKey != null)
            {
                if (_navAnimating || (!double.IsNaN(_pinnedOffset) && Math.Abs(sv.VerticalOffset - _pinnedOffset) < 2))
                {
                    SetHighlightedSection(_pinnedSectionKey);
                    return;
                }
                _pinnedSectionKey = null; // user scrolled away
            }

            var visible = _sections.Where(s => s.Panel.Visibility == Visibility.Visible).ToList();
            if (visible.Count == 0) return;
            double offset = sv.VerticalOffset;
            string key = visible[0].Key;
            if (sv.ScrollableHeight > 0 && offset >= sv.ScrollableHeight - 2)
            {
                key = visible[^1].Key; // at the very bottom
            }
            else
            {
                double line = offset + sv.ViewportHeight / 3;
                foreach (var s in visible)
                {
                    var top = GetTopInSettingsContent(s.Panel);
                    if (top == null) continue;
                    if (top.Value <= line) key = s.Key;
                    else break;
                }
            }
            SetHighlightedSection(key);
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.UpdateSectionHighlight] {ex.Message}"); }
    }

    private void SetHighlightedSection(string? key)
    {
        if (string.Equals(_highlightedSectionKey, key, StringComparison.Ordinal) && key != null) return;
        _highlightedSectionKey = key;
        foreach (var s in _sections)
            ApplyNavButtonStyle(s.Button, key != null && string.Equals(s.Key, key, StringComparison.Ordinal));
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer?.Start();
    }

    private void SettingsSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_settingsSearchBox == null) return;
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (_settingsSearchBox.Text.Length > 0 || IsSettingsSearchActive)
            {
                ExitSettingsSearch(clearBox: true, restoreScroll: true);
                e.Handled = true;
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounceTimer?.Stop();
            ApplySettingsSearch(_settingsSearchBox.Text);
            e.Handled = true;
        }
    }

    /// <summary>Called by the dev Freeze Diagnostics card code after it appends its card, so a search
    /// that is already running keeps it hidden.</summary>
    private void HideNewCardWhileSearching(UIElement card)
    {
        if (!IsSettingsSearchActive || _searchHiddenElements.ContainsKey(card)) return;
        _searchHiddenElements[card] = card.Visibility;
        card.Visibility = Visibility.Collapsed;
    }

    private void ApplySettingsSearch(string rawQuery)
    {
        try
        {
            var sv = _settingsScroll;
            if (sv == null || _searchResultsPanel == null) return;
            var q = System.Text.RegularExpressions.Regex.Replace(rawQuery ?? "", @"\s+", " ").Trim();
            if (string.Equals(q, _appliedSearchQuery, StringComparison.Ordinal)) return;
            if (q.Length == 0) { ExitSettingsSearch(clearBox: false, restoreScroll: true); return; }

            bool entering = !IsSettingsSearchActive;
            if (entering)
            {
                _preSearchHighlightKey = _highlightedSectionKey;
                _preSearchOffset = sv.VerticalOffset;
                _restoreKey = null;
                _restoreOffset = double.NaN;
                _pinnedSectionKey = null;
                EndCardFlash();
            }

            var entries = BuildSettingsSearchIndex();
            _appliedSearchQuery = q;

            // Hide every section (and Freeze Diagnostics / the hidden vestigial panel) — remember originals
            foreach (var child in _window.SettingsCardsPanel.Children)
            {
                if (ReferenceEquals(child, _searchResultsPanel) || _searchHiddenElements.ContainsKey(child)) continue;
                _searchHiddenElements[child] = child.Visibility;
                child.Visibility = Visibility.Collapsed;
            }

            RenderSearchResults(q, MatchSearchEntries(entries, q));
            _searchResultsPanel.Visibility = Visibility.Visible;
            SetHighlightedSection(null);
            if (entering) sv.ChangeView(null, 0, null, true);
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.ApplySettingsSearch] {ex.Message}\n{ex.StackTrace}"); }
    }

    private void ExitSettingsSearch(bool clearBox, bool restoreScroll)
    {
        _searchDebounceTimer?.Stop();
        if (clearBox && _settingsSearchBox != null && _settingsSearchBox.Text.Length > 0)
            _settingsSearchBox.Text = ""; // TextChanged → debounce → "" equals applied query → no-op
        if (!IsSettingsSearchActive && _searchHiddenElements.Count == 0) return;

        _appliedSearchQuery = "";
        foreach (var kv in _searchHiddenElements) kv.Key.Visibility = kv.Value;
        _searchHiddenElements.Clear();
        if (_searchResultsPanel != null)
        {
            _searchResultsPanel.Visibility = Visibility.Collapsed;
            _searchResultsPanel.Children.Clear();
        }
        SetHighlightedSection(_preSearchHighlightKey);
        if (restoreScroll && _settingsScroll != null)
        {
            _window.SettingsCardsPanel.UpdateLayout();
            _settingsScroll.ChangeView(null, _preSearchOffset, null, true);
        }
    }

    /// <summary>Walks every visible card and collects searchable texts (labels, button text, tooltips).</summary>
    private List<SearchEntry> BuildSettingsSearchIndex()
    {
        var list = new List<SearchEntry>();
        bool WasHidden(UIElement el)
            => (_searchHiddenElements.TryGetValue(el, out var v) ? v : el.Visibility) == Visibility.Collapsed;

        foreach (var s in _sections)
        {
            if (WasHidden(s.Panel)) continue;
            for (int i = 1; i < s.Panel.Children.Count; i++) // [0] = section heading
                IndexSettingsNode(s.Panel.Children[i], new IndexContext(s), list);
        }

        // Cards appended in C# outside the sections (dev-only Freeze Diagnostics) count as the last section
        var last = _sections[^1];
        foreach (var child in _window.SettingsCardsPanel.Children)
        {
            if (ReferenceEquals(child, _searchResultsPanel) || WasHidden(child)) continue;
            if (child is StackPanel sp && sp.Tag is string tag && tag.StartsWith(SectionTagPrefix, StringComparison.Ordinal)) continue;
            IndexSettingsNode(child, new IndexContext(last), list, isRoot: true);
        }
        return list;
    }

    private void IndexSettingsNode(DependencyObject? node, IndexContext ctx, List<SearchEntry> list, bool isRoot = false)
    {
        if (node is UIElement ue && !isRoot && (ue.Visibility == Visibility.Collapsed || ue.Opacity <= 0)) return;
        if (node is Border cardCandidate && ctx.Card == null) ctx.Card = cardCandidate;

        var tip = GetTooltipText(node);
        string ContextFor(string? sub) => sub == null ? ctx.Section.Title : $"{ctx.Section.Title} › {sub}";
        void Add(string label, FrameworkElement el, string context)
            => list.Add(new SearchEntry(label, tip, el, ctx.Card, ctx.Section.Key, context));

        switch (node)
        {
            case TextBlock tb:
            {
                var text = NormalizeSearchText(GetTextBlockText(tb));
                if (text.Length == 0) return;
                bool isTitle = tb.FontSize >= 14 && tb.FontWeight.Weight >= 600;
                bool isSub = !isTitle && tb.FontSize >= 13 && tb.FontWeight.Weight >= 600;
                string context;
                if (isTitle && ctx.CardTitle == null)
                {
                    ctx.CardTitle = text;
                    ctx.SubHeading = null;
                    context = ContextFor(null);
                }
                else if (isTitle || isSub)
                {
                    ctx.SubHeading = text;
                    context = ContextFor(ctx.CardTitle);
                }
                else context = ContextFor(ctx.SubHeading ?? ctx.CardTitle);
                Add(text, tb, context);
                if (text.Length <= 60) ctx.LastLabel = text;
                return;
            }
            case RichTextBlock rtb:
            {
                var text = NormalizeSearchText(CollectText(rtb));
                if (text.Length > 0) Add(text, rtb, ContextFor(ctx.SubHeading ?? ctx.CardTitle));
                return;
            }
            case ButtonBase btn:
            {
                string? label = btn.Content switch
                {
                    string s => s,
                    DependencyObject d => CollectText(d),
                    _ => null,
                };
                label = NormalizeSearchText(label);
                if (!label.Any(char.IsLetterOrDigit)) label = ctx.LastLabel ?? ""; // glyph-only button (⚙ ↺ ⓘ)
                if (label.Length > 0) Add(label, btn, ContextFor(ctx.SubHeading ?? ctx.CardTitle));
                return;
            }
            case ComboBox or TextBox or ToggleSwitch or PasswordBox or Slider:
            {
                var header = node switch
                {
                    ComboBox c => c.Header as string,
                    TextBox t => t.Header as string,
                    ToggleSwitch t => t.Header as string,
                    PasswordBox p => p.Header as string,
                    Slider s => s.Header as string,
                    _ => null,
                };
                var label = NormalizeSearchText(header);
                if (label.Length == 0) label = ctx.LastLabel ?? "";
                if (label.Length > 0 && (tip != null || header != null))
                    Add(label, (FrameworkElement)node, ContextFor(ctx.SubHeading ?? ctx.CardTitle));
                return; // never index item lists / templates
            }
            case Panel p:
                if (tip != null) AddContainerTip(p);
                foreach (var c in p.Children) IndexSettingsNode(c, ctx, list);
                return;
            case Border b:
                if (tip != null) AddContainerTip(b);
                IndexSettingsNode(b.Child, ctx, list);
                return;
            case Viewbox vb:
                IndexSettingsNode(vb.Child, ctx, list);
                return;
            case ContentControl cc:
                if (cc.Content is DependencyObject inner) IndexSettingsNode(inner, ctx, list);
                else if (cc.Content is string str && NormalizeSearchText(str).Length > 0)
                    Add(NormalizeSearchText(str), cc, ContextFor(ctx.SubHeading ?? ctx.CardTitle));
                return;
        }

        void AddContainerTip(FrameworkElement container)
        {
            var label = NormalizeSearchText(CollectText(container));
            if (label.Length > 60) label = label[..60].TrimEnd() + "…";
            if (label.Length > 0) Add(label, container, ContextFor(ctx.SubHeading ?? ctx.CardTitle));
        }
    }

    private static List<(SearchEntry Entry, string Display)> MatchSearchEntries(List<SearchEntry> entries, string q)
    {
        var results = new List<(SearchEntry, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            bool inLabel = e.Label.Contains(q, StringComparison.OrdinalIgnoreCase);
            bool inTip = !inLabel && e.Tooltip != null && e.Tooltip.Contains(q, StringComparison.OrdinalIgnoreCase);
            if (!inLabel && !inTip) continue;
            // One row per label/control: the label TextBlock (earlier in the tree) wins over its control's tooltip
            if (!seen.Add(e.Context + "\u001F" + e.Label)) continue;
            var display = inLabel
                ? Excerpt(e.Label, q, 160)
                : $"{Excerpt(e.Label, e.Label, 60)} — {Excerpt(e.Tooltip!, q, 110)}";
            results.Add((e, display));
        }
        return results;
    }

    /// <summary>Shortens <paramref name="text"/> to about <paramref name="max"/> chars around the first match.</summary>
    internal static string Excerpt(string text, string q, int max)
    {
        if (text.Length <= max) return text;
        int idx = Math.Max(0, text.IndexOf(q, StringComparison.OrdinalIgnoreCase));
        int start = Math.Max(0, Math.Min(idx - max / 3, text.Length - max));
        var part = text.Substring(start, max).Trim();
        return (start > 0 ? "…" : "") + part + (start + max < text.Length ? "…" : "");
    }

    private void RenderSearchResults(string q, List<(SearchEntry Entry, string Display)> matches)
    {
        var panel = _searchResultsPanel!;
        panel.Children.Clear();
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = "Search results",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        string countText = matches.Count switch
        {
            0 => $"No settings match \"{q}\"",
            1 => $"1 setting matches \"{q}\"",
            _ => $"{matches.Count} settings match \"{q}\"",
        };
        if (matches.Count > MaxSearchResults) countText += $". Showing the first {MaxSearchResults}; keep typing to narrow it down.";
        header.Children.Add(new TextBlock
        {
            Text = countText,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        panel.Children.Add(header);
        if (matches.Count == 0) return;

        var list = new StackPanel { Spacing = 2 };
        foreach (var (entry, display) in matches.Take(MaxSearchResults))
        {
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock
            {
                Text = display,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            content.Children.Add(new TextBlock
            {
                Text = entry.Context,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            });
            var row = new Button
            {
                Content = content,
                Background = s_transparentBrush,
                BorderBrush = s_transparentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            ToolTipService.SetToolTip(row, "Go to this setting");
            var captured = entry;
            row.Click += (_, _) =>
            {
                try { GoToSearchResult(captured); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.SearchResult_Click] {ex.Message}"); }
            };
            list.Children.Add(row);
        }
        panel.Children.Add(new Border
        {
            Background = UIFactory.Brush(ResourceKeys.SurfaceRaisedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = list,
        });
    }

    private void GoToSearchResult(SearchEntry entry)
    {
        var sv = _settingsScroll;
        if (sv == null) return;
        // Park focus on the search box first: the clicked row is about to be removed
        _settingsSearchBox?.Focus(FocusState.Programmatic);
        ExitSettingsSearch(clearBox: true, restoreScroll: false);
        _window.SettingsCardsPanel.UpdateLayout();

        var y = GetTopInSettingsContent(entry.Element)
                ?? (entry.Card != null ? GetTopInSettingsContent(entry.Card) : null);
        if (y == null) return;
        double target = Math.Clamp(y.Value - 80, 0, Math.Max(0, sv.ScrollableHeight));
        _pinnedSectionKey = entry.SectionKey;
        _pinnedOffset = target;
        _navAnimating = false;
        sv.ChangeView(null, target, null, true);
        SetHighlightedSection(entry.SectionKey);
        FlashCard(entry.Card);
    }

    private void FlashCard(Border? card)
    {
        EndCardFlash();
        if (card == null || _cardFlashTimer == null) return;
        _flashCard = card;
        _flashOriginalBrush = card.BorderBrush;
        card.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
        _cardFlashTimer.Start();
    }

    private void EndCardFlash()
    {
        _cardFlashTimer?.Stop();
        if (_flashCard == null) return;
        _flashCard.BorderBrush = _flashOriginalBrush;
        _flashCard = null;
        _flashOriginalBrush = null;
    }

    // ── Text extraction helpers ───────────────────────────────────────────────

    private static string NormalizeSearchText(string? s)
        => string.IsNullOrWhiteSpace(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    private static string? GetTooltipText(DependencyObject? node)
    {
        if (node == null) return null;
        var tip = ToolTipService.GetToolTip(node);
        string? text = tip switch
        {
            null => null,
            string s => s,
            ToolTip tt => tt.Content switch
            {
                string s => s,
                DependencyObject d => CollectText(d),
                _ => null,
            },
            DependencyObject d => CollectText(d),
            _ => tip.ToString(),
        };
        text = NormalizeSearchText(text);
        return text.Length == 0 ? null : text;
    }

    private static string GetTextBlockText(TextBlock tb)
    {
        if (tb.Inlines.Count == 0) return tb.Text ?? "";
        var sb = new System.Text.StringBuilder();
        AppendInlines(tb.Inlines, sb);
        return sb.Length > 0 ? sb.ToString() : tb.Text ?? "";
    }

    private static void AppendInlines(InlineCollection inlines, System.Text.StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run r: sb.Append(r.Text); break;
                case LineBreak: sb.Append(' '); break;
                case Span span: AppendInlines(span.Inlines, sb); break;
            }
        }
    }

    /// <summary>Concatenates the visible text inside a small element tree (tooltips, button content).</summary>
    private static string CollectText(DependencyObject d)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(DependencyObject? n)
        {
            switch (n)
            {
                case null: return;
                case UIElement u when u.Visibility == Visibility.Collapsed: return;
                case TextBlock tb: sb.Append(GetTextBlockText(tb)).Append(' '); return;
                case RichTextBlock rtb:
                    foreach (var block in rtb.Blocks)
                        if (block is Paragraph para) { AppendInlines(para.Inlines, sb); sb.Append(' '); }
                    return;
                case Panel p: foreach (var c in p.Children) Walk(c); return;
                case Border b: Walk(b.Child); return;
                case ContentControl cc:
                    if (cc.Content is string s) sb.Append(s).Append(' ');
                    else Walk(cc.Content as DependencyObject);
                    return;
            }
        }
        Walk(d);
        return sb.ToString();
    }
}
