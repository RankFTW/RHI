// SettingsHandler.Layout.cs -- "Game page" card in Settings › General: chooses the game page
// layout (DetailLayoutHost / MainLayout). Built in C# (XAML constraint), added once when Settings
// first opens.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private bool _layoutSettingDone;

    private static readonly (MainLayout Layout, string Label)[] LayoutChoices =
    {
        (MainLayout.Classic, "Classic"),
        (MainLayout.Tabs, "Tabs"),
        (MainLayout.Spotlight, "Spotlight"),
    };

    private void EnsureLayoutSetting()
    {
        if (_layoutSettingDone) return;
        _layoutSettingDone = true;
        try
        {
            StackPanel? general = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string tag && tag == SectionTagPrefix + "General")
                { general = sp; break; }
            if (general == null)
            {
                CrashReporter.Log("[SettingsHandler.EnsureLayoutSetting] General section not found — Layout setting not added");
                return;
            }

            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            foreach (var (_, label) in LayoutChoices) combo.Items.Add(label);
            var current = Enum.TryParse<MainLayout>(ViewModel.Settings.MainLayout, true, out var l) ? l : MainLayout.Classic;
            combo.SelectedIndex = Math.Max(0, Array.FindIndex(LayoutChoices, c => c.Layout == current));
            ToolTipService.SetToolTip(combo,
                "Classic: every section of the game page in one long page; collapse and drag sections to arrange them.\n" +
                "Tabs: a redesigned window. A larger game header with a Launch button, an Overview tab with a card for each component and a summary of every section, " +
                "then one tab per section, plus a simpler top bar and sidebar.\n" +
                "Spotlight: the game's Steam artwork as a large banner, your games as a row of covers, and tiles that open each section in a side panel. " +
                "Downloads game artwork from Steam while it's in use.\nEvery setting is in every layout.");
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedIndex < 0) return;
                var chosen = LayoutChoices[combo.SelectedIndex].Layout.ToString();
                if (string.Equals(ViewModel.Settings.MainLayout, chosen, StringComparison.Ordinal)) return;
                ViewModel.Settings.MainLayout = chosen;
                ViewModel.SaveSettingsPublic();
                _window.ApplyMainLayout();
            };

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            row.Children.Add(new TextBlock { Text = "Layout", Style = FindStyle("SettingsLabelStyle") });
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            var column = new StackPanel { Spacing = 9, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top };
            column.Children.Add(row);
            column.Children.Add(new TextBlock
            {
                Text = "Classic shows every section in one page. Tabs is a redesigned window with an Overview and one tab per section. Spotlight shows the game's artwork, your games as covers, and opens each section in a side panel.",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            });

            // Same two-column grid as the other cards, so the setting lines up with theirs
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(column);

            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(new TextBlock { Text = "Game page", Style = FindStyle("SettingsCardTitleStyle") });
            body.Children.Add(grid);
            general.Children.Add(new Border { Style = FindStyle("SettingsCardStyle"), Child = body });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureLayoutSetting] Failed — {ex.Message}");
        }
    }
}
