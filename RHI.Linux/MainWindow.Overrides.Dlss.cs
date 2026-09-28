using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Nvidia Profile Overrides (DLSS / Streamline columns), Quick Apply and NR DLL deploy.
// Mirrors upstream DetailPanelBuilder.Overrides.Dlss.cs.
public sealed partial class MainWindow
{
    // ── Nvidia Profile Overrides (DLSS / Streamline) ─────────────────────────
    private Control NvidiaProfileSection(Game game, InstallationStatus state)
    {
        var title = "Nvidia Profile Overrides" + (DriverVersion == null ? "" : " — Driver " + DriverVersion);
        var nr = DlssState(game);
        if (nr == null) return Section("NvidiaProfile", title, null, Label("Checking this game's DLSS files…", 11, Muted), "NvidiaProfile");
        var prefs = _settings.For(game); var detection = nr.Detection;
        var body = new StackPanel { Spacing = 8 };
        if (!detection.HasAny)
            body.Children.Add(Label("No DLSS or Streamline DLLs were found in this game. Install a Neural Rendering method above, or use Advanced settings if the game keeps them elsewhere.", 11, Muted));
        else
        {
            var grid = new Grid { ColumnDefinitions = new("*,Auto,*,Auto,*,Auto,*,Auto,*") };
            var column = 0;
            foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG, DlssKind.NR, DlssKind.Streamline })
            {
                if (column > 0) { var divider = new Border { Width = 1, Background = Brush("#283240"), Margin = new Thickness(8, 0) }; Grid.SetColumn(divider, column - 1); grid.Children.Add(divider); }
                var col = DlssColumn(game, prefs, detection, kind); Grid.SetColumn(col, column); grid.Children.Add(col);
                column += 2;
            }
            body.Children.Add(grid);
        }
        var extras = Extras(game);
        var needsLaunch = !state.DlssLaunchConfigured;
        var launch = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        var settingsCount = DlssProfile.Managed.Count(id => DlssProfile.Has(prefs, id));
        launch.Children.Add(new StackPanel { Spacing = 3, Children =
        {
            Row(Badge(needsLaunch ? "Launch settings need updating" : settingsCount == 0 && extras.Dlls.Count == 0 && !extras.Environment.Values.Any(v => v != null) ? "Driver defaults" : "✓ Launch settings applied", !needsLaunch && settingsCount > 0, needsLaunch)),
            Label("Presets, render scale, Multi Frame Gen and NVIDIA Override are applied through dxvk-nvapi (DXVK_NVAPI_DRS_SETTINGS) in the game's Steam launch options.", 10, Muted),
        } });
        if (needsLaunch)
        {
            var apply = Action("Apply launch settings", () => ShowSteamSetup(game), "success", "ApplyDlssLaunch"); apply.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(apply, 1); launch.Children.Add(apply);
        }
        body.Children.Add(launch);
        var summary = string.Join("   ", new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG, DlssKind.NR, DlssKind.Streamline }.Where(detection.Has)
            .Select(k => DlssFiles.Short(k) + ": " + (k != DlssKind.Streamline && k != DlssKind.NR && DlssProfile.DriverOverride(prefs, k) ? "NV Override" : detection.Version(k))));
        return Section("NvidiaProfile", title, summary.Length == 0 ? null : summary, body, "NvidiaProfile");
    }

    private StackPanel DlssColumn(Game game, GamePreferences prefs, DlssDetection detection, DlssKind kind)
    {
        var present = detection.Has(kind) && !(kind is DlssKind.SR or DlssKind.RR or DlssKind.Streamline && detection.Version(kind)?.StartsWith("1.") == true);
        var overridable = kind is DlssKind.SR or DlssKind.RR or DlssKind.FG;
        var overrideActive = overridable && DlssProfile.DriverOverride(prefs, kind);
        var col = new StackPanel { Spacing = 3, Opacity = present || overridable ? 1 : 0.4 };
        col.Children.Add(Label(DlssFiles.Label(kind), 11, null, true));

        // Version: Default (original) / versions / Custom / NVIDIA Override
        var items = new List<string>();
        var installed = detection.IsCustom(kind) ? "Custom" : detection.Version(kind);
        if (!present && !overridable) items.Add("None");
        else
        {
            var original = detection.Original(kind);
            items.Add(original != null ? $"Default ({original})" : "Default");
            items.AddRange(_dlss.Versions(kind));
            items.Add("Custom");
            if (overridable) items.Add("NVIDIA Override");
        }
        var index = 0;
        if (overrideActive) index = items.Count - 1;
        else if (installed != null && present && detection.Path(kind) is { } current)
        {
            // Until RHI swaps a file (leaving a ".original"), the game's own DLL is the Default entry.
            var swapped = kind == DlssKind.Streamline ? DlssFiles.Streamline.Any(f => Sentinel.Placed(Path.Combine(current, f))) : Sentinel.Placed(current);
            if (installed == "Custom") index = items.IndexOf("Custom");
            else if (swapped)
            {
                index = items.FindIndex(1, i => i == installed || DlssCatalog.StripSuffix(i) == installed);
                if (index < 0) { index = items.IndexOf("Custom"); items.Insert(index, installed); }
            }
        }
        var version = Combo(items, Math.Max(0, index), "DlssVersion" + DlssFiles.Short(kind));
        version.IsEnabled = present || overridable;
        ToolTip.SetTip(version, overrideActive
            ? "NVIDIA Override is active — the driver supplies its own latest DLL for this game. Select any other version to disable the override and deploy that version instead."
            : "Selects which DLL version is copied into the game folder. Default restores the original game DLL. Custom uses your own file from " + (kind == DlssKind.Streamline ? DlssFiles.CustomStreamlineDirectory : DlssFiles.CustomDirectory) + "." +
              (overridable ? " NVIDIA Override lets the driver supply its latest version instead (Proton NGX updater)." : ""));
        col.Children.Add(Field("Version", version));
        var initial = version.SelectedItem as string;
        version.SelectionChanged += async (_, _) =>
        {
            if (version.SelectedItem is not string selected || selected == initial) return;
            await Run(async () =>
            {
                if (selected == "NVIDIA Override") DlssProfile.SetDriverOverride(prefs, kind, true);
                else
                {
                    if (overrideActive) DlssProfile.SetDriverOverride(prefs, kind, false);
                    if (detection.Path(kind) is not { } target)
                    { if (selected != "None" && !selected.StartsWith("Default")) throw new IOException($"This game has no {DlssFiles.DllName(kind)} to replace."); }
                    else { GameSetup.RequireClosed(game); await _swap.Apply(kind, target, selected, Progress); }
                }
                _settings.Save(); InvalidateDlss(game); await ReadStates(); Filter();
                _status.Text = $"{DlssFiles.Label(kind)}: {selected}";
            });
        };

        // Preset
        if (kind != DlssKind.Streamline && present)
        {
            var presets = DlssProfile.Presets(kind);
            var current = DlssProfile.Preset(prefs, kind);
            var presetCombo = Combo(presets.Select(p => (object)p.Name), Math.Max(0, Array.FindIndex(presets, p => p.Value == current)), "DlssPreset" + DlssFiles.Short(kind));
            ToolTip.SetTip(presetCombo, kind switch
            {
                DlssKind.SR => "Override the DLSS upscaling model. J/K use the 1st-gen transformer (DLSS 4.0). L/M use the 2nd-gen transformer (DLSS 4.5) with better temporal stability. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                DlssKind.RR => "Override the Ray Reconstruction denoising model. Higher presets are newer model iterations. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                DlssKind.FG => "Override the Frame Generation interpolation model. Higher presets are newer model iterations. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                _ => "Override the Neural Rendering model preset."
            });
            presetCombo.SelectionChanged += async (_, _) =>
            {
                var i = presetCombo.SelectedIndex; if (i < 0 || presets[i].Value == current) return;
                DlssProfile.SetPreset(prefs, kind, presets[i].Value); await DriverSettingChanged(game, $"{DlssFiles.Label(kind)} preset: {presets[i].Name}");
            };
            col.Children.Add(Field("Preset", presetCombo));
        }
        else if (kind == DlssKind.NR) col.Children.Add(new Border { Height = 46 });

        // Render scale (SR and RR)
        if (kind is DlssKind.SR or DlssKind.RR && present)
        {
            var options = DlssProfile.RenderScaleOptions;
            var scale = DlssProfile.RenderScale(prefs, kind);
            var labels = options.Select(o => o.Name).ToList();
            var scaleIndex = scale == 0 ? 0 : Array.FindIndex(options, o => o.Value == scale);
            if (scaleIndex < 0) { scaleIndex = labels.Count - 1; labels[^1] = $"Custom ({scale}%)"; }
            var scaleCombo = Combo(labels, scaleIndex, "DlssScale" + DlssFiles.Short(kind));
            ToolTip.SetTip(scaleCombo, "Override the DLSS render resolution scale. Off = game controls the scale.\nNamed presets set a fixed percentage. Custom lets you enter any value from 33-100%.");
            scaleCombo.SelectionChanged += async (_, _) =>
            {
                var i = scaleCombo.SelectedIndex; if (i < 0 || i == scaleIndex) return;
                uint value = options[i].Value;
                if (options[i].Name == "Custom") { if (await AskPercent(scale) is not { } custom) { ShowGame(); return; } value = custom; }
                DlssProfile.SetRenderScale(prefs, kind, value); await DriverSettingChanged(game, $"{DlssFiles.Label(kind)} render scale: {(value == 0 ? "Off" : value + "%")}");
            };
            col.Children.Add(Field("Render Scale", scaleCombo));
        }

        if (kind == DlssKind.FG)
        {
            var mfg = Action("Multi Frame Gen", () => MultiFrameGen(game), "action", "MultiFrameGen");
            mfg.HorizontalAlignment = HorizontalAlignment.Stretch; mfg.IsEnabled = present; mfg.Opacity = present ? 1 : 0.4; mfg.Margin = new Thickness(0, 17, 0, 0);
            ToolTip.SetTip(mfg, "Configure NVIDIA Multi Frame Generation: mode, frame count multiplier, and dynamic target frame rate. Requires 50 Series GPU.");
            col.Children.Add(mfg);
        }
        if (kind == DlssKind.NR)
        {
            var deploy = Action("Deploy DLL", () => DeployNrDll(game, version.SelectedItem as string), "action", "DeployNrDll");
            deploy.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTip.SetTip(deploy, "Download and copy nvngx_dlssnr.dll to the game folder. Supports RTX 40 and 50 Series GPUs. Neural Rendering methods above also deploy it automatically.");
            var delete = Action("✕", async () =>
            {
                if (detection.Path(DlssKind.NR) is not { } path) return;
                if (!Sentinel.Placed(path) && !await Confirm("Delete the game's NR DLL?", "This nvngx_dlssnr.dll came with the game, not from RHI, so there is no backup to restore. Verify the game's files in Steam to get it back.", "Delete")) return;
                GameSetup.RequireClosed(game);
                DlssSwap.Restore(DlssKind.NR, path);
                if (File.Exists(path) && !Sentinel.Placed(path)) File.Delete(path);
                InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "nvngx_dlssnr.dll removed";
            }, "danger", "DeleteNrDll");
            delete.Width = 36; delete.IsVisible = present;
            ToolTip.SetTip(delete, "Delete nvngx_dlssnr.dll from the game folder.");
            var deployRow = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 17, 0, 0) };
            deployRow.Children.Add(deploy); delete.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(delete, 1); deployRow.Children.Add(delete);
            col.Children.Add(deployRow);
            if (!present) foreach (var child in col.Children.Where(c => c != deployRow)) child.Opacity = 0.4;
            col.Opacity = 1;
        }
        if (kind == DlssKind.Streamline)
        {
            var defaults = _settings.DlssDefaults;
            var quick = Action("Quick Apply", () => QuickApply(game), defaults.IsEmpty ? "" : "action", "DlssQuickApply");
            quick.HorizontalAlignment = HorizontalAlignment.Stretch; quick.IsEnabled = !defaults.IsEmpty; quick.Margin = new Thickness(0, 17, 0, 0);
            ToolTip.SetTip(quick, "Apply your configured DLSS/Streamline default versions, presets, and render scales to this game (Settings → DLSS defaults). Downloads versions on-demand if not cached.");
            var restoreEnabled = detection.HasBackup || !DlssProfile.IsDefault(prefs);
            var restore = Action("Restore DLSS/SL", async () =>
            {
                GameSetup.RequireClosed(game);
                DlssSwap.RestoreAll(detection); DlssProfile.Reset(prefs); _settings.Save();
                InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "DLSS and Streamline restored to the game's originals";
            }, restoreEnabled ? "action" : "", "DlssRestoreAll");
            restore.HorizontalAlignment = HorizontalAlignment.Stretch; restore.IsEnabled = restoreEnabled; restore.Margin = new Thickness(0, 8, 0, 0);
            ToolTip.SetTip(restore, "Restore all DLSS and Streamline DLLs to their original game versions and reset presets to Default.");
            col.Children.Add(quick); col.Children.Add(restore);
            if (!present) { col.Opacity = 1; foreach (var child in col.Children.Where(c => c != quick && c != restore)) child.Opacity = 0.4; }
        }
        return col;
    }

    private async Task<uint?> AskPercent(uint current)
    {
        var box = new TextBox { Watermark = "33-100", Text = current > 0 ? current.ToString() : "", MaxLength = 3, Name = "RenderScalePercent" };
        var body = new StackPanel { Spacing = 12, Children = { Label("Enter a render scale from 33 to 100 percent.", 13, Secondary), box } };
        var dialog = Dialog("Custom render scale", body, 420); uint? result = null;
        body.Children.Add(DialogAction("Apply", body, () =>
        {
            if (!uint.TryParse(box.Text, out var value) || value is < 33 or > 100) throw new FormatException("Enter a whole number from 33 to 100.");
            result = value; dialog.Close(); return Task.CompletedTask;
        }));
        await dialog.ShowDialog(this); return result;
    }

    private async Task DeployNrDll(Game game, string? selection)
    {
        GameSetup.RequireClosed(game);
        var detection = NeuralRenderingSetup.Read(game).Detection;
        var target = detection.Path(DlssKind.NR) ?? LinuxPaths.ResolveCase(game.InstallDirectory, DlssFiles.Nr);
        var choice = selection == null || selection.StartsWith("Default") || selection == "None" ? null : selection;
        if (choice == "Custom") await _swap.Apply(DlssKind.NR, target, "Custom", Progress);
        else Sentinel.Deploy(await _dlss.Fetch(DlssKind.NR, choice, Progress), target);
        // The NR runtime loads beside the DLSS SR DLL, so deploy that to the install root too.
        var sr = LinuxPaths.ResolveCase(game.InstallDirectory, DlssFiles.Sr);
        if (!File.Exists(sr) || Sentinel.Placed(sr)) Sentinel.Deploy(await _dlss.Fetch(DlssKind.SR, null, Progress), sr);
        InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "nvngx_dlssnr.dll deployed";
    }

    private async Task QuickApply(Game game)
    {
        GameSetup.RequireClosed(game);
        var prefs = _settings.For(game);
        var detection = NeuralRenderingSetup.Read(game).Detection;
        await ApplyDefaults(game, prefs, detection, _settings.DlssDefaults);
        _settings.Save(); InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "DLSS defaults applied to " + game.Name;
    }

    private async Task ApplyDefaults(Game game, GamePreferences prefs, DlssDetection detection, DlssDefaults defaults)
    {
        foreach (var kind in DlssFiles.Dlls.Append(DlssKind.Streamline))
        {
            if (!detection.Has(kind) || kind is DlssKind.SR or DlssKind.RR or DlssKind.Streamline && detection.Version(kind)?.StartsWith("1.") == true) continue;
            var key = kind.ToString();
            if (defaults.DriverOverrides.Contains(key)) { DlssProfile.SetDriverOverride(prefs, kind, true); continue; }
            if (defaults.Versions.TryGetValue(key, out var version)) await _swap.Apply(kind, detection.Path(kind)!, version, Progress);
            if (kind != DlssKind.Streamline && defaults.Presets.TryGetValue(key, out var preset) && preset != 0) DlssProfile.SetPreset(prefs, kind, preset);
        }
        if (defaults.SrScale != 0 && detection.Has(DlssKind.SR)) DlssProfile.SetRenderScale(prefs, DlssKind.SR, defaults.SrScale);
        if (defaults.RrScale != 0 && detection.Has(DlssKind.RR)) DlssProfile.SetRenderScale(prefs, DlssKind.RR, defaults.RrScale);
    }
}
