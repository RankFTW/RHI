// SettingsHandler.UiScale.cs — Settings › General › Windows: Interface size.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _uiScaleCombo;
    private bool _uiScaleSelecting;

    /// <summary>Adds the "Interface size" row under Start with Windows (once) and selects the saved value.</summary>
    private void EnsureUiScaleRow()
    {
        try
        {
            if (_uiScaleCombo == null)
            {
                if (FindRowOf(_window.SettingsCardsPanel, _window.StartWithWindowsCombo) is not { } found)
                {
                    CrashReporter.Log("[SettingsHandler.EnsureUiScaleRow] Start with Windows row not found");
                    return;
                }
                var (list, anchorRow) = found;

                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
                var label = new TextBlock { Text = "Interface size" };
                if (Application.Current.Resources.TryGetValue("SettingsLabelStyle", out var ls) && ls is Style labelStyle)
                    label.Style = labelStyle;
                row.Children.Add(label);

                var combo = new ComboBox();
                if (Application.Current.Resources.TryGetValue("SettingsComboStyle", out var cs) && cs is Style comboStyle)
                    combo.Style = comboStyle;
                foreach (var p in MainWindow.UiScaleOptions) combo.Items.Add(p == 100 ? "100% (normal)" : $"{p}%");
                ToolTipService.SetToolTip(combo,
                    "Makes RHI's window smaller or larger without changing Windows' scaling. " +
                    "Useful on TVs and high-DPI screens set to 200% or more. " +
                    "You can also hold Ctrl and turn the mouse wheel anywhere in RHI. " +
                    "Pop-ups, dropdown lists and tooltips keep the Windows size.");
                Grid.SetColumn(combo, 1);
                row.Children.Add(combo);
                list.Children.Insert(list.Children.IndexOf(anchorRow) + 1, row);
                _uiScaleCombo = combo;
                combo.SelectionChanged += UiScaleCombo_SelectionChanged;
            }
            SelectUiScale();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureUiScaleRow] {ex.Message}");
        }
    }

    /// <summary>Selects the saved size in the combo (also called after Ctrl + mouse wheel changes it).</summary>
    internal void SelectUiScale()
    {
        if (_uiScaleCombo == null) return;
        _uiScaleSelecting = true;
        int idx = Array.IndexOf(MainWindow.UiScaleOptions, ViewModel.Settings.UiScale);
        _uiScaleCombo.SelectedIndex = idx >= 0 ? idx : Array.IndexOf(MainWindow.UiScaleOptions, 100);
        _uiScaleSelecting = false;
    }

    private void UiScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_uiScaleSelecting || _uiScaleCombo == null) return;
        try
        {
            int idx = _uiScaleCombo.SelectedIndex;
            if (idx < 0 || idx >= MainWindow.UiScaleOptions.Length) return;
            int percent = MainWindow.UiScaleOptions[idx];
            if (percent == ViewModel.Settings.UiScale) return;
            ViewModel.Settings.UiScale = percent;
            ViewModel.SaveSettingsPublic();
            _window.ApplyUiScale(percent);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.UiScaleCombo_SelectionChanged] {ex.Message}");
        }
    }
}
