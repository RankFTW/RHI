// SettingsHandler.Theme.cs — Settings › General › "Colour theme" card (built in C#, XAML constraint): pick a
// built-in theme or Custom, where the user picks nine colours with colour pickers. Applied live through
// ThemeService; the theme id and the custom colours persist in settings (ColorTheme / CustomThemeColors).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _themeCombo;
    private StackPanel? _themeCustomPanel;
    private TextBlock? _themeContrastNote;
    private readonly Dictionary<string, (Border Swatch, TextBlock Hex)> _themeSwatches = new();
    private bool _themeCardFailed;
    private bool _themeSelecting;
    private ThemePalette? _pendingCustom;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _themeDragTimer;

    private void EnsureColorThemeCard()
    {
        if (_themeCombo != null) { SelectSavedTheme(); return; }
        if (_themeCardFailed) return;
        try
        {
            StackPanel? section = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string t && t == "Section:General") { section = sp; break; }
            if (section == null)
            {
                _themeCardFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureColorThemeCard] General section not found — card not added");
                return;
            }

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var outer = new StackPanel { Spacing = 10 };
            card.Child = outer;
            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock { Text = "Colour theme", Style = FindStyle("SettingsCardTitleStyle") });
            head.Children.Add(new TextBlock
            {
                Text = "Changes RHI's colours. Pick a theme, or Custom to choose your own colours. Changes show straight away.",
                Style = FindStyle("SettingsCardDescStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            outer.Children.Add(head);

            // Theme row
            var row = MakeThemeRow("Theme", out var themeCell);
            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            foreach (var p in ThemePresets.All) combo.Items.Add(p.Label);
            combo.Items.Add("Custom");
            ToolTipService.SetToolTip(combo,
                "OLED Black uses a true black background. High Contrast makes text and borders stronger. " +
                "Colour-blind Friendly uses status colours that stay apart with red-green colour blindness " +
                "(blue = installed, pink = update, yellow = warning, orange = error).");
            themeCell.Child = combo;
            outer.Children.Add(row);
            _themeCombo = combo;
            combo.SelectionChanged += ThemeCombo_SelectionChanged;

            // Custom colours: two columns with a divider, like the other cards
            _themeCustomPanel = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Spacing = 9, Margin = new Thickness(0, 0, 20, 0) };
            var right = new StackPanel { Spacing = 9, Margin = new Thickness(20, 0, 0, 0) };
            var divider = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush) };
            Grid.SetColumn(divider, 1);
            Grid.SetColumn(right, 2);
            cols.Children.Add(left);
            cols.Children.Add(divider);
            cols.Children.Add(right);

            left.Children.Add(new TextBlock { Text = "Look", Style = FindStyle("SettingsHeadStyle") });
            right.Children.Add(new TextBlock { Text = "Status colours", Style = FindStyle("SettingsHeadStyle") });
            foreach (var (key, label) in ThemePalette.CustomKeys)
            {
                bool status = key is "Installed" or "Update" or "Warning" or "Error";
                (status ? right : left).Children.Add(MakeColourRow(key, label));
            }
            _themeCustomPanel.Children.Add(cols);

            _themeContrastNote = new TextBlock
            {
                Text = "Text may be hard to read on this background. Pick a lighter text colour or a darker background.",
                Style = FindStyle("SettingsNoteStyle"),
                Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            _themeCustomPanel.Children.Add(_themeContrastNote);

            var buttons = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            buttons.Children.Add(MakeThemeButton("Start from a theme...", "Replaces your custom colours with a built-in theme's colours.",
                (btn) => ShowStartFromMenu(btn)));
            buttons.Children.Add(MakeThemeButton("Reset to default", "Sets your custom colours back to RHI's default colours.",
                _ => SetCustom(ThemePalette.Default, save: true)));
            buttons.Children.Add(MakeThemeButton("Share theme", "Copies a code for your colours. Anyone can paste it into Import theme to use them.",
                (btn) => ShowShareThemeFlyout(btn)));
            buttons.Children.Add(MakeThemeButton("Import theme...", "Paste a theme code someone shared to use their colours. Replaces your custom colours.",
                (btn) => ShowImportThemeFlyout(btn)));
            _themeCustomPanel.Children.Add(buttons);
            outer.Children.Add(_themeCustomPanel);

            section.Children.Add(card);
            SelectSavedTheme();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _themeCardFailed = true;
            _themeCombo = null;
            CrashReporter.Log($"[SettingsHandler.EnsureColorThemeCard] Failed — {ex.Message}");
        }
    }

    private Grid MakeThemeRow(string labelText, out Border cell)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
        row.Children.Add(new TextBlock { Text = labelText, Style = FindStyle("SettingsLabelStyle") });
        cell = new Border();
        Grid.SetColumn(cell, 1);
        row.Children.Add(cell);
        return row;
    }

    /// <summary>"Label | [swatch #RRGGBB]" — the button opens a colour picker; the swatch shows the real colour (not themed).</summary>
    private Grid MakeColourRow(string key, string label)
    {
        var row = MakeThemeRow(label, out var cell);
        var swatch = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
        };
        var hex = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(swatch);
        content.Children.Add(hex);
        var btn = new Button
        {
            Content = content,
            Style = FindStyle("SettingsButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        ToolTipService.SetToolTip(btn, $"Pick the {label.ToLowerInvariant()} colour.");

        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
        };
        var flyout = new Flyout { Content = picker };
        bool opening = false;
        flyout.Opening += (_, _) =>
        {
            opening = true;
            picker.Color = CurrentCustom().Get(key);
            opening = false;
        };
        picker.ColorChanged += (_, args) =>
        {
            if (opening) return;
            var c = Windows.UI.Color.FromArgb(255, args.NewColor.R, args.NewColor.G, args.NewColor.B);
            SetCustom(CurrentCustom().With(key, c), save: false);
        };
        flyout.Closed += (_, _) => SaveCustomNow();
        btn.Flyout = flyout;

        _themeSwatches[key] = (swatch, hex);
        cell.Child = btn;
        return row;
    }

    private Button MakeThemeButton(string text, string tooltip, Action<Button> click)
    {
        var btn = new Button { Content = text, Style = FindStyle("SettingsButtonStyle") };
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Click += (s, _) =>
        {
            try { click((Button)s); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                CrashReporter.Log($"[SettingsHandler.ThemeButton] {text}: {ex.Message}");
            }
        };
        return btn;
    }

    private void ShowStartFromMenu(Button anchor)
    {
        var menu = new MenuFlyout();
        foreach (var preset in ThemePresets.All)
        {
            var item = new MenuFlyoutItem { Text = preset.Label };
            var palette = preset.Palette;
            item.Click += (_, _) => SetCustom(palette, save: true);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>The custom palette being edited (unsaved drag changes first, then the saved one).</summary>
    private ThemePalette CurrentCustom() =>
        _pendingCustom ?? ThemePalette.ParseCustom(ViewModel.Settings.CustomThemeColors, ThemePalette.Default);

    private void SelectSavedTheme()
    {
        if (_themeCombo == null) return;
        _themeSelecting = true;
        try
        {
            var id = ViewModel.Settings.ColorTheme;
            int idx = string.Equals(id, ThemePresets.CustomId, StringComparison.OrdinalIgnoreCase)
                ? ThemePresets.All.Count
                : Math.Max(0, ThemePresets.All.ToList().FindIndex(p => p == ThemePresets.Find(id)));
            _themeCombo.SelectedIndex = idx;
            RefreshCustomPanel();
        }
        finally { _themeSelecting = false; }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_themeSelecting || _themeCombo == null) return;
        try
        {
            int idx = _themeCombo.SelectedIndex;
            if (idx < 0) return;
            var settings = ViewModel.Settings;
            string previous = settings.ColorTheme;
            if (idx >= ThemePresets.All.Count)
            {
                // First time on Custom: start from the theme that was showing
                if (string.IsNullOrWhiteSpace(settings.CustomThemeColors))
                    settings.CustomThemeColors = ThemePresets.Resolve(previous, null).ToCustomString();
                settings.ColorTheme = ThemePresets.CustomId;
            }
            else
            {
                settings.ColorTheme = ThemePresets.All[idx].Id;
            }
            _pendingCustom = null;
            ViewModel.SaveSettingsPublic();
            ThemeService.Apply(ThemePresets.Resolve(settings.ColorTheme, settings.CustomThemeColors));
            RefreshCustomPanel();
            CrashReporter.Log($"[SettingsHandler.ThemeCombo_SelectionChanged] Colour theme {previous} -> {settings.ColorTheme}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[SettingsHandler.ThemeCombo_SelectionChanged] {ex.Message}");
        }
    }

    /// <summary>Applies a custom palette. While a picker is dragged (<paramref name="save"/> false) changes are applied at most every 60 ms and saved when it closes.</summary>
    private void SetCustom(ThemePalette palette, bool save)
    {
        _pendingCustom = palette;
        UpdateSwatches(palette);
        if (save)
        {
            SaveCustomNow();
            return;
        }
        if (_themeDragTimer == null)
        {
            _themeDragTimer = _window.DispatcherQueue.CreateTimer();
            _themeDragTimer.Interval = TimeSpan.FromMilliseconds(60);
            _themeDragTimer.IsRepeating = false;
            _themeDragTimer.Tick += (_, _) =>
            {
                if (_pendingCustom is { } p) ThemeService.Apply(p, log: false);
            };
        }
        if (!_themeDragTimer.IsRunning) _themeDragTimer.Start();
    }

    private void SaveCustomNow()
    {
        if (_pendingCustom is not { } p) return;
        _themeDragTimer?.Stop();
        var settings = ViewModel.Settings;
        settings.CustomThemeColors = p.ToCustomString();
        settings.ColorTheme = ThemePresets.CustomId;
        _pendingCustom = null;
        ViewModel.SaveSettingsPublic();
        ThemeService.Apply(p);
        CrashReporter.Log($"[SettingsHandler.SaveCustomNow] Custom colours {settings.CustomThemeColors}");
    }

    private void RefreshCustomPanel()
    {
        if (_themeCustomPanel == null) return;
        bool custom = string.Equals(ViewModel.Settings.ColorTheme, ThemePresets.CustomId, StringComparison.OrdinalIgnoreCase);
        _themeCustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom) UpdateSwatches(CurrentCustom());
    }

    private void UpdateSwatches(ThemePalette p)
    {
        foreach (var (key, (swatch, hex)) in _themeSwatches)
        {
            var c = p.Get(key);
            // A plain brush on purpose: the swatch shows the picked colour itself, not a themed version
            swatch.Background = new SolidColorBrush(c);
            hex.Text = ThemePalette.ToHex(c);
        }
        if (_themeContrastNote != null)
        {
            bool low = ThemeColorMapper.Contrast(p.Text, p.Background) < 4.5
                    || ThemeColorMapper.Contrast(p.Text, p.Panel) < 4.5;
            _themeContrastNote.Visibility = low ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
