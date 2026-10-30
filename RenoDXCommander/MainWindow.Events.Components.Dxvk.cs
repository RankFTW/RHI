// MainWindow.Events.Components.Dxvk.cs — DXVK cog dialog handler (DxvkCogButton_Click).
// Built with CogDialog: every setting applies as soon as it changes; results show in the dialog.

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
    internal async void DxvkCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            var gameName = card.GameName;
            var store    = card.Source ?? "";

            // Pre-fetch both values off the UI thread — each calls _sessionLock.Wait(5000)
            // and will block the UI thread if NVAPI is hung after GPU sleep/wake.
            var (currentPresentMethod, currentPresentFlags) = await Task.Run(() => (
                _dlssPresetService.GetVulkanPresentMethod(card.GameName, card.InstallPath ?? ""),
                _dlssPresetService.GetVulkanPresentMethodFlags(card.GameName, card.InstallPath ?? "")
            ));

            var cog = new CogDialog("DXVK", Content.XamlRoot);

            // ── Variant selector ──────────────────────────────────────────────────
            // Resolve current variant — Lilium HDR is default when no override is set
            var currentOverride = ViewModel.GetDxvkVariantOverride(gameName, store);
            string currentVariantDisplay;
            if (currentOverride != null)
            {
                currentVariantDisplay = currentOverride switch
                {
                    "Stable"    => "Stable",
                    "LiliumHdr" => "Lilium HDR",
                    _           => "Development",
                };
            }
            else if (card.DxvkEnabled)
            {
                // Already installed — show effective global variant
                currentVariantDisplay = ViewModel.ResolveDxvkVariant(gameName, store) switch
                {
                    DxvkVariant.Stable    => "Stable",
                    DxvkVariant.LiliumHdr => "Lilium HDR",
                    _                     => "Development",
                };
            }
            else
            {
                // Default: Lilium HDR
                currentVariantDisplay = "Lilium HDR";
            }

            var variantItems = new List<string> { "Lilium HDR", "Development", "Stable" };
            int variantIdx = variantItems.IndexOf(currentVariantDisplay);
            if (variantIdx < 0) variantIdx = 0;

            cog.AddSection("DXVK");

            FrameworkElement? liliumPresetRow = null;
            ComboBox? variantCombo = null;
            variantCombo = cog.AddCombo("Variant",
                "Lilium HDR (default) is an HDR-focused build. Development is the latest nightly build, Stable the last release. " +
                "Changing it on a game with DXVK installed uninstalls DXVK so you can install the new variant.",
                variantItems, variantIdx, i =>
                {
                    var selected = variantItems[i];
                    if (liliumPresetRow != null)
                        liliumPresetRow.Visibility = selected == "Lilium HDR" ? Visibility.Visible : Visibility.Collapsed;
                    if (variantCombo != null)
                        _ = DxvkCogChangeVariantAsync(cog, card, variantCombo, selected);
                    return true;
                });

            // ── Lilium HDR Preset row (shown when Lilium selected) ─────────────────
            bool isLiliumSelected = variantItems[variantIdx] == "Lilium HDR";
            var isDx9Api = card.DxvkRecord?.InstalledDlls?.Any(d => d.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) == true
                           || card.GraphicsApi is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9;
            var presetArray = isDx9Api ? DxvkService.LiliumD3d9Presets : DxvkService.LiliumD3d11Presets;
            var presetNames = presetArray.Select(p => p.Name).ToList();
            int currentPresetIdx = ViewModel.GetLiliumPreset(gameName, store);

            var liliumPresetCombo = cog.AddCombo("Lilium Preset",
                "Safest only upgrades the swap chain and works almost everywhere. Higher tiers also upgrade back buffers and " +
                "render targets for better HDR, but may cause visual issues. Changing it rewrites dxvk.conf.",
                presetNames,
                currentPresetIdx >= 0 && currentPresetIdx < presetNames.Count ? currentPresetIdx : 0,
                idx =>
                {
                    ViewModel.SetLiliumPreset(gameName, idx, store);
                    // Re-deploy dxvk.conf with new preset if DXVK is installed
                    if (!string.IsNullOrEmpty(card.InstallPath))
                        _ = DxvkCogWriteLiliumConfAsync(cog, card.InstallPath, isDx9Api, idx, presetNames[idx]);
                    else
                        cog.SetSuccess($"Lilium preset set to {presetNames[idx]}.");
                });
            // AddRow puts the combo in a grid inside the row panel: hide the whole row when not Lilium
            liliumPresetRow = (liliumPresetCombo.Parent as FrameworkElement)?.Parent as FrameworkElement;
            if (liliumPresetRow != null)
                liliumPresetRow.Visibility = isLiliumSelected ? Visibility.Visible : Visibility.Collapsed;

            // ── Deploy dxvk.conf button ───────────────────────────────────────────
            cog.AddSection("dxvk.conf");
            cog.AddButtons(cog.MakeButton("Deploy dxvk.conf", CogButtonKind.Primary, () =>
            {
                var error = ViewModel.CopyDxvkConf(card);
                if (error == null) cog.SetSuccess("dxvk.conf copied to the game folder.");
                else cog.SetError($"Couldn't copy dxvk.conf: {error}");
            }));
            cog.AddNote("Copies dxvk.conf into the game folder: the selected Lilium preset for Lilium HDR installs, otherwise your dxvk.conf template.");

            // ── Vulkan/OpenGL Present Method ──────────────────────────────────────
            cog.AddSection("NVIDIA Driver Profile");

            var flagOptions = new (string Label, uint Value)[]
            {
                ("Standard",    0x000802A5u),
                ("Alternative", 0x00080004u),
            };
            bool presentIsYes = currentPresentMethod == 0x00000001;
            ComboBox? flagsCombo = null;

            // Prefer DXGI Swapchain: No = 0x00000002 (Auto), Yes = 0x00000001 (Preferred layered on DXGI Swapchain)
            cog.AddCombo("Prefer DXGI Swapchain",
                "Sets the Vulkan/OpenGL present method to 'Prefer layered on DXGI Swapchain' in the game's NVIDIA driver profile. " +
                "Recommended for DXVK: better compatibility and HDR support.",
                new List<string> { "No", "Yes" }, presentIsYes ? 1 : 0, i =>
                {
                    bool isYes = i == 1;
                    uint value = isYes ? 0x00000001u : 0x00000002u;
                    if (flagsCombo != null)
                    {
                        flagsCombo.IsEnabled = isYes;
                        flagsCombo.Opacity = isYes ? 1.0 : CogDialog.DisabledOpacity;
                    }
                    // No → clear the flags; Yes → write the selected flags
                    uint? flagsValue = null;
                    if (!isYes)
                        flagsValue = 0x00000000u;
                    else if (flagsCombo != null && flagsCombo.SelectedIndex >= 0 && flagsCombo.SelectedIndex < flagOptions.Length)
                        flagsValue = flagOptions[flagsCombo.SelectedIndex].Value;
                    var capturedName = card.GameName; var capturedPath = card.InstallPath ?? "";
                    _ = DxvkCogProfileWriteAsync(cog, "Prefer DXGI Swapchain", () =>
                    {
                        bool ok = _dlssPresetService.SetVulkanPresentMethod(capturedName, capturedPath, value);
                        if (flagsValue.HasValue)
                            ok &= _dlssPresetService.SetVulkanPresentMethodFlags(capturedName, capturedPath, flagsValue.Value);
                        return ok;
                    });
                });

            // DXVK as Native flags — only active when Prefer DXGI Swapchain is Yes
            int flagsIdx = 0;
            if (presentIsYes)
            {
                var flagMatch = Array.FindIndex(flagOptions, f => f.Value == currentPresentFlags);
                flagsIdx = flagMatch >= 0 ? flagMatch : 0; // default to Standard
            }
            flagsCombo = cog.AddCombo("DXVK as Native",
                "Standard treats DXVK as native. Alternative allows DXVK promotion and DirectFlip. Only used when Prefer DXGI Swapchain is Yes.",
                flagOptions.Select(f => f.Label).ToList(), flagsIdx, i =>
                {
                    var capturedName = card.GameName; var capturedPath = card.InstallPath ?? "";
                    var capturedVal = flagOptions[i].Value;
                    _ = DxvkCogProfileWriteAsync(cog, "DXVK as Native",
                        () => _dlssPresetService.SetVulkanPresentMethodFlags(capturedName, capturedPath, capturedVal));
                }, isEnabled: presentIsYes);
            if (!presentIsYes) flagsCombo.Opacity = CogDialog.DisabledOpacity;

            await cog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DxvkCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Saves the per-game DXVK variant override. If DXVK is installed it's uninstalled first so the
    /// user can install the new variant; the detail panel is rebuilt either way.
    /// </summary>
    private async Task DxvkCogChangeVariantAsync(CogDialog cog, GameCardViewModel card, ComboBox variantCombo, string selected)
    {
        var gameName = card.GameName;
        var store    = card.Source ?? "";
        string variantValue = selected switch
        {
            "Lilium HDR"  => "LiliumHdr",
            "Development" => "Development",
            "Stable"      => "Stable",
            _             => "LiliumHdr",
        };
        variantCombo.IsEnabled = false;
        try
        {
            ViewModel.SetDxvkVariantOverride(gameName, variantValue, store);

            bool wasInstalled = card.DxvkEnabled || card.DxvkStatus == GameStatus.Installed || card.DxvkStatus == GameStatus.UpdateAvailable;
            if (wasInstalled)
            {
                // Already installed — uninstall first so user can install the new variant
                cog.SetStatus("Uninstalling DXVK...");
                await ViewModel.HandleDxvkToggleAsync(card, false, Content.XamlRoot);
            }
            card.DxvkVariantPending = true;
            card.NotifyAll();
            PopulateDetailPanel(card);
            BuildOverridesPanel(card);

            cog.SetSuccess(wasInstalled
                ? $"Variant set to {selected}. DXVK was uninstalled; install it again to use the new variant."
                : $"Variant set to {selected}. It will be used when you install DXVK.");
            CrashReporter.Log($"[MainWindow.DxvkCogChangeVariantAsync] '{gameName}' variant → {variantValue} (wasInstalled={wasInstalled})");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DxvkCogChangeVariantAsync] Failed for '{gameName}' — {ex.Message}\n{ex.StackTrace}");
            cog.SetError($"Couldn't change the variant: {ex.Message}");
        }
        finally
        {
            variantCombo.IsEnabled = true;
        }
    }

    /// <summary>Rewrites the game's dxvk.conf with the chosen Lilium HDR preset (off the UI thread).</summary>
    private static async Task DxvkCogWriteLiliumConfAsync(CogDialog cog, string installPath, bool isDx9Api, int idx, string presetName)
    {
        var confPath = Path.Combine(installPath, "dxvk.conf");
        var confContent = isDx9Api
            ? DxvkService.GetLiliumD3d9ConfContent(idx)
            : DxvkService.GetLiliumD3d11ConfContent(idx);
        try
        {
            await Task.Run(() => File.WriteAllText(confPath, confContent));
            cog.SetSuccess($"Lilium preset set to {presetName}. dxvk.conf updated.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DxvkCog.LiliumPreset] Failed to write dxvk.conf — {ex.Message}");
            cog.SetError($"Lilium preset saved, but dxvk.conf couldn't be written: {ex.Message}");
        }
    }

    /// <summary>Runs an NVIDIA profile write off the UI thread and reports the result in the dialog.</summary>
    private static async Task DxvkCogProfileWriteAsync(CogDialog cog, string settingName, Func<bool> write)
    {
        try
        {
            bool ok = await Task.Run(write);
            if (ok) cog.SetSuccess($"{settingName} saved to the NVIDIA driver profile.");
            else cog.SetError($"Couldn't save {settingName} to the NVIDIA driver profile. See the log for details.");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DxvkCogProfileWriteAsync] {settingName} write failed — {ex.Message}");
            cog.SetError($"Couldn't save {settingName}: {ex.Message}");
        }
    }
}
