// SettingsHandler.ReLimiterKeys.cs: Settings › Components › ReLimiter rows for the preset, Move OSD, blackout and
// OLED Care keys (built in C#, inserted under the Overlay key row). The values are read from the global relimiter.ini
// where it has them, else ReLimiter's shared presets file (ReLimiterPresetKeys); a change is written there straight away, and "Write to all ReLimiter
// games" also writes them into every game's relimiter.ini.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private readonly Dictionary<string, TextBox> _ulPresetKeyBoxes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds the key rows once, then shows the values in presets.ini (called each time Settings opens).</summary>
    private void EnsureUlPresetKeyRows()
    {
        try
        {
            if (_ulPresetKeyBoxes.Count == 0)
            {
                // Searched down from SettingsCardsPanel: FrameworkElement.Parent can be null on the first open
                if (FindUlOverlayRow(_window.SettingsCardsPanel) is not ({ } column, { } overlayRow))
                {
                    CrashReporter.Log("[SettingsHandler.EnsureUlPresetKeyRows] ReLimiter column not found");
                    return;
                }
                int at = column.Children.IndexOf(overlayRow) + 1;
                foreach (var bind in ReLimiterPresetKeys.Keys)
                    column.Children.Insert(at++, MakeUlPresetKeyRow(bind));
            }
            var values = ReLimiterPresetKeys.ReadAll();
            OverlayUlPresetKeysFromTemplate(values); // the global relimiter.ini wins where it has a key
            foreach (var (key, box) in _ulPresetKeyBoxes)
                box.Text = values.TryGetValue(key, out var v) ? v : "";
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureUlPresetKeyRows] Failed — {ex.Message}");
        }
    }

    /// <summary>The ReLimiter column and its Overlay key row (the Grid holding UlHotkeyBox), found by walking down.</summary>
    private (Panel Column, FrameworkElement Row)? FindUlOverlayRow(DependencyObject node)
    {
        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Panel row && row.Children.Contains(_window.UlHotkeyBox))
                    return (panel, row);
            foreach (var child in panel.Children)
                if (FindUlOverlayRow(child) is { } found) return found;
        }
        else if (node is Border { Child: { } bc }) return FindUlOverlayRow(bc);
        else if (node is ScrollViewer { Content: DependencyObject sc }) return FindUlOverlayRow(sc);
        else if (node is ContentControl { Content: DependencyObject cc }) return FindUlOverlayRow(cc);
        return null;
    }

    private Grid MakeUlPresetKeyRow(ReLimiterPresetKeys.Bind bind)
    {
        var tip = bind.Help + " Click the box and press a key (Ctrl, Alt and Shift combinations work); "
            + "Backspace or ↺ clears it.\n\nSaved in ReLimiter's shared presets file straight away, which games with "
            + "Shared presets on read. Write to all ReLimiter games also puts it in every game's relimiter.ini.";
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
        var label = new TextBlock { Text = bind.Label, Style = FindStyle("SettingsLabelStyle"), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(label, tip);
        row.Children.Add(label);

        var inner = new Grid { ColumnSpacing = 6 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            Style = FindStyle("SettingsTextBoxStyle"),
            IsReadOnly = true,
            PlaceholderText = "None",
            FontFamily = new FontFamily("Consolas"),
        };
        ToolTipService.SetToolTip(box, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, bind.Label);
        box.PreviewKeyDown += (s, e) => UlPresetKeyBox_PreviewKeyDown(bind, box, e);
        inner.Children.Add(box);

        var clear = new Button { Content = "↺", Style = FindStyle("SettingsIconButtonStyle") };
        ToolTipService.SetToolTip(clear, "Clear this key");
        clear.Click += (s, e) => SaveUlPresetKey(bind, box, "");
        Grid.SetColumn(clear, 1);
        inner.Children.Add(clear);

        Grid.SetColumn(inner, 1);
        row.Children.Add(inner);
        _ulPresetKeyBoxes[bind.PresetKey] = box;
        return row;
    }

    private void UlPresetKeyBox_PreviewKeyDown(ReLimiterPresetKeys.Bind bind, TextBox box, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.Menu
            or Windows.System.VirtualKey.LeftControl or Windows.System.VirtualKey.RightControl
            or Windows.System.VirtualKey.LeftShift or Windows.System.VirtualKey.RightShift
            or Windows.System.VirtualKey.LeftMenu or Windows.System.VirtualKey.RightMenu
            or Windows.System.VirtualKey.LeftWindows or Windows.System.VirtualKey.RightWindows)
            return;
        if (IsHotkeyBoxPassThroughKey(key)) return; // Tab / Enter / Esc move focus
        e.Handled = true;
        if (key == Windows.System.VirtualKey.Back)
        {
            SaveUlPresetKey(bind, box, "");
            return;
        }
        static bool Down(Windows.System.VirtualKey k) => Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var value = HotkeyManager.BuildUlHotkeyString((int)key,
            Down(Windows.System.VirtualKey.Shift), Down(Windows.System.VirtualKey.Control), Down(Windows.System.VirtualKey.Menu));
        SaveUlPresetKey(bind, box, value);
    }

    private void SaveUlPresetKey(ReLimiterPresetKeys.Bind bind, TextBox box, string value)
    {
        if (!ReLimiterPresetKeys.WritePreset(bind.PresetKey, value))
        {
            // Show what the file really has
            box.Text = ReLimiterPresetKeys.ReadAll().TryGetValue(bind.PresetKey, out var v) ? v : "";
            return;
        }
        box.Text = value;
    }

    /// <summary>Writes the keys from presets.ini into one relimiter.ini (from Write to all ReLimiter games).</summary>
    private static void WriteUlPresetKeysTo(string iniPath, IReadOnlyDictionary<string, string> values)
    {
        try { ReLimiterPresetKeys.WriteToGameIni(iniPath, values); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SettingsHandler.WriteUlPresetKeysTo] '{iniPath}': {ex.Message}");
        }
    }
}
