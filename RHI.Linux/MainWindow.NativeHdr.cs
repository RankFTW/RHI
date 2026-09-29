using Avalonia.Controls;
using Avalonia.Layout;
using RHI.Linux.Core;

namespace RHI.Linux;

public sealed partial class MainWindow
{
    private Control NativeHdrSection(Game game)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Label("Native HDR", 13, null, true));
        body.Children.Add(Label("For games with built-in HDR. Enable HDR in the game and on your display; a compatible Proton build is required.", 11, Secondary));
        var configs = Proton.LocalConfigs(game).ToList();
        var saved = _settings.For(game).SteamConfig;
        var picker = new ComboBox
        {
            ItemsSource = configs.Select(c => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(c)))).ToList(),
            SelectedIndex = saved != null && configs.Contains(saved) ? configs.IndexOf(saved) : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (configs.Count > 1) body.Children.Add(picker);
        var status = Label("", 11, Muted);
        Button toggle = null!;
        string? Config() => picker.SelectedIndex >= 0 && picker.SelectedIndex < configs.Count ? configs[picker.SelectedIndex] : null;
        void Update()
        {
            toggle.Classes.Remove("success");
            toggle.IsEnabled = true;
            try
            {
                var enabled = Config() is { } config && HdrLaunchOptions.IsEnabled(Proton.ReadOptions(config, game.AppId!) ?? "");
                toggle.Content = enabled ? "✓ Configured — Disable" : Config() == null ? "Set up Native HDR…" : "Enable Native HDR";
                if (enabled) toggle.Classes.Add("success");
                status.Text = Config() == null ? "Copy launch options into your launcher using the setup helper." : enabled ? "HDR launch option configured. HDR output is not verified." : "HDR launch option is not configured.";
            }
            catch (Exception ex) { status.Text = "Could not read HDR settings: " + ex.Message; toggle.IsEnabled = false; }
        }
        toggle = Action("Enable Native HDR", async () =>
        {
            if (Config() is not { } config) { await new HdrLaunchWindow(game).ShowDialog(this); Update(); return; }
            var enabled = HdrLaunchOptions.IsEnabled(Proton.ReadOptions(config, game.AppId!) ?? "");
            string Merge(string options) => enabled ? HdrLaunchOptions.Disable(options) : HdrLaunchOptions.Merge(options, false, false);
            _ = Merge(Proton.ReadOptions(config, game.AppId!) ?? "");
            if (Proton.SteamRunning() && !await Confirm("Restart Steam?", "Steam needs to close and reopen to save this game's Native HDR launch option. Close running games first.", "Apply & restart Steam")) return;
            await GameSetup.ConfigureLaunchOptions(game, config, Progress, Merge);
            _settings.For(game).SteamConfig = config;
            _settings.Save();
            await ReadStates(); ShowGame();
            _status.Text = enabled ? "Native HDR launch option removed." : "Native HDR launch option configured.";
        }, "action", "NativeHdrToggle");
        ToolTip.SetTip(toggle, "Green means PROTON_ENABLE_HDR=1 is saved for the selected Steam account. Disabling removes only this option; Wayland and HDR WSI settings are preserved.");
        var settings = Action("⚙", async () => { await new HdrLaunchWindow(game).ShowDialog(this); Update(); }, "", "NativeHdrSettings");
        ToolTip.SetTip(settings, "Optional Wayland / HDR WSI launch options");
        body.Children.Add(Row(toggle, settings));
        body.Children.Add(status);
        picker.SelectionChanged += (_, _) => Update();
        Update();
        return Card(body);
    }
}
