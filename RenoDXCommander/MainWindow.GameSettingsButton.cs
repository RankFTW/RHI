// MainWindow.GameSettingsButton.cs -- Classic+ header: a ⚙ right after the game's name that opens the per-game
// Game settings as a dialog (DetailLayoutHost.ShowGameSettingsCogAsync), like the cog beside the title in Tabs
// and Spotlight (DetailLayoutHost.Header.cs). Built in C# (XAML constraint: no new controls in MainWindow.xaml):
// the title TextBlock is moved into a small Grid [title | cog] at its place in the header.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private Button? _classicGameSettingsBtn;

    /// <summary>Puts the cog beside the Classic header's game name, once.</summary>
    internal void EnsureClassicGameSettingsButton()
    {
        if (_classicGameSettingsBtn != null) return;
        if (DetailGameName.Parent is not Panel host) return;
        int index = host.Children.IndexOf(DetailGameName);
        if (index < 0) return;

        var btn = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 }, // Setting
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            Background = UIFactory.GetBrush("#00000000"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            MinWidth = 0,
            MinHeight = 0,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(btn, "Game settings");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, "Game settings");
        btn.Click += async (s, e) =>
        {
            try { await _detailPanelBuilder.Layout.ShowGameSettingsCogAsync(btn.XamlRoot); }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.GameSettingsButton] {ex.Message}"); }
        };

        // Left-aligned Grid: the star column fits the name, so the cog sits right after it, and a long
        // name still trims (the column is limited to the space left).
        var row = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.Children.RemoveAt(index);
        row.Children.Add(DetailGameName);
        Grid.SetColumn(btn, 1);
        row.Children.Add(btn);
        host.Children.Insert(index, row);
        _classicGameSettingsBtn = btn;
    }
}
