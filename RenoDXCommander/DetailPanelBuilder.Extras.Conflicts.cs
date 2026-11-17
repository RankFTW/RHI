// DetailPanelBuilder.Extras.Conflicts.cs — Extras that can't be installed in the same game. One table, checked
// both ways: a row's install button is disabled (labelled "Remove X first") while a conflicting extra is
// installed. Only fresh installs are blocked; an extra that's already installed can still be reinstalled
// or updated, and every row's remove button stays available.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace RenoDXCommander;

internal enum ExtraKind { OptiScaler, DlssEnabler, UniversalMfg, MfgUnlockAddon, Dlssg2030, RtxEncore, Hsf }

public partial class DetailPanelBuilder
{
    /// <summary>Pairs of extras that can't be installed in the same game.</summary>
    internal static readonly (ExtraKind A, ExtraKind B)[] ExclusiveExtras =
    {
        (ExtraKind.OptiScaler,     ExtraKind.DlssEnabler),
        (ExtraKind.DlssEnabler,    ExtraKind.UniversalMfg),
        (ExtraKind.DlssEnabler,    ExtraKind.MfgUnlockAddon),
        (ExtraKind.DlssEnabler,    ExtraKind.Dlssg2030),
        (ExtraKind.DlssEnabler,    ExtraKind.Hsf),
        (ExtraKind.Hsf,            ExtraKind.Dlssg2030),
        (ExtraKind.UniversalMfg,   ExtraKind.MfgUnlockAddon),
        (ExtraKind.UniversalMfg,   ExtraKind.Dlssg2030),
        (ExtraKind.MfgUnlockAddon, ExtraKind.Dlssg2030),
        (ExtraKind.RtxEncore,      ExtraKind.OptiScaler),
        (ExtraKind.RtxEncore,      ExtraKind.DlssEnabler),
        (ExtraKind.RtxEncore,      ExtraKind.UniversalMfg),
        (ExtraKind.RtxEncore,      ExtraKind.MfgUnlockAddon),
        (ExtraKind.RtxEncore,      ExtraKind.Dlssg2030),
    };

    internal static string ExtraDisplayName(ExtraKind kind) => kind switch
    {
        ExtraKind.OptiScaler     => "OptiScaler",
        ExtraKind.DlssEnabler    => "DLSS Enabler",
        ExtraKind.UniversalMfg   => "Universal MFG Unlocker",
        ExtraKind.MfgUnlockAddon => "MFG Unlock",
        ExtraKind.Dlssg2030      => "20/30 FG Unlock",
        ExtraKind.RtxEncore      => "RTX Encore",
        ExtraKind.Hsf            => "HSF",
        _                        => kind.ToString(),
    };

    /// <summary>Short names for the button label (it's narrow in Tabs / Spotlight).</summary>
    private static string ExtraShortName(ExtraKind kind) => kind switch
    {
        ExtraKind.UniversalMfg => "Universal MFG",
        ExtraKind.Dlssg2030    => "20/30 FG",
        _                      => ExtraDisplayName(kind),
    };

    /// <summary>The extras installed in this game, using the same checks as the rows.</summary>
    internal HashSet<ExtraKind> GetInstalledExtras(GameCardViewModel card)
    {
        var vm          = _window.ViewModel;
        var gn          = card.GameName;
        var gs          = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        var set = new HashSet<ExtraKind>();
        if (card.IsOsInstalled)      set.Add(ExtraKind.OptiScaler);
        if (card.Rtx40MfgInstalled)  set.Add(ExtraKind.UniversalMfg);
        if (card.MfgAdaInstalled)    set.Add(ExtraKind.MfgUnlockAddon);
        if (string.IsNullOrEmpty(installPath)) return set;
        if (HsfService.IsInstalledIn(installPath, card.Is32Bit)) set.Add(ExtraKind.Hsf);
        if (App.Services.GetRequiredService<DlssEnablerService>().IsStandaloneInstalledIn(installPath, vm.GetDeInstalledAs(gn, gs)))
            set.Add(ExtraKind.DlssEnabler);
        if (App.Services.GetRequiredService<Dlssg20_30Service>().IsInstalledIn(installPath, vm.GetDlssg2030InstalledAs(gn, gs)))
            set.Add(ExtraKind.Dlssg2030);
        if (App.Services.GetRequiredService<RtxEncoreService>().IsInstalledIn(installPath, vm.GetRtxEncoreInstalledAs(gn, gs)))
            set.Add(ExtraKind.RtxEncore);
        return set;
    }

    /// <summary>
    /// The installed extra that blocks installing <paramref name="self"/>, or null. Null when
    /// <paramref name="self"/> is already installed: reinstalling or updating it is still allowed.
    /// </summary>
    internal ExtraKind? ExtraBlocker(GameCardViewModel card, ExtraKind self)
    {
        var installed = GetInstalledExtras(card);
        if (installed.Contains(self)) return null;
        foreach (var (a, b) in ExclusiveExtras)
        {
            if (a == self && installed.Contains(b)) return b;
            if (b == self && installed.Contains(a)) return a;
        }
        return null;
    }

    /// <summary>Disables an Extras install button because <paramref name="blocker"/> is installed.</summary>
    private static void ApplyExtraBlock(Button installBtn, TextBlock? label, ExtraKind self, ExtraKind blocker, string? reason = null)
    {
        reason ??= $"{ExtraDisplayName(blocker)} is installed in this game and can't be used together with {ExtraDisplayName(self)}. Remove {ExtraDisplayName(blocker)} first.";
        installBtn.IsEnabled        = false;
        installBtn.IsHitTestVisible = false;
        installBtn.Opacity          = 0.35;
        installBtn.Content          = $"Remove {ExtraShortName(blocker)} first";
        ToolTipService.SetToolTip(installBtn, reason);
        // Disabled buttons show no tooltip: put the reason on the row's label too
        if (label != null)
        {
            var existing = ToolTipService.GetToolTip(label) as string;
            ToolTipService.SetToolTip(label, string.IsNullOrEmpty(existing) ? reason : $"{existing}\n\n{reason}");
        }
        CrashReporter.Log($"[DetailPanelBuilder.ApplyExtraBlock] {self} blocked by {blocker}");
    }
}
