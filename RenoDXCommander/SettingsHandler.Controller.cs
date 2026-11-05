// SettingsHandler.Controller.cs -- the "Controller support" row in Settings › General › Windows and
// Admin Mode, under Drop Helper. Built in C# (XAML constraint), added the first time Settings opens.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _controllerCombo;
    private bool _controllerRowFailed;

    private void EnsureControllerSetting()
    {
        if (_controllerCombo != null)
        {
            _controllerCombo.SelectedIndex = ViewModel.Settings.ControllerSupport ? 1 : 0;
            return;
        }
        if (_controllerRowFailed) return;
        try
        {
            // The Drop Helper row's Grid sits in the card column's StackPanel: add ours right after it
            // Found by walking the cards (the combo's Parent was null when Settings first opened)
            if (FindRowWith(_window.SettingsCardsPanel, _window.DropHelperCombo, null) is not (Grid dropRow, StackPanel column))
            {
                _controllerRowFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureControllerSetting] Drop Helper row not found — setting not added");
                return;
            }

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            row.Children.Add(new TextBlock
            {
                Text = "Controller support",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            combo.Items.Add(new ComboBoxItem { Content = "Off" });
            combo.Items.Add(new ComboBoxItem { Content = "On" });
            combo.SelectedIndex = ViewModel.Settings.ControllerSupport ? 1 : 0;
            ToolTipService.SetToolTip(combo,
                "Use an Xbox controller in the Spotlight layout, only while RHI is the active window.\n"
              + "D-pad or left stick: move   A: select   B: back   Y: Launch   X: game menu\n"
              + "LB / RB: previous / next game (in the side panel and Settings: section)\n"
              + "LT / RT: 10 games back / on (hold to keep going)   View: Filters   Menu: Settings\n"
              + "Right stick: scroll   Click left stick: All / Installed / Favourites   Click right stick: back to the game's cover\n"
              + "PlayStation controllers work through Steam Input or DS4Windows.");
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedIndex < 0) return;
                bool on = combo.SelectedIndex == 1;
                if (on == ViewModel.Settings.ControllerSupport) return;
                ViewModel.Settings.ControllerSupport = on;
                ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[SettingsHandler.ControllerSupport] {(on ? "On" : "Off")}");
                _window.UpdateControllerTimer();
            };
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            column.Children.Insert(column.Children.IndexOf(dropRow) + 1, row);
            _controllerCombo = combo;
        }
        catch (Exception ex)
        {
            _controllerRowFailed = true;
            CrashReporter.Log($"[SettingsHandler.EnsureControllerSetting] Failed — {ex.Message}");
        }
    }

    /// <summary>The row Grid holding <paramref name="target"/> and the StackPanel that holds the row.</summary>
    private static (Grid Row, StackPanel Column)? FindRowWith(UIElement node, UIElement target, StackPanel? column)
    {
        switch (node)
        {
            case Grid g when g.Children.Contains(target) && column != null:
                return (g, column);
            case Panel p:
                var col = p as StackPanel ?? column;
                foreach (var child in p.Children)
                    if (FindRowWith(child, target, col) is { } found) return found;
                break;
            case Border { Child: { } child }:
                return FindRowWith(child, target, column);
            case ScrollViewer { Content: UIElement content }:
                return FindRowWith(content, target, column);
        }
        return null;
    }
}
