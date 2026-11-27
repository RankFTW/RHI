// DetailPanelBuilder.NeuralRendering.Shared.cs -- installing and removing a Neural Rendering method, shared by the
// section's Install / Remove buttons and Tune presets (MainWindow.Tune.cs). Tune passes small detached combos holding
// its chosen versions (the installers read SelectedItem as a string: "Latest..." or a version).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>
    /// Installs a Neural Rendering method the way the section's Install button does: ReShade first if needed, the
    /// method's files, the Cost Scaler when it's on for the game, the method saved for the game and in rhi_install.txt,
    /// conflicting global add-ons dropped. <paramref name="statusBtn"/> shows progress text.
    /// </summary>
    internal async Task InstallNrMethodCoreAsync(GameCardViewModel card, string selKey, Button statusBtn,
        ComboBox addonVersionCombo, ComboBox nrVersionCombo, ComboBox packVersionCombo)
    {
        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();
        var addonSvc    = _window.ViewModel.AddonPackServiceInstance;
        var dlssSvc     = _dlssStreamlineService;
        var upliftSvc   = App.Services.GetRequiredService<UpliftService>();

        // Feeder's shaders are component files (FeederShaders): the game's shader mode and selection
        // are left alone (issue #182: switching it to Select wiped a Global game's shaders).

        // Ensure ReShade is installed first — all NR methods require it
        if (!card.IsRsInstalled)
        {
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Installing ReShade...");

            // For DX9 Feeder: dgVoodoo2 will own d3d9.dll, so ReShade must be dxgi.dll.
            // Call InstallReShadeInternalAsync directly with the forced filename instead of
            // going through InstallReShadeCommand which uses auto-detection (returns d3d9.dll for DX9).
            bool feederDx9 = selKey == NrMethodFeeder && IsDx9FeederGame(card);
            if (feederDx9)
                await _window.ViewModel.InstallReShadeInternalAsync(card, "dxgi.dll").ConfigureAwait(false);
            else
                await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(card).ConfigureAwait(false);

            // Wait for card to reflect installed state
            await Task.Delay(500).ConfigureAwait(false);
        }
        else if (selKey == NrMethodFeeder
            && IsDx9FeederGame(card)
            && card.RsRecord?.InstalledAs?.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase) == true)
        {
            // ReShade already installed as d3d9.dll (wrong for dgVoodoo) — reinstall as dxgi.dll
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Fixing ReShade filename...");
            await _window.ViewModel.InstallReShadeInternalAsync(card, "dxgi.dll").ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
        }

        switch (selKey)
        {
            case NrMethodDlss5Tool:
                await InstallDlss5ToolAsync(card, statusBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                break;

            case NrMethodDlss5ToolBridge:
                await InstallDlss5ToolAsync(card, statusBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                await InstallBridgeAddonAsync(card, statusBtn, addonSvc, packVersionCombo);
                // Append bridge file to the Dlss5Tool component record
                {
                    var bFiles = Models.RhiInstallManifest.GetComponentFiles(installPath, "Dlss5Tool").ToList();
                    if (!bFiles.Contains(BridgeDeployFile, StringComparer.OrdinalIgnoreCase))
                        bFiles.Add(BridgeDeployFile);
                    Models.RhiInstallManifest.SetComponent(installPath, "Dlss5Tool", bFiles);
                }
                break;

            case NrMethodShortFuse:
                await InstallShortFuseAsync(card, statusBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc);
                break;

            case NrMethodFeeder:
                await InstallFeederAddonAsync(card, statusBtn, addonSvc, dlssSvc, nrVersionCombo, addonVersionCombo, packVersionCombo);
                break;

            case NrMethodUplift:
                await InstallUpliftAsync(card, statusBtn, addonVersionCombo, nrVersionCombo, dlssSvc, upliftSvc);
                break;
        }

        // If Cost Scaler preference is On, deploy it now (NR DLL is freshly placed).
        // Not for GITC Uplift: its NR DLL isn't beside the game exe, where the Cost Scaler works.
        if (selKey != NrMethodUplift && _window.ViewModel.GetNrCostScalerEnabled(gameName, store))
        {
            var csSvc = App.Services.GetRequiredService<DlssNrCostScalerService>();
            if (csSvc.IsStagingReady)
                csSvc.Install(installPath);
        }

        // Persist chosen method
        _window.ViewModel.SetNrMethodOverride(gameName, selKey, store);

        // Record the active NR method in rhi_install.txt for cross-component reference
        // (e.g. OptiScaler uninstall uses this to know nvngx_dlssnr.dll is NR-owned)
        Models.RhiInstallManifest.SetNrMethod(installPath, selKey);

        // Remove conflicting global addons — DLSS5 Tool and ShortFuse both deploy NR addons
        // that conflict with the NR section. Remove them from the global set so they don't
        // get re-deployed on every refresh.
        var globalAddons = _window.ViewModel.Settings.EnabledGlobalAddons;
        var conflicting  = new[] { "DLSS5 Tool", "ShortFuse DLSS Tool" };
        bool removedAny  = false;
        foreach (var c in conflicting)
            if (globalAddons.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0)
                removedAny = true;
        if (removedAny)
        {
            _window.ViewModel.SaveSettingsPublic();
            CrashReporter.Log($"[NeuralRendering.Install] Removed conflicting global addons (DLSS5 Tool / ShortFuse) for '{gameName}'");
        }

        // Also remove from per-game selection if the game uses one
        RemoveNrConflictingAddonsFromPerGameSelection(gameName, store, conflicting);

        // Re-deploy addons for this game so stale NR addon files are removed immediately
        _window.ViewModel.DeployAddonsForCard(gameName);
    }

    /// <summary>
    /// Removes a Neural Rendering method's files the way the section's Remove button does (Cost Scaler first, then the
    /// method, then the saved method and conflicting add-ons). Does file work: call it off the UI thread.
    /// </summary>
    internal void RemoveNrMethodCore(GameCardViewModel card, string selKey)
    {
        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();
        var upliftSvc   = App.Services.GetRequiredService<UpliftService>();

        // Uninstall Cost Scaler first (restores _real → nvngx_dlssnr.dll before NR cleanup)
        var csSvcRemove = App.Services.GetRequiredService<DlssNrCostScalerService>();
        csSvcRemove.Uninstall(installPath);
        _window.ViewModel.SetNrCostScalerEnabled(gameName, false, store);

        switch (selKey)
        {
            case NrMethodDlss5Tool:
                rdx5Svc.Uninstall(installPath);
                RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                break;

            case NrMethodDlss5ToolBridge:
                rdx5Svc.Uninstall(installPath);
                RemoveAddonFile(installPath, BridgeDeployFile, "NeuralRendering.Remove.Bridge");
                RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                break;

            case NrMethodShortFuse:
            {
                var det = _dlssStreamlineService.Detect(installPath);
                rdx5Svc.UninstallSf(installPath, det.HasAny ? det : null);

                // Revert ShortFuse auto-config (rename Reshade64.asi back, remove UAL)
                _window.ViewModel.RevertSfAutoConfig(card);
                break;
            }

            case NrMethodFeeder:
            {
                var file = card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
                RemoveAddonFile(installPath, file, "NeuralRendering.Remove.Feeder");

                // Remove DLSS5 Tool neural consumer
                rdx5Svc.Uninstall(installPath);

                // Remove nvngx_dlss.dll if we placed it (sentinel)
                var dlssDest = Path.Combine(installPath, "nvngx_dlss.dll");
                var dlssSentinel = dlssDest + ".original";
                if (File.Exists(dlssSentinel))
                {
                    var info = new FileInfo(dlssSentinel);
                    if (info.Length == 0) { try { File.Delete(dlssDest); File.Delete(dlssSentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to remove RHI-placed nvngx_dlss.dll — {ex.Message}"); } }
                    else { try { File.Copy(dlssSentinel, dlssDest, overwrite: true); File.Delete(dlssSentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildNeuralRenderingSectionWithData] Failed to restore original nvngx_dlss.dll — {ex.Message}"); } }
                }

                // Remove NR dll
                rdx5Svc.RemoveNrDll(installPath, "Feeder");

                // Remove only DLSS5Feeder + LumeniteFX shader files — never wipe the whole folder
                RemoveFeederShaders(installPath, gameName, store, card);

                // Remove host64\ folder (entirely RHI-managed — no game files in it)
                var host64Dir = Path.Combine(installPath, "host64");
                if (Directory.Exists(host64Dir))
                {
                    try { Directory.Delete(host64Dir, recursive: true); CrashReporter.Log($"[NeuralRendering] Removed host64\\ from '{installPath}'"); }
                    catch (Exception h64Ex) { CrashReporter.Log($"[NeuralRendering] host64\\ removal failed — {h64Ex.Message}"); }
                }

                // Remove dgVoodoo2 if it was deployed by RHI (sentinel present)
                // Only remove if Luma isn't also installed (Luma needs D3D9.dll too)
                if (card.LumaStatus != GameStatus.Installed)
                    App.Services.GetRequiredService<DgVoodooService>().RemoveFromGame(installPath);
                else
                    CrashReporter.Log($"[NeuralRendering] Luma still installed — preserving dgVoodoo2 for '{gameName}'");

                Models.RhiInstallManifest.RemoveComponent(installPath, "Feeder");
                break;
            }

            case NrMethodUplift:
                upliftSvc.Uninstall(installPath);
                _window.DispatcherQueue?.TryEnqueue(() => _window.ViewModel.DeployShadersForCard(gameName)); // drops its shaders
                break;
        }

        _window.ViewModel.SetNrMethodOverride(gameName, null, store);

        // Clear NR method from rhi_install.txt
        Models.RhiInstallManifest.SetNrMethod(installPath, null);

        // Remove conflicting addons from global and per-game selections
        var conflictingRemove = new[] { "DLSS5 Tool", "ShortFuse DLSS Tool" };
        var globalAddonsRemove = _window.ViewModel.Settings.EnabledGlobalAddons;
        bool removedGlobal = false;
        foreach (var c in conflictingRemove)
            if (globalAddonsRemove.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0)
                removedGlobal = true;
        if (removedGlobal) _window.ViewModel.SaveSettingsPublic();
        RemoveNrConflictingAddonsFromPerGameSelection(gameName, store, conflictingRemove);
    }

    // ── Tune presets ──────────────────────────────────────────────────────────

    /// <summary>Method keys a Tune preset can pick (stored in settings.json: never rename).</summary>
    internal static readonly IReadOnlyList<string> TuneNrMethods =
        new[] { NrMethodShortFuse, NrMethodDlss5Tool, NrMethodDlss5ToolBridge, NrMethodFeeder, NrMethodUplift };

    internal static string NrMethodLabel(string key) => key switch
    {
        NrMethodShortFuse       => "ShortFuse DLSS Tool",
        NrMethodDlss5Tool       => "DLSS5 Tool",
        NrMethodDlss5ToolBridge => "DLSS5 Tool + DX11 Bridge",
        NrMethodFeeder          => "DLSS5 Feeder",
        NrMethodUplift          => "GITC Uplift",
        _                       => key,
    };

    /// <summary>The add-on version list a method's version pick comes from (empty when the list isn't loaded yet).</summary>
    internal static IReadOnlyList<string> NrAddonVersionsFor(string key)
    {
        try
        {
            if (key == NrMethodUplift) return App.Services.GetRequiredService<UpliftService>().AvailableVersions;
            return App.Services.GetRequiredService<Renodx5AddonService>().GetAvailableVersions(AddonTypeFor(key));
        }
        catch (InvalidOperationException ex)
        {
            CrashReporter.Log($"[DetailPanelBuilder.NrAddonVersionsFor] {key} — {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Why a method can't be used in this game (same rules as the section's greyed-out items), or null.</summary>
    internal static string? NrMethodBlocked(GameCardViewModel card, string key)
    {
        bool is32 = card.Is32Bit;
        bool hasDlss = card.HasAnyDlssStreamline;
        var api = card.GraphicsApi;
        return key switch
        {
            NrMethodShortFuse when is32 => "Needs a 64-bit game",
            NrMethodShortFuse when api == GraphicsApiType.OpenGL => "Not for OpenGL games",
            NrMethodDlss5Tool when is32 => "Needs a 64-bit game",
            NrMethodDlss5Tool when !hasDlss => "Needs a game with DLSS",
            NrMethodDlss5ToolBridge when is32 => "Needs a 64-bit game",
            NrMethodDlss5ToolBridge when !hasDlss => "Needs a game with DLSS",
            NrMethodDlss5ToolBridge when api is not (GraphicsApiType.DirectX11 or GraphicsApiType.Vulkan) => "Needs a DirectX 11 or Vulkan game",
            _ => null,
        };
    }

    /// <summary>The method the section picks for a game when none is chosen.</summary>
    internal static string AutoNrMethod(GameCardViewModel card)
    {
        bool is32 = card.Is32Bit;
        var api = card.GraphicsApi;
        bool isDx11 = api == GraphicsApiType.DirectX11, isDx12 = api == GraphicsApiType.DirectX12, isVulkan = api == GraphicsApiType.Vulkan;
        bool upliftApplicable = !is32 && (isDx11 || isDx12 || isVulkan);
        return is32                              ? NrMethodFeeder
             : api == GraphicsApiType.OpenGL     ? NrMethodFeeder
             : !card.HasAnyDlssStreamline        ? (upliftApplicable ? NrMethodUplift : NrMethodFeeder)
             : (isDx11 || isVulkan)              ? NrMethodDlss5ToolBridge
             :                                     NrMethodShortFuse;
    }

    /// <summary>The method installed in the game folder, or null (file checks: call off the UI thread).</summary>
    internal static string? DetectInstalledNrMethod(string installPath, bool is32Bit)
    {
        if (string.IsNullOrEmpty(installPath)) return null;
        var rdx5 = App.Services.GetRequiredService<Renodx5AddonService>();
        if (rdx5.IsSfInstalledIn(installPath)) return NrMethodShortFuse;
        if (File.Exists(Path.Combine(installPath, is32Bit ? FeederDeployFile32 : FeederDeployFile64))) return NrMethodFeeder;
        if (rdx5.IsInstalledIn(installPath))
            return File.Exists(Path.Combine(installPath, BridgeDeployFile)) ? NrMethodDlss5ToolBridge : NrMethodDlss5Tool;
        if (UpliftService.IsRhiInstalled(installPath)) return NrMethodUplift;
        return null;
    }

    /// <summary>
    /// Installs a method for Tune with the given versions ("" = Latest). The per-game version choices are saved like
    /// the section saves them. True when the method is in the game afterwards.
    /// </summary>
    internal async Task<bool> InstallNrForTuneAsync(GameCardViewModel card, string key, string addonVersion, string nrDllVersion)
    {
        var gameName = card.GameName;
        var store = card.Source ?? "";
        static ComboBox One(string? version) => new() { ItemsSource = new List<string> { string.IsNullOrEmpty(version) ? "Latest" : version }, SelectedIndex = 0 };
        var statusBtn = new Button();
        _window.ViewModel.SetNrAddonVersion(gameName, string.IsNullOrEmpty(addonVersion) ? null : addonVersion, store);
        try
        {
            await InstallNrMethodCoreAsync(card, key, statusBtn, One(addonVersion), One(nrDllVersion), One(null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            CrashReporter.Log($"[DetailPanelBuilder.InstallNrForTuneAsync] '{gameName}' {key} failed — {ex.Message}");
        }
        _window.ViewModel.SetNrDllVersion(gameName, string.IsNullOrEmpty(nrDllVersion) ? null : nrDllVersion, store);
        var installPath = card.InstallPath ?? "";
        bool is32 = card.Is32Bit;
        var now = await Task.Run(() => DetectInstalledNrMethod(installPath, is32));
        await Task.Run(() => RefreshDlssDetection(card));
        CrashReporter.Log($"[DetailPanelBuilder.InstallNrForTuneAsync] '{gameName}' {key} (addon {(addonVersion.Length == 0 ? "Latest" : addonVersion)}, NR DLL {(nrDllVersion.Length == 0 ? "Latest" : nrDllVersion)}) → installed: {now ?? "none"}");
        return now == key;
    }

    /// <summary>Removes a method for Tune (the section's Remove), then re-reads the game's DLSS files.</summary>
    internal async Task RemoveNrForTuneAsync(GameCardViewModel card, string key)
    {
        await Task.Run(() => RemoveNrMethodCore(card, key));
        await Task.Run(() => RefreshDlssDetection(card));
    }

    /// <summary>NR methods replace and restore DLSS files: the card's detection is read again.</summary>
    private void RefreshDlssDetection(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var det = _dlssStreamlineService.Detect(card.InstallPath);
        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            card.DlssDetection = det;
            card.ApplyDlssDetection(det);
            card.RefreshDlssVersions(_dlssStreamlineService);
        });
    }
}
