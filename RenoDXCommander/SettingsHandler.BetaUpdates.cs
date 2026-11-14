// SettingsHandler.BetaUpdates.cs -- the "Get beta updates" row in Settings › General › Updates, under
// Background update checks. Built in C# (XAML constraint), added the first time Settings opens.
// Betas come from RankFTW/rhi-beta (UpdateService); plan and release steps: docs/beta-channel.md.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _betaUpdatesCombo;
    private bool _betaRowFailed;

    private void EnsureBetaUpdatesSetting()
    {
        if (_betaUpdatesCombo != null)
        {
            _betaUpdatesCombo.SelectedIndex = ViewModel.BetaOptIn ? 1 : 0;
            return;
        }
        if (_betaRowFailed) return;
        try
        {
            if (FindRowWith(_window.SettingsCardsPanel, _window.BackgroundUpdateChecksCombo, null) is not (Grid anchorRow, StackPanel column))
            {
                _betaRowFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureBetaUpdatesSetting] Background update checks row not found — setting not added");
                return;
            }

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            row.Children.Add(new TextBlock
            {
                Text = "Get beta updates",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
            combo.Items.Add(new ComboBoxItem { Content = "Off" });
            combo.Items.Add(new ComboBoxItem { Content = "On" });
            combo.SelectedIndex = ViewModel.BetaOptIn ? 1 : 0;
            ToolTipService.SetToolTip(combo,
                "Betas are test builds of the next RHI version and can have bugs.\n"
              + "With this on, RHI offers each new beta, and moves you to the full release when it's out.\n"
              + "Turning it off doesn't downgrade you: you stay on your beta until the next full release.");
            combo.SelectionChanged += async (s, e) =>
            {
                try
                {
                    if (combo.SelectedIndex < 0) return;
                    bool on = combo.SelectedIndex == 1;
                    if (on == ViewModel.BetaOptIn) return;
                    ViewModel.BetaOptIn = on;
                    ViewModel.SaveSettingsPublic();
                    CrashReporter.Log($"[SettingsHandler.BetaUpdates] {(on ? "On" : "Off")} (running {AppVersionInfo.Display})");
                    // Turning it on looks for a beta straight away
                    if (on) await _window.CheckForAppUpdateNowAsync();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[SettingsHandler.BetaUpdates] Failed — {ex.Message}");
                }
            };
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            var help = new TextBlock
            {
                Text = "Test builds of the next version. You move to the full release when it's out.",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            };

            int at = column.Children.IndexOf(anchorRow) + 1;
            column.Children.Insert(at, row);
            column.Children.Insert(at + 1, help);
            _betaUpdatesCombo = combo;
        }
        catch (Exception ex)
        {
            _betaRowFailed = true;
            CrashReporter.Log($"[SettingsHandler.EnsureBetaUpdatesSetting] Failed — {ex.Message}");
        }
    }
}
