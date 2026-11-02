// MainWindow.Events.Components.DisplayCommander.cs — Display Commander (DC) cog dialog.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const string DcSection = "DisplayCommander";

    /// <summary>
    /// Display Commander ⚙: the [DisplayCommander] settings worth choosing before launch, written to the
    /// game's DisplayCommander.ini. Labels match Display Commander's own menu. Defaults are the mod's own
    /// (settings/main_tab_settings.cpp and advanced_tab_settings.cpp in pmnoxx/display-commander).
    /// </summary>
    private async void DcCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;

            // DisplayCommander.ini lives in the addon deploy path (honours AddonPath in reshade.ini), same as CopyDcIni.
            // Re-resolved on every write, as AddonPath may change while the dialog is open.
            string? CurrentIni() => string.IsNullOrEmpty(card.InstallPath)
                ? null
                : Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), "DisplayCommander.ini");
            var dcIniFile = CurrentIni();

            string? Read(string key)
            {
                try { return dcIniFile != null ? Dlssg20_30Service.ReadIniValue(dcIniFile, DcSection, key) : null; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[MainWindow.DcCogButton_Click] Couldn't read {key} — {ex.Message}");
                    return null;
                }
            }
            int ReadInt(string key, int fallback) => int.TryParse(Read(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            bool ReadBool(string key) => ReadInt(key, 0) != 0;
            double fpsLimit = double.TryParse(Read("fps_limit"), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0 ? f : 0;

            var cog = new CogDialog("Display Commander", Content.XamlRoot);

            // Writes one key; shows the result in the dialog. Returns false (pick reverted) on failure.
            bool Write(string key, string value, string label, string shown)
            {
                var iniFile = CurrentIni();
                if (iniFile == null || !Directory.Exists(Path.GetDirectoryName(iniFile)))
                {
                    cog.SetError("The game folder wasn't found.");
                    return false;
                }
                try
                {
                    Dlssg20_30Service.WriteIniValue(iniFile, DcSection, key, value);
                    cog.SetSuccess($"{label}: {shown}.");
                    CrashReporter.Log($"[MainWindow.DcCogButton_Click] {key}={value} for '{card.GameName}'");
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    cog.SetError($"Couldn't save {label}: {ex.Message}");
                    CrashReporter.Log($"[MainWindow.DcCogButton_Click] {key} write failed — {ex.Message}");
                    return false;
                }
            }
            CogDialog.CompactCell OnOff(string key, string label, string help) =>
                cog.CompactCombo(label, help, new[] { "Off", "On" }, ReadBool(key) ? 1 : 0,
                    i => Write(key, i == 1 ? "1" : "0", label, i == 1 ? "On" : "Off"));

            cog.AddIntro("Saved in this game's DisplayCommander.ini. Display Commander saves its settings while the game runs, " +
                         "so change these with the game closed. Everything else is in its in-game menu.");
            if (card.IsRunning)
                cog.AddWarning("The game is running. Display Commander may overwrite changes made now; close the game first.");

            // ── Frame rate ─────────────────────────────────────────────────────────
            cog.AddSection("Frame Rate");
            AddDcFpsLimitRow(cog, fpsLimit, fps => Write("fps_limit",
                fps.ToString("0.###", CultureInfo.InvariantCulture), "FPS Limit",
                fps == 0 ? "No limit" : $"{fps.ToString("0.###", CultureInfo.InvariantCulture)} FPS"));

            var limiterModes = new[] { "DC's fps limiter", "NVIDIA Reflex" };
            var windowModes = new[] { "No changes", "Borderless fullscreen", "Borderless windowed", "No exclusive fullscreen" };
            int windowMode = Math.Clamp(ReadInt("WindowMode", 0), 0, windowModes.Length - 1);
            cog.AddCompactGrid(
                cog.CompactCombo("FPS Limiter Mode",
                    "How Display Commander limits the frame rate. NVIDIA Reflex works in DX11 and DX12 games; Vulkan games need the game's own Reflex.",
                    limiterModes, Math.Clamp(ReadInt("fps_limiter_mode", 0), 0, 1),
                    i => Write("fps_limiter_mode", i.ToString(CultureInfo.InvariantCulture), "FPS Limiter Mode", limiterModes[i])),
                OnOff("inject_reflex", "Inject Reflex",
                    "When the game has no Reflex of its own, use Display Commander's Reflex (sleep and latency markers) for lower latency."),
                cog.CompactCombo("Window Mode",
                    "No changes: leave the window alone. Borderless fullscreen / windowed: resize the game's window. " +
                    "No exclusive fullscreen: stop the game switching to exclusive fullscreen, without resizing.",
                    windowModes, windowMode,
                    i => Write("WindowMode", i.ToString(CultureInfo.InvariantCulture), "Window Mode", windowModes[i])),
                null);

            // ── Reflex (Advanced tab, "NVIDIA Reflex (Minimal)") ──────────────────
            cog.AddSection("Reflex");
            cog.AddCompactGrid(
                OnOff("ReflexAutoConfigure", "Auto Configure Reflex", "Configure Reflex automatically when the game starts."),
                OnOff("ReflexEnableSleep", "Enable Reflex Sleep Mode", "Let Display Commander make Reflex sleep calls. Off by default for safety."),
                OnOff("ReflexUseMarkers", "Use Reflex Markers", "Tell NVIDIA Reflex to use latency markers."),
                OnOff("ReflexGenerateMarkers", "Generate Reflex Markers", "Add markers to the frame timeline for latency measurement."));

            // ── Other ─────────────────────────────────────────────────────────────
            cog.AddSection("Other");
            var graceSeconds = new[] { 0, 1, 2, 5, 10, 61 };
            var graceLabels = new[] { "Off", "1 second", "2 seconds", "5 seconds", "10 seconds", "Always" };
            int grace = ReadInt("WinUpGraceSeconds", 1);
            int graceIdx = Array.IndexOf(graceSeconds, grace);
            var graceItems = graceLabels.ToList();
            if (graceIdx < 0) { graceItems.Add($"{grace} seconds"); graceIdx = graceItems.Count - 1; }
            cog.AddCompactGrid(
                cog.CompactCombo("Win+Up grace time",
                    "How long after you switch away from a borderless game Win+Up still restores its window. " +
                    "Not in Display Commander's menu; only set in its ini.",
                    graceItems, graceIdx, i =>
                    {
                        if (i >= graceSeconds.Length) return true; // existing custom value
                        return Write("WinUpGraceSeconds", graceSeconds[i].ToString(CultureInfo.InvariantCulture), "Win+Up grace time", graceLabels[i]);
                    }),
                null);
            cog.AddNote("Target Display and Display Commander's ReShade options (global shaders path, screenshots path) are left to its in-game menu. " +
                        "RHI manages ReShade's shader and screenshot folders itself.");

            // ── Files ─────────────────────────────────────────────────────────────
            cog.AddSection("Files");
            var openIniBtn = cog.MakeOpenFileButton("Open DisplayCommander.ini", dcIniFile);
            var deployBtn = cog.MakeButton("Deploy DisplayCommander.ini", CogButtonKind.Primary, () =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                AuxInstallService.CopyDcIni(card.InstallPath);
                openIniBtn.IsEnabled = dcIniFile != null && File.Exists(dcIniFile);
                cog.SetSuccess("DisplayCommander.ini copied to the game folder. Close and reopen this dialog to see its settings.");
                CrashReporter.Log($"[MainWindow.DcCogButton_Click] Deployed DisplayCommander.ini for '{card.GameName}'");
            }, "Copies RHI's DisplayCommander.ini template into the game folder, replacing the game's current one.");
            cog.AddButtons(deployBtn, openIniBtn);

            // No default button: Enter in the FPS box must not close the dialog
            cog.Build().DefaultButton = ContentDialogButton.None;
            await cog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DcCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// FPS Limit dropdown (No limit, VRR presets, Custom...) with an inline box for a custom value.
    /// Display Commander stores a decimal (e.g. 239.99), so custom values may have decimals.
    /// </summary>
    private static void AddDcFpsLimitRow(CogDialog cog, double current, Func<double, bool> write)
    {
        var presets = VrrFpsPresets;
        string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        List<string> Items(double fps)
        {
            var items = new List<string> { "No limit" };
            items.AddRange(presets.Select(p => p.Label));
            if (fps > 0 && !presets.Any(p => p.Fps == fps)) items.Add($"{Fmt(fps)} (Custom)");
            items.Add("Custom...");
            return items;
        }
        int Index(List<string> items, double fps)
        {
            if (fps <= 0) return 0;
            int i = Array.FindIndex(presets, p => p.Fps == fps);
            return i >= 0 ? i + 1 : items.Count - 2;
        }

        var items0 = Items(current);
        var combo = CogDialog.MakeCombo(items0, Index(items0, current));
        cog.AddRow("FPS Limit", combo,
            "Frame rate cap for this game. Each VRR preset sits just under that refresh rate. Custom... lets you type your own value.");

        var box = CogDialog.MakeTextBox("", "20-1000, decimals allowed");
        var setBtn = CogDialog.StyledButton("Set", CogButtonKind.Secondary, compact: true);
        var customRow = cog.AddRow("Custom FPS", CogDialog.Inline(box, setBtn), "Type a frame rate from 20 to 1000, then press Set or Enter.");
        customRow.Visibility = Visibility.Collapsed;

        bool suppress = false;
        void Refresh(double fps)
        {
            suppress = true;
            current = fps;
            var items = Items(fps);
            combo.ItemsSource = items;
            combo.SelectedIndex = Index(items, fps);
            customRow.Visibility = Visibility.Collapsed;
            suppress = false;
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (suppress) return;
            if (combo.SelectedItem as string == "Custom...")
            {
                customRow.Visibility = Visibility.Visible;
                box.Text = "";
                box.Focus(FocusState.Programmatic);
                return;
            }
            customRow.Visibility = Visibility.Collapsed;
            int idx = combo.SelectedIndex;
            double fps;
            if (idx == 0) fps = 0;
            else if (idx > 0 && idx - 1 < presets.Length) fps = presets[idx - 1].Fps;
            else return; // the "(Custom)" item: already the current value
            if (write(fps)) current = fps;
            else Refresh(current);
        };

        void SetCustom()
        {
            var text = box.Text.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) || fps < 20 || fps > 1000)
            {
                cog.SetError("Enter a frame rate from 20 to 1000.");
                return;
            }
            fps = Math.Round(fps, 3);
            if (write(fps)) Refresh(fps);
        }
        setBtn.Click += (_, _) => SetCustom();
        box.KeyDown += (_, ev) =>
        {
            if (ev.Key != Windows.System.VirtualKey.Enter) return;
            ev.Handled = true;
            SetCustom();
        };
    }
}
