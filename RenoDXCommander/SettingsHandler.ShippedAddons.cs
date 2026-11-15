// SettingsHandler.ShippedAddons.cs -- the "Built-in add-ons" row in Settings › ReShade › Keys and
// screenshots, under Effect list style. Built in C# (XAML constraint), added the first time Settings opens.
// RHI's reshade.ini templates disable Generic Depth and Effect Runtime Sync; this keeps them enabled.
// Saved on change; applied on the next reshade.ini write and by "Write these to all games".

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _shippedAddonsCombo;
    private bool _shippedAddonsRowFailed;

    private void EnsureShippedAddonsSetting()
    {
        if (_shippedAddonsCombo != null)
        {
            _shippedAddonsCombo.SelectedIndex = ViewModel.Settings.RsShippedAddonsEnabled ? 1 : 0;
            return;
        }
        if (_shippedAddonsRowFailed) return;
        try
        {
            if (FindRowWith(_window.SettingsCardsPanel, _window.RsVariableListUseTabsCombo, null) is not (Grid styleRow, StackPanel column))
            {
                _shippedAddonsRowFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureShippedAddonsSetting] Effect list style row not found — setting not added");
                return;
            }

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            row.Children.Add(new TextBlock
            {
                Text = "Built-in add-ons",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            combo.Items.Add(new ComboBoxItem { Content = "Disabled" });
            combo.Items.Add(new ComboBoxItem { Content = "Enabled" });
            combo.SelectedIndex = ViewModel.Settings.RsShippedAddonsEnabled ? 1 : 0;
            ToolTipService.SetToolTip(combo,
                "ReShade's own Generic Depth and Effect Runtime Sync add-ons.\n"
              + "Disabled (RHI default): reshade.ini turns them off.\n"
              + "Enabled: RHI leaves them on. Depth-based shaders need Generic Depth.\n"
              + "Applies the next time RHI writes a game's reshade.ini, or straight away with Write these to all games.");
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedIndex < 0) return;
                bool on = combo.SelectedIndex == 1;
                if (on == ViewModel.Settings.RsShippedAddonsEnabled) return;
                ViewModel.Settings.RsShippedAddonsEnabled = on;
                ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[SettingsHandler.ShippedAddons] {(on ? "Enabled" : "Disabled")}");
            };
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            column.Children.Insert(column.Children.IndexOf(styleRow) + 1, row);
            _shippedAddonsCombo = combo;

            ToolTipService.SetToolTip(_window.ApplyHotkeyBtn,
                "Writes the overlay key, screenshot key, screenshot folder, effect list style and built-in add-ons setting into every game's reshade.ini. Skips hidden games and games set to keep their own reshade.ini.");
        }
        catch (Exception ex)
        {
            _shippedAddonsRowFailed = true;
            CrashReporter.Log($"[SettingsHandler.EnsureShippedAddonsSetting] Failed — {ex.Message}");
        }
    }
}
