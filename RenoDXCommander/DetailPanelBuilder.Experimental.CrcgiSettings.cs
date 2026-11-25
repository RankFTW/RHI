// DetailPanelBuilder.Experimental.CrcgiSettings.cs -- CRCGI's ⚙: the effect's main settings and quality switches in the
// game's ReShade preset (compact grid, help in tooltips), the add-on's level (crcgi_injector.ini next to the game exe),
// the author's ready-made preset for this game when the repo has one for the installed version, and a reset to CRCGI's
// own defaults. The ~140 Expert settings and the add-on's buffer setup stay in ReShade's overlay (tuned per game while
// watching it). Writes go through CrcgiService.Settings.cs; values follow ReShade's own format (floats as 1.000000).

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>One ⚙ setting: a uniform (key in the effect's section) or a compile-time switch (IsDefine).</summary>
    private sealed record CrcgiSetting(string Label, string Help, string Key, bool IsDefine, string[] Displays, string[] Values, string Default);

    private static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
    private static readonly string[] CrcgiOffOn = { "Off", "On" };
    private static readonly string[] CrcgiZeroOne = { "0", "1" };

    /// <summary>Defaults and ranges from CRCGI_v2.fx (README: every setting at 1 or half its range).</summary>
    private static readonly CrcgiSetting[] CrcgiSettings =
    {
        new("GI Intensity", "How strong the bounced light is. 1 is CRCGI's default; above 2 is very strong.",
            "Intensity", false, new[] { "0.5", "0.75", "1 (default)", "1.25", "1.5", "2", "3" },
            new[] { F(0.5), F(0.75), F(1), F(1.25), F(1.5), F(2), F(3) }, F(1)),
        new("Shadow Amount", "Soft contact shadows in crevices and corners. Below 0 lights them instead of darkening them.",
            "ShadowAmount", false, new[] { "-0.5 (lighter)", "0 (off)", "0.5", "1 (default)" },
            new[] { F(-0.5), F(0), F(0.5), F(1) }, F(1)),
        new("Multibounce", "Light keeps bouncing from surface to surface over a few frames, filling rooms more evenly.",
            "Multibounce", false, CrcgiOffOn, CrcgiZeroOne, "1"),
        new("Multibounce strength", "How much light each extra bounce passes on.",
            "MultibounceAmount", false, new[] { "0.5", "1 (default)", "1.5", "2" },
            new[] { F(0.5), F(1), F(1.5), F(2) }, F(1)),
        new("Sky Light", "Adds ambient light from the sky to open areas (its strength and colour are in the Sky settings in the overlay).",
            "ProcessSkyColor", false, CrcgiOffOn, CrcgiZeroOne, "0"),
        new("Dark Area Boost", "Lifts the bounced light in dark scenes.",
            "DarkAreaBoost", false, new[] { "0 (default)", "0.5", "1", "2" },
            new[] { F(0), F(0.5), F(1), F(2) }, F(0)),
        new("Frames Accumulated", "How many frames the light is averaged over: more is smoother and less noisy, fewer reacts faster with less trailing.",
            "TemporalFrames", false, new[] { "8", "16 (default)", "32", "64" },
            new[] { F(8), F(16), F(32), F(64) }, F(16)),
        new("Far cascades at half rate", "Traces half of the far light per frame: faster, with a little more noise far away.",
            "FarHalfRate", false, CrcgiOffOn, CrcgiZeroOne, "0"),
        new("Effect resolution", "The resolution the light is worked out at (the biggest performance lever). Higher costs much more GPU time.",
            "CRCGI_RESOLUTION_DIVISOR", true, new[] { "Full", "Half", "Third", "Quarter (default)", "Fifth", "Sixth" },
            new[] { "1", "2", "3", "4", "5", "6" }, "4"),
        new("Cascades", "How far the light reaches: each extra cascade adds a longer range, more rays and more memory.",
            "CRCGI_CASCADES", true, new[] { "4", "5 (default)", "6" },
            new[] { "4", "5", "6" }, "5"),
    };

    private static int CrcgiIndex(string[] values, string? current, string def)
    {
        var v = current ?? def;
        for (int i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i], v, StringComparison.OrdinalIgnoreCase)) return i;
            if (double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && Math.Abs(a - b) < 1e-4)
                return i;
        }
        return -1;
    }

    internal async Task ShowCrcgiSettingsAsync(GameCardViewModel card)
    {
        var installPath = card.InstallPath ?? "";
        bool isInstalled = CrcgiService.IsInstalledIn(installPath);
        bool isOurs = isInstalled && CrcgiService.IsRhiInstalled(installPath);
        var svc = App.Services.GetRequiredService<CrcgiService>();
        var section = isInstalled ? CrcgiService.EffectSection(installPath) : null;
        bool canEdit = section != null;
        bool reopen = false;

        // Gathered before the dialog: the preset text, the add-on's level, the author's preset for this game (GitHub)
        var presetText = canEdit ? await Task.Run(() => CrcgiService.ReadPreset(installPath)) : null;
        var exeDir = isInstalled ? await Task.Run(() => CrcgiService.ExeDir(installPath, App.Services.GetRequiredService<IPeHeaderService>())) : installPath;
        int level = isInstalled ? CrcgiService.ReadInjectorLevel(exeDir) : 0;
        CrcgiService.AuthorPresetSet? authorSet = null;
        if (canEdit)
        {
            var lookup = svc.FindAuthorPresetsAsync(card.GameName, section);
            if (await Task.WhenAny(lookup, Task.Delay(5000)) == lookup) authorSet = await lookup;
        }

        var cog = new CogDialog("CRCGI", _window.Content.XamlRoot);
        cog.AddIntro("CRCGI adds global illumination: light bouncing off lit surfaces onto nearby ones, and soft contact shadows. "
            + "It's a ReShade effect built on radiance cascades; its CRCGI Injector add-on hands it the game's own normals, colours, motion and depth "
            + "and keeps the UI and fog out of it. It costs GPU time.");
        cog.AddWarning("Single player only: the add-on hooks the game's rendering. Never use it online.");
        if (card.IsRunning)
            cog.AddWarning("The game is running: ReShade reads these at the next start (or reload the preset in ReShade). Saving in ReShade's overlay overwrites them.");

        cog.AddSection("This game");
        cog.AddNote(!isInstalled ? "Not installed. Install CRCGI to change its settings here."
            : !isOurs ? "Installed by hand (not by RHI). Install replaces it with the latest release and RHI keeps it updated."
            : $"Installed: v{CrcgiService.InstalledVersion(installPath) ?? "?"}, {section} and the {(card.Is32Bit ? "32" : "64")}-bit add-on. "
              + "RHI turned the effect on in the game's ReShade preset and turned on ReShade's Generic Depth add-on, which it reads depth through.");

        // ── Author's preset ───────────────────────────────────────────────────
        if (authorSet != null)
        {
            cog.AddSection("Author's preset");
            cog.AddNote($"CRCGI's author made a preset for this game ({authorSet.Folder}). It sets every CRCGI value in this game's ReShade preset "
                + "(RHI backs the preset up once first); your other effects stay as they are."
                + (authorSet.Targets != null ? $" It also copies {authorSet.Targets.Name} next to the game's exe: import it in ReShade › Add-ons › CRCGI Injector › Options › Import." : ""));
            int pick = 0;
            var names = authorSet.Presets.Select(p => Path.GetFileNameWithoutExtension(p.Name)).ToList();
            if (names.Count > 1)
                cog.AddCompactGrid(cog.CompactCombo("Preset", "The author's presets for this CRCGI version.", names, 0, i => { pick = i; return true; }));
            else
                cog.AddNote(names[0]);
            var buttons = new List<Microsoft.UI.Xaml.Controls.Button>();
            Microsoft.UI.Xaml.Controls.Button? applyBtn = null;
            applyBtn = cog.MakeButton("Use the author's preset", CogButtonKind.Primary, async () =>
            {
                applyBtn!.IsEnabled = false;
                cog.SetStatus("Downloading the preset...");
                var err = await svc.ApplyAuthorPresetAsync(installPath, authorSet.Presets[pick], authorSet.Targets, exeDir,
                    App.Services.GetRequiredService<UpliftService>());
                applyBtn.IsEnabled = true;
                if (err != null) { cog.SetError(err); return; }
                reopen = true; // show the new values
                cog.Hide();
            });
            buttons.Add(applyBtn);
            if (authorSet.ReadmeUrl != null)
                buttons.Add(cog.MakeButton("Preset notes", CogButtonKind.Secondary,
                    async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(authorSet.ReadmeUrl)); }));
            cog.AddButtons(buttons.ToArray());
        }

        // ── Main settings + quality ───────────────────────────────────────────
        CogDialog.CompactCell Cell(CrcgiSetting s)
        {
            var current = !canEdit ? null
                : s.IsDefine ? CrcgiPreset.GetDefine(presetText, section!, s.Key)
                : CrcgiPreset.GetValue(presetText, section!, s.Key);
            var displays = s.Displays;
            var values = s.Values;
            int idx = CrcgiIndex(values, current, s.Default);
            if (idx < 0 && current != null)
            {
                // A value set in the overlay that isn't in the list: show it as it is
                displays = displays.Append(current).ToArray();
                values = values.Append(current).ToArray();
                idx = values.Length - 1;
            }
            if (idx < 0) idx = Math.Max(0, Array.IndexOf(values, s.Default));
            return cog.CompactCombo(s.Label, s.Help, displays, idx, i =>
            {
                if (!canEdit) return false;
                var value = values[i];
                bool ok = svc.EditPreset(installPath,
                    t => s.IsDefine ? CrcgiPreset.SetDefine(t, section!, s.Key, value) : CrcgiPreset.SetValue(t, section!, s.Key, value),
                    $"{s.Key}={value}");
                if (ok) cog.SetSuccess(card.IsRunning ? "Saved. Reload the preset in ReShade (or restart the game) to see it." : "Saved.");
                else cog.SetError("Couldn't write the game's ReShade preset. Close the game and try again.");
                return ok;
            }, isEnabled: canEdit);
        }

        cog.AddSection("Light");
        cog.AddCompactGrid(CrcgiSettings.Where(s => !s.IsDefine && s.Key is not ("TemporalFrames" or "FarHalfRate")).Select(Cell).ToArray());
        cog.AddSection("Quality and speed");
        cog.AddNote("Changing these makes ReShade recompile the effect (a short pause).");
        cog.AddCompactGrid(CrcgiSettings.Where(s => s.IsDefine || s.Key is "TemporalFrames" or "FarHalfRate").Select(Cell).ToArray());

        // ── Add-on ────────────────────────────────────────────────────────────
        cog.AddSection("CRCGI Injector (add-on)");
        cog.AddCompactGrid(cog.CompactCombo("Level",
            "A: the effect runs at the end of the frame like any ReShade effect (safest; the add-on changes nothing). "
            + "B: the add-on runs it inside the frame, before the UI and fog, with the game's exact depth and camera. "
            + "C: B plus the game's own normals, colours, motion vectors and HDR light, picked in ReShade › Add-ons › CRCGI Injector. "
            + "Saved in crcgi_injector.ini next to the game's exe; the add-on reads it at game start.",
            new[] { "A (default)", "B", "C" }, level, i =>
            {
                if (!isInstalled) return false;
                bool ok = svc.WriteInjectorLevel(exeDir, i);
                if (ok) cog.SetSuccess("Saved. The add-on uses it from the next game start.");
                else cog.SetError("Couldn't write crcgi_injector.ini next to the game's exe.");
                return ok;
            }, isEnabled: isInstalled));
        cog.AddNote("Which of the game's buffers feed the effect is picked in the game (ReShade › Add-ons › CRCGI Injector); the README's first-run steps walk through it.");

        // ── Files / reset ─────────────────────────────────────────────────────
        cog.AddSection("More");
        cog.AddNote("The other settings (about 140, mostly Expert: hit detection, depth, sky, colours, denoise) are in ReShade's overlay under CRCGI v2. "
            + "Reset puts every value RHI can change back to CRCGI's defaults; values you set in the overlay that RHI doesn't show are kept.");
        var resetBtn = cog.MakeButton("Reset to CRCGI's defaults", CogButtonKind.Secondary, () =>
        {
            if (!canEdit) return;
            bool ok = svc.EditPreset(installPath, t =>
            {
                var r = CrcgiPreset.RemoveKeys(t, section!, CrcgiSettings.Where(s => !s.IsDefine).Select(s => s.Key));
                foreach (var d in CrcgiSettings.Where(s => s.IsDefine)) r = CrcgiPreset.SetDefine(r, section!, d.Key, null);
                return r;
            }, "Reset to defaults");
            if (ok) { reopen = true; cog.Hide(); }
            else cog.SetError("Couldn't write the game's ReShade preset. Close the game and try again.");
        });
        resetBtn.IsEnabled = canEdit;
        cog.AddButtons(resetBtn,
            cog.MakeOpenFileButton("Open ReShade preset", canEdit ? CrcgiService.PresetFile(installPath) : null),
            cog.MakeButton("Open README", CogButtonKind.Secondary,
                async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(CrcgiService.ReadmeUrl)); }));

        await cog.ShowAsync();
        if (reopen) await ShowCrcgiSettingsAsync(card);
    }
}
