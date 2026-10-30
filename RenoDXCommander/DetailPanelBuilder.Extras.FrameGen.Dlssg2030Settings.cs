// DetailPanelBuilder.Extras.FrameGen.Dlssg2030Settings.cs — ⚙ dialog for 20/30 FG Unlock.
// Edits the game's dlssg_sm86.ini. The mod treats a mistyped value as "turn frame generation
// off", so every setting is a dropdown of valid values only.

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private async Task ShowDlssg2030SettingsAsync(string installPath, bool isInstalled)
    {
        var iniPath = string.IsNullOrEmpty(installPath) ? "" : Path.Combine(installPath, Dlssg20_30Service.IniFileName);
        var logPath = string.IsNullOrEmpty(installPath) ? "" : Path.Combine(installPath, Dlssg20_30Service.LogRelPath);
        bool hasIni = isInstalled && File.Exists(iniPath);

        var cog = new CogDialog("20/30 FG Unlock", _window.Content.XamlRoot);
        cog.AddIntro("These settings are saved in the game's dlssg_sm86.ini. Exit the game completely and start it again for changes to apply. " +
                     "Needs NVIDIA driver R580 or newer; on older drivers the mod turns itself off.");
        if (!hasIni)
            cog.AddWarning("Install 20/30 FG Unlock on this game to change these settings.");

        // One labelled dropdown bound to [section] key. Options are (label, ini value).
        ComboBox AddSetting(string label, string help, string section, string key, string fallback,
            (string Label, string Value)[] options, Func<string, bool>? beforeWrite = null)
        {
            var current = hasIni ? Dlssg20_30Service.ReadIniValue(iniPath, section, key) : null;
            var idx = Array.FindIndex(options, o => o.Value.Equals(current ?? fallback, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = Array.FindIndex(options, o => o.Value == fallback);
            return cog.AddCombo(label, help, options.Select(o => o.Label).ToList(), idx, i =>
            {
                var value = options[i].Value;
                if (beforeWrite != null && !beforeWrite(value)) return false;
                try
                {
                    Dlssg20_30Service.WriteIniValue(iniPath, section, key, value);
                    cog.SetSuccess($"Saved {label}. Restart the game to apply.");
                    CrashReporter.Log($"[Dlssg2030Settings] {section}.{key}={value} in '{installPath}'");
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    cog.SetError($"Couldn't save {label}: {ex.Message}");
                    CrashReporter.Log($"[Dlssg2030Settings] Write failed — {ex.Message}");
                    return false;
                }
            }, isEnabled: hasIni);
        }

        AddSetting("Frame Generation",
            "Off leaves the game's own DLSS Frame Generation untouched (no FG on RTX 20/30) without uninstalling.",
            "General", "Enabled", "1",
            new[] { ("On", "1"), ("Off", "0") });

        ComboBox? framesCombo = null;
        bool engineIsXess = hasIni && "XeSS".Equals(Dlssg20_30Service.ReadIniValue(iniPath, "FrameGeneration", "Engine"), StringComparison.OrdinalIgnoreCase);
        AddSetting("Engine",
            "DLSS uses NVIDIA's frame generation model. XeSS uses Intel's model: up to 4X, and it doesn't work with HDR (16-bit) output.",
            "FrameGeneration", "Engine", "DLSS",
            new[] { ("DLSS (default)", "DLSS"), ("XeSS", "XeSS") },
            beforeWrite: value =>
            {
                engineIsXess = value == "XeSS";
                // XeSS can't do 5X/6X: the mod would turn FG off, so drop to 4X
                if (engineIsXess && framesCombo != null && framesCombo.SelectedIndex is 3 or 4)
                    framesCombo.SelectedIndex = 2;
                return true;
            });

        framesCombo = AddSetting("Max Frames",
            "The highest multiplier the game can offer. The game's own menu can still pick a lower one. 6X has to be chosen here.",
            "FrameGeneration", "MaxGeneratedFrames", "3",
            new[] { ("2X", "1"), ("3X", "2"), ("4X (default)", "3"), ("5X", "4"), ("6X", "5"), ("Game decides", "0") },
            beforeWrite: value =>
            {
                if ((value == "4" || value == "5") && engineIsXess)
                {
                    cog.SetError("XeSS goes up to 4X. Switch Engine to DLSS for 5X or 6X.");
                    return false;
                }
                return true;
            });

        AddSetting("Preset",
            "A is the default. B cleans up the HUD in generated frames, but only works if the game provides the extra images (most don't). C is FG Lite: much cheaper to run, slightly lower quality.",
            "DLSSG", "Preset", "A",
            new[] { ("A (default)", "A"), ("B: HUD recomposition", "B"), ("C: FG Lite", "C") });

        AddSetting("Speed Tier",
            "How far the image may differ from NVIDIA's official output. Exact tiers match it bit for bit; Fast and Fastest are a few percent quicker with tiny differences.",
            "DLSSG", "Optimized", "0",
            new[] { ("Exact", "0"), ("Exact, optimised (default)", "1"), ("Fast", "2"), ("Fastest", "3") });

        AddSetting("Super Resolution",
            "RTX 20/30 only. Pins DLSS preset M or L. INT8 uses the mod's faster kernels: quicker in Performance and Ultra Performance, slower than the game's default in Quality, Balanced and DLAA. No effect while Ray Reconstruction is on.",
            "SuperResolution", "Mode", "Off",
            new[] { ("Off (default)", "Off"), ("M-INT8", "M-INT8"), ("L-INT8", "L-INT8"), ("M-FP16", "M-FP16"), ("L-FP16", "L-FP16") });

        cog.AddSection("Files");
        cog.AddButtons(cog.MakeOpenFileButton("Open dlssg_sm86.ini", iniPath), cog.MakeOpenFileButton("Open log", logPath));

        await cog.ShowAsync();
    }
}
