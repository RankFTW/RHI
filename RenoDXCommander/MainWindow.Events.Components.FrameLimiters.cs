// MainWindow.Events.Components.FrameLimiters.cs — ReLimiter (UL) and Display Commander (DC) cog dialog handlers.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private async void UlCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            // relimiter.ini lives in the addon deploy path (honours AddonPath in reshade.ini)
            string? ulIniFile = string.IsNullOrEmpty(card.InstallPath)
                ? null
                : Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), "relimiter.ini");
            bool ulIniExists = ulIniFile != null && File.Exists(ulIniFile);

            // Re-resolved on every write, as before (AddonPath may change while the dialog is open)
            string? CurrentUlIniFile() => string.IsNullOrEmpty(card.InstallPath)
                ? null
                : Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), "relimiter.ini");

            // Find the relimiter log file (relimiter_*.log)
            string? logFile = null;
            if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
            {
                try
                {
                    logFile = Directory.GetFiles(card.InstallPath, "relimiter_*.log").FirstOrDefault();
                }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.UlCogButton_Click] Failed to locate relimiter log file — {ex.Message}"); }
            }

            // Read current per-game values from the game's relimiter.ini
            int currentTargetFps = 0;
            bool currentDlssHooks = ViewModel.Settings.UlDlssHooks; // default to global
            if (ulIniExists)
            {
                try
                {
                    var ulIni = AuxInstallService.ParseIni(File.ReadAllLines(ulIniFile!));
                    if (ulIni.TryGetValue("FrameLimiter", out var flSection))
                    {
                        if (flSection.TryGetValue("target_fps", out var fpsVal) && int.TryParse(fpsVal, out var parsedFps))
                            currentTargetFps = parsedFps;
                        if (flSection.TryGetValue("dlss_info_hooks", out var hooksVal))
                            currentDlssHooks = hooksVal.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.UlCogButton_Click] Failed to read relimiter.ini, using defaults — {ex.Message}"); }
            }

            var cog = new CogDialog("ReLimiter", Content.XamlRoot);

            // ── Frame Limiter ─────────────────────────────────────────────────────
            cog.AddSection("Frame Limiter");
            if (!ulIniExists)
                cog.AddWarning("Deploy relimiter.ini (under Files) to change these settings.");

            // VRR preset options (same as global settings)
            var vrrPresets = new (int Fps, string Label)[]
            {
                (59,  "59 (60Hz VRR)"),
                (73,  "73 (75Hz VRR)"),
                (97,  "97 (100Hz VRR)"),
                (116, "116 (120Hz VRR)"),
                (138, "138 (144Hz VRR)"),
                (157, "157 (165Hz VRR)"),
                (171, "171 (180Hz VRR)"),
                (189, "189 (200Hz VRR)"),
                (224, "224 (240Hz VRR)"),
                (258, "258 (280Hz VRR)"),
                (275, "275 (300Hz VRR)"),
                (324, "324 (360Hz VRR)"),
                (416, "416 (480Hz VRR)"),
                (431, "431 (500Hz VRR)"),
            };
            var vrrFpsSet = new HashSet<int>(vrrPresets.Select(p => p.Fps));

            // Items: Off, presets, "<n> (Custom)" when the current value isn't a preset, "Custom..."
            List<string> BuildFpsItems(int fps)
            {
                var items = new List<string> { "Off" };
                items.AddRange(vrrPresets.Select(p => p.Label));
                if (fps > 0 && !vrrFpsSet.Contains(fps))
                    items.Add($"{fps} (Custom)");
                items.Add("Custom...");
                return items;
            }
            int FpsIndex(List<string> items, int fps)
            {
                if (fps == 0) return 0; // Off
                int matchIdx = Array.FindIndex(vrrPresets, p => p.Fps == fps);
                return matchIdx >= 0 ? matchIdx + 1 : items.Count - 2; // +1 for "Off"; custom item sits before "Custom..."
            }

            var fpsItems = BuildFpsItems(currentTargetFps);
            var targetFpsCombo = CogDialog.MakeCombo(fpsItems, FpsIndex(fpsItems, currentTargetFps));
            bool suppressFpsChange = false;

            var targetFpsRow = cog.AddRow("Target FPS", targetFpsCombo,
                "Frame rate cap for this game, saved in its relimiter.ini. Each VRR preset sits just under that refresh rate. " +
                "Off removes the cap; Custom... lets you type your own value.");

            // Inline custom FPS input (shown when "Custom..." is selected; no nested dialogs)
            var customFpsBox = CogDialog.MakeTextBox("", "20-1000");
            var customFpsBtn = CogDialog.StyledButton("Set", CogButtonKind.Secondary, compact: true);
            var customFpsRow = cog.AddRow("Custom FPS", CogDialog.Inline(customFpsBox, customFpsBtn),
                "Type a frame rate from 20 to 1000, then press Set or Enter.");
            customFpsRow.Visibility = Visibility.Collapsed;

            // Rebuild the combo after setting a custom value
            void RefreshFpsCombo(int newFps)
            {
                suppressFpsChange = true;
                currentTargetFps = newFps;
                var items = BuildFpsItems(newFps);
                targetFpsCombo.ItemsSource = items;
                targetFpsCombo.SelectedIndex = FpsIndex(items, newFps);
                customFpsRow.Visibility = Visibility.Collapsed;
                suppressFpsChange = false;
            }

            // Writes target_fps; returns false (with the error shown) on failure
            bool WriteTargetFps(int fps)
            {
                var iniFile = CurrentUlIniFile();
                if (iniFile == null || !File.Exists(iniFile))
                {
                    cog.SetError("relimiter.ini wasn't found. Deploy it first.");
                    return false;
                }
                try
                {
                    AuxInstallService.ApplyUlTargetFps(iniFile, fps);
                    currentTargetFps = fps;
                    cog.SetSuccess(fps == 0 ? "Target FPS turned off for this game." : $"Target FPS set to {fps} for this game.");
                    CrashReporter.Log($"[MainWindow.UlCogButton_Click] target_fps={fps} for '{card.GameName}'");
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    cog.SetError($"Couldn't save Target FPS: {ex.Message}");
                    CrashReporter.Log($"[MainWindow.UlCogButton_Click] target_fps write failed — {ex.Message}");
                    return false;
                }
            }

            targetFpsCombo.SelectionChanged += (s, ev) =>
            {
                if (suppressFpsChange) return;
                if (string.IsNullOrEmpty(card.InstallPath)) return;

                var selectedText = targetFpsCombo.SelectedItem as string ?? "";

                // "Custom..." shows the inline TextBox for manual entry
                if (selectedText == "Custom...")
                {
                    customFpsRow.Visibility = Visibility.Visible;
                    customFpsBox.Text = "";
                    customFpsBox.Focus(FocusState.Programmatic);
                    return;
                }

                customFpsRow.Visibility = Visibility.Collapsed;

                // Handle preset/Off selection
                int newFps;
                var idx = targetFpsCombo.SelectedIndex;
                if (idx == 0)
                    newFps = 0; // Off
                else if (idx > 0 && idx - 1 < vrrPresets.Length)
                    newFps = vrrPresets[idx - 1].Fps;
                else
                    return; // Custom label item — don't set

                WriteTargetFps(newFps);
            };

            void SetCustomFps()
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                if (!int.TryParse(customFpsBox.Text, out var customFps) || customFps < 20 || customFps > 1000)
                {
                    cog.SetError("Enter a whole number from 20 to 1000.");
                    return;
                }
                if (WriteTargetFps(customFps))
                    RefreshFpsCombo(customFps);
            }
            customFpsBtn.Click += (s, ev) => SetCustomFps();
            customFpsBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter)
                {
                    ev.Handled = true;
                    SetCustomFps();
                }
            };

            // ── Compatibility ─────────────────────────────────────────────────────
            cog.AddSection("Compatibility");
            var dlssHooksCombo = cog.AddCombo("DLSS Hooks",
                "Shows DLSS version and preset info on the ReLimiter overlay. Turn it off if the game crashes with it on.",
                new[] { "Off", "On" }, currentDlssHooks ? 1 : 0, i =>
                {
                    var iniFile = CurrentUlIniFile();
                    if (iniFile == null || !File.Exists(iniFile))
                    {
                        cog.SetError("relimiter.ini wasn't found. Deploy it first.");
                        return false;
                    }
                    try
                    {
                        AuxInstallService.ApplyUlDlssHooks(iniFile, i == 1);
                        cog.SetSuccess(i == 1 ? "DLSS Hooks turned on for this game." : "DLSS Hooks turned off for this game.");
                        CrashReporter.Log($"[MainWindow.UlCogButton_Click] dlss_info_hooks={(i == 1)} for '{card.GameName}'");
                        return true;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        cog.SetError($"Couldn't save DLSS Hooks: {ex.Message}");
                        CrashReporter.Log($"[MainWindow.UlCogButton_Click] dlss_info_hooks write failed — {ex.Message}");
                        return false;
                    }
                });

            // Greyed out until relimiter.ini exists (rows stay in place so the layout doesn't jump)
            if (!ulIniExists)
            {
                // AddCombo returns the ComboBox; its row is combo → label grid → row panel
                var dlssHooksRow = ((dlssHooksCombo.Parent as FrameworkElement)?.Parent as FrameworkElement) ?? dlssHooksCombo;
                foreach (var row in new[] { targetFpsRow, dlssHooksRow })
                {
                    row.Opacity = CogDialog.DisabledOpacity;
                    row.IsHitTestVisible = false;
                }
                targetFpsCombo.IsTabStop = false;
                dlssHooksCombo.IsTabStop = false;
            }

            // ── Files ─────────────────────────────────────────────────────────────
            cog.AddSection("Files");
            var openIniBtn = cog.MakeOpenFileButton("Open relimiter.ini", ulIniFile);
            var deployBtn = cog.MakeButton("Deploy relimiter.ini", CogButtonKind.Primary, () =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                AuxInstallService.CopyUlIni(card.InstallPath);
                openIniBtn.IsEnabled = ulIniFile != null && File.Exists(ulIniFile);
                cog.SetSuccess("relimiter.ini copied to the game folder. Close and reopen this dialog to change its settings.");
                CrashReporter.Log($"[MainWindow.UlCogButton_Click] Deployed relimiter.ini for '{card.GameName}'");
            }, "Copies RHI's relimiter.ini template into the game folder, replacing the game's current one.");

            var openLogBtn = cog.MakeOpenFileButton("Open ReLimiter log", logFile);

            // Copied as a file (not text) so Discord shows the log's real name
            var copyLogBtn = cog.MakeButton("Copy ReLimiter log", CogButtonKind.Secondary, async () =>
            {
                if (logFile == null || !File.Exists(logFile)) { cog.SetError("The ReLimiter log wasn't found."); return; }
                await CopyFileToClipboardAsFileAsync(logFile);
                cog.SetSuccess($"Copied {Path.GetFileName(logFile)} to the clipboard. Paste it into Discord to attach it.");
            }, "Copies the ReLimiter log to the clipboard as a file.");
            copyLogBtn.IsEnabled = logFile != null;

            cog.AddButtons(deployBtn, openIniBtn, openLogBtn, copyLogBtn);

            // No default button: Enter in the key/number boxes must not close the dialog
            cog.Build().DefaultButton = ContentDialogButton.None;
            await cog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.UlCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void DcCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            // DisplayCommander.ini lives in the addon deploy path (honours AddonPath in reshade.ini), same as CopyDcIni
            string? dcIniFile = string.IsNullOrEmpty(card.InstallPath)
                ? null
                : Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), "DisplayCommander.ini");

            var cog = new CogDialog("Display Commander", Content.XamlRoot);
            cog.AddSection("Files");
            var openIniBtn = cog.MakeOpenFileButton("Open DisplayCommander.ini", dcIniFile);
            var deployBtn = cog.MakeButton("Deploy DisplayCommander.ini", CogButtonKind.Primary, () =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                AuxInstallService.CopyDcIni(card.InstallPath);
                openIniBtn.IsEnabled = dcIniFile != null && File.Exists(dcIniFile);
                cog.SetSuccess("DisplayCommander.ini copied to the game folder.");
                CrashReporter.Log($"[MainWindow.DcCogButton_Click] Deployed DisplayCommander.ini for '{card.GameName}'");
            }, "Copies RHI's DisplayCommander.ini template into the game folder, replacing the game's current one.");
            cog.AddButtons(deployBtn, openIniBtn);

            await cog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DcCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
