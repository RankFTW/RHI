// MainWindow.Events.Components.cs — Per-component cog button (⚙️) dialog handlers (RS, RDX, UL, DC, OS, DXVK).

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
    // ── Component Cog Button Handlers ────────────────────────────────────────────

    private async void RsCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            if (string.IsNullOrEmpty(card.InstallPath)) return;

            // Read all file state on background thread to avoid blocking UI
            var installPath = card.InstallPath;
            bool rsPresetExists = false;
            bool reshadeIniExists = false;
            bool reshadeLogExists = false;
            string currentHotkey = ViewModel.Settings.OverlayHotkey;
            string currentScreenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
            
            await Task.Run(() =>
            {
                rsPresetExists = File.Exists(AuxInstallService.RsPresetIniPath);
                reshadeIniExists = File.Exists(Path.Combine(installPath, "reshade.ini"));
                reshadeLogExists = File.Exists(Path.Combine(installPath, "ReShade.log"));
                
                // Read current keys from reshade.ini (game folder)
                var iniPath = Path.Combine(installPath, "reshade.ini");
                if (reshadeIniExists)
                {
                    try
                    {
                        var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                        if (ini.TryGetValue("INPUT", out var inputSection))
                        {
                            if (inputSection.TryGetValue("KeyOverlay", out var ko) && !string.IsNullOrWhiteSpace(ko))
                                currentHotkey = ko;
                            if (inputSection.TryGetValue("KeyScreenshot", out var ks2) && !string.IsNullOrWhiteSpace(ks2))
                                currentScreenshotHotkey = ks2;
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.RsCogButton_Click] Failed to read ReShade ini hotkeys, using fallback — {ex.Message}"); }
                }
            });

            var reshadeIniPath = Path.Combine(installPath, "reshade.ini");
            var reshadeLogPath = Path.Combine(installPath, "ReShade.log");
            var cog = new CogDialog("ReShade", Content.XamlRoot);

            // ── Hotkeys ───────────────────────────────────────────────────────────
            // Capture box + Apply per key: a key press only fills the box, Apply writes it.
            cog.AddSection("Hotkeys");
            var hotkeyString = currentHotkey;
            var screenshotHotkeyString = currentScreenshotHotkey;

            TextBox MakeHotkeyBox(Func<string> getValue, Action<string> setValue)
            {
                var box = CogDialog.MakeTextBox(HotkeyManager.FormatHotkeyDisplay(getValue()), "Click then press a key...");
                box.IsReadOnly = true;
                box.GotFocus += (s, ev) => box.Text = "Press a key...";
                box.KeyDown += (s, ev) =>
                {
                    var vk = (int)ev.Key;
                    if (vk == 0 || vk == 16 || vk == 17 || vk == 18) return;
                    bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    bool ctrl  = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    bool alt   = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    setValue(HotkeyManager.BuildScreenshotHotkeyString(vk, shift, ctrl, alt));
                    box.Text = HotkeyManager.FormatHotkeyDisplay(getValue());
                    ev.Handled = true;
                };
                box.LostFocus += (s, ev) => { if (box.Text == "Press a key...") box.Text = HotkeyManager.FormatHotkeyDisplay(getValue()); };
                return box;
            }

            // Writes the key to every reshade*.ini in the game folder.
            async Task ApplyHotkeyAsync(string keyLabel, string hotkey, Action<string, string> applyToIni)
            {
                var gamePath = card.InstallPath;
                if (string.IsNullOrEmpty(gamePath)) return;
                var (count, error) = await Task.Run(() =>
                {
                    try
                    {
                        var iniFiles = Directory.EnumerateFiles(gamePath, "reshade*.ini")
                            .Where(f => Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase)
                                     && Path.GetFileNameWithoutExtension(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        foreach (var file in iniFiles)
                            applyToIni(file, hotkey);
                        return (iniFiles.Count, (string?)null);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        return (0, ex.Message);
                    }
                });
                if (error != null)
                {
                    cog.SetError($"Couldn't set the {keyLabel.ToLowerInvariant()}: {error}");
                    _crashReporter.Log($"[RsCogButton_Click] Failed to apply {keyLabel.ToLowerInvariant()} for '{card.GameName}' — {error}");
                    return;
                }
                if (count == 0)
                {
                    cog.SetStatus("No reshade.ini in the game folder, so nothing was changed.");
                    return;
                }
                cog.SetSuccess($"{keyLabel} set to {HotkeyManager.FormatHotkeyDisplay(hotkey)} in {count} ini file(s).");
                _crashReporter.Log($"[RsCogButton_Click] Applied {keyLabel.ToLowerInvariant()} '{hotkey}' to {count} ini file(s) for '{card.GameName}'");
            }

            var hotkeyBox = MakeHotkeyBox(() => hotkeyString, v => hotkeyString = v);
            var applyKeyBtn = CogDialog.StyledButton("Apply", CogButtonKind.Secondary, compact: true);
            applyKeyBtn.Click += async (s, ev) =>
            {
                try { await ApplyHotkeyAsync("Overlay key", hotkeyString, AuxInstallService.ApplyOverlayHotkey); }
                catch (Exception ex)
                {
                    cog.SetError($"Couldn't set the overlay key: {ex.Message}");
                    _crashReporter.Log($"[RsCogButton_Click] Overlay key apply failed — {ex}");
                }
            };
            cog.AddRow("Overlay Key", CogDialog.Inline(hotkeyBox, applyKeyBtn),
                "Opens the ReShade overlay in game. Click the box, press a key, then Apply. Saved to every reshade*.ini in the game folder.");

            var screenshotHotkeyBox = MakeHotkeyBox(() => screenshotHotkeyString, v => screenshotHotkeyString = v);
            var applyScreenshotKeyBtn = CogDialog.StyledButton("Apply", CogButtonKind.Secondary, compact: true);
            applyScreenshotKeyBtn.Click += async (s, ev) =>
            {
                try { await ApplyHotkeyAsync("Screenshot key", screenshotHotkeyString, AuxInstallService.ApplyScreenshotHotkey); }
                catch (Exception ex)
                {
                    cog.SetError($"Couldn't set the screenshot key: {ex.Message}");
                    _crashReporter.Log($"[RsCogButton_Click] Screenshot key apply failed — {ex}");
                }
            };
            cog.AddRow("Screenshot Key", CogDialog.Inline(screenshotHotkeyBox, applyScreenshotKeyBtn),
                "Takes a ReShade screenshot. Click the box, press a key (Backspace for none), then Apply. Saved to every reshade*.ini in the game folder.");

            // ── Keep ReShade.ini Updated ──────────────────────────────────────────
            cog.AddSection("ReShade.ini");
            var capturedKeepGameName = card.GameName;
            var capturedKeepSource = card.Source ?? "";
            cog.AddCombo("Keep ReShade.ini Updated",
                "No stops RHI from changing this game's reshade.ini when ReShade is installed or updated, or when you use Apply to All Games.",
                new[] { "Yes", "No" },
                ViewModel.GetKeepRsIniUpdated(capturedKeepGameName, capturedKeepSource) ? 0 : 1,
                i =>
                {
                    ViewModel.SetKeepRsIniUpdated(capturedKeepGameName, i == 0, capturedKeepSource);
                    cog.SetSuccess(i == 0
                        ? "RHI will keep this game's reshade.ini updated."
                        : "RHI will leave this game's reshade.ini alone.");
                });

            // ── Files ─────────────────────────────────────────────────────────────
            cog.AddSection("Files");
            var openIniBtn = cog.MakeOpenFileButton("Open ReShade.ini", reshadeIniPath);

            var deployIniBtn = cog.MakeButton("Deploy ReShade.ini", CogButtonKind.Primary, () =>
            {
                var screenshotPath = BuildScreenshotSavePath(card.GameName);
                var overlayHotkey = ViewModel.Settings.OverlayHotkey;
                var screenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
                if (card.RequiresVulkanInstall)
                {
                    AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey, screenshotHotkey);
                    VulkanFootprintService.Create(card.InstallPath);
                    ViewModel.DeployShadersForCard(card.GameName);
                }
                else
                    AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey, screenshotHotkey);

                if (card.UseUeExtended && card.Status == GameStatus.Installed)
                    AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath);

                // Force-apply manifest [renodx] INI overrides on redeploy
                if (AuxInstallService.GlobalManifest?.RenodxIniOverrides != null
                    && AuxInstallService.GlobalManifest.RenodxIniOverrides.TryGetValue(card.GameName, out var cogIniOvr))
                    AuxInstallService.ApplyRenodxIniOverrides(card.InstallPath, cogIniOvr, forceOverwrite: true);

                openIniBtn.IsEnabled = File.Exists(reshadeIniPath);
                cog.SetSuccess("ReShade.ini deployed.");
                _crashReporter.Log($"[RsCogButton_Click] Deployed ReShade.ini for '{card.GameName}'");
            }, "Merges RHI's ReShade.ini template into this game's reshade.ini. Template values replace existing ones; other settings are kept.");

            var deployPresetBtn = cog.MakeButton("Deploy ReShadePreset.ini", CogButtonKind.Primary, () =>
            {
                AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
                cog.SetSuccess("ReShadePreset.ini deployed.");
                _crashReporter.Log($"[RsCogButton_Click] Deployed ReShadePreset.ini for '{card.GameName}'");
            }, rsPresetExists ? "Copies ReShadePreset.ini from RHI's config folder into the game folder." : "No ReShadePreset.ini found in RHI config folder");
            deployPresetBtn.IsEnabled = rsPresetExists;

            var openLogBtn = cog.MakeOpenFileButton("Open ReShade.log", reshadeLogPath);

            // Copied as a file (not text) so Discord shows "ReShade.log"
            var copyLogBtn = cog.MakeButton("Copy ReShade.log", CogButtonKind.Secondary, async () =>
            {
                if (!File.Exists(reshadeLogPath)) { cog.SetError("ReShade.log wasn't found."); return; }
                await CopyFileToClipboardAsFileAsync(reshadeLogPath);
                cog.SetSuccess("Copied ReShade.log to the clipboard. Paste it into Discord to attach it.");
            }, "Copies ReShade.log to the clipboard as a file.");
            copyLogBtn.IsEnabled = reshadeLogExists;

            cog.AddButtons(deployIniBtn, deployPresetBtn, openIniBtn, openLogBtn, copyLogBtn);

            // No default button: Enter in the key/number boxes must not close the dialog
            cog.Build().DefaultButton = ContentDialogButton.None;
            await cog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RsCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void RefCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            if (string.IsNullOrEmpty(card.InstallPath)) return;

            var installPath = card.InstallPath;
            var storageDir = Path.Combine(installPath, "_storage_");
            bool storageExists = Directory.Exists(storageDir);

            var cog = new CogDialog("RE Framework", Content.XamlRoot);
            cog.AddSection("Files");
            cog.AddIntro("The _storage_ folder in the game folder holds data saved by RE Framework and its scripts. Deleting it clears that data.");
            var foundNote = cog.AddNote(storageExists ? $"Found: {storageDir}" : "No _storage_ folder in the game folder.");

            const string deleteText = "Delete _storage_ folder";
            Button? deleteStorageBtn = null;
            deleteStorageBtn = cog.MakeButton(deleteText, CogButtonKind.Danger, async () =>
            {
                var btn = deleteStorageBtn!;
                btn.IsEnabled = false;
                btn.Content = "Deleting...";
                try
                {
                    await Task.Run(() => Directory.Delete(storageDir, recursive: true));
                    btn.Content = deleteText;
                    btn.Opacity = CogDialog.DisabledOpacity;
                    foundNote.Text = "No _storage_ folder in the game folder.";
                    cog.SetSuccess("_storage_ folder deleted.");
                    CrashReporter.Log($"[RefCogButton_Click] Deleted _storage_ folder: '{storageDir}'");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    btn.IsEnabled = true;
                    btn.Content = deleteText;
                    cog.SetError($"Couldn't delete the _storage_ folder: {ex.Message}");
                    CrashReporter.Log($"[RefCogButton_Click] Failed to delete _storage_ folder: {ex.Message}");
                }
            });
            deleteStorageBtn.IsEnabled = storageExists;
            deleteStorageBtn.Opacity = storageExists ? 1.0 : CogDialog.DisabledOpacity;
            cog.AddButtons(deleteStorageBtn);

            await cog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RefCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Copies a file (e.g. a log) to the clipboard as a file rather than text, so pasting it into
    /// Discord attaches it under its real name. Used by the ReShade and ReLimiter cog dialogs.
    /// </summary>
    private static async Task CopyFileToClipboardAsFileAsync(string path)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RHI_clipboard");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, Path.GetFileName(path));
        File.Copy(path, tempFile, overwrite: true);

        var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempFile);
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetStorageItems(new[] { storageFile });
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    private void SupportGuide_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md"));
    }

    private void SupportKofi_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://ko-fi.com/rankftw"));
    }

}
