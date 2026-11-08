// SettingsHandler.Overlay.cs -- the "RHI overlay" section of Settings (before Tools & Maintenance, after
// Achievements): on / off and the hotkey. Built in C# (XAML constraint), inserted the first time Settings
// opens, before the section navigation is built so it gets a jump button. See docs/rhi-overlay-plan.md.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _rhiOverlayCombo;
    private ComboBox? _rhiOverlayPadCombo;
    private ComboBox? _rhiOverlayPauseCombo;
    private TextBox? _rhiOverlayHotkeyBox;
    private TextBlock? _rhiOverlayStatus;
    private bool _rhiOverlaySectionFailed;

    private void EnsureRhiOverlaySection()
    {
        if (_rhiOverlayCombo != null)
        {
            _rhiOverlayCombo.SelectedIndex = ViewModel.Settings.RhiOverlayEnabled ? 1 : 0;
            if (_rhiOverlayPadCombo != null) _rhiOverlayPadCombo.SelectedIndex = ViewModel.Settings.RhiOverlayController ? 1 : 0;
            if (_rhiOverlayPauseCombo != null) _rhiOverlayPauseCombo.SelectedIndex = ViewModel.Settings.RhiOverlayPauseGame ? 1 : 0;
            if (_rhiOverlayHotkeyBox != null) _rhiOverlayHotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(ViewModel.Settings.RhiOverlayHotkey);
            UpdateRhiOverlayStatus();
            return;
        }
        if (_rhiOverlaySectionFailed) return;
        try
        {
            var panel = _window.SettingsCardsPanel;
            int toolsIndex = -1;
            for (int i = 0; i < panel.Children.Count; i++)
                if (panel.Children[i] is StackPanel sp && sp.Tag is string t && t == "Section:Tools") { toolsIndex = i; break; }
            if (toolsIndex < 0)
            {
                _rhiOverlaySectionFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureRhiOverlaySection] Tools section not found — section not added");
                return;
            }

            var section = new StackPanel { Tag = "Section:RhiOverlay", Spacing = 12 };
            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock
            {
                Text = "RHI overlay",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            head.Children.Add(new TextBlock
            {
                Text = "A full-screen RHI page over your game, opened with a key or a controller: achievements, screenshots, your notes, "
                     + "playtime and the game's mod setup. Works while RHI is running, including from the tray.",
                FontSize = 12,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });
            section.Children.Add(head);

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var body = new StackPanel { Spacing = 12 };
            card.Child = body;
            body.Children.Add(new TextBlock { Text = "Overlay", Style = FindStyle("SettingsCardTitleStyle") });

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Spacing = 9, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top };
            var divider = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush) };
            var right = new StackPanel { Spacing = 9, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(divider, 1);
            Grid.SetColumn(right, 2);
            columns.Children.Add(left);
            columns.Children.Add(divider);
            columns.Children.Add(right);

            // Left: on / off
            var onRow = MakeSettingRow("RHI overlay", out var onCell);
            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            combo.Items.Add(new ComboBoxItem { Content = "Off" });
            combo.Items.Add(new ComboBoxItem { Content = "On" });
            combo.SelectedIndex = ViewModel.Settings.RhiOverlayEnabled ? 1 : 0;
            ToolTipService.SetToolTip(combo,
                "Press the hotkey in a game to open RHI over it; press it again, or Esc, to go back.\n"
              + "Nothing is installed into the game. Works in borderless and windowed games and in games using "
              + "Windows' fullscreen optimisations; it can't show over true exclusive fullscreen.");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, "RHI overlay");
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedIndex < 0) return;
                bool on = combo.SelectedIndex == 1;
                if (on == ViewModel.Settings.RhiOverlayEnabled) return;
                ViewModel.Settings.RhiOverlayEnabled = on;
                ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[SettingsHandler.RhiOverlay] {(on ? "On" : "Off")}");
                _window.ApplyRhiOverlaySetting();
                UpdateRhiOverlayStatus();
            };
            onCell.Child = combo;
            left.Children.Add(onRow);
            _rhiOverlayCombo = combo;

            var padRow = MakeSettingRow("Controller (hold View + Menu)", out var padCell);
            var padCombo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            padCombo.Items.Add(new ComboBoxItem { Content = "Off" });
            padCombo.Items.Add(new ComboBoxItem { Content = "On" });
            padCombo.SelectedIndex = ViewModel.Settings.RhiOverlayController ? 1 : 0;
            ToolTipService.SetToolTip(padCombo,
                "Hold View + Menu (Back + Start; Share + Options on PlayStation) for half a second in a game to open or close "
              + "the overlay. While it's open: D-pad to move, A to select, B to go back, LB / RB to jump between columns, right stick to scroll.");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(padCombo, "Open the RHI overlay with a controller");
            padCombo.SelectionChanged += (s, e) =>
            {
                if (padCombo.SelectedIndex < 0) return;
                bool on = padCombo.SelectedIndex == 1;
                if (on == ViewModel.Settings.RhiOverlayController) return;
                ViewModel.Settings.RhiOverlayController = on;
                ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[SettingsHandler.RhiOverlayController] {(on ? "On" : "Off")}");
                _window.ApplyRhiOverlaySetting();
            };
            padCell.Child = padCombo;
            left.Children.Add(padRow);
            _rhiOverlayPadCombo = padCombo;

            // Right: hotkey
            var keyRow = MakeSettingRow("Hotkey", out var keyCell);
            var box = new TextBox
            {
                Style = FindStyle("SettingsTextBoxStyle"),
                IsReadOnly = true,
                Text = HotkeyManager.FormatHotkeyDisplay(ViewModel.Settings.RhiOverlayHotkey),
            };
            ToolTipService.SetToolTip(box,
                "Click here, then press the key you want, with Ctrl, Alt or Shift (for example Alt + `). "
              + "Backspace puts back the default (Alt + `).");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "RHI overlay hotkey");
            box.PreviewKeyDown += RhiOverlayHotkeyBox_PreviewKeyDown;
            keyCell.Child = box;
            right.Children.Add(keyRow);
            _rhiOverlayHotkeyBox = box;

            var pauseRow = MakeSettingRow("Pause the game while open", out var pauseCell);
            var pauseCombo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            pauseCombo.Items.Add(new ComboBoxItem { Content = "Off" });
            pauseCombo.Items.Add(new ComboBoxItem { Content = "On" });
            pauseCombo.SelectedIndex = ViewModel.Settings.RhiOverlayPauseGame ? 1 : 0;
            ToolTipService.SetToolTip(pauseCombo,
                "Freezes the game while the overlay is open and unfreezes it when you go back, so the game doesn't react to "
              + "your controller while you use the overlay (Windows can't block a controller from a game any other way).\n"
              + "Experimental: online games may lose their connection, anti-cheat may object, and some games stutter for a "
              + "moment when they resume. Single-player games only.");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pauseCombo, "Pause the game while the RHI overlay is open");
            pauseCombo.SelectionChanged += (s, e) =>
            {
                if (pauseCombo.SelectedIndex < 0) return;
                bool on = pauseCombo.SelectedIndex == 1;
                if (on == ViewModel.Settings.RhiOverlayPauseGame) return;
                ViewModel.Settings.RhiOverlayPauseGame = on;
                ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[SettingsHandler.RhiOverlayPauseGame] {(on ? "On" : "Off")}");
            };
            pauseCell.Child = pauseCombo;
            right.Children.Add(pauseRow);
            _rhiOverlayPauseCombo = pauseCombo;

            body.Children.Add(columns);
            _rhiOverlayStatus = new TextBlock { Style = FindStyle("SettingsHelpStyle"), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(_rhiOverlayStatus);
            UpdateRhiOverlayStatus();
            section.Children.Add(card);

            panel.Children.Insert(toolsIndex, section);
        }
        catch (Exception ex)
        {
            _rhiOverlaySectionFailed = true;
            _rhiOverlayCombo = null;
            CrashReporter.Log($"[SettingsHandler.EnsureRhiOverlaySection] Failed — {ex.Message}");
        }
    }

    /// <summary>A Settings row: label left, the control (put in <paramref name="cell"/>) right.</summary>
    private Grid MakeSettingRow(string label, out Border cell)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
        row.Children.Add(new TextBlock { Text = label, Style = FindStyle("SettingsLabelStyle"), VerticalAlignment = VerticalAlignment.Center });
        cell = new Border();
        Grid.SetColumn(cell, 1);
        row.Children.Add(cell);
        return row;
    }

    private void UpdateRhiOverlayStatus()
    {
        if (_rhiOverlayStatus == null) return;
        var key = HotkeyManager.FormatHotkeyDisplay(ViewModel.Settings.RhiOverlayHotkey);
        if (!ViewModel.Settings.RhiOverlayEnabled)
        {
            _rhiOverlayStatus.Text = "The overlay is off.";
            _rhiOverlayStatus.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
        }
        else if (_window.RhiOverlayHotkeyActive)
        {
            _rhiOverlayStatus.Text = $"Press {key} in a game to open the overlay.";
            _rhiOverlayStatus.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
        }
        else
        {
            _rhiOverlayStatus.Text = $"Another program is using {key}. Pick a different key.";
            _rhiOverlayStatus.Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush);
        }
    }

    private void RhiOverlayHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (IsModifierKey(key) || IsHotkeyBoxPassThroughKey(key)) return;
        e.Handled = true;

        string hotkey;
        if (key == Windows.System.VirtualKey.Back)
            hotkey = SettingsViewModel.DefaultRhiOverlayHotkey;
        else
        {
            bool shift = IsKeyDown(Windows.System.VirtualKey.Shift);
            bool ctrl = IsKeyDown(Windows.System.VirtualKey.Control);
            bool alt = IsKeyDown(Windows.System.VirtualKey.Menu);
            int vk = (int)key;
            // A key on its own (or Shift + a letter) would be taken from every program while RHI runs
            bool functionKey = vk >= 112 && vk <= 135;
            if (!ctrl && !alt && !(shift && functionKey))
            {
                if (_rhiOverlayStatus != null)
                {
                    _rhiOverlayStatus.Text = "Use the key with Ctrl or Alt (or Shift with an F key), so games and other programs still get the key on its own.";
                    _rhiOverlayStatus.Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush);
                }
                return;
            }
            hotkey = HotkeyManager.BuildHotkeyString(vk, shift, ctrl, alt);
        }

        if (sender is TextBox box) box.Text = HotkeyManager.FormatHotkeyDisplay(hotkey);
        if (hotkey != ViewModel.Settings.RhiOverlayHotkey)
        {
            ViewModel.Settings.RhiOverlayHotkey = hotkey;
            ViewModel.SaveSettingsPublic();
            CrashReporter.Log($"[SettingsHandler.RhiOverlayHotkey] Saved: {hotkey}");
        }
        _window.ApplyRhiOverlaySetting();
        UpdateRhiOverlayStatus();
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsModifierKey(Windows.System.VirtualKey key) =>
        key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.Menu
            or Windows.System.VirtualKey.LeftControl or Windows.System.VirtualKey.RightControl
            or Windows.System.VirtualKey.LeftShift or Windows.System.VirtualKey.RightShift
            or Windows.System.VirtualKey.LeftMenu or Windows.System.VirtualKey.RightMenu
            or (Windows.System.VirtualKey)91 or (Windows.System.VirtualKey)92;
}
