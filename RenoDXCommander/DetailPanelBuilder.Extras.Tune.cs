// DetailPanelBuilder.Extras.Tune.cs — Tune presets: install / remove the game page's Extras without the name / variant
// pickers. Every install and remove runs the same core methods as the Extras rows' buttons (the *CoreAsync / *Core
// methods next to each row), so prompts, order, logging and rebuilds match the rows.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RenoDXCommander;

/// <summary>Ids of the Extras a Tune preset can install. Stored in user settings: never rename.</summary>
internal static class TuneExtraIds
{
    public const string AsiLoader    = "AsiLoader";
    public const string Hsf          = "HSF";
    public const string RtxEncore    = "RtxEncore";
    public const string DlssEnabler  = "DlssEnabler";
    public const string UniversalMfg = "UniversalMfg";
    public const string MfgUnlock    = "MfgUnlock";
    public const string Dlssg2030    = "Dlssg2030";
    public const string BfiPresenter = "BfiPresenter";
    public const string Dxvk         = "DXVK";
    public const string DgVoodoo     = "dgVoodoo2";

    public static readonly IReadOnlyList<string> All = new[]
    {
        AsiLoader, Hsf, RtxEncore, DlssEnabler, UniversalMfg, MfgUnlock, Dlssg2030, BfiPresenter, Dxvk, DgVoodoo,
    };
}

public partial class DetailPanelBuilder
{
    /// <summary>What the core install methods return when the user cancelled a prompt.</summary>
    internal const string TuneCancelled = "Cancelled";

    private static readonly string[] TuneDxvkVariants = { "Stable", "Development", "LiliumHdr" };

    internal static string TuneExtraLabel(string id) => id switch
    {
        TuneExtraIds.AsiLoader    => "Ultimate ASI Loader",
        TuneExtraIds.Hsf          => "HSF",
        TuneExtraIds.RtxEncore    => "RTX Encore",
        TuneExtraIds.DlssEnabler  => "DLSS Enabler",
        TuneExtraIds.UniversalMfg => "Universal MFG Unlocker",
        TuneExtraIds.MfgUnlock    => "MFG Unlock",
        TuneExtraIds.Dlssg2030    => "20/30 FG Unlock",
        TuneExtraIds.BfiPresenter => "BFI Presenter",
        TuneExtraIds.Dxvk         => "DXVK",
        TuneExtraIds.DgVoodoo     => "dgVoodoo2",
        _                         => id,
    };

    internal static string TuneExtraOptionLabel(string id) => id switch
    {
        TuneExtraIds.AsiLoader or TuneExtraIds.RtxEncore or TuneExtraIds.DlssEnabler
            or TuneExtraIds.UniversalMfg or TuneExtraIds.Dlssg2030 => "DLL name",
        TuneExtraIds.Dxvk     => "Variant",
        TuneExtraIds.DgVoodoo => "Version",
        _                     => "",
    };

    /// <summary>Every option value for the preset editor ("" = default is added by the editor).</summary>
    internal IReadOnlyList<string> TuneExtraOptions(string id) => id switch
    {
        TuneExtraIds.AsiLoader    => UltimateAsiLoaderService.Win64Names,
        TuneExtraIds.RtxEncore    => RtxEncoreService.KnownProxyNames,
        TuneExtraIds.DlssEnabler  => DeDllNames,
        TuneExtraIds.UniversalMfg => MfgDllNames,
        TuneExtraIds.Dlssg2030    => Dlssg20_30Service.ProxyNames,
        TuneExtraIds.Dxvk         => TuneDxvkVariants,
        TuneExtraIds.DgVoodoo     => _window.ViewModel.Manifest?.DgVoodooVersions?.Keys.ToList() ?? new List<string>(),
        _                         => Array.Empty<string>(),
    };

    /// <summary>
    /// Why the extra can't be installed in this game now (the same rules that hide the row or disable its install
    /// button), or null when it can. UI thread.
    /// </summary>
    internal string? TuneExtraBlocked(GameCardViewModel card, string id)
    {
        var installPath = card.InstallPath ?? "";
        if (string.IsNullOrEmpty(installPath)) return "This game has no install folder.";
        string? Conflict(ExtraKind self) => ExtraBlocker(card, self) is { } b ? ExtraBlockReason(self, b) : null;

        switch (id)
        {
            case TuneExtraIds.AsiLoader:
                return null;
            case TuneExtraIds.Hsf:
                if (!FeatureFlags.Hsf && !HsfService.IsInstalledIn(installPath, card.Is32Bit))
                    return "HSF isn't available in this version of RHI's online data.";
                if (MainViewModel.HsfBlock(card) is { } hsfBlock) return hsfBlock.Long;
                return Conflict(ExtraKind.Hsf);
            case TuneExtraIds.RtxEncore:
                return Conflict(ExtraKind.RtxEncore);
            case TuneExtraIds.DlssEnabler:
                if (ExtraBlocker(card, ExtraKind.DlssEnabler) == ExtraKind.OptiScaler)
                    return "Cannot install alongside OptiScaler — DLSS Enabler is already included within OptiScaler";
                return Conflict(ExtraKind.DlssEnabler);
            case TuneExtraIds.UniversalMfg:
                return Conflict(ExtraKind.UniversalMfg);
            case TuneExtraIds.MfgUnlock:
                if (!card.IsRsInstalled) return "Install ReShade first — MFG Unlock requires it";
                if (card.Is32Bit) return "MFG Unlock works in 64-bit games only.";
                return Conflict(ExtraKind.MfgUnlockAddon);
            case TuneExtraIds.Dlssg2030:
                return Conflict(ExtraKind.Dlssg2030);
            case TuneExtraIds.BfiPresenter:
            {
                var installedAs = _window.ViewModel.GetBfiPresenterInstalledAs(card.GameName, card.Source ?? "");
                if (BfiPresenterService.IsInstalledIn(installPath, installedAs)) return null;
                var (route, blocked) = BfiRouteFor(card);
                return route == null ? blocked ?? "BFI Presenter isn't available in this game." : null;
            }
            case TuneExtraIds.Dxvk:
                if (!card.IsDxvkToggleVisible) return "DXVK isn't offered for this game (DirectX 8 to 11 games only).";
                if (!card.IsDxvkToggleEnabled) return card.DxvkToggleTooltip ?? "DXVK can't be installed in this game.";
                if (card.DxvkIsInstalling) return "DXVK is being installed or removed already.";
                return null;
            case TuneExtraIds.DgVoodoo:
            {
                bool deployed = App.Services.GetRequiredService<DgVoodooService>().IsDeployed(installPath);
                if (card.GraphicsApi != GraphicsApiType.DirectX9 && !deployed) return "dgVoodoo2 is for DirectX 9 games only.";
                if (!(_window.ViewModel.Manifest?.DgVoodooVersions?.Count > 0))
                    return "No dgVoodoo2 versions are listed in the online data. Try Refresh later.";
                return null;
            }
            default:
                return $"Unknown extra '{id}'.";
        }
    }

    /// <summary>Live installed check (file I/O, no UI: may run on a background thread).</summary>
    internal bool TuneExtraInstalled(GameCardViewModel card, string id)
    {
        var vm          = _window.ViewModel;
        var gn          = card.GameName;
        var gs          = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        if (string.IsNullOrEmpty(installPath)) return false;
        switch (id)
        {
            case TuneExtraIds.AsiLoader:
                return _auxInstallService.FindRecord(gn, installPath, UltimateAsiLoaderService.AddonType) != null;
            case TuneExtraIds.Hsf:
                return HsfService.IsInstalledIn(installPath, card.Is32Bit);
            case TuneExtraIds.RtxEncore:
                return App.Services.GetRequiredService<RtxEncoreService>().IsInstalledIn(installPath, vm.GetRtxEncoreInstalledAs(gn, gs));
            case TuneExtraIds.DlssEnabler:
                return App.Services.GetRequiredService<DlssEnablerService>().IsStandaloneInstalledIn(installPath, vm.GetDeInstalledAs(gn, gs));
            case TuneExtraIds.UniversalMfg:
            {
                var name = vm.GetRtx40MfgInstalledAs(gn, gs);
                return !string.IsNullOrEmpty(name) && App.Services.GetRequiredService<Rtx40MfgService>().IsInstalledIn(installPath, name);
            }
            case TuneExtraIds.MfgUnlock:
            {
                var deployDir = ModInstallService.GetAddonDeployPath(installPath);
                return File.Exists(Path.Combine(deployDir, MfgAdaDeployFileName))
                    || File.Exists(Path.Combine(installPath, MfgAdaDeployFileName));
            }
            case TuneExtraIds.Dlssg2030:
                return App.Services.GetRequiredService<Dlssg20_30Service>().IsInstalledIn(installPath, vm.GetDlssg2030InstalledAs(gn, gs));
            case TuneExtraIds.BfiPresenter:
                return BfiPresenterService.IsInstalledIn(installPath, vm.GetBfiPresenterInstalledAs(gn, gs));
            case TuneExtraIds.Dxvk:
                return card.IsDxvkInstalled;
            case TuneExtraIds.DgVoodoo:
                return App.Services.GetRequiredService<DgVoodooService>().IsDeployed(installPath);
            default:
                return false;
        }
    }

    /// <summary>
    /// Turns a preset's option into the value to install with. "" = the row's default; a name that's taken or not valid
    /// for this game gives an Error. Extras with no option give ("", null).
    /// </summary>
    internal (string? Option, string? Error) TuneExtraResolveOption(GameCardViewModel card, string id, string wanted)
    {
        var vm          = _window.ViewModel;
        var gn          = card.GameName;
        var gs          = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        wanted ??= "";
        var label = TuneExtraLabel(id);
        bool OnDisk(string name) => !string.IsNullOrEmpty(installPath) && File.Exists(
            name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(installPath, "plugins", name)
                : Path.Combine(installPath, name));

        switch (id)
        {
            case TuneExtraIds.AsiLoader:
            {
                var current = _auxInstallService.FindRecord(gn, installPath, UltimateAsiLoaderService.AddonType)?.InstalledAs;
                var owners  = UltimateAsiLoaderService.FixedRhiDllOwners(card);
                if (!string.IsNullOrEmpty(current)) owners.Remove(current);
                var names = card.Is32Bit ? UltimateAsiLoaderService.Win32Names : UltimateAsiLoaderService.Win64Names;
                var options = names.Select(n => new TuneDllChoice(n, owners.TryGetValue(n, out var o) ? o : null,
                    UltimateAsiLoaderService.RecommendedNames.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
                // Recommended names in their own order (version, winmm, winhttp)
                options = options.OrderBy(o => o.Recommended
                    ? UltimateAsiLoaderService.RecommendedNames.ToList().FindIndex(r => r.Equals(o.Name, StringComparison.OrdinalIgnoreCase))
                    : int.MaxValue).ToList();
                return PickTuneDll(label, options, current, wanted, OnDisk);
            }
            case TuneExtraIds.RtxEncore:
            {
                var current = vm.GetRtxEncoreInstalledAs(gn, gs);
                if (!App.Services.GetRequiredService<RtxEncoreService>().IsInstalledIn(installPath, current)) current = null;
                var preferred = vm.GetRtxEncoreRecommendedDll(gn);
                var options = GetExtraDllOptions(card, RtxEncoreService.KnownProxyNames, current,
                        noHooked: n => n.StartsWith("bink", StringComparison.OrdinalIgnoreCase), preferred: preferred)
                    .Select(o => new TuneDllChoice(o.Name, o.BlockedBy, o.IsRecommended)).ToList();
                return PickTuneDll(label, options, current, wanted, OnDisk);
            }
            case TuneExtraIds.DlssEnabler:
            {
                var current = vm.GetDeInstalledAs(gn, gs);
                if (!App.Services.GetRequiredService<DlssEnablerService>().IsStandaloneInstalledIn(installPath, current)) current = null;
                var options = GetExtraDllOptions(card, DeDllNames, current)
                    .Select(o => new TuneDllChoice(o.Name, o.BlockedBy, o.IsRecommended)).ToList();
                return PickTuneDll(label, options, current, wanted, OnDisk);
            }
            case TuneExtraIds.Dlssg2030:
            {
                var current = vm.GetDlssg2030InstalledAs(gn, gs);
                if (!App.Services.GetRequiredService<Dlssg20_30Service>().IsInstalledIn(installPath, current)) current = null;
                var options = GetExtraDllOptions(card, Dlssg20_30Service.ProxyNames, current)
                    .Select(o => new TuneDllChoice(o.Name, o.BlockedBy, o.IsRecommended)).ToList();
                return PickTuneDll(label, options, current, wanted, OnDisk);
            }
            case TuneExtraIds.UniversalMfg:
            {
                var mfgSvc = App.Services.GetRequiredService<Rtx40MfgService>();
                var live   = vm.GetRtx40MfgInstalledAs(gn, gs);
                bool liveInstalled = !string.IsNullOrEmpty(live) && mfgSvc.IsInstalledIn(installPath, live);
                bool hasHsf = HsfService.IsInstalledIn(installPath, false) || HsfService.IsInstalledIn(installPath, true);
                // Same placement rules as the row: HSF's mods folder, then OptiScaler's plugins folder (no name to pick)
                if ((liveInstalled && Rtx40MfgService.IsHsfModsInstall(live)) || (hasHsf && card.Is32Bit))
                    return (Rtx40MfgService.HsfModsRelPath(installPath), null);
                if (vm.IsOptiScalerPluginHost(card))
                    return (Rtx40MfgService.PluginRelPath, null);
                var current = liveInstalled && !Rtx40MfgService.IsPluginInstall(live) ? live : null;
                // Bink names need the game's own Bink DLL (and not another mod's Bink slot), as in the picker
                var options = GetExtraDllOptions(card, MfgDllNames, current, noHooked: Rtx40MfgService.IsBinkName)
                    .Select(o =>
                    {
                        string? blocked = o.BlockedBy;
                        if (blocked == null && Rtx40MfgService.IsBinkName(o.Name)
                            && !string.Equals(o.Name, current, StringComparison.OrdinalIgnoreCase))
                        {
                            var path = Path.Combine(installPath, o.Name);
                            if (!File.Exists(path)) blocked = $"nothing: the game has no {o.Name} (Bink names need the game's own Bink DLL)";
                            else if (File.Exists(Rtx40MfgService.BinkHookedPath(path))) blocked = "another mod's Bink slot";
                        }
                        return new TuneDllChoice(o.Name, blocked, o.IsRecommended);
                    }).ToList();
                return PickTuneDll(label, options, current, wanted, OnDisk);
            }
            case TuneExtraIds.Dxvk:
            {
                if (wanted.Length == 0)
                    return (vm.GetDxvkVariantOverride(gn, gs) ?? vm.ResolveDxvkVariant(gn, gs).ToString(), null);
                var hit = TuneDxvkVariants.FirstOrDefault(v => v.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                return hit != null ? (hit, null) : (null, $"'{wanted}' isn't a DXVK variant.");
            }
            case TuneExtraIds.DgVoodoo:
            {
                var versions = vm.Manifest?.DgVoodooVersions;
                if (versions == null || versions.Count == 0)
                    return (null, "No dgVoodoo2 versions are listed in the online data. Try Refresh later.");
                if (wanted.Length == 0)
                {
                    var stored = vm.GetDgVoodooVersion(gn, gs);
                    return (!string.IsNullOrEmpty(stored) && versions.ContainsKey(stored) ? stored : versions.Keys.First(), null);
                }
                var match = versions.Keys.FirstOrDefault(k => k.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                return match != null ? (match, null) : (null, $"dgVoodoo2 {wanted} isn't in the online data any more.");
            }
            default:
                return ("", null);
        }
    }

    private readonly record struct TuneDllChoice(string Name, string? BlockedBy, bool Recommended);

    /// <summary>
    /// "" = the current name when installed, else the recommended name, preferring names with no file in the game folder;
    /// a named choice must be in the list and not held by another component.
    /// </summary>
    private static (string? Option, string? Error) PickTuneDll(string label, IReadOnlyList<TuneDllChoice> options,
        string? current, string wanted, Func<string, bool> onDisk)
    {
        if (wanted.Length == 0)
        {
            if (!string.IsNullOrEmpty(current) && options.Any(o => o.BlockedBy == null && o.Name.Equals(current, StringComparison.OrdinalIgnoreCase)))
                return (current, null);
            var allowed = options.Where(o => o.BlockedBy == null).ToList();
            var pick = allowed.FirstOrDefault(o => o.Recommended && !onDisk(o.Name)).Name
                    ?? allowed.FirstOrDefault(o => !onDisk(o.Name)).Name
                    ?? allowed.FirstOrDefault(o => o.Recommended).Name
                    ?? allowed.FirstOrDefault().Name;
            return pick != null ? (pick, null) : (null, $"Every DLL name {label} can use is taken in this game.");
        }
        var hit = options.FirstOrDefault(o => o.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (hit.Name == null) return (null, $"{wanted} can't be used for {label} in this game.");
        if (hit.BlockedBy != null) return (null, $"{wanted} is used by {hit.BlockedBy} in this game.");
        return (hit.Name, null);
    }

    /// <summary>
    /// Installs an extra for a Tune preset with the resolved <paramref name="option"/>: the same prompts and work as the
    /// row's install button, minus the name / variant pickers. Returns null on success, "Cancelled", or an error.
    /// Returns on the caller's (UI) thread.
    /// </summary>
    internal async Task<string?> InstallExtraForTuneAsync(GameCardViewModel card, string id, string option, XamlRoot? xamlRoot)
    {
        var vm          = _window.ViewModel;
        var gn          = card.GameName;
        var gs          = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        option ??= "";
        xamlRoot ??= _window.Content?.XamlRoot;
        CrashReporter.Log($"[DetailPanelBuilder.InstallExtraForTuneAsync] '{gn}': {id} ({(option.Length > 0 ? option : "default")})");
        if (string.IsNullOrEmpty(installPath)) return "This game has no install folder.";

        string? result;
        try
        {
            result = await InstallExtraForTuneCoreAsync(card, id, option, xamlRoot, vm, gn, gs, installPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or HttpRequestException or TaskCanceledException or COMException)
        {
            CrashReporter.Log($"[DetailPanelBuilder.InstallExtraForTuneAsync] '{gn}': {id} failed — {ex.Message}");
            result = $"{TuneExtraLabel(id)} couldn't be installed: {ex.Message}";
        }
        RequestExtrasRebuild(card);
        CrashReporter.Log($"[DetailPanelBuilder.InstallExtraForTuneAsync] '{gn}': {id} → {result ?? "installed"}");
        return result;
    }

    private async Task<string?> InstallExtraForTuneCoreAsync(GameCardViewModel card, string id, string option, XamlRoot? xamlRoot,
        MainViewModel vm, string gn, string gs, string installPath)
    {
        // Options are resolved by the caller; "" still gets the row's default here
        string Name()
        {
            if (option.Length > 0) return option;
            var (o, _) = TuneExtraResolveOption(card, id, "");
            return o ?? "";
        }

        switch (id)
        {
            case TuneExtraIds.AsiLoader:
            {
                var name = Name();
                if (name.Length == 0) return "No free DLL name for ASI Loader in this game.";
                var previous = _auxInstallService.FindRecord(gn, installPath, UltimateAsiLoaderService.AddonType)?.InstalledAs;
                return await InstallUalCoreAsync(card, name, previous, null);
            }
            case TuneExtraIds.Hsf:
            {
                if (!await ShowHsfNoticeAsync(card, xamlRoot)) return TuneCancelled;
                var error = await InstallHsfCoreAsync(card);
                if (error == null) RequestExperimentalRebuild(card); // URTI's HSF warning
                return error;
            }
            case TuneExtraIds.RtxEncore:
            {
                var svc = App.Services.GetRequiredService<RtxEncoreService>();
                var current = vm.GetRtxEncoreInstalledAs(gn, gs);
                bool isInstalled = svc.IsInstalledIn(installPath, current);
                var st = vm.Settings;
                if (!isInstalled && !await ShowClosedSourceNoticeAsync("RTX Encore", "SilyNoMeta",
                        () => st.RtxEncoreClosedSourceDismissed, () => st.RtxEncoreClosedSourceDismissed = true, xamlRoot))
                    return TuneCancelled;
                var name = Name();
                if (name.Length == 0) return "No free DLL name for RTX Encore in this game.";
                return await InstallRtxEncoreCoreAsync(card, name, current, null, xamlRoot);
            }
            case TuneExtraIds.DlssEnabler:
            {
                var name = Name();
                if (name.Length == 0) return "No free DLL name for DLSS Enabler in this game.";
                return await InstallDeCoreAsync(card, name, vm.GetDeInstalledAs(gn, gs), null);
            }
            case TuneExtraIds.UniversalMfg:
            {
                bool hasHsf = HsfService.IsInstalledIn(installPath, false) || HsfService.IsInstalledIn(installPath, true);
                var name = Name();
                if (name.Length == 0) return "No free DLL name for Universal MFG Unlocker in this game.";
                return await InstallUmfgCoreAsync(card, hasHsf, name, null, xamlRoot);
            }
            case TuneExtraIds.MfgUnlock:
                if (card.Is32Bit) return "MFG Unlock works in 64-bit games only.";
                return await InstallMfgAdaCoreAsync(card, null, "", xamlRoot);
            case TuneExtraIds.Dlssg2030:
            {
                var svc = App.Services.GetRequiredService<Dlssg20_30Service>();
                var current = vm.GetDlssg2030InstalledAs(gn, gs);
                bool isInstalled = svc.IsInstalledIn(installPath, current);
                var name = Name();
                if (name.Length == 0) return "No free DLL name for 20/30 FG Unlock in this game.";
                return await InstallDlssg2030CoreAsync(card, isInstalled, current, name, null, xamlRoot);
            }
            case TuneExtraIds.BfiPresenter:
            {
                var installedAs = vm.GetBfiPresenterInstalledAs(gn, gs);
                bool isInstalled = BfiPresenterService.IsInstalledIn(installPath, installedAs);
                var (route, blocked) = isInstalled ? (BfiPresenterService.RouteOf(installedAs!), null) : BfiRouteFor(card);
                if (route == null) return blocked ?? "BFI Presenter isn't available in this game.";
                var choice = await ShowBfiInstallDialogAsync(card, route.Value, isInstalled, xamlRoot);
                if (choice == null) return TuneCancelled;
                var error = await InstallBfiPresenterAsync(card, route.Value, choice);
                return error;
            }
            case TuneExtraIds.Dxvk:
                return await InstallDxvkForTuneAsync(card, option, xamlRoot);
            case TuneExtraIds.DgVoodoo:
                if (option.Length > 0) vm.SetDgVoodooVersion(gn, option, gs);
                return await InstallDgVoodooCoreAsync(card, null);
            default:
                return $"Unknown extra '{id}'.";
        }
    }

    /// <summary>
    /// DXVK through the same install as the row (InstallDxvkAsync: manifest warning, DXVK warning, install). That
    /// returns nothing, so the card's install state tells cancelled (never started), installed (the success message)
    /// or failed (the last message) apart.
    /// </summary>
    private async Task<string?> InstallDxvkForTuneAsync(GameCardViewModel card, string variant, XamlRoot? xamlRoot)
    {
        if (card.DxvkIsInstalling) return "DXVK is being installed or removed already.";
        if (variant.Length > 0) _window.ViewModel.SetDxvkVariantOverride(card.GameName, variant, card.Source ?? "");

        bool started = false, succeeded = false;
        string? lastMessage = null;
        void OnChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameCardViewModel.DxvkIsInstalling) && card.DxvkIsInstalling) started = true;
            else if (e.PropertyName == nameof(GameCardViewModel.DxvkActionMessage) && !string.IsNullOrEmpty(card.DxvkActionMessage))
            {
                lastMessage = card.DxvkActionMessage;
                if (lastMessage.StartsWith("✅", StringComparison.Ordinal)) succeeded = true;
            }
        }
        card.PropertyChanged += OnChanged;
        try { await _window.ViewModel.InstallDxvkAsync(card, xamlRoot); }
        finally { card.PropertyChanged -= OnChanged; }

        if (!started) return TuneCancelled;
        if (succeeded) return null;
        return string.IsNullOrEmpty(lastMessage) ? "DXVK couldn't be installed. See the log for details." : lastMessage;
    }

    /// <summary>Removes an extra for Tune's undo: the same work as the row's ✕. Null = removed (or not there), else why not.</summary>
    internal async Task<string?> UninstallExtraForTuneAsync(GameCardViewModel card, string id)
    {
        var vm          = _window.ViewModel;
        var gn          = card.GameName;
        var gs          = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        CrashReporter.Log($"[DetailPanelBuilder.UninstallExtraForTuneAsync] '{gn}': {id}");
        if (string.IsNullOrEmpty(installPath) || !TuneExtraInstalled(card, id)) return null;

        string? result = null;
        try
        {
            switch (id)
            {
                case TuneExtraIds.AsiLoader:
                    UninstallUalCore(card, _auxInstallService.FindRecord(gn, installPath, UltimateAsiLoaderService.AddonType)?.InstalledAs);
                    break;
                case TuneExtraIds.Hsf:
                    await UninstallHsfCoreAsync(card);
                    break;
                case TuneExtraIds.RtxEncore:
                {
                    var current = vm.GetRtxEncoreInstalledAs(gn, gs);
                    if (!string.IsNullOrEmpty(current))
                        UninstallRtxEncoreCore(card, current, vm.GetRtxEncoreNrDllDeployed(gn, gs));
                    break;
                }
                case TuneExtraIds.DlssEnabler:
                {
                    var current = vm.GetDeInstalledAs(gn, gs);
                    if (!string.IsNullOrEmpty(current)) result = UninstallDeCore(card, current);
                    break;
                }
                case TuneExtraIds.UniversalMfg:
                    result = await UninstallUmfgCoreAsync(card, null, null);
                    break;
                case TuneExtraIds.MfgUnlock:
                    result = UninstallMfgAdaCore(card);
                    break;
                case TuneExtraIds.Dlssg2030:
                    UninstallDlssg2030Core(card, vm.GetDlssg2030InstalledAs(gn, gs));
                    break;
                case TuneExtraIds.BfiPresenter:
                {
                    var installedAs = vm.GetBfiPresenterInstalledAs(gn, gs);
                    if (!string.IsNullOrEmpty(installedAs)) result = UninstallBfiCore(card, installedAs);
                    break;
                }
                case TuneExtraIds.Dxvk:
                    if (!await vm.UninstallDxvkAsync(card))
                        result = string.IsNullOrEmpty(card.DxvkActionMessage) ? "DXVK couldn't be removed. Close the game and try again." : card.DxvkActionMessage;
                    break;
                case TuneExtraIds.DgVoodoo:
                    UninstallDgVoodooCore(card);
                    break;
                default:
                    result = $"Unknown extra '{id}'.";
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or HttpRequestException or TaskCanceledException or COMException)
        {
            CrashReporter.Log($"[DetailPanelBuilder.UninstallExtraForTuneAsync] '{gn}': {id} failed — {ex.Message}");
            result = $"{TuneExtraLabel(id)} couldn't be removed: {ex.Message}";
        }
        RequestExtrasRebuild(card);
        CrashReporter.Log($"[DetailPanelBuilder.UninstallExtraForTuneAsync] '{gn}': {id} → {result ?? "removed"}");
        return result;
    }

    /// <summary>The per-game setting Tune changes before an install: DXVK variant override, dgVoodoo2 version; else null.</summary>
    internal string? GetTuneExtraSetting(GameCardViewModel card, string id) => id switch
    {
        TuneExtraIds.Dxvk     => _window.ViewModel.GetDxvkVariantOverride(card.GameName, card.Source ?? ""),
        TuneExtraIds.DgVoodoo => _window.ViewModel.GetDgVoodooVersion(card.GameName, card.Source ?? ""),
        _                     => null,
    };

    /// <summary>Puts back the setting <see cref="GetTuneExtraSetting"/> read.</summary>
    internal void SetTuneExtraSetting(GameCardViewModel card, string id, string? value)
    {
        switch (id)
        {
            case TuneExtraIds.Dxvk:
                _window.ViewModel.SetDxvkVariantOverride(card.GameName, value, card.Source ?? "");
                break;
            case TuneExtraIds.DgVoodoo:
                _window.ViewModel.SetDgVoodooVersion(card.GameName, value, card.Source ?? "");
                break;
        }
    }
}
