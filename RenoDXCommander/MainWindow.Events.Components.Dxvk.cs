// MainWindow.Events.Components.Dxvk.cs — DXVK cog dialog handler (DxvkCogButton_Click).

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
            var content = new StackPanel { Spacing = 12 };
            var gameName = card.GameName;
            var store    = card.Source ?? "";

            // ── Variant selector ──────────────────────────────────────────────────
            var variantGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
            variantGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            variantGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140, GridUnitType.Pixel) });
            variantGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            variantGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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

            var variantLabel = new TextBlock
            {
                Text = "Variant",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(variantLabel, "Development: latest nightly build. Stable: last stable release. Lilium HDR: HDR-optimised variant (default).");
            Grid.SetRow(variantLabel, 0); Grid.SetColumn(variantLabel, 0);
            variantGrid.Children.Add(variantLabel);

            var variantCombo = new ComboBox { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
            variantCombo.Items.Add("Lilium HDR");
            variantCombo.Items.Add("Development");
            variantCombo.Items.Add("Stable");
            variantCombo.SelectedItem = currentVariantDisplay;
            if (variantCombo.SelectedIndex < 0) variantCombo.SelectedIndex = 0;

            Grid.SetRow(variantCombo, 0); Grid.SetColumn(variantCombo, 1);
            variantGrid.Children.Add(variantCombo);

            // ── Lilium HDR Preset row (row 1, shown when Lilium selected) ─────────
            bool isLiliumSelected = (variantCombo.SelectedItem as string) == "Lilium HDR";
            var isDx9Api = card.DxvkRecord?.InstalledDlls?.Any(d => d.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) == true
                           || card.GraphicsApi is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9;
            var presetArray = isDx9Api ? DxvkService.LiliumD3d9Presets : DxvkService.LiliumD3d11Presets;
            var presetNames = presetArray.Select(p => p.Name).ToList();
            int currentPresetIdx = ViewModel.GetLiliumPreset(gameName, store);

            var liliumPresetLabel = new TextBlock
            {
                Text = "Lilium Preset",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = isLiliumSelected ? Visibility.Visible : Visibility.Collapsed,
            };
            ToolTipService.SetToolTip(liliumPresetLabel,
                "Safest = swap chain only (near 100% compatible).\nHigher tiers upgrade back buffers and render targets — better HDR but may cause visual issues.");
            Grid.SetRow(liliumPresetLabel, 1); Grid.SetColumn(liliumPresetLabel, 0);
            variantGrid.Children.Add(liliumPresetLabel);

            var liliumPresetCombo = new ComboBox
            {
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = presetNames,
                SelectedIndex = currentPresetIdx >= 0 && currentPresetIdx < presetNames.Count ? currentPresetIdx : 0,
                Visibility = isLiliumSelected ? Visibility.Visible : Visibility.Collapsed,
            };
            Grid.SetRow(liliumPresetCombo, 1); Grid.SetColumn(liliumPresetCombo, 1);
            variantGrid.Children.Add(liliumPresetCombo);
            content.Children.Add(variantGrid);

            // Wire variant combo — show/hide Lilium preset, save override, set DxvkVariantPending
            var variantComboInit = true;
            variantCombo.SelectionChanged += async (s, ev) =>
            {
                if (variantComboInit) return;
                var selected = variantCombo.SelectedItem as string;
                bool liliumNow = selected == "Lilium HDR";
                liliumPresetLabel.Visibility = liliumNow ? Visibility.Visible : Visibility.Collapsed;
                liliumPresetCombo.Visibility = liliumNow ? Visibility.Visible : Visibility.Collapsed;

                string? variantValue = selected switch
                {
                    "Lilium HDR"  => "LiliumHdr",
                    "Development" => "Development",
                    "Stable"      => "Stable",
                    _             => "LiliumHdr",
                };
                ViewModel.SetDxvkVariantOverride(gameName, variantValue, store);

                if (card.DxvkEnabled || card.DxvkStatus == GameStatus.Installed || card.DxvkStatus == GameStatus.UpdateAvailable)
                {
                    // Already installed — uninstall first so user can install the new variant
                    await ViewModel.HandleDxvkToggleAsync(card, false, Content.XamlRoot);
                    card.DxvkVariantPending = true;
                }
                else
                {
                    card.DxvkVariantPending = true;
                }
                card.NotifyAll();
                PopulateDetailPanel(card);
                BuildOverridesPanel(card);
            };
            variantComboInit = false;

            // Wire Lilium preset combo
            var liliumPresetInit = true;
            liliumPresetCombo.SelectionChanged += (s, ev) =>
            {
                if (liliumPresetInit) return;
                int idx = liliumPresetCombo.SelectedIndex;
                if (idx < 0) return;
                ViewModel.SetLiliumPreset(gameName, idx, store);
                // Re-deploy dxvk.conf with new preset if DXVK is installed
                if (!string.IsNullOrEmpty(card.InstallPath))
                {
                    var confPath = System.IO.Path.Combine(card.InstallPath, "dxvk.conf");
                    var confContent = isDx9Api
                        ? DxvkService.GetLiliumD3d9ConfContent(idx)
                        : DxvkService.GetLiliumD3d11ConfContent(idx);
                    try { System.IO.File.WriteAllText(confPath, confContent); }
                    catch (Exception ex) { CrashReporter.Log($"[DxvkCog.LiliumPreset] Failed to write dxvk.conf — {ex.Message}"); }
                }
            };
            liliumPresetInit = false;

            // ── Separator ─────────────────────────────────────────────────────────
            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 4, 0, 0) });

            // ── Deploy dxvk.conf button ───────────────────────────────────────────
            var deployBtn = new Button
            {
                Content = "Deploy dxvk.conf",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            };
            deployBtn.Click += (s, ev) => ViewModel.CopyDxvkConf(card);
            content.Children.Add(deployBtn);

            // ── Vulkan/OpenGL Present Method ──────────────────────────────────
            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 4, 0, 0) });

            // Pre-fetch both values off the UI thread — each calls _sessionLock.Wait(5000)
            // and will block the UI thread if NVAPI is hung after GPU sleep/wake.
            var (currentPresentMethod, currentPresentFlags) = await Task.Run(() => (
                _dlssPresetService.GetVulkanPresentMethod(card.GameName, card.InstallPath ?? ""),
                _dlssPresetService.GetVulkanPresentMethodFlags(card.GameName, card.InstallPath ?? "")
            ));

            var presentGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
            presentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            presentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140, GridUnitType.Pixel) });
            presentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            presentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Prefer DXGI Swapchain
            var presentLabel = new TextBlock
            {
                Text = "Prefer DXGI Swapchain",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(presentLabel, "Sets Vulkan/OpenGL Present Method to 'Preferred layered on DXGI Swapchain' in the NVIDIA driver profile. Recommended for DXVK — improves compatibility and HDR support.");
            Grid.SetRow(presentLabel, 0); Grid.SetColumn(presentLabel, 0);
            presentGrid.Children.Add(presentLabel);

            var presentCombo = new ComboBox { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
            presentCombo.Items.Add("No");   // 0x00000002 — Auto
            presentCombo.Items.Add("Yes");  // 0x00000001 — Preferred layered on DXGI Swapchain
            var currentPresentMethod2 = currentPresentMethod; // already fetched above
            presentCombo.SelectedIndex = currentPresentMethod2 == 0x00000001 ? 1 : 0;
            presentCombo.SelectionChanged += (s, ev) =>
            {
                uint value = presentCombo.SelectedIndex == 1 ? 0x00000001u : 0x00000002u;
                var capturedName = card.GameName; var capturedPath = card.InstallPath ?? "";
                _ = Task.Run(() => _dlssPresetService.SetVulkanPresentMethod(capturedName, capturedPath, value));
            };
            Grid.SetRow(presentCombo, 0); Grid.SetColumn(presentCombo, 1);
            presentGrid.Children.Add(presentCombo);

            // Row 1: DXVK as Native Flags
            var flagsLabel = new TextBlock
            {
                Text = "DXVK as Native",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(flagsLabel, "Standard: Treat DXVK as Native (0x000802A5). Alternative: Allow DXVK Promotion + DirectFlip (0x00080004). Only active when Prefer DXGI Swapchain is Yes.");
            Grid.SetRow(flagsLabel, 1); Grid.SetColumn(flagsLabel, 0);
            presentGrid.Children.Add(flagsLabel);

            var flagOptions = new (string Label, uint Value)[]
            {
                ("Standard",    0x000802A5u),
                ("Alternative", 0x00080004u),
            };
            var flagsCombo = new ComboBox { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var (lbl, _) in flagOptions) flagsCombo.Items.Add(lbl);
            ToolTipService.SetToolTip(flagsCombo, "Standard (0x000802A5): Treat DXVK as Native\nAlternative (0x00080004): Allow DXVK Promotion + DirectFlip");

            var currentFlags = currentPresentFlags; // already fetched above
            bool presentIsYes = currentPresentMethod2 == 0x00000001;

            if (presentIsYes)
            {
                var flagMatch = Array.FindIndex(flagOptions, f => f.Value == currentFlags);
                flagsCombo.SelectedIndex = flagMatch >= 0 ? flagMatch : 0; // default to Standard
            }
            else
            {
                flagsCombo.SelectedIndex = 0;
                flagsCombo.IsEnabled = false;
                flagsCombo.Opacity = 0.35;
            }

            flagsCombo.SelectionChanged += (s, ev) =>
            {
                if (flagsCombo.SelectedIndex >= 0 && flagsCombo.SelectedIndex < flagOptions.Length)
                {
                    var capturedName = card.GameName; var capturedPath = card.InstallPath ?? "";
                    var capturedVal = flagOptions[flagsCombo.SelectedIndex].Value;
                    _ = Task.Run(() => _dlssPresetService.SetVulkanPresentMethodFlags(capturedName, capturedPath, capturedVal));
                }
            };
            Grid.SetRow(flagsCombo, 1); Grid.SetColumn(flagsCombo, 1);
            presentGrid.Children.Add(flagsCombo);

            // When Prefer DXGI Swapchain changes, update flags combo state
            presentCombo.SelectionChanged += (s, ev) =>
            {
                bool isYes = presentCombo.SelectedIndex == 1;
                flagsCombo.IsEnabled = isYes;
                flagsCombo.Opacity = isYes ? 1.0 : 0.35;
                var capturedName2 = card.GameName; var capturedPath2 = card.InstallPath ?? "";
                if (!isYes)
                {
                    _ = Task.Run(() => _dlssPresetService.SetVulkanPresentMethodFlags(capturedName2, capturedPath2, 0x00000000u));
                }
                else if (flagsCombo.SelectedIndex >= 0 && flagsCombo.SelectedIndex < flagOptions.Length)
                {
                    var capturedVal2 = flagOptions[flagsCombo.SelectedIndex].Value;
                    _ = Task.Run(() => _dlssPresetService.SetVulkanPresentMethodFlags(capturedName2, capturedPath2, capturedVal2));
                }
            };

            content.Children.Add(presentGrid);

            var dialog = new ContentDialog
            {
                Title = "DXVK Settings",
                Content = content,
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DxvkCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
