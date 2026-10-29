using Avalonia.Controls;
using Avalonia.Layout;
using RHI.Linux.Core;

namespace RHI.Linux;

public sealed partial class MainWindow
{
    private Control ReEngineLaunchSection(Game game)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Label("RE Engine Wine detection", 13, null, true));
        body.Children.Add(Label("Bypass Wine detection to expose ray tracing in supported RE Engine games. Enable ray tracing in-game afterwards. This workaround may cause game or driver issues; disable it if needed.", 11, Secondary));
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
                var enabled = Config() is { } config && ReEngineLaunchOptions.IsEnabled(Proton.ReadOptions(config, game.AppId!) ?? "");
                toggle.Content = enabled ? "✓ Configured — Disable" : Config() == null ? "Set up Wine detection bypass…" : "Bypass Wine detection";
                if (enabled) toggle.Classes.Add("success");
                status.Text = Config() == null ? "Copy launch options into your launcher using the setup helper." : enabled ? "Wine detection bypass configured. Ray tracing support is not verified." : "Wine detection bypass is off.";
            }
            catch (Exception ex) { status.Text = "Could not read launch settings: " + ex.Message; toggle.IsEnabled = false; }
        }
        toggle = Action("Bypass Wine detection", async () =>
        {
            if (Config() is not { } config) { await ShowReEngineLaunchPreview(); Update(); return; }
            var enabled = ReEngineLaunchOptions.IsEnabled(Proton.ReadOptions(config, game.AppId!) ?? "");
            string Merge(string options) => ReEngineLaunchOptions.SetEnabled(options, !enabled);
            _ = Merge(Proton.ReadOptions(config, game.AppId!) ?? "");
            if (Proton.SteamRunning() && !await Confirm("Restart Steam?", "Steam needs to close and reopen to save this game's RE Engine Wine detection launch option. Close running games first.", "Apply & restart Steam")) return;
            await GameSetup.ConfigureLaunchOptions(game, config, Progress, Merge);
            _settings.For(game).SteamConfig = config;
            _settings.Save();
            await ReadStates(); ShowGame();
            _status.Text = enabled ? "Wine detection bypass removed." : "Wine detection bypass configured.";
        }, "action", "ReEngineWineDetectionToggle");
        ToolTip.SetTip(toggle, "Adds /WineDetectionEnabled:False after %command%. Disabling removes only this argument.");
        body.Children.Add(toggle);
        body.Children.Add(status);
        picker.SelectionChanged += (_, _) => Update();
        Update();
        return Card(body);
    }

    private async Task ShowReEngineLaunchPreview()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Label("Paste your current launch options, then preview and copy the result into your launcher. Nothing is saved automatically.", 12, Secondary));
        var original = new TextBox { Text = "%command%", Watermark = "Existing launch options" };
        var preview = new TextBox { IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var status = Label("", 11, Muted);
        var copy = DialogAction("Copy preview", body, async () =>
        {
            if (Clipboard == null) throw new IOException("Clipboard unavailable.");
            await Clipboard.SetTextAsync(preview.Text ?? "");
            status.Text = "Copied. Paste into your game's launch options.";
        });
        copy.IsEnabled = false;
        void Generate(bool enabled)
        {
            preview.Text = ""; copy.IsEnabled = false;
            try { preview.Text = ReEngineLaunchOptions.SetEnabled(original.Text ?? "", enabled); copy.IsEnabled = true; status.Text = "Review the preview before copying."; }
            catch (FormatException ex) { status.Text = ex.Message; }
        }
        original.TextChanged += (_, _) => { preview.Text = ""; copy.IsEnabled = false; status.Text = "Generate a new preview after editing."; };
        body.Children.Add(original);
        body.Children.Add(Row(Plain("Preview bypass", () => Generate(true)), Plain("Preview removal", () => Generate(false))));
        body.Children.Add(preview); body.Children.Add(copy); body.Children.Add(status);
        await Dialog("RE Engine Wine detection", body).ShowDialog(this);
    }
}
