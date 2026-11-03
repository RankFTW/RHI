using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>The game page layouts (see docs/layouts-plan-2.9.0.md). Saved as the enum name.</summary>
public enum MainLayout
{
    Classic,
    Tabs,
    /// <summary>Mockup D: artwork hero, cover shelf, tiles that open sections in a slide-in panel. Uses the Tabs look and arrangers.</summary>
    Spotlight,
}

/// <summary>
/// Owns where the game page's sections go: which sections exist, their order, whether each is
/// collapsed, and (Tabs) which one is showing. Section builders ask the host instead of reading
/// settings directly, so every layout arranges the same section containers without copying any
/// section UI.
/// Classic: the sections stack in DetailPanel below its two header Grids, each collapsible and
/// reorderable, with collapse state and order saved in settings.
/// Tabs: a tab strip sits below the header Grids and only the chosen tab shows. The first tab is
/// Overview (component cards + "At a glance" tiles, DetailLayoutHost.Overview.cs), which replaces
/// the Components section; the other tabs are the remaining sections. Section headers (arrow,
/// title, ≡ handle, summary) are hidden and every section is expanded. Switching tab only changes
/// visibility, it never rebuilds a section.
/// </summary>
internal sealed partial class DetailLayoutHost
{
    /// <summary>Section keys, in the default order.</summary>
    internal static IReadOnlyList<string> SectionKeys => SettingsViewModel.DefaultSectionOrder;

    /// <summary>DetailPanel children before the layout's own content (the two game header Grids).</summary>
    internal const int FirstSectionIndex = 2;

    private readonly MainWindow _window;
    private readonly Dictionary<string, FrameworkElement> _headers = new(StringComparer.Ordinal);
    private Border? _tabStrip;
    private StackPanel? _tabChrome; // Tabs: mockup header + tab strip, at DetailPanel index 2
    private Controls.WrapPanel? _tabRow;
    private bool _tabsApplied; // Tabs hid sections; Classic must show them again once

    /// <summary>Key of the Overview tab (Tabs only; not a section).</summary>
    internal const string OverviewKey = "Overview";

    internal DetailLayoutHost(MainWindow window) => _window = window;

    private SettingsViewModel Settings => _window.ViewModel.Settings;

    internal MainLayout Layout =>
        Enum.TryParse<MainLayout>(Settings.MainLayout, ignoreCase: true, out var l) ? l : MainLayout.Classic;

    /// <summary>The panel that holds the section containers.</summary>
    internal StackPanel SectionsPanel => _window.DetailPanel;

    /// <summary>Maps a section key to its container Border, or null for an unknown key.</summary>
    internal Border? GetContainer(string key) => key switch
    {
        "Components"          => _window.DetailComponentSection,
        "GameOverrides"       => _window.OverridesContainer,
        "NeuralRendering"     => _window.NeuralRenderingContainer,
        "NvidiaProfileDlss"   => _window.NvidiaProfileDlssContainer,
        "NvidiaProfileDriver" => _window.NvidiaProfileDriverContainer,
        "Management"          => _window.ManagementContainer,
        "Extras"              => _window.ExtrasContainer,
        _                     => null,
    };

    /// <summary>Maps a container Border back to its section key, or null.</summary>
    internal string? GetKey(UIElement? container)
    {
        if (container == null) return null;
        foreach (var key in SectionKeys)
            if (ReferenceEquals(GetContainer(key), container)) return key;
        return null;
    }

    /// <summary>Short name for a section, used on tabs.</summary>
    internal static string GetTitle(string key) => key switch
    {
        "Components"          => "Components",
        "GameOverrides"       => "Game Overrides",
        "NeuralRendering"     => "Neural Rendering",
        "NvidiaProfileDlss"   => "DLSS / Streamline",
        "NvidiaProfileDriver" => "Driver Settings",
        "Management"          => "Management",
        "Extras"              => "Extras",
        OverviewKey           => "Overview",
        _                     => key,
    };

    // ── Section headers ───────────────────────────────────────────────────────

    /// <summary>
    /// Every section builder registers its header row (arrow, title, ≡ handle, summary) here
    /// after building it. Tabs hides it, because the tab strip names the section instead.
    /// </summary>
    internal void RegisterHeader(string key, FrameworkElement header)
    {
        _headers[key] = header;
        header.Visibility = IsTabs ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Collapse state ────────────────────────────────────────────────────────

    /// <summary>Whether the section's body should start collapsed. Tabs never collapses.</summary>
    internal bool IsCollapsed(string key) =>
        Layout == MainLayout.Classic && Settings.CollapsedDetailSections.Contains(key);

    /// <summary>Records a section being collapsed or expanded by the user and saves it.</summary>
    internal void SetCollapsed(string key, bool collapsed)
    {
        if (collapsed) Settings.CollapsedDetailSections.Add(key);
        else           Settings.CollapsedDetailSections.Remove(key);
        _window.ViewModel.SaveSettingsPublic();
    }

    // ── Showing sections ──────────────────────────────────────────────────────

    /// <summary>
    /// Shows a section that a builder has just filled outside the normal selection path
    /// (e.g. an Extras rebuild). In Tabs it stays hidden unless it's the chosen tab.
    /// </summary>
    internal void ShowSection(string key)
    {
        var container = GetContainer(key);
        if (container == null) return;
        container.Visibility = IsSpotlight ? (IsInPanel(key) ? Visibility.Visible : Visibility.Collapsed)
            : IsTabs && !IsSelectedTab(key) ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool IsSelectedTab(string key) => string.Equals(SelectedTab, key, StringComparison.Ordinal);

    /// <summary>The tab shown in Tabs. Overview replaces Components, and is the fallback.</summary>
    private string SelectedTab =>
        Settings.DetailSelectedTab is { Length: > 0 } t && t != "Components" && GetContainer(t) != null
            ? t
            : OverviewKey;

    /// <summary>Tabs in order: Overview, then every section except Components (shown by Overview).</summary>
    // Tabs has its own fixed order; Classic's drag order (DetailSectionOrder) doesn't apply
    private static readonly string[] FixedTabOrder =
        { OverviewKey, "GameOverrides", "Extras", "NvidiaProfileDlss", "NvidiaProfileDriver", "NeuralRendering", "Management" };

    private IEnumerable<string> TabKeys =>
        FixedTabOrder.Where(k => k == OverviewKey || GetContainer(k) != null);

    // ── Order ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the section containers in the saved order and applies the layout (tab strip and
    /// which section shows). Call after all sections are populated and their visibility is set;
    /// the header Grids before FirstSectionIndex stay in place.
    /// </summary>
    internal void ApplySectionOrder()
    {
        var panel = SectionsPanel;
        bool tabs = IsTabs;
        if (!IsSpotlight) ClosePanel(); // the panel only exists in Spotlight
        EnsureTabStrip(tabs);
        ApplyHeroMode(IsSpotlight);
        EnsureOverview(tabs && !IsSpotlight);
        ApplyContainerChrome(tabs);
        SetClassicHeaderVisible(!tabs);
        if (tabs) RefreshHeader();
        int start = SectionStartIndex;

        var desired = Settings.DetailSectionOrder
            .Select(GetContainer)
            .Where(b => b != null).Cast<Border>().ToList();

        // Moving an element can reset its Visibility, so snapshot and restore it
        var visibilities = new Dictionary<UIElement, Visibility>();
        for (int i = start; i < panel.Children.Count; i++)
            visibilities[panel.Children[i]] = panel.Children[i].Visibility;

        int insertAt = start;
        foreach (var border in desired)
        {
            var currentIndex = panel.Children.IndexOf(border);
            if (currentIndex < 0) continue;
            if (currentIndex == insertAt) { insertAt++; continue; }
            panel.Children.RemoveAt(currentIndex);
            panel.Children.Insert(insertAt, border);
            insertAt++;
        }

        for (int i = start; i < panel.Children.Count; i++)
            if (visibilities.TryGetValue(panel.Children[i], out var vis))
                panel.Children[i].Visibility = vis;

        foreach (var (key, header) in _headers)
            header.Visibility = tabs ? Visibility.Collapsed : Visibility.Visible;

        if (tabs)
        {
            _tabsApplied = true;
            ApplyTabSelection();
        }
        else if (_tabsApplied)
        {
            // Back from Tabs: show the sections it hid (the selection path normally does this)
            _tabsApplied = false;
            if (panel.Visibility == Visibility.Visible)
                foreach (var key in SectionKeys)
                    if (GetContainer(key) is { } c) c.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Index of the first section container (after the tab strip and Overview in Tabs).</summary>
    private int SectionStartIndex
    {
        get
        {
            int i = FirstSectionIndex;
            var children = SectionsPanel.Children;
            if (_tabChrome != null && i < children.Count && ReferenceEquals(children[i], _tabChrome)) i++;
            if (_overview != null && i < children.Count && ReferenceEquals(children[i], _overview)) i++;
            return i;
        }
    }

    /// <summary>Saves the current on-screen order of the sections (after a drag).</summary>
    internal void SaveSectionOrder()
    {
        var panel = SectionsPanel;
        var keyOrder = new List<string>();
        for (int i = FirstSectionIndex; i < panel.Children.Count; i++)
            if (GetKey(panel.Children[i]) is { } key) keyOrder.Add(key);
        if (keyOrder.Count == 0) return;
        Settings.DetailSectionOrder = keyOrder;
        _window.ViewModel.SaveSettingsPublic();
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    /// <summary>Adds the tab strip below the header Grids (Tabs) or removes it (other layouts).</summary>
    private void EnsureTabStrip(bool wanted)
    {
        var panel = SectionsPanel;
        if (!wanted)
        {
            if (_tabChrome != null && panel.Children.Contains(_tabChrome))
                panel.Children.Remove(_tabChrome);
            return;
        }

        if (_tabStrip == null)
        {
            // Mockup A: tabs wrap with 24 px between them, on a 1 px #1F2737 line
            _tabRow = new Controls.WrapPanel { HorizontalSpacing = 24, VerticalSpacing = 4 };
            _tabStrip = new Border
            {
                BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = _tabRow,
            };
        }

        if (_tabChrome == null)
        {
            _header = BuildHeader();
            _tabChrome = new StackPanel { Spacing = 20 };
            _tabChrome.Children.Add(_header);
            _tabChrome.Children.Add(_tabStrip);
        }

        int idx = panel.Children.IndexOf(_tabChrome);
        if (idx == FirstSectionIndex) return;
        if (idx >= 0) panel.Children.RemoveAt(idx);
        panel.Children.Insert(Math.Min(FirstSectionIndex, panel.Children.Count), _tabChrome);
    }

    /// <summary>Rebuilds the tab buttons in the saved order and shows only the chosen tab.</summary>
    private void ApplyTabSelection()
    {
        if (IsSpotlight) { ApplySpotlightSelection(); return; }
        if (_tabRow == null) return;
        var selected = SelectedTab;
        _tabRow.Children.Clear();
        foreach (var key in TabKeys)
            _tabRow.Children.Add(MakeTab(key, key == selected));

        // Components is never shown in Tabs: Overview shows its rows as cards
        foreach (var key in SectionKeys)
            if (GetContainer(key) is { } container)
                container.Visibility = key == selected ? Visibility.Visible : Visibility.Collapsed;

        if (_overview != null)
        {
            _overview.Visibility = selected == OverviewKey ? Visibility.Visible : Visibility.Collapsed;
            if (selected == OverviewKey) RefreshOverview();
        }
    }

    private Border MakeTab(string key, bool selected)
    {
        var label = new TextBlock
        {
            Text = GetTitle(key),
            FontSize = 14,
            FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = TabsTheme.B(selected ? TabsTheme.TextPrimary : TabsTheme.TextSecondary),
        };
        var tab = new Border
        {
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 0, 0, -1), // underline sits on the strip's line
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = selected
                ? TabsTheme.B(TabsTheme.Accent)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            // Transparent background so the whole tab is clickable, not just the text
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = label,
        };

        TabsTheme.UseHandCursor(tab);
        tab.PointerEntered += (s, e) =>
        {
            if (!selected) label.Foreground = TabsTheme.B(TabsTheme.TextPrimary);
        };
        tab.PointerExited += (s, e) =>
        {
            if (!selected) label.Foreground = TabsTheme.B(TabsTheme.TextSecondary);
        };
        tab.Tapped += (s, e) => SelectTab(key);
        return tab;
    }

    /// <summary>Shows another section. Only visibility changes; nothing is rebuilt.</summary>
    internal void SelectTab(string key)
    {
        if (IsSpotlight) { OpenPanel(key); return; }
        if (Layout != MainLayout.Tabs || IsSelectedTab(key)) return;
        if (key != OverviewKey && GetContainer(key) == null) return;
        Settings.DetailSelectedTab = key;
        _window.ViewModel.SaveSettingsPublic();
        ApplyTabSelection();
    }
}
