using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Multi Frame Generation and the Settings → DLSS defaults dialog.
public sealed partial class MainWindow
{
    // ── Multi Frame Generation ───────────────────────────────────────────────
    private async Task MultiFrameGen(Game game)
    {
        if (!_settings.MfgWarningDismissed)
        {
            var dismiss = new CheckBox { Content = "Don't show this warning again" };
            var warning = new StackPanel { Spacing = 12, Children =
            {
                Label("Multi Frame Generation (MFG) and Dynamic MFG are only supported on NVIDIA 50 Series GPUs (Blackwell architecture).\n\nMinimum driver requirements:\n• MFG (Fixed): Driver 572.16+\n• DMFG (Dynamic): Driver 595.97+\n\nThese settings will have no effect on 40 Series or older hardware.", 13, Amber),
                dismiss,
            } };
            var proceed = false; var dialog = Dialog("Multi Frame Generation", warning, 480);
            warning.Children.Add(DialogAction("OK", warning, () => { proceed = true; dialog.Close(); return Task.CompletedTask; }));
            await dialog.ShowDialog(this);
            if (!proceed) return;
            if (dismiss.IsChecked == true) { _settings.MfgWarningDismissed = true; _settings.Save(); }
        }
        var prefs = _settings.For(game);
        var mode = DlssProfile.Get(prefs, DlssProfile.MfgModeId);
        var modeCombo = Combo(["Default", "Fixed", "Dynamic"], mode == DlssProfile.MfgFixed ? 1 : mode == DlssProfile.MfgDynamic ? 2 : 0, "MfgMode");
        var countCombo = Combo([], 0, "MfgCount"); var fpsCombo = Combo([], 0, "MfgTargetFps");
        var target = DlssProfile.Get(prefs, DlssProfile.MfgTargetFpsId);
        void Populate()
        {
            var fixedMode = modeCombo.SelectedIndex == 1; var dynamic = modeCombo.SelectedIndex == 2;
            var count = fixedMode ? DlssProfile.Get(prefs, DlssProfile.MfgFactorId) : DlssProfile.Get(prefs, DlssProfile.MfgDynamicMaxId);
            countCombo.ItemsSource = fixedMode ? new[] { "2x", "3x", "4x", "5x", "6x" } : dynamic ? new[] { "Up to 2x", "Up to 3x", "Up to 4x", "Up to 5x", "Up to 6x" } : new[] { "—" };
            countCombo.SelectedIndex = fixedMode || dynamic ? (int)Math.Clamp(count == 0 ? 0 : count - 1, 0, 4) : 0; countCombo.IsEnabled = fixedMode || dynamic;
            var fps = new List<string> { "Off", "Max Refresh Rate" };
            fps.AddRange(DlssProfile.TargetFpsOptions.Select(o => o.Label));
            if (target > 0 && target != DlssProfile.TargetFpsMaxRefresh && DlssProfile.TargetFpsOptions.All(o => o.Fps != target)) fps.Add($"{target} FPS (Custom)");
            fps.Add("Custom…");
            fpsCombo.ItemsSource = dynamic ? fps : new List<string> { "—" }; fpsCombo.IsEnabled = dynamic;
            fpsCombo.SelectedIndex = !dynamic || target == 0 ? 0 : target == DlssProfile.TargetFpsMaxRefresh ? 1
                : Array.FindIndex(DlssProfile.TargetFpsOptions, o => o.Fps == target) is var i and >= 0 ? i + 2 : fps.Count - 2;
        }
        Populate();
        modeCombo.SelectionChanged += (_, _) => Populate();
        var custom = new TextBox { Watermark = "20-1000", IsVisible = false, Name = "MfgCustomFps" };
        fpsCombo.SelectionChanged += (_, _) => custom.IsVisible = fpsCombo.SelectedItem as string == "Custom…";
        var body = new StackPanel { Spacing = 12, Children =
        {
            Field("FG Mode", modeCombo), Field("Frame Count", countCombo), Field("Target Frame Rate", fpsCombo), custom,
            Label("Fixed generates a set number of frames per rendered frame. Dynamic adjusts the multiplier up to the chosen maximum to reach the target frame rate.", 11, Muted),
        } };
        var dialog2 = Dialog("Multi Frame Generation", body, 460);
        body.Children.Add(DialogAction("Save", body, async () =>
        {
            var newMode = modeCombo.SelectedIndex switch { 1 => DlssProfile.MfgFixed, 2 => DlssProfile.MfgDynamic, _ => DlssProfile.MfgOff };
            uint fps = 0;
            if (newMode == DlssProfile.MfgDynamic && fpsCombo.SelectedItem is string choice)
            {
                if (choice == "Custom…") { if (!uint.TryParse(custom.Text, out fps) || fps is < 20 or > 1000) throw new FormatException("Enter a target frame rate from 20 to 1000."); }
                else if (choice == "Max Refresh Rate") fps = DlssProfile.TargetFpsMaxRefresh;
                else if (choice.EndsWith("(Custom)")) fps = target;
                else if (fpsCombo.SelectedIndex >= 2) fps = DlssProfile.TargetFpsOptions[fpsCombo.SelectedIndex - 2].Fps;
            }
            DlssProfile.SetMfg(prefs, newMode, newMode == DlssProfile.MfgOff ? 0 : (uint)countCombo.SelectedIndex + 1, fps);
            dialog2.Close();
            await DriverSettingChanged(game, "Multi Frame Generation: " + (modeCombo.SelectedItem as string));
        }));
        await dialog2.ShowDialog(this);
    }

    // ── Settings → DLSS defaults (Quick Apply / apply to all games) ──────────
    private async Task ShowDlssDefaults()
    {
        var defaults = _settings.DlssDefaults;
        var body = new StackPanel { Spacing = 12, Children = { Label("Choose the DLSS versions and presets that Quick Apply deploys. Leave a component on “Game default” to keep the game's own version.", 12, Secondary) } };
        var versionCombos = new Dictionary<DlssKind, ComboBox>(); var presetCombos = new Dictionary<DlssKind, ComboBox>(); var overrides = new Dictionary<DlssKind, CheckBox>();
        var grid = new Grid { ColumnDefinitions = new("150,*,8,*"), RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto") };
        var r = 0;
        foreach (var kind in DlssFiles.Dlls.Append(DlssKind.Streamline))
        {
            var key = kind.ToString();
            var name = Label(DlssFiles.Label(kind), 12, Secondary); Grid.SetRow(name, r); grid.Children.Add(name);
            var versions = _dlss.Versions(kind).Prepend("Game default").Append("Custom").ToList();
            var v = Combo(versions, defaults.Versions.TryGetValue(key, out var saved) ? Math.Max(0, versions.IndexOf(saved)) : 0, "Default" + key);
            v.Margin = new Thickness(0, 3); Grid.SetRow(v, r); Grid.SetColumn(v, 1); grid.Children.Add(v); versionCombos[kind] = v;
            if (kind != DlssKind.Streamline)
            {
                var presets = DlssProfile.Presets(kind);
                var p = Combo(presets.Select(x => (object)x.Name), Math.Max(0, Array.FindIndex(presets, x => x.Value == defaults.Presets.GetValueOrDefault(key))));
                p.Margin = new Thickness(0, 3); Grid.SetRow(p, r); Grid.SetColumn(p, 3); grid.Children.Add(p); presetCombos[kind] = p;
            }
            r++;
        }
        body.Children.Add(Row(Label("Version", 10, Muted), Label("Preset", 10, Muted))); body.Children.Add(grid);
        var scaleItems = DlssProfile.RenderScaleOptions.Where(o => o.Name != "Custom").Select(o => o.Name).ToList();
        var srScale = Combo(scaleItems, Math.Max(0, Array.FindIndex(DlssProfile.RenderScaleOptions, o => o.Value == defaults.SrScale)));
        var rrScale = Combo(scaleItems, Math.Max(0, Array.FindIndex(DlssProfile.RenderScaleOptions, o => o.Value == defaults.RrScale)));
        var scales = new Grid { ColumnDefinitions = new("*,8,*") }; scales.Children.Add(Field("SR render scale", srScale)); var rrField = Field("RR render scale", rrScale); Grid.SetColumn(rrField, 2); scales.Children.Add(rrField);
        body.Children.Add(scales);
        var overrideRow = new WrapPanel();
        foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG })
        { var box = new CheckBox { Content = "NVIDIA Override " + DlssFiles.Short(kind), IsChecked = defaults.DriverOverrides.Contains(kind.ToString()), Margin = new Thickness(0, 0, 12, 0) }; overrides[kind] = box; overrideRow.Children.Add(box); }
        body.Children.Add(overrideRow);
        var dialog = Dialog("DLSS defaults", body, 640);
        void Save()
        {
            defaults.Versions.Clear(); defaults.Presets.Clear(); defaults.DriverOverrides.Clear();
            foreach (var (kind, combo) in versionCombos) if (combo.SelectedIndex > 0 && combo.SelectedItem is string s) defaults.Versions[kind.ToString()] = s;
            foreach (var (kind, combo) in presetCombos) if (combo.SelectedIndex > 0) defaults.Presets[kind.ToString()] = DlssProfile.Presets(kind)[combo.SelectedIndex].Value;
            foreach (var (kind, box) in overrides) if (box.IsChecked == true) defaults.DriverOverrides.Add(kind.ToString());
            defaults.SrScale = DlssProfile.RenderScaleOptions[srScale.SelectedIndex].Value; defaults.RrScale = DlssProfile.RenderScaleOptions[rrScale.SelectedIndex].Value;
            _settings.Save();
        }
        body.Children.Add(Row(DialogAction("Save defaults", body, () => { Save(); dialog.Close(); ShowGame(); return Task.CompletedTask; }),
            DialogAction("Apply to all DLSS games…", body, async () => { Save(); dialog.Close(); await ApplyDefaultsToAll(); })));
        await dialog.ShowDialog(this);
    }

    private async Task ApplyDefaultsToAll()
    {
        if (_settings.DlssDefaults.IsEmpty) { await Message("DLSS defaults", "Choose at least one default version, preset or render scale first."); return; }
        var candidates = new List<(Game Game, DlssDetection Detection)>();
        _status.Text = "Finding games with DLSS…";
        foreach (var game in _games.Where(g => g.Executable != null))
        {
            var detection = await Task.Run(() => DlssScanner.Detect(game.Root));
            if (detection.HasAny) candidates.Add((game, detection));
        }
        if (candidates.Count == 0) { await Message("DLSS defaults", "No installed games with DLSS or Streamline DLLs were found."); return; }
        var boxes = candidates.Select(c => new CheckBox { Content = c.Game.Name + "  —  " + string.Join(", ", DlssFiles.Dlls.Where(c.Detection.Has).Select(k => DlssFiles.Short(k) + " " + c.Detection.Version(k))), IsChecked = true }).ToList();
        var list = new StackPanel { Spacing = 4 }; foreach (var box in boxes) list.Children.Add(box);
        var body = new StackPanel { Spacing = 12, Children = { Label("Apply your DLSS defaults to these games. Close them first; originals are backed up and Restore DLSS/SL undoes it.", 12, Secondary), new ScrollViewer { Content = list, MaxHeight = 360 } } };
        var dialog = Dialog("Apply DLSS defaults", body, 640);
        body.Children.Add(DialogAction("Apply to selected games", body, async () =>
        {
            var done = 0; var failed = new List<string>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (boxes[i].IsChecked != true) continue;
                var (game, detection) = candidates[i];
                try { GameSetup.RequireClosed(game); await ApplyDefaults(game, _settings.For(game), detection, _settings.DlssDefaults); InvalidateDlss(game); done++; }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { failed.Add(game.Name + ": " + ex.Message); }
            }
            _settings.Save(); dialog.Close(); await ReadStates(); Filter();
            _status.Text = $"DLSS defaults applied to {done} game(s)." + (failed.Count > 0 ? " Some failed." : "");
            if (failed.Count > 0) await Message("Some games were skipped", string.Join("\n", failed));
        }, "success"));
        await dialog.ShowDialog(this);
    }
}
