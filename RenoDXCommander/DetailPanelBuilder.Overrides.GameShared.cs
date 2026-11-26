// DetailPanelBuilder.Overrides.GameShared.cs — game-wide per-game settings (they were in Game Overrides until 2.9.6.3).
// Game name, bitness, Graphics API (ApiShared) and Reset → the game settings ⚙ (DetailLayoutHost.GameSettings.cs);
// wiki mod name and wiki lookup → the RenoDX ⚙.

using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal static readonly string[] BitnessItems = { "Auto", "32-bit", "64-bit" };

    // ── Bitness ─────────────────────────────────────────────────────────────────

    internal string GetBitnessDisplay(GameCardViewModel card) => _window.ViewModel.GetBitnessOverride(card.GameName, card.Source) switch
    {
        "32" => "32-bit",
        "64" => "64-bit",
        _ => "Auto",
    };

    /// <summary>
    /// Saves the bitness override ("Auto" clears it). When the game's effective bitness changes, every component is
    /// uninstalled first (their files are named by bitness), except in Vulkan games (the global layer has both).
    /// <paramref name="rebuildNow"/>: rebuild the detail panel straight away. Returns true when components were removed.
    /// </summary>
    internal bool ApplyBitnessOverride(GameCardViewModel card, string capturedName, string selected, bool rebuildNow)
    {
        string? overrideValue = selected switch { "32-bit" => "32", "64-bit" => "64", _ => null };
        _window.ViewModel.SetBitnessOverride(capturedName, overrideValue, card.Source);

        var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
            c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
        if (targetCard == null) return false;

        var previousIs32Bit = targetCard.Is32Bit;
        bool newIs32Bit = overrideValue switch
        {
            "32" => true,
            "64" => false,
            // "Auto": re-resolve from auto-detection
            _ => _window.ViewModel.ResolveIs32Bit(capturedName, _peHeaderService.DetectGameArchitecture(targetCard.InstallPath), targetCard.Source ?? ""),
        };

        // If bitness actually changed, uninstall all components BEFORE updating card.Is32Bit
        // (uninstall methods use card.Is32Bit to resolve filenames of deployed DLLs)
        bool removed = false;
        if (previousIs32Bit != newIs32Bit && !targetCard.RequiresVulkanInstall)
        {
            removed = true;
            if (targetCard.IsRsInstalled) _window.ViewModel.UninstallReShade(targetCard);
            if (targetCard.DcStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallDc(targetCard);
            if (targetCard.InstalledRecord != null) _window.ViewModel.UninstallMod(targetCard);
            if (targetCard.UlStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallUl(targetCard);
            if (targetCard.OsStatus == Models.GameStatus.Installed) _optiScalerService.Uninstall(targetCard);
            if (targetCard.DxvkStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallDxvk(targetCard);
            if (targetCard.RefStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallREFramework(targetCard);
            if (targetCard.LumaStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallLuma(targetCard);
        }

        targetCard.Is32Bit = newIs32Bit;
        targetCard.NotifyAll();
        CrashReporter.Log($"[BitnessOverride] '{capturedName}' = {selected}{(removed ? " (bitness changed: components uninstalled)" : "")}");

        if (rebuildNow)
        {
            // RequestReselect is a no-op when the game is already selected: rebuild directly
            _window.PopulateDetailPanel(targetCard);
            _window.RequestReselect(capturedName);
        }
        else
        {
            UpdateDetailComponentRows(targetCard);
            QueueOverridesRebuild(capturedName);
        }
        return removed;
    }

    // ── Game name and wiki name ─────────────────────────────────────────────────

    /// <summary>The auto-detected store name to go back to (the current name when it was never renamed).</summary>
    internal string GetOriginalGameName(string gameName) => (_window.ViewModel.GetOriginalStoreName(gameName) ?? gameName).Trim();

    /// <summary>Renames the game (all per-game settings follow). False when the name is empty or unchanged.</summary>
    internal bool RenameGameTo(string currentName, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return false;
        _window.ViewModel.RenameGame(currentName, newName);
        _window.RequestReselect(newName);
        return true;
    }

    internal string GetWikiName(string gameName) => _window.ViewModel.GetUserNameMapping(gameName) ?? "";

    /// <summary>Saves the wiki mod name the game is looked up by ("" removes it, so the game name is used).</summary>
    internal void SetWikiName(string gameName, string wikiName)
    {
        wikiName = wikiName.Trim();
        var existing = _window.ViewModel.GetNameMapping(gameName);
        if (wikiName.Length > 0)
        {
            if (!wikiName.Equals(existing, StringComparison.OrdinalIgnoreCase))
                _window.ViewModel.AddNameMapping(gameName, wikiName);
        }
        else if (existing != null)
            _window.ViewModel.RemoveNameMapping(gameName);
    }

    internal string GetWikiLookupDisplay(string gameName) =>
        _window.ViewModel.IsWikiExcluded(gameName) ? WikiExcludedItem : WikiIncludedItem;

    internal void SetWikiLookup(string gameName, string choice)
    {
        bool exclude = choice == WikiExcludedItem;
        if (exclude != _window.ViewModel.IsWikiExcluded(gameName))
            _window.ViewModel.ToggleWikiExclusion(gameName);
    }

    // ── Reset ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets this game's per-game settings to their defaults (the game settings ⚙'s Reset; it was Game Overrides'
    /// Reset Overrides until 2.9.6.3): name, wiki name and lookup, shaders, addons, DLL names, update inclusion,
    /// No Addons ReShade, DXVK, bitness, Graphics API, ReShade channel, launch options, DLSS presets, display
    /// modes and the RHI overlay.
    /// </summary>
    internal void ResetGameSettings(GameCardViewModel card)
    {
        var vm = _window.ViewModel;
        var name = card.GameName;
        var store = card.Source;

        // Bitness and Graphics API go through the shared methods first when they are overridden: a change of
        // bitness uninstalls the components, and a switch between DirectX and Vulkan removes the other ReShade.
        if (GetBitnessDisplay(card) != "Auto") ApplyBitnessOverride(card, name, "Auto", rebuildNow: true);
        if (GetApiDisplay(card) != "Auto") ApplyApiOverride(card, name, "Auto", rebuildNow: true);

        // Name back to the detected one (all per-game settings follow the rename)
        var resetName = GetOriginalGameName(name);
        bool nameChanged = !resetName.Equals(name, StringComparison.OrdinalIgnoreCase);
        if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
        {
            vm.RenameGame(name, resetName);
            name = resetName;
        }

        // Wiki mod name and lookup
        if (vm.GetNameMapping(name) != null) vm.RemoveNameMapping(name);
        if (vm.IsWikiExcluded(name)) vm.ToggleWikiExclusion(name);

        // Shader mode → Global
        if (vm.GetPerGameShaderMode(name, store) != "Global")
        {
            vm.SetPerGameShaderMode(name, "Global", store);
            card.ShaderModeOverride = null; // update card in-memory so DeployShadersForCard uses global
            _gameNameService.PerGameShaderSelection.Remove(GameKey.FromCard(name, store).ToKey());
            _gameNameService.PerGameShaderSelection.Remove(name); // legacy fallback
            vm.DeployShadersForCard(name);
        }

        // Addon mode → Global
        if (vm.GetPerGameAddonMode(name, store) != "Global")
        {
            vm.SetPerGameAddonMode(name, "Global", store);
            vm.DeployAddonsForCard(name);
        }

        // DLL names back to the defaults
        _dllOverrideService.SetOsDllOverride(name, "");
        vm.DisableDllOverride(card);
        card.NotifyAll();

        // Include everything in Update All
        if (vm.IsUpdateAllExcludedReShade(name, store)) vm.ToggleUpdateAllExclusionReShade(name, store);
        if (vm.IsUpdateAllExcludedRenoDx(name, store)) vm.ToggleUpdateAllExclusionRenoDx(name, store);
        if (vm.IsUpdateAllExcludedUl(name, store)) vm.ToggleUpdateAllExclusionUl(name, store);
        if (vm.IsUpdateAllExcludedDc(name, store)) vm.ToggleUpdateAllExclusionDc(name, store);
        if (vm.IsUpdateAllExcludedOs(name, store)) vm.ToggleUpdateAllExclusionOs(name, store);
        if (vm.IsUpdateAllExcludedDxvk(name, store)) vm.ToggleUpdateAllExclusionDxvk(name, store);

        // ReShade without add-ons → with add-ons
        if (card.UseNormalReShade) vm.SetUseNormalReShade(card, false);

        // DXVK: uninstall if active. The variant and Lilium preset describe the installed DXVK: clear them only
        // once it's gone, or Update would deploy another variant's DLLs over a kept (game running) install.
        {
            bool dxvkInstalled = card.DxvkEnabled || card.DxvkStatus is GameStatus.Installed or GameStatus.UpdateAvailable;
            var dxvkResetName = name;
            _ = ResetDxvkOverridesAsync();
            async Task ResetDxvkOverridesAsync()
            {
                try
                {
                    if (dxvkInstalled)
                    {
                        await vm.HandleDxvkToggleAsync(card, false, _window.Content.XamlRoot);
                        if (card.DxvkStatus is GameStatus.Installed or GameStatus.UpdateAvailable)
                        {
                            CrashReporter.Log($"[DetailPanelBuilder.ResetGameSettings] '{dxvkResetName}': DXVK not uninstalled — variant and Lilium preset kept");
                            return;
                        }
                    }
                    vm.SetDxvkVariantOverride(dxvkResetName, null, store);
                    vm.SetLiliumPreset(dxvkResetName, 0, store);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DetailPanelBuilder.ResetGameSettings] '{dxvkResetName}': DXVK reset failed — {ex.Message}");
                }
            }
        }

        // Bitness and Graphics API overrides (cleared above when they were set; this covers every stored form)
        vm.SetBitnessOverride(name, null, store);
        vm.SetApiOverride(name, null, store);

        // ReShade channel → Stable; an install on another channel is reinstalled
        var previousChannel = vm.GetReShadeChannelOverride(name, store);
        vm.SetReShadeChannelOverride(name, null, store);
        if (card.RsRecord != null && !string.IsNullOrEmpty(previousChannel))
            _ = vm.InstallReShadeAsync(card);

        // Custom ReShade DLL selection (composite key + legacy fallback)
        _gameNameService.CustomReShadeSelection.Remove(GameKey.FromCard(name, store).ToKey());
        _gameNameService.CustomReShadeSelection.Remove(name);

        // Launch executable and arguments
        _gameNameService.LaunchExeOverrides.Remove(name);
        _gameNameService.LaunchArgsOverrides.Remove(name);
        vm.SaveSettingsPublic();

        // Card back to the auto-detected bitness and APIs (the overrides are cleared now)
        card.Is32Bit = vm.ResolveIs32Bit(name, _peHeaderService.DetectGameArchitecture(card.InstallPath), card.Source ?? "");
        card.DetectedApis = vm._DetectAllApisForCard(card.InstallPath, name, card.Source);
        card.IsDualApiGame = GraphicsApiDetector.IsDualApi(card.DetectedApis);
        card.GraphicsApi = vm.DetectGraphicsApi(card.InstallPath, EngineType.Unknown, name, card.Source);
        card.NotifyAll();

        // DLSS presets → Default
        if (_dlssPresetService.IsSupported)
        {
            _dlssPresetService.SetSrPreset(name, card.InstallPath, 0);
            _dlssPresetService.SetRrPreset(name, card.InstallPath, 0);
            _dlssPresetService.SetFgPreset(name, card.InstallPath, 0);
        }

        // Forget the game's display mode state (what it swaps and its values per mode; files are left alone)
        vm.ResetDisplayModeState(card);
        _window.RefreshDisplayModeUi(card);

        // Turn the RHI overlay back on
        if (vm.IsOverlayDisabled(name, store))
        {
            vm.SetOverlayDisabled(name, false, store);
            _window.RefreshOverlayPassthrough();
        }

        CrashReporter.Log($"[DetailPanelBuilder.ResetGameSettings] Settings reset for: {name}");

        if (nameChanged) _window.RequestReselect(name);
        else QueueOverridesRebuild(name); // the sections and the override notes show the defaults again
    }
}
