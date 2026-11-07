// SettingsHandler.Achievements.cs -- the "Achievements" section of Settings (before Tools & Maintenance):
// opt-in toggles for showing a game's achievements on the Spotlight page. Built in C# (XAML
// constraint), inserted the first time Settings opens, before the section navigation is built so it
// gets a jump button like the XAML sections.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _steamAchCombo;
    private bool _achievementsSectionFailed;

    private void EnsureAchievementsSection()
    {
        if (_steamAchCombo != null)
        {
            _steamAchCombo.SelectedIndex = ViewModel.Settings.SteamAchievements ? 1 : 0;
            return;
        }
        if (_achievementsSectionFailed) return;
        try
        {
            var panel = _window.SettingsCardsPanel;
            int toolsIndex = -1;
            for (int i = 0; i < panel.Children.Count; i++)
                if (panel.Children[i] is StackPanel sp && sp.Tag is string t && t == "Section:Tools") { toolsIndex = i; break; }
            if (toolsIndex < 0)
            {
                _achievementsSectionFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureAchievementsSection] Tools section not found — section not added");
                return;
            }

            var section = new StackPanel { Tag = "Section:Achievements", Spacing = 12 };
            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock
            {
                Text = "Achievements",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            head.Children.Add(new TextBlock
            {
                Text = "Show your achievements on the Spotlight page. Off by default; turn on the stores you want to see.",
                FontSize = 12,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });
            section.Children.Add(head);

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var body = new StackPanel { Spacing = 12 };
            card.Child = body;
            body.Children.Add(new TextBlock { Text = "Steam", Style = FindStyle("SettingsCardTitleStyle") });

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Spacing = 9, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top };
            columns.Children.Add(left);

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            row.Children.Add(new TextBlock
            {
                Text = "Show Steam achievements",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            combo.Items.Add(new ComboBoxItem { Content = "Off" });
            combo.Items.Add(new ComboBoxItem { Content = "On" });
            combo.SelectedIndex = ViewModel.Settings.SteamAchievements ? 1 : 0;
            ToolTipService.SetToolTip(combo,
                "Shows the selected Steam game's achievements under the Launch button in the Spotlight layout: "
              + "your progress, latest unlock and recent unlocks. Click it for the full list with unlock dates and rarity.\n"
              + "Read from Steam's files on this PC, so no sign-in, API key or public profile is needed. "
              + "Games you haven't played on this PC show no achievements until you run them once.");
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedIndex < 0) return;
                bool on = combo.SelectedIndex == 1;
                if (on == ViewModel.Settings.SteamAchievements) return;
                ViewModel.Settings.SteamAchievements = on;
                ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[SettingsHandler.SteamAchievements] {(on ? "On" : "Off")}");
                _window.AchievementsSettingChanged();
            };
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);
            left.Children.Add(row);
            left.Children.Add(new TextBlock
            {
                Text = "Progress, unlock dates, icons and how rare each achievement is. Spotlight layout only.",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            });

            body.Children.Add(columns);
            section.Children.Add(card);
            panel.Children.Insert(toolsIndex, section);
            _steamAchCombo = combo;
        }
        catch (Exception ex)
        {
            _achievementsSectionFailed = true;
            CrashReporter.Log($"[SettingsHandler.EnsureAchievementsSection] Failed — {ex.Message}");
        }
    }
}
