// DetailPanelBuilder.Overrides.RsShared.cs — ReShade per-game settings shared by Game Overrides and the ReShade ⚙:
// ReShade Channel, Shaders, Addons, Select ReShade Preset and the ReShade DLL name.
// Both places read the same saved values and call these methods, so a change in one shows in the other.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal const string RsChannelLegacyItem = "Legacy...";
    internal static readonly string[] ShaderModeItems = { "Global", "Custom", "Select", "Off" };
    internal static readonly string[] AddonModeItems = { "Global", "Select", "Off" };

    /// <summary>Rebuilds Game Overrides at low priority so it shows the saved values (after a change made elsewhere).</summary>
    internal void QueueOverridesRebuild(string gameName)
    {
        var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (refreshCard != null)
            _window.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    BuildOverridesPanel(refreshCard);
                    if (ReferenceEquals(_window.ViewModel.SelectedGame, refreshCard)) RefreshOverrideNotes(refreshCard);
                });
    }

    /// <summary>
    /// Shows a changed override where it now lives (ComponentOverrideNotes and the header tags): the API / Launch args
    /// tags, the Components summary, the Overview cards and Spotlight's What's installed tiles.
    /// </summary>
    internal void RefreshOverrideNotes(GameCardViewModel card)
    {
        try
        {
            UpdateGraphicsApiBadges(_window, card);
            if (_componentsSummaryCard == card && _componentsSummary != null)
            {
                var visibility = _componentsSummary.Visibility;
                _window.DetailComponentsHeader.Children.Remove(_componentsSummary);
                _componentsSummary = BuildComponentsSummary(card);
                if (_componentsSummary != null)
                {
                    _componentsSummary.Visibility = visibility;
                    _window.DetailComponentsHeader.Children.Add(_componentsSummary);
                }
            }
            if (Layout.IsTabs) Layout.RefreshHeader();
            Layout.RefreshOverviewCards();
            if (Layout.IsSpotlight) Layout.RefreshSpotStatus();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[DetailPanelBuilder.RefreshOverrideNotes] {ex.Message}");
        }
    }

    private GameCardViewModel? FindCard(string gameName) =>
        _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));

    // ── ReShade Channel ─────────────────────────────────────────────────────────

    /// <summary>The channel dropdown's items (an active legacy version is listed before "Legacy...") and the current one.</summary>
    internal (List<string> Items, string Selected) GetRsChannelChoices(GameCardViewModel card)
    {
        var gameName = card.GameName;
        var items = new List<string> { "Stable", "Nightly", "Custom", "No Addons", RsChannelLegacyItem };
        // For Vulkan games, show the effective Vulkan-wide override (any Vulkan game's override applies to all)
        var current = _window.ViewModel.GetReShadeChannelOverride(gameName, card.Source ?? "");
        if (current == null && card.RequiresVulkanInstall)
        {
            current = _window.ViewModel.AllCards
                .Where(c => c.RequiresVulkanInstall && c.GameName != gameName)
                .Select(c => _window.ViewModel.GetReShadeChannelOverride(c.GameName, c.Source ?? ""))
                .FirstOrDefault(ch => ch != null);
        }

        if (card.UseNormalReShade) return (items, "No Addons");
        if (string.Equals(current, "Custom", StringComparison.OrdinalIgnoreCase)) return (items, "Custom");
        if (MainViewModel.IsLegacyVersion(current))
        {
            items.Insert(items.IndexOf(RsChannelLegacyItem), current!);
            return (items, current!);
        }
        return (items, current == "Nightly" ? "Nightly" : "Stable");
    }

    /// <summary>True when picking this channel opens a dialog (picker or the Vulkan "apply to all" confirmation).</summary>
    internal static bool RsChannelPickNeedsDialog(GameCardViewModel card, string selected) =>
        selected == RsChannelLegacyItem || selected == "Custom"
        || (card.RequiresVulkanInstall && selected == "Nightly");

    private async Task<bool> ConfirmVulkanChannelAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Vulkan ReShade Channel Override",
            Content = "Vulkan games share a global ReShade layer.\n\n" + message,
            PrimaryButtonText = "Apply to All Vulkan Games",
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        return await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Applies a ReShade Channel pick ("Stable", "Nightly", "Custom", "No Addons", "Legacy..." or an active legacy version):
    /// shows any picker / confirmation, saves it, reinstalls ReShade where needed and rebuilds Game Overrides.
    /// Returns false when the user cancelled or nothing could be applied, so the caller puts its dropdown back.
    /// </summary>
    internal async Task<bool> ApplyRsChannelAsync(GameCardViewModel card, string capturedName, string selected)
    {
        CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' selection changed to: '{selected}'");

        // ── "Legacy..." opens the version picker dialog ──
        if (selected == RsChannelLegacyItem)
        {
            var legacyVersions = _window.ViewModel.Manifest?.LegacyReShadeAvailable;
            if (legacyVersions == null || legacyVersions.Count == 0) return false;

            var radioButtons = new RadioButtons { MaxColumns = 1 };
            foreach (var ver in legacyVersions)
                radioButtons.Items.Add(ver);

            var pickerContent = new StackPanel { Spacing = 12 };
            pickerContent.Children.Add(new TextBlock
            {
                Text = "⚠ Older ReShade versions may not support newer addons.\nThe game will be excluded from automatic ReShade updates.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                FontSize = 12,
            });
            pickerContent.Children.Add(radioButtons);

            var pickerDialog = new ContentDialog
            {
                Title = "Select Legacy ReShade Version",
                Content = new ScrollViewer { Content = pickerContent, MaxHeight = 400 },
                PrimaryButtonText = "Confirm",
                CloseButtonText = "Cancel",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var pickerResult = await DialogService.ShowSafeAsync(pickerDialog);
            if (pickerResult != ContentDialogResult.Primary || radioButtons.SelectedItem is not string pickedVersion)
                return false;

            bool legacyIsVulkan = card.RequiresVulkanInstall;
            if (legacyIsVulkan
                && !await ConfirmVulkanChannelAsync($"Switching to ReShade {pickedVersion} will change it for ALL Vulkan games."))
                return false;

            if (!AuxInstallService.IsLegacyVersionCached(pickedVersion)
                && !await AuxInstallService.DownloadLegacyReShadeAsync(pickedVersion, _window.ViewModel.HttpClient))
                return false;

            _window.ViewModel.SetReShadeChannelOverride(capturedName, pickedVersion, card.Source ?? "");
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → legacy {pickedVersion}{(legacyIsVulkan ? " (all Vulkan games)" : "")}");
            if (legacyIsVulkan)
            {
                foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                {
                    _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, pickedVersion, vCard.Source ?? "");
                    if (vCard.UseNormalReShade)
                        _window.ViewModel.SetUseNormalReShade(vCard, false);
                    vCard.NotifyAll();
                }
            }

            // Clear No Addons mode if active (legacy is an addon build)
            var targetCardLegacy = FindCard(capturedName);
            if (targetCardLegacy != null && targetCardLegacy.UseNormalReShade)
                _window.ViewModel.SetUseNormalReShade(targetCardLegacy, false);

            if (targetCardLegacy != null)
            {
                if (legacyIsVulkan && !targetCardLegacy.IsRsInstalled)
                    await _window.ViewModel.SyncVulkanLayerAsync(targetCardLegacy);
                else
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardLegacy);
            }

            _window.ViewModel.NotifyUpdateButtonChanged();
            QueueOverridesRebuild(capturedName); // lists the new legacy version in the dropdown
            return true;
        }

        // ── An active legacy version picked again: nothing to do ──
        if (selected != "Stable" && selected != "Nightly" && selected != "No Addons" && selected != "Custom"
            && MainViewModel.IsLegacyVersion(selected))
            return true;

        // ── "Custom" — use user-supplied ReShade DLLs ──
        if (selected == "Custom")
            return await ApplyCustomRsChannelAsync(card, capturedName);

        // ── "No Addons" — switch to normal (non-addon) ReShade ──
        if (selected == "No Addons")
        {
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → No Addons mode");
            var targetCardNoAddon = FindCard(capturedName);
            if (targetCardNoAddon != null && !targetCardNoAddon.UseNormalReShade)
            {
                _window.ViewModel.SetUseNormalReShade(targetCardNoAddon, true);
                // Clear any channel override since we're switching to normal
                _window.ViewModel.SetReShadeChannelOverride(capturedName, null, card.Source ?? "");
                // Auto-install the normal (no-addon) version
                if (targetCardNoAddon.RsStatus == GameStatus.NotInstalled || targetCardNoAddon.IsRsInstalled)
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardNoAddon);
            }
            QueueOverridesRebuild(capturedName); // greys out the addon controls
            return true;
        }

        // ── Switching away from "No Addons" — re-enable addon ReShade ──
        var targetCard = FindCard(capturedName);
        if (targetCard != null && targetCard.UseNormalReShade)
        {
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → leaving No Addons mode, re-enabling addon support");
            _window.ViewModel.SetUseNormalReShade(targetCard, false);
        }

        string? channelValue = selected == "Nightly" ? "Nightly" : null; // "Stable" = default, clears the per-game override
        CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → channelValue={channelValue ?? "Stable (null)"}");

        if (targetCard?.RequiresVulkanInstall == true)
        {
            // ── Vulkan game: affects ALL Vulkan games ──
            if (channelValue != null
                && !await ConfirmVulkanChannelAsync("Changing the channel for this game will change it for ALL Vulkan games."))
                return false;

            foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
            {
                _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, channelValue, vCard.Source ?? "");
                vCard.NotifyAll();
            }

            // Put the chosen channel's build into the shared layer (logs what it did)
            await _window.ViewModel.SyncVulkanLayerAsync(targetCard);

            // Mark all Vulkan games as installed (layer updated in-place)
            foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall && c.IsRsInstalled))
            {
                vCard.RsStatus = GameStatus.Installed;
                vCard.NotifyAll();
            }
        }
        else
        {
            // ── Non-Vulkan game: per-game only ──
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → setting per-game override to: {channelValue ?? "(null/Global)"}");
            _window.ViewModel.SetReShadeChannelOverride(capturedName, channelValue, card.Source ?? "");

            // Auto-reinstall ReShade with the new channel if it's currently installed
            if (targetCard != null && targetCard.IsRsInstalled)
                await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard);
        }

        QueueOverridesRebuild(capturedName); // reflects update inclusion changes
        _window.ViewModel.NotifyUpdateButtonChanged();
        return true;
    }

    private async Task<bool> ApplyCustomRsChannelAsync(GameCardViewModel card, string capturedName)
    {
        CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → Custom ReShade");

        var customDir = DlssStreamlineService.RsCustomDir;
        string[] dllFiles;
        try
        {
            Directory.CreateDirectory(customDir);
            dllFiles = Directory.GetFiles(customDir, "*.dll");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] Couldn't read '{customDir}' — {ex.Message}");
            dllFiles = Array.Empty<string>();
        }

        HyperlinkButton MakeFolderLink(double fontSize)
        {
            var link = new HyperlinkButton
            {
                Content = customDir,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = fontSize,
                Padding = new Thickness(0),
            };
            link.Click += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(customDir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(customDir) { UseShellExecute = true });
                }
                catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RSChannel] Couldn't open '{customDir}' — {ex.Message}"); }
            };
            return link;
        }

        if (dllFiles.Length == 0)
        {
            // No custom DLLs found — warn user and revert
            var warnContent = new StackPanel { Spacing = 8 };
            warnContent.Children.Add(new TextBlock
            {
                Text = "No custom ReShade DLLs found.\n\nPlace your .dll files in:",
                TextWrapping = TextWrapping.Wrap,
            });
            warnContent.Children.Add(MakeFolderLink(12));

            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Custom ReShade Not Found",
                Content = warnContent,
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
            return false;
        }

        // ── Picker dialog with all DLL files; pre-select the previous pick if it's still there ──
        string? previousSelection = null;
        var customKey = GameKey.FromCard(capturedName, card.Source ?? "").ToKey();
        if (_gameNameService.CustomReShadeSelection.TryGetValue(customKey, out var prevSel)
            && dllFiles.Any(f => Path.GetFileName(f).Equals(prevSel, StringComparison.OrdinalIgnoreCase)))
            previousSelection = prevSel;

        var pickerPanel = new StackPanel { Spacing = 8 };
        pickerPanel.Children.Add(MakeFolderLink(11));
        var radioButtons = new RadioButtons { MaxColumns = 1 };
        foreach (var dllPath in dllFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            radioButtons.Items.Add(Path.GetFileName(dllPath));
        if (previousSelection != null)
        {
            var idx = radioButtons.Items.IndexOf(previousSelection);
            if (idx >= 0) radioButtons.SelectedIndex = idx;
        }
        pickerPanel.Children.Add(radioButtons);

        var pickerResult = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Select Custom ReShade",
            PrimaryButtonText = "Deploy",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Content = new ScrollViewer { Content = pickerPanel, MaxHeight = 400 },
        });
        if (pickerResult != ContentDialogResult.Primary
            || radioButtons.SelectedItem is not string selectedFilename || string.IsNullOrEmpty(selectedFilename))
            return false;

        var targetCardCustom = FindCard(capturedName);

        if (targetCardCustom?.RequiresVulkanInstall == true)
        {
            // Vulkan game: affects ALL Vulkan games
            if (!await ConfirmVulkanChannelAsync("Changing the channel for this game will change it for ALL Vulkan games."))
                return false;

            _gameNameService.CustomReShadeSelection[customKey] = selectedFilename;
            foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
            {
                _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, "Custom", vCard.Source ?? "");
                _gameNameService.CustomReShadeSelection[GameKey.FromCard(vCard.GameName, vCard.Source ?? "").ToKey()] = selectedFilename;
                if (vCard.UseNormalReShade)
                    _window.ViewModel.SetUseNormalReShade(vCard, false);
                vCard.NotifyAll();
            }

            // Copy custom DLL to ProgramData Vulkan layer
            try
            {
                var customFilePath = AuxInstallService.GetCustomReShadePathForFile(selectedFilename);
                var layer64 = Path.Combine(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                if (File.Exists(customFilePath) && File.Exists(layer64))
                    await Task.Run(() => AuxInstallService.CopyFileWithElevation(customFilePath, layer64));
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailPanelBuilder] Failed to update Vulkan layer with custom ReShade — {ex.Message}");
            }
            _window.ViewModel.SaveSettingsPublic();
        }
        else
        {
            // Non-Vulkan: per-game only
            _gameNameService.CustomReShadeSelection[customKey] = selectedFilename;
            _window.ViewModel.SaveSettingsPublic();
            _window.ViewModel.SetReShadeChannelOverride(capturedName, "Custom", card.Source ?? "");
            if (targetCardCustom != null && targetCardCustom.UseNormalReShade)
                _window.ViewModel.SetUseNormalReShade(targetCardCustom, false);

            // Reinstall with custom DLLs
            if (targetCardCustom != null)
                await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardCustom);
        }
        CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → Custom ReShade selected: '{selectedFilename}'");

        _window.ViewModel.NotifyUpdateButtonChanged();
        QueueOverridesRebuild(capturedName); // reflects update inclusion changes
        return true;
    }

    // ── Shaders ─────────────────────────────────────────────────────────────────

    /// <summary>The Shaders dropdown value: the per-game mode, or the global setting when the game follows Global.</summary>
    internal string GetShaderModeDisplay(GameCardViewModel card)
    {
        var gameName = card.GameName;
        string mode = _window.ViewModel.GetPerGameShaderMode(gameName, card.Source ?? "");
        var key = GameKey.FromCard(gameName, card.Source ?? "").ToKey();
        bool hasPerGameOverride = _gameNameService.PerGameShaderMode.ContainsKey(key)
                               || _gameNameService.PerGameShaderMode.ContainsKey(gameName);
        if (mode == "Global" && !hasPerGameOverride)
        {
            if (_window.ViewModel.Settings.GlobalShadersOff) return "Off";
            if (_window.ViewModel.Settings.UseCustomShaders) return "Custom";
        }
        return ShaderModeItems.Contains(mode) ? mode : "Global";
    }

    /// <summary>Applies a Shaders pick; "Select" opens the per-game picker. False = cancelled.</summary>
    internal async Task<bool> ApplyShaderModeAsync(GameCardViewModel card, string capturedName, string selected)
    {
        CrashReporter.Log($"[DetailPanelBuilder.ShaderMode] '{capturedName}' selection changed to: '{selected}'");
        if (selected == "Select")
        {
            var key = GameKey.FromCard(capturedName, card.Source ?? "").ToKey();
            List<string>? current = _gameNameService.PerGameShaderSelection.TryGetValue(key, out var existing)
                ? existing
                : (_gameNameService.PerGameShaderSelection.TryGetValue(capturedName, out existing)
                    ? existing
                    // Fall back to global selection but strip custom file IDs — they are user-specific
                    // files that should not auto-populate every new per-game selection.
                    : _window.ViewModel.Settings.SelectedShaderPacks
                        .Where(id => !id.StartsWith(ShaderPackService.CustomFilePackId, StringComparison.OrdinalIgnoreCase))
                        .ToList());
            var result = await ShaderPopupHelper.ShowAsync(
                _window.Content.XamlRoot, _shaderPackService, current, ShaderPopupHelper.PopupContext.PerGame);
            if (result == null) return false;
            _gameNameService.PerGameShaderSelection[key] = result;
        }
        else if (selected != "Off" && selected != "Custom")
            selected = "Global";

        _window.ViewModel.SetPerGameShaderMode(capturedName, selected, card.Source ?? "");
        _window.ViewModel.DeployShadersForCard(capturedName);
        return true;
    }

    // ── Addons ──────────────────────────────────────────────────────────────────

    internal string GetAddonModeDisplay(GameCardViewModel card)
    {
        var mode = _window.ViewModel.GetPerGameAddonMode(card.GameName, card.Source ?? "");
        return mode == "Off" ? "Off" : (mode == "Select" ? "Select" : "Global");
    }

    /// <summary>Applies an Addons pick; "Select" opens the per-game picker. False = cancelled.</summary>
    internal async Task<bool> ApplyAddonModeAsync(GameCardViewModel card, string capturedName, string selected)
    {
        CrashReporter.Log($"[DetailPanelBuilder.AddonMode] '{capturedName}' selection changed to: '{selected}'");
        if (selected == "Select")
        {
            var key = GameKey.FromCard(capturedName, card.Source ?? "").ToKey();
            List<string>? current = _gameNameService.PerGameAddonSelection.TryGetValue(key, out var existing)
                ? existing
                : (_gameNameService.PerGameAddonSelection.TryGetValue(capturedName, out existing) ? existing : null);

            var result = await AddonPopupHelper.ShowAsync(
                _window.Content.XamlRoot, _window.ViewModel.AddonPackServiceInstance, current,
                AddonPopupHelper.PopupContext.PerGame, card.InstallPath);
            if (result == null) return false;
            _gameNameService.PerGameAddonSelection[key] = result;
        }
        else if (selected != "Off")
            selected = "Global";

        _window.ViewModel.SetPerGameAddonMode(capturedName, selected, card.Source ?? "");
        _window.ViewModel.DeployAddonsForCard(capturedName);
        return true;
    }

    // ── Select ReShade Preset ───────────────────────────────────────────────────

    /// <summary>
    /// Opens the preset picker, copies the picked presets into the game folder and offers to install their shaders.
    /// Returns true when the shaders were installed (the Shaders mode becomes "Select").
    /// </summary>
    internal async Task<bool> PickReShadePresetAsync(GameCardViewModel card, string capturedName)
    {
        var selected = await PresetPopupHelper.ShowAsync(_window.Content.XamlRoot);
        if (selected == null || selected.Count == 0) return false;

        var targetCard = FindCard(capturedName);
        if (targetCard == null || string.IsNullOrEmpty(targetCard.InstallPath)) return false;

        int count = PresetPopupHelper.DeployPresets(selected, targetCard.InstallPath);
        CrashReporter.Log($"[DetailPanelBuilder] Deployed {count} preset(s) to '{capturedName}'");
        if (count == 0) return false;

        var shaderResult = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Install Shaders?",
            Content = "Also install the required shaders and textures?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
        if (shaderResult != ContentDialogResult.Primary) return false;

        var presetPaths = selected.Select(f => Path.Combine(PresetPopupHelper.PresetsDir, f)).ToList();
        await _window.ViewModel.ApplyPresetShadersAsync(capturedName, presetPaths, card.Source ?? "");
        QueueOverridesRebuild(capturedName); // the Shaders dropdown shows the new "Select" mode
        return true;
    }

    // ── ReShade DLL name ────────────────────────────────────────────────────────

    /// <summary>
    /// The ReShade DLL name dropdown's items ("--------" = default name first; names used by Display Commander or
    /// OptiScaler left out) and the saved name.
    /// </summary>
    internal (string[] Items, string Selected) GetRsDllNameChoices(GameCardViewModel card)
    {
        const string sentinel = DllOverrideService.PlaceholderName;
        var cfg = _window.ViewModel.GetDllOverride(card.GameName);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(cfg?.DcFileName)) excluded.Add(cfg.DcFileName);
        if (!string.IsNullOrEmpty(cfg?.OsFileName)) excluded.Add(cfg.OsFileName);
        if (!string.IsNullOrEmpty(card.OsInstalledFile)) excluded.Add(card.OsInstalledFile);

        var current = cfg?.ReShadeFileName ?? "";
        var items = DllOverrideConstants.CommonDllNames.Where(n => !excluded.Contains(n)).Prepend(sentinel).ToList();
        if (string.IsNullOrEmpty(current)) return (items.ToArray(), sentinel);
        var match = items.FirstOrDefault(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        if (match == null) { items.Add(current); match = current; }
        return (items.ToArray(), match);
    }

    /// <summary>
    /// Saves a ReShade DLL name and renames the installed DLL ("--------" = back to the default name).
    /// Returns false when the name is OptiScaler's (nothing changed).
    /// </summary>
    internal bool ApplyRsDllName(GameCardViewModel card, string capturedName, string rsName)
    {
        var cfg = _window.ViewModel.GetDllOverride(capturedName);
        var dcName = cfg?.DcFileName ?? "";

        // -------- = clear RS override, revert DLL to default name
        if (rsName == DllOverrideService.PlaceholderName)
        {
            // Use live card state: the name may have changed since any panel was built
            var currentRsInstalled = card.RsInstalledFile ?? card.RsRecord?.InstalledAs;
            var defName = AuxInstallService.RsNormalName;
            if (!string.IsNullOrEmpty(currentRsInstalled) && card.RsRecord != null
                && !string.IsNullOrEmpty(card.InstallPath)
                && !currentRsInstalled.Equals(defName, StringComparison.OrdinalIgnoreCase))
            {
                var oldP = Path.Combine(card.InstallPath, currentRsInstalled);
                var newP = Path.Combine(card.InstallPath, defName);
                try
                {
                    if (File.Exists(oldP))
                    {
                        // If the default name (dxgi.dll) is occupied by OptiScaler, fall back to ReShade64/32.dll
                        if (File.Exists(newP) && !newP.Equals(oldP, StringComparison.OrdinalIgnoreCase))
                        {
                            var coexistName = card.Is32Bit ? AuxInstallService.RsStaged32 : AuxInstallService.RsStaged64;
                            var coexistP = Path.Combine(card.InstallPath, coexistName);
                            if (!File.Exists(coexistP))
                            {
                                File.Move(oldP, coexistP);
                                card.RsRecord.InstalledAs = coexistName;
                                _auxInstallService.SaveAuxRecord(card.RsRecord);
                                card.RsInstalledFile = coexistName;
                                CrashReporter.Log($"[DetailPanelBuilder] Reverted RS DLL '{currentRsInstalled}' → '{coexistName}' (dxgi.dll occupied by OS) for '{capturedName}'");
                            }
                        }
                        else if (!File.Exists(newP))
                        {
                            File.Move(oldP, newP);
                            card.RsRecord.InstalledAs = defName;
                            _auxInstallService.SaveAuxRecord(card.RsRecord);
                            card.RsInstalledFile = defName;
                            CrashReporter.Log($"[DetailPanelBuilder] Reverted RS DLL '{currentRsInstalled}' → '{defName}' for '{capturedName}'");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[DetailPanelBuilder] RS revert failed — {ex.Message}");
                }
            }
            // Clear RS from config — keep DC/OS if set
            _window.ViewModel.SetDllOverrideNames(card.GameName, "", dcName);
            if (!_window.ViewModel.HasDllOverride(capturedName))
                card.DllOverrideEnabled = false;
            return true;
        }

        CrashReporter.Log($"[DetailPanelBuilder] RS DLL name changed → '{rsName}' for '{capturedName}'");

        // Collision guard: block if RS name matches the installed OS name
        var osInstalledName = !string.IsNullOrEmpty(card.OsInstalledFile) ? card.OsInstalledFile : cfg?.OsFileName;
        if (!string.IsNullOrEmpty(osInstalledName) && rsName.Equals(osInstalledName, StringComparison.OrdinalIgnoreCase))
        {
            CrashReporter.Log($"[DetailPanelBuilder] Blocking RS DLL selection '{rsName}' — collides with OS name for '{capturedName}'.");
            return false;
        }

        if (_window.ViewModel.HasDllOverride(capturedName))
            _window.ViewModel.UpdateDllOverrideNames(card, rsName, dcName);
        else
            _window.ViewModel.EnableDllOverride(card, rsName, dcName);
        card.NotifyAll();
        return true;
    }
}
