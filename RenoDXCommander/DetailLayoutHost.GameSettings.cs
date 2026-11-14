// DetailLayoutHost.GameSettings.cs -- Spotlight's Game settings panel (the cog beside the game's title): per-game
// versions of Settings-page options that the Tune this game sections don't cover. A built panel, like Screenshots.
// Rows are label + one-line description + control (AddGameSettingRow); add new settings there.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    internal const string GameSettingsKey = "GameSettings";

    /// <summary>The panel's content (OpenPanel).</summary>
    private FrameworkElement? BuildGameSettingsPanel()
    {
        if (_window.ViewModel.SelectedGame is not { } card) return null;
        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(MutedText("Settings that apply to this game only."));

        var rows = new StackPanel { Spacing = 16 };
        AddOverlayRow(rows, card);
        AddDisplayModeRow(rows, card);
        AddSteamUpdateBlockRow(rows, card);
        root.Children.Add(TabsTheme.MakeCard(rows, 16));
        return root;
    }

    /// <summary>
    /// Steam games only: makes the game's appmanifest_&lt;appid&gt;.acf read-only so Steam can't update it.
    /// The file's attribute is the state (nothing saved), so the toggle matches the file.
    /// </summary>
    private void AddSteamUpdateBlockRow(StackPanel rows, GameCardViewModel card)
    {
        if (!string.Equals(card.Source, "Steam", StringComparison.OrdinalIgnoreCase)) return;
        var acf = SteamUpdateBlocker.FindAppManifest(card.InstallPath, card.DetectedGame?.SteamAppId);
        if (acf == null) return;

        const string baseDesc = "Makes the game's Steam app manifest read-only so Steam can't update it. "
            + "Steam may show the game as needing an update, and may not launch it until updates are allowed again. Turn off before verifying or uninstalling in Steam.";
        var toggle = new ToggleSwitch
        {
            IsOn = SteamUpdateBlocker.IsBlocked(acf),
            OnContent = "On",
            OffContent = "Off",
            MinWidth = 0,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "Block Steam updates");
        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
            Visibility = Visibility.Collapsed,
        };
        bool reverting = false;
        toggle.Toggled += (s, e) =>
        {
            if (reverting) return;
            var error = SteamUpdateBlocker.SetBlocked(acf, toggle.IsOn);
            if (error == null) { status.Visibility = Visibility.Collapsed; return; }
            reverting = true;
            toggle.IsOn = !toggle.IsOn;
            reverting = false;
            status.Text = $"Couldn't change the app manifest: {error}";
            status.Visibility = Visibility.Visible;
        };
        AddGameSettingRow(rows, "Block Steam updates", baseDesc, toggle);
        rows.Children.Add(status);
    }

    private void AddOverlayRow(StackPanel rows, GameCardViewModel card)
    {
        var vm = _window.ViewModel;
        string name = card.GameName, store = card.Source ?? "";
        var toggle = new ToggleSwitch
        {
            IsOn = !vm.IsOverlayDisabled(name, store),
            OnContent = "On",
            OffContent = "Off",
            MinWidth = 0,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "RHI overlay");
        toggle.Toggled += (s, e) =>
        {
            try
            {
                vm.SetOverlayDisabled(name, !toggle.IsOn, store);
                _window.RefreshOverlayPassthrough();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailLayoutHost.GameSettings] Overlay toggle for '{name}' failed — {ex.Message}");
            }
        };
        string desc = "Open the RHI overlay over this game with its hotkey or the controller. When off, the hotkey goes to the game.";
        if (!vm.Settings.RhiOverlayEnabled) desc += " The overlay is currently off for all games in Settings.";
        AddGameSettingRow(rows, "RHI overlay", desc, toggle);
    }

    private void AddDisplayModeRow(StackPanel rows, GameCardViewModel card)
    {
        var vm = _window.ViewModel;
        var btn = new Button { Content = "Choose...", Padding = new Thickness(14, 0, 14, 0) };
        TabsTheme.Apply(btn, TabsTheme.BlueButton, height: 34, fontSize: 12);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, "Display mode settings");
        btn.Click += async (s, e) =>
        {
            try { await _window.ShowDisplayModeGameDialogAsync(card); }
            catch (Exception ex) when (ex is InvalidOperationException) { CrashReporter.Log($"[DetailLayoutHost.GameSettings] Display modes — {ex.Message}"); }
        };
        string desc = vm.DisplayModesOn
            ? $"What this game swaps between display modes (now {vm.ActiveDisplayModeName(card)}), and its values per mode."
            : "What this game swaps between display modes, and its values per mode. Display modes are off in Settings › Display.";
        AddGameSettingRow(rows, "Display modes", desc, btn);
    }

    /// <summary>One setting: label and a one-line description on the left, the control on the right.</summary>
    private static void AddGameSettingRow(StackPanel rows, string label, string description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
        });
        grid.Children.Add(text);

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        rows.Children.Add(grid);
    }
}
