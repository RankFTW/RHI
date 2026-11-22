// DetailLayoutHost.GameSettings.Identity.cs — game settings ⚙ rows moved from Game Overrides (v2.9.6; Game Overrides goes away
// in 2.9.8): Game name, Bitness and Graphics API at the top, Reset at the bottom. Shared with Game Overrides
// (DetailPanelBuilder.Overrides.GameShared.cs / ApiShared.cs), so a change in one shows in the other.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    private void AddIdentityRows(StackPanel rows, GameCardViewModel card)
    {
        var dp = _window.DetailPanelBuilderInstance;
        string name = card.GameName;
        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
            Visibility = Visibility.Collapsed,
        };
        void Status(string text) { status.Text = text; status.Visibility = Visibility.Visible; }

        // ── Game name: box + Reset ──
        var nameBox = new TextBox { Text = name, FontSize = 12, Width = 280 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nameBox, "Game name");
        void SaveName()
        {
            var newName = nameBox.Text?.Trim() ?? "";
            if (newName.Length == 0) { nameBox.Text = name; return; }
            if (dp.RenameGameTo(name, newName))
            {
                name = newName;
                Status($"Renamed to {newName}. Its settings came along.");
            }
        }
        nameBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; SaveName(); } };
        nameBox.LostFocus += (s, e) => SaveName();
        var resetNameBtn = new Button { Content = "Reset", Padding = new Thickness(14, 0, 14, 0) };
        TabsTheme.Apply(resetNameBtn, TabsTheme.IconButton, height: 32, fontSize: 12);
        ToolTipService.SetToolTip(resetNameBtn, "Back to the name the store detected");
        resetNameBtn.Click += (s, e) =>
        {
            var original = dp.GetOriginalGameName(name);
            nameBox.Text = original;
            if (dp.RenameGameTo(name, original))
            {
                name = original;
                Status($"Back to {original}.");
            }
        };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(nameBox);
        nameRow.Children.Add(resetNameBtn);
        AddGameSettingRow(rows, "Game name",
            "The name RHI shows and uses for this game (its settings move with it). Press Enter to rename; Reset goes back to the store's name.",
            nameRow);

        // ── Bitness ──
        var bitness = new ComboBox { ItemsSource = DetailPanelBuilder.BitnessItems, SelectedItem = dp.GetBitnessDisplay(card), MinWidth = 140 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bitness, "Bitness");
        bitness.SelectionChanged += (s, e) =>
        {
            try
            {
                if (bitness.SelectedItem is not string sel || sel == dp.GetBitnessDisplay(card)) return;
                bool removed = dp.ApplyBitnessOverride(card, name, sel, rebuildNow: false);
                Status(removed
                    ? $"Bitness set to {sel}. The game's bitness changed, so its components were uninstalled: install them again."
                    : $"Bitness set to {sel}.");
            }
            catch (Exception ex) { CrashReporter.Log($"[DetailLayoutHost.GameSettings] Bitness for '{name}' — {ex.Message}"); }
        };
        AddGameSettingRow(rows, "Bitness",
            "Auto uses the bitness RHI detected from the game's exe. Changing it when it changes the game's bitness uninstalls every component in this game.",
            bitness);

        // ── Graphics API ──
        var api = new ComboBox { ItemsSource = DetailPanelBuilder.ApiItems, SelectedItem = dp.GetApiDisplay(card), MinWidth = 140 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(api, "Graphics API");
        api.SelectionChanged += (s, e) =>
        {
            try
            {
                if (api.SelectedItem is not string sel || sel == dp.GetApiDisplay(card)) return;
                bool removed = dp.ApplyApiOverride(card, name, sel, rebuildNow: false);
                Status(removed
                    ? $"Graphics API set to {sel}. ReShade was removed because the game moved between DirectX and Vulkan: install it again."
                    : $"Graphics API set to {sel}.");
            }
            catch (Exception ex) { CrashReporter.Log($"[DetailLayoutHost.GameSettings] Graphics API for '{name}' — {ex.Message}"); }
        };
        AddGameSettingRow(rows, "Graphics API",
            "The graphics API RHI treats this game as using. Auto uses the detected one. Switching between DirectX and Vulkan removes the ReShade install of the other kind.",
            api);
        rows.Children.Add(status);
    }

    /// <summary>Reset (the same as Game Overrides' Reset Overrides). Two clicks: the first asks.</summary>
    private void AddResetRow(StackPanel rows, GameCardViewModel card)
    {
        var dp = _window.DetailPanelBuilderInstance;
        var btn = new Button { Content = "Reset", Padding = new Thickness(14, 0, 14, 0) };
        TabsTheme.Apply(btn, TabsTheme.DangerButton, height: 32, fontSize: 12);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, "Reset this game's settings");
        bool armed = false;
        btn.Click += (s, e) =>
        {
            try
            {
                if (!armed)
                {
                    armed = true;
                    btn.Content = "Click again to reset";
                    return;
                }
                armed = false;
                btn.Content = "Reset";
                dp.ResetGameSettings(card);
                btn.Content = "Reset done";
                btn.IsEnabled = false;
            }
            catch (Exception ex) { CrashReporter.Log($"[DetailLayoutHost.GameSettings] Reset for '{card.GameName}' — {ex.Message}"); }
        };
        AddGameSettingRow(rows, "Reset this game's settings",
            "Puts this game's per-game settings back to their defaults: name, bitness, Graphics API, ReShade channel, shaders and addons, DLL names, "
            + "launch options, update inclusion, DXVK, DLSS presets, display modes and the overlay. Click twice.",
            btn);
    }
}
