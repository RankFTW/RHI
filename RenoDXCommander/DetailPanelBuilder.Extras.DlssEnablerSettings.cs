// DetailPanelBuilder.Extras.DlssEnablerSettings.cs — ⚙ for the standalone DLSS Enabler: its frame generation settings
// in dlss-enabler.ini (beside its DLL), set before the game starts. Help texts follow the comments DLSS Enabler
// 4.10.0.23 writes in that file. The rest (ray tracing, colour grade, Reflex, debug) stays in its in-game menu (~).

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private static readonly string[] DeOffOn = { "Off", "On" };
    private static readonly string[] DeOffOnValues = { "false", "true" };

    /// <summary>⚙ dialog for the standalone DLSS Enabler. Every change is saved as soon as it's made.</summary>
    private async Task ShowDlssEnablerSettingsAsync(GameCardViewModel card, DlssEnablerService svc, string installPath, bool isInstalled)
    {
        var gameName = card.GameName;
        bool canEdit = isInstalled && !string.IsNullOrEmpty(installPath);
        string? cfgPath = canEdit ? DlssEnablerConfig.PathIn(installPath) : null;
        string cfgText = "";
        if (canEdit)
        {
            // Installed before RHI wrote the file at install: create the defaults now so there is something to change
            cfgText = await Task.Run(() =>
            {
                svc.EnsureStandaloneConfig(installPath);
                try { return File.Exists(cfgPath) ? File.ReadAllText(cfgPath!) : ""; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[DlssEnablerCog] Couldn't read '{cfgPath}' — {ex.Message}");
                    return "";
                }
            });
            if (cfgText.Length == 0) canEdit = false;
        }

        var cog = new CogDialog("DLSS Enabler", _window.Content.XamlRoot);
        cog.AddIntro("Set frame generation before you play. These are saved in dlss-enabler.ini beside DLSS Enabler; "
                     + "its in-game menu (~ by default) changes the same settings, plus ray tracing, colour, Reflex and debug options. "
                     + "DLSS Enabler answers the game's own DLSS Frame Generation, so the game needs native DLSS-G (DirectX 12).");
        if (!isInstalled)
            cog.AddNote("Install DLSS Enabler to change these settings. RHI then creates dlss-enabler.ini with DLSS Enabler's defaults.");
        else if (!canEdit)
            cog.AddWarning("Couldn't read or create dlss-enabler.ini in the game folder. Check that the folder can be written to.");
        if (card.IsRunning)
            cog.AddWarning("The game is running. DLSS Enabler rewrites dlss-enabler.ini while it runs, so change these with the game closed.");

        // One dropdown bound to one value in dlss-enabler.ini. A current value not in the list is shown as an extra item.
        CogDialog.CompactCell Choice(string label, string help, string section, string key,
            string[] displays, string[] values, string defaultValue)
        {
            var current = canEdit ? DlssEnablerConfig.GetFromText(cfgText, section, key) : null;
            var d = displays.ToList();
            var v = values.ToList();
            if (current != null && !v.Contains(current, StringComparer.OrdinalIgnoreCase)) { d.Add(current); v.Add(current); }
            int idx = v.FindIndex(x => string.Equals(x, current ?? defaultValue, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = v.FindIndex(x => string.Equals(x, defaultValue, StringComparison.OrdinalIgnoreCase));
            return cog.CompactCombo(label, help, d, idx, i =>
            {
                if (!canEdit) return false;
                DlssEnablerConfig.Set(cfgPath!, section, key, v[i]);
                CrashReporter.Log($"[DlssEnablerCog] '{gameName}': {section}.{key} = {v[i]}");
                cog.SetSuccess("Saved. Applies the next time the game starts.");
                return true;
            }, isEnabled: canEdit);
        }

        cog.AddSection("Frame generation");
        cog.AddCompactGrid(
            Choice("Forced FG activation",
                "Loads DLSS frame generation even when the game doesn't ask for it, in Streamline 2 games whose frame generation option doesn't appear or stays off. Highly experimental: needs Reflex on in the game. It can't add frame generation to games without Streamline.",
                "Performance", "ForceLoadDLSSG", DeOffOn, DeOffOnValues, "false"),
            Choice("Multiplier",
                "Forces the frame generation multiplier. Game decides keeps the game's own choice; Off (1x) turns frame generation off. 5x and 6x need Streamline (sl.dlss_g) 2.11 or newer.",
                "Performance", "MFGOverrideMode",
                new[] { "Game decides", "Off (1x)", "2x", "3x", "4x", "5x", "6x" },
                new[] { "0", "1", "2", "3", "4", "5", "6" }, "0"),
            Choice("Dynamic MFG",
                "Picks the multiplier automatically from the base frame rate, aiming for the target below.",
                "Performance", "DynamicMFG", DeOffOn, DeOffOnValues, "false"),
            Choice("Dynamic target FPS",
                "Output frame rate Dynamic MFG aims for (30 to 300). Only used with Dynamic MFG on.",
                "Performance", "DFGTargetFps",
                new[] { "60", "90", "120", "144", "165", "180", "200", "240", "300" },
                new[] { "60", "90", "120", "144", "165", "180", "200", "240", "300" }, "120"),
            Choice("MFG hotkeys",
                "Ctrl+1 to Ctrl+6 switch the multiplier while playing, no restart needed.",
                "Performance", "MFGHotkeys", DeOffOn, DeOffOnValues, "false"),
            Choice("Generator",
                "DLSS-G uses NVIDIA's frame generation where it can. FSR only forces AMD FSR frame generation instead, bypassing DLSS-G.",
                "Debug", "UseFsrOnly", new[] { "DLSS-G", "FSR only" }, DeOffOnValues, "false"),
            Choice("HUD detection",
                "How the HUD is kept out of generated frames. Game uses what the game provides.",
                "NGX", "HudDetectionMode", new[] { "Game", "Off", "On" }, new[] { "0", "1", "2" }, "0"),
            Choice("GhostBuster",
                "Anti-ghosting correction for generated frames.",
                "GhostBuster", "Enabled", DeOffOn, DeOffOnValues, "true"));

        cog.AddSection("Overlay");
        cog.AddCompactGrid(
            Choice("Status bar",
                "DLSS Enabler's performance monitoring bar (Ctrl + ~ toggles it in game).",
                "UI", "Monitoring", DeOffOn, DeOffOnValues, "true"),
            Choice("Menu key",
                "Key that opens DLSS Enabler's in-game menu. Insert opens the OptiScaler menu built into it (and RTX Encore's menu), so it isn't offered here.",
                "UI", "ToggleKey",
                new[] { "~ (Tilde)", "F9", "F10", "F11", "F12" },
                new[] { "Tilde", "F9", "F10", "F11", "F12" }, "Tilde"));

        cog.AddButtons(cog.MakeOpenFileButton("Open dlss-enabler.ini", canEdit ? cfgPath : null));
        await cog.ShowAsync();
    }
}
