// SettingsHandler.Tabs.cs -- optional tabbed Settings (Settings.SettingsTabbed, the "Tabs" button beside the search
// box): one section at a time with a tab strip above it, like the Tabs layout's game page. The search box moves
// above the tabs and still searches every section; picking a result opens its tab. Off: the long list with the
// jump list on the left (SettingsHandler.Navigation.cs).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private Button? _tabsToggle;
    private Border? _searchSlot;                // holds the search box when it sits above the sections
    private Border? _settingsTabStrip;
    private Controls.WrapPanel? _settingsTabRow;
    private double _lastSettingsWidth;
    // Sections (and stray cards) the tabs hid, so turning tabs off or switching tab shows exactly those again
    private readonly HashSet<UIElement> _tabHidden = new();

    private bool SettingsTabbed => ViewModel.Settings.SettingsTabbed && _sections.Count > 0;

    /// <summary>The "Tabs" button beside the search box.</summary>
    private Button MakeTabsToggle()
    {
        var b = new Button
        {
            Content = "Tabs",
            FontSize = 13,
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        StyleTabsToggle(b);
        b.Click += (_, _) =>
        {
            try { SetSettingsTabbed(!ViewModel.Settings.SettingsTabbed); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.TabsToggle_Click] {ex.Message}"); }
        };
        return b;
    }

    private void StyleTabsToggle(Button b)
    {
        bool on = ViewModel.Settings.SettingsTabbed;
        b.Background = UIFactory.Brush(on ? ResourceKeys.AccentBlueBgBrush : ResourceKeys.SurfaceInputBrush);
        b.BorderBrush = UIFactory.Brush(on ? ResourceKeys.AccentBlueBorderBrush : ResourceKeys.BorderDefaultBrush);
        b.Foreground = UIFactory.Brush(on ? ResourceKeys.TextPrimaryBrush : ResourceKeys.TextSecondaryBrush);
        ToolTipService.SetToolTip(b, on
            ? "Settings are shown one section at a time as tabs. Click for the full list."
            : "Show one section at a time, as tabs");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, on ? "Settings tabs: on" : "Settings tabs: off");
    }

    /// <summary>The tab strip shown above the sections in tabbed mode (built once, hidden otherwise).</summary>
    private Border BuildSettingsTabStrip()
    {
        _settingsTabRow = new Controls.WrapPanel { HorizontalSpacing = 22, VerticalSpacing = 2 };
        _settingsTabStrip = new Border
        {
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _settingsTabRow,
            Visibility = Visibility.Collapsed,
        };
        return _settingsTabStrip;
    }

    private void SetSettingsTabbed(bool on)
    {
        if (ViewModel.Settings.SettingsTabbed == on) return;
        ViewModel.Settings.SettingsTabbed = on;
        ViewModel.SaveSettingsPublic();
        if (_tabsToggle != null) StyleTabsToggle(_tabsToggle);
        CrashReporter.Log($"[SettingsHandler.SetSettingsTabbed] {(on ? "tabs" : "list")}");

        var key = _highlightedSectionKey ?? _preSearchHighlightKey ?? _sections.FirstOrDefault()?.Key;
        UpdateSettingsNarrowLayout(_lastSettingsWidth);
        if (on)
        {
            if (!IsSettingsSearchActive) ShowSettingsTab(key);
        }
        else
        {
            ShowAllSettingsSections();
        }
        if (_settingsTabStrip != null) _settingsTabStrip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!IsSettingsSearchActive && key != null)
        {
            _window.SettingsCardsPanel.UpdateLayout();
            JumpToSection(key, animate: false, saveAsLast: false);
        }
    }

    /// <summary>Sections a tab can show: visible ones and the ones the tabs hid (not sections hidden for other reasons).</summary>
    private List<SettingsSection> TabbableSections()
        => _sections.Where(s => _tabHidden.Contains(s.Panel)
            // a running search hides every section: look at what it'll put back
            || (_searchHiddenElements.TryGetValue(s.Panel, out var v) ? v : s.Panel.Visibility) == Visibility.Visible).ToList();

    /// <summary>Tabbed mode: shows only <paramref name="key"/>'s section (the first one when unknown). Not during a search.</summary>
    private void ShowSettingsTab(string? key)
    {
        if (!SettingsTabbed || IsSettingsSearchActive) return;
        var tabbable = TabbableSections();
        if (tabbable.Count == 0) return;
        var section = tabbable.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) ?? tabbable[0];

        ShowAllSettingsSections();
        foreach (var s in tabbable)
        {
            if (ReferenceEquals(s, section)) continue;
            s.Panel.Visibility = Visibility.Collapsed;
            _tabHidden.Add(s.Panel);
        }
        // Cards added in C# outside the sections (none today) belong to the last section
        bool lastSelected = ReferenceEquals(section, _sections[^1]);
        foreach (var child in _window.SettingsCardsPanel.Children)
        {
            if (lastSelected || ReferenceEquals(child, _searchResultsPanel) || child.Visibility != Visibility.Visible) continue;
            if (child is StackPanel sp && sp.Tag is string tag && tag.StartsWith(SectionTagPrefix, StringComparison.Ordinal)) continue;
            child.Visibility = Visibility.Collapsed;
            _tabHidden.Add(child);
        }
        if (_settingsTabStrip != null) _settingsTabStrip.Visibility = Visibility.Visible;
        SetHighlightedSection(section.Key);
        RebuildSettingsTabs();
    }

    private void ShowAllSettingsSections()
    {
        foreach (var el in _tabHidden)
        {
            // During a search the search hides everything: change what it restores instead
            if (_searchHiddenElements.ContainsKey(el)) _searchHiddenElements[el] = Visibility.Visible;
            else el.Visibility = Visibility.Visible;
        }
        _tabHidden.Clear();
    }

    /// <summary>The tab buttons, the highlighted section's tab selected.</summary>
    private void RebuildSettingsTabs()
    {
        if (_settingsTabRow == null || !SettingsTabbed) return;
        _settingsTabRow.Children.Clear();
        foreach (var s in TabbableSections())
            _settingsTabRow.Children.Add(MakeSettingsTab(s, string.Equals(s.Key, _highlightedSectionKey, StringComparison.Ordinal)));
    }

    private Button MakeSettingsTab(SettingsSection section, bool selected)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var underline = selected ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : transparent;
        var b = new Button
        {
            Content = section.Title,
            Tag = section.Key,
            FontSize = 14,
            FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            MinHeight = 0,
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 0, 0, -1), // the underline sits on the strip's line
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Background = transparent,
            BorderBrush = underline,
            Foreground = UIFactory.Brush(selected ? ResourceKeys.TextPrimaryBrush : ResourceKeys.TextSecondaryBrush),
        };
        // Text-only tabs: no button fill on hover / press, brighter text instead
        b.Resources["ButtonBackgroundPointerOver"] = transparent;
        b.Resources["ButtonBackgroundPressed"] = transparent;
        b.Resources["ButtonBorderBrushPointerOver"] = underline;
        b.Resources["ButtonBorderBrushPressed"] = underline;
        b.Resources["ButtonForegroundPointerOver"] = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        b.Resources["ButtonForegroundPressed"] = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var key = section.Key;
        b.Click += (_, _) =>
        {
            try { JumpToSection(key, animate: false, saveAsLast: true); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.SettingsTab_Click] '{key}' failed — {ex.Message}"); }
        };
        return b;
    }

    /// <summary>The selected tab (controller start point in tabbed mode), or null.</summary>
    private Button? SelectedSettingsTab()
        => _settingsTabRow?.Children.OfType<Button>()
               .FirstOrDefault(b => string.Equals(b.Tag as string, _highlightedSectionKey, StringComparison.Ordinal) && b.IsLoaded);
}
