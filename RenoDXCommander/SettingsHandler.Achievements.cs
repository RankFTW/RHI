// SettingsHandler.Achievements.cs -- the "Achievements" section of Settings (before Tools & Maintenance):
// opt-in toggles, one per store in a single card, for showing a game's achievements on the Spotlight page. Built in
// C# (XAML constraint), inserted the first time Settings opens, before the section navigation is built
// so it gets a jump button like the XAML sections.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _steamAchCombo;
    private ComboBox? _xboxAchCombo;
    private bool _achievementsSectionFailed;

    private void EnsureAchievementsSection()
    {
        if (_steamAchCombo != null)
        {
            _steamAchCombo.SelectedIndex = ViewModel.Settings.SteamAchievements ? 1 : 0;
            if (_xboxAchCombo != null) _xboxAchCombo.SelectedIndex = ViewModel.Settings.XboxAchievements ? 1 : 0;
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

            // One card: Steam left, Xbox right (the usual two-column Settings grid)
            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var body = new StackPanel { Spacing = 12 };
            card.Child = body;
            body.Children.Add(new TextBlock { Text = "Stores", Style = FindStyle("SettingsCardTitleStyle") });

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

            left.Children.Add(MakeAchievementRow(
                "Steam",
                "Shows the selected Steam game's achievements under the Launch button in the Spotlight layout: "
              + "your progress, latest unlock and recent unlocks. Click it for the full list with unlock dates and rarity.\n"
              + "Read from Steam's files on this PC, so no sign-in, API key or public profile is needed. "
              + "Games you haven't played on this PC show no achievements until you run them once.",
                () => ViewModel.Settings.SteamAchievements,
                on => ViewModel.Settings.SteamAchievements = on,
                out _steamAchCombo));
            right.Children.Add(MakeAchievementRow(
                "Xbox",
                "Shows the selected Xbox / Game Pass game's achievements under the Launch button in the Spotlight layout, "
              + "with unlock dates, rarity and Gamerscore.\n"
              + "Read from the Xbox app's data on this PC, so no sign-in is needed in RHI. The Xbox app only keeps a game's "
              + "achievements after it has shown them: if a game shows none, or new unlocks are missing, open the game's page "
              + "in the Xbox app once.",
                () => ViewModel.Settings.XboxAchievements,
                on => ViewModel.Settings.XboxAchievements = on,
                out _xboxAchCombo));
            body.Children.Add(columns);
            body.Children.Add(new TextBlock
            {
                Text = "Progress, unlock dates, icons and rarity, read from each store's files on this PC. Spotlight layout only.",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            section.Children.Add(card);

            panel.Children.Insert(toolsIndex, section);
        }
        catch (Exception ex)
        {
            _achievementsSectionFailed = true;
            _steamAchCombo = null;
            CrashReporter.Log($"[SettingsHandler.EnsureAchievementsSection] Failed — {ex.Message}");
        }
    }

    /// <summary>One store's "Steam  [Off / On]" row; details in the combo's tooltip.</summary>
    private Grid MakeAchievementRow(string store, string tooltip, Func<bool> get, Action<bool> set, out ComboBox comboOut)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
        row.Children.Add(new TextBlock
        {
            Text = store,
            Style = FindStyle("SettingsLabelStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
        combo.Items.Add(new ComboBoxItem { Content = "Off" });
        combo.Items.Add(new ComboBoxItem { Content = "On" });
        combo.SelectedIndex = get() ? 1 : 0;
        ToolTipService.SetToolTip(combo, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, $"Show {store} achievements");
        combo.SelectionChanged += (s, e) =>
        {
            if (combo.SelectedIndex < 0) return;
            bool on = combo.SelectedIndex == 1;
            if (on == get()) return;
            set(on);
            ViewModel.SaveSettingsPublic();
            CrashReporter.Log($"[SettingsHandler.{store}Achievements] {(on ? "On" : "Off")}");
            _window.AchievementsSettingChanged();
        };
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);
        comboOut = combo;
        return row;
    }
}
