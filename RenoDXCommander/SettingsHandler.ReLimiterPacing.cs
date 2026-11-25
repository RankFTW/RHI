// SettingsHandler.ReLimiterPacing.cs -- "Pacing preference" row in Settings › Components › ReLimiter, under DLSS hooks.
// Built in C# (XAML constraint), added the first time Settings opens. ReLimiter v4's [FrameLimiter] pacing_preference
// (latency | balanced | smooth, shown as Lower latency / Balanced / Smoother like its overlay). Saved in settings.json
// (UlPacingPreference); the global relimiter.ini is its source like the other ReLimiter settings (SettingsHandler.
// ReLimiterTemplate.cs). Reaches games through Write to all ReLimiter games (v4 games only), Save to global
// relimiter.ini and seeding of new v4 installs (UlV4Rules.BuildSeededIni). Not part of the shared presets file.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private ComboBox? _ulPacingCombo;
    private bool _ulPacingRowFailed;
    private bool _ulPacingFilling;

    /// <summary>Adds the row once, then shows the saved value (called each time Settings opens, after the template sync).</summary>
    private void EnsureUlPacingRow()
    {
        if (_ulPacingCombo == null && !_ulPacingRowFailed)
        {
            try
            {
                if (FindRowWith(_window.SettingsCardsPanel, _window.UlDlssHooksCombo, null) is not (Grid hooksRow, StackPanel column))
                {
                    _ulPacingRowFailed = true;
                    CrashReporter.Log("[SettingsHandler.EnsureUlPacingRow] DLSS hooks row not found — setting not added");
                    return;
                }

                const string tip = "ReLimiter v4's pacing preference (64-bit DirectX 12 games).\n"
                    + "Lower latency: keeps less work waiting for the GPU, for slightly lower latency.\n"
                    + "Balanced (ReLimiter's default): the recommended behaviour.\n"
                    + "Smoother: more even frames for slightly more latency, mainly with DLSS frame generation.\n\n"
                    + "Saved for new ReLimiter v4 installs and by Save to global relimiter.ini. Write to all ReLimiter games "
                    + "also puts it in every game with ReLimiter v4 (v3 doesn't have it). You can still change it per game in ReLimiter's overlay.";

                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
                var label = new TextBlock
                {
                    Text = "Pacing preference (v4)",
                    Style = FindStyle("SettingsLabelStyle"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(label, tip);
                row.Children.Add(label);

                var combo = new ComboBox { Style = FindStyle("SettingsComboStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var l in UlV4Rules.PacingLabels) combo.Items.Add(new ComboBoxItem { Content = l });
                ToolTipService.SetToolTip(combo, tip);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, "Pacing preference");
                combo.SelectionChanged += UlPacingCombo_SelectionChanged;
                Grid.SetColumn(combo, 1);
                row.Children.Add(combo);

                column.Children.Insert(column.Children.IndexOf(hooksRow) + 1, row);
                _ulPacingCombo = combo;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                _ulPacingRowFailed = true;
                CrashReporter.Log($"[SettingsHandler.EnsureUlPacingRow] Failed — {ex.Message}");
                return;
            }
        }
        if (_ulPacingCombo == null) return;
        _ulPacingFilling = true;
        int idx = Array.IndexOf(UlV4Rules.PacingPreferences, ViewModel.Settings.UlPacingPreference);
        _ulPacingCombo.SelectedIndex = idx >= 0 ? idx : Array.IndexOf(UlV4Rules.PacingPreferences, UlV4Rules.DefaultPacingPreference);
        _ulPacingFilling = false;
    }

    private void UlPacingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ulPacingFilling || _ulPacingCombo is not { SelectedIndex: >= 0 } combo) return;
        var value = UlV4Rules.PacingPreferences[combo.SelectedIndex];
        if (value == ViewModel.Settings.UlPacingPreference) return;
        ViewModel.Settings.UlPacingPreference = value;
        ViewModel.SaveSettingsPublic();
        CrashReporter.Log($"[SettingsHandler.UlPacingCombo_SelectionChanged] pacing_preference = {value}");
    }
}
