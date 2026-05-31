using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// RenoDX status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── RenoDX computed properties ────────────────────────────────────────────────

    public string InstallActionLabel
    {
        get
        {
            if (IsInstalling) return L("Installing...");
            if (!IsRsInstalled && Mod?.SnapshotUrl != null && !IsExternalOnly)
                return L("⚠  ReShade required");
            // No mod available and nothing manually installed
            if (Mod?.SnapshotUrl == null && !IsExternalOnly && string.IsNullOrEmpty(InstalledAddonFileName))
                return L("No RenoDX mod available");
            return Status == GameStatus.UpdateAvailable ? L("⬆  Update RenoDX")
                 : Status == GameStatus.Installed       ? L("↺  Reinstall RenoDX")
                 : L("⬇  Install RenoDX");
        }
    }

    public bool CanInstall => Mod?.SnapshotUrl != null && !IsInstalling && !IsExternalOnly && IsRsInstalled;

    public string GenericModLabel => IsGenericMod
        ? (EngineHint.Contains("Unity")
           ? L("Generic Unity")
           : (IsNativeHdrGame ? L("UE Extended Native HDR")
              : (IsManifestUeExtended || UseUeExtended) ? L("UE Extended")
              : L("Generic UE")))
        : "";

    // Update button colours — purple when an update is available, normal blue otherwise
    public string InstallBtnBackground  => Status == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string InstallBtnForeground  => Status == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string InstallBtnBorderBrush => Status == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    // UE-Extended toggle label and styling
    public string UeExtendedLabel      => UseUeExtended ? L("⚡ UE Extended ON") : L("⚡ UE Extended");
    public string UeExtendedBackground => UseUeExtended ? "#201838" : "#1E242C";
    public string UeExtendedForeground => UseUeExtended ? "#B898E8" : "#6B7A8E";
    public string UeExtendedBorderBrush => UseUeExtended ? "#3A2860" : "#283240";
    // Visible on all UE cards that don't have a named mod (generic UE) — not legacy, not native HDR
    public Visibility UeExtendedToggleVisibility =>
        (EngineHint.Contains("Unreal") && !EngineHint.Contains("Legacy")
         && !IsNativeHdrGame && (IsGenericMod || Mod == null || Mod.IsGenericUnreal))
            ? Visibility.Visible : Visibility.Collapsed;

    // ── Combined status for simplified card ──────────────────────────────────────
    /// <summary>True when all components that are relevant are installed (or N/A).</summary>
    private bool AllInstalled => (Mod == null || Status == GameStatus.Installed || Status == GameStatus.UpdateAvailable)
                              && (RsStatus == GameStatus.Installed || RsStatus == GameStatus.UpdateAvailable || RsStatus == GameStatus.NotInstalled);

    private bool AnyInstalling => IsInstalling || RsIsInstalling;

    /// <summary>Status dot: 🟢 all installed, 🟠 update available, ⚪ not installed.</summary>
    public string CombinedStatusDot => AnyUpdateAvailable ? "🟠"
        : (Status == GameStatus.Installed && !AnyUpdateAvailable) ? "🟢" : "⚪";

    /// <summary>Label for the primary combined action button.</summary>
    public string CombinedActionLabel
    {
        get
        {
            if (AnyInstalling) return L("Installing…");
            if (IsExternalOnly)
            {
                // External-only: no RenoDX to install, only ReShade
                if (RsStatus == GameStatus.UpdateAvailable) return L("⬆  Update ReShade");
                if (RsStatus == GameStatus.Installed) return L("↺  Reinstall ReShade");
                return L("⬇  Install ReShade");
            }
            if (AnyUpdateAvailable) return L("⬆  Update All");
            if (Status == GameStatus.Installed) return L("↺  Reinstall All");
            return L("⬇  Install All");
        }
    }

    /// <summary>Can the combined button be clicked?</summary>
    public bool CanCombinedInstall => !AnyInstalling;

    /// <summary>Background for combined button — purple when update, blue otherwise.</summary>
    public string CombinedBtnBackground  => AnyUpdateAvailable ? "#201838" : "#182840";
    public string CombinedBtnForeground  => AnyUpdateAvailable ? "#B898E8" : "#7AACDD";
    public string CombinedBtnBorderBrush => AnyUpdateAvailable ? "#3A2860" : "#2A4468";

    /// <summary>Visibility for the combined action row (non-Luma, non-external, has mod).</summary>
    public Visibility CombinedRowVisibility => ((!IsExternalOnly && Mod?.SnapshotUrl != null || IsExternalOnly)
        && !EffectiveLumaMode && Is32BitUeWipVisibility == Visibility.Collapsed)
        ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visibility for the expand/collapse chevron on the combined row.</summary>
    public Visibility ComponentExpandVisibility => CombinedRowVisibility;

    /// <summary>Chevron corner radius — rounded right when delete button is hidden.</summary>
    public string ChevronCornerRadius => ReinstallRowVisibility == Visibility.Visible ? "0" : "0,10,10,0";
    /// <summary>Chevron border — full right border when it's the last button.</summary>
    public string ChevronBorderThickness => ReinstallRowVisibility == Visibility.Visible ? "1,1,0,1" : "1";

    // ── Component table: RDX short status text + short action labels ─────────────
    public string RdxStatusText => IsInstalling ? L("Installing…")
        : Status == GameStatus.UpdateAvailable ? (RdxInstalledVersion ?? L("Update"))
        : Status == GameStatus.Installed       ? (RdxInstalledVersion ?? L("Installed"))
        : Mod?.SnapshotUrl != null             ? L("Ready") : "—";
    public string RdxStatusColor => IsInstalling ? "#D4A856"
        : Status == GameStatus.UpdateAvailable ? "#B898E8"
        : Status == GameStatus.Installed       ? "#5ECB7D"
        : Mod?.SnapshotUrl != null             ? "#A0AABB" : "#404858";
    public string RdxShortAction => IsInstalling ? "…"
        : Status == GameStatus.UpdateAvailable ? L("⬆ Update")
        : Status == GameStatus.Installed       ? L("↺ Reinstall")
        : L("⬇ Install");

    // Negated installing flag — used for IsEnabled bindings to avoid converter in DataTemplate
    public bool IsNotInstalling => !IsInstalling;

    // ── Dynamic corner radius for Row 7b install buttons ─────────────────────────
    private bool HasR7bRightButtons => R7bLumaSwitchVisibility == Visibility.Visible || UeExtendedToggleVisibility == Visibility.Visible;
    public string R7bInstallCornerRadius     => HasR7bRightButtons ? "10,0,0,10" : "10";
    public string R7bInstallBorderThickness  => HasR7bRightButtons ? "1,1,0,1"   : "1";
    public string R7bInstallMargin           => HasR7bRightButtons ? "0,0,1,0"   : "0";
    // Row 7b: Luma switch button — visible when game supports both RenoDX and Luma
    public Visibility R7bLumaSwitchVisibility     => (LumaFeatureEnabled && IsLumaAvailable) ? Visibility.Visible : Visibility.Collapsed;
    public string R7bLumaSwitchCornerRadius       => UeExtendedToggleVisibility == Visibility.Visible ? "0" : "0,10,10,0";
    public string R7bLumaSwitchBorderThickness    => UeExtendedToggleVisibility == Visibility.Visible ? "0,1,0,1" : "0,1,1,1";
    public string R7bLumaSwitchMargin             => UeExtendedToggleVisibility == Visibility.Visible ? "0,0,1,0" : "0";

    /// <summary>True when any component (RenoDX, ReShade, Luma, DC) is installed or has an update.</summary>
    public bool IsManaged =>
        Status is GameStatus.Installed or GameStatus.UpdateAvailable ||
        RsStatus is GameStatus.Installed or GameStatus.UpdateAvailable ||
        LumaStatus is GameStatus.Installed or GameStatus.UpdateAvailable ||
        DcStatus is GameStatus.Installed or GameStatus.UpdateAvailable;

    // ── Per-component installed state (card install flyout uninstall visibility) ──
    public bool IsRdxInstalled  => Status is GameStatus.Installed or GameStatus.UpdateAvailable;

    // ── External link label: "Update" when Nexus update available, "Redownload" when installed ──
    public string ExternalDisplayLabel =>
        Status == GameStatus.UpdateAvailable && IsRdxInstalled
            ? L("⬆  Update RenoDX")
            : IsRdxInstalled && !string.IsNullOrEmpty(ExternalLabel)
                ? "↺  " + L(ExternalLabel.Replace("Download", "Redownload"))
                : "⬇  " + L(ExternalLabel);

    // Compact list item highlight — purple when any component has an update available
    private bool AnyUpdateAvailable => Status == GameStatus.UpdateAvailable
                                    || RsStatus == GameStatus.UpdateAvailable;

    // ── Targeted notification: Status changed ─────────────────────────────────────
    private void NotifyStatusDependents()
    {
        OnPropertyChanged(nameof(InstallActionLabel));
        OnPropertyChanged(nameof(CanInstall));
        // GenericModLabel removed — it depends on IsGenericMod/EngineHint/UseUeExtended, not Status
        OnPropertyChanged(nameof(InstallBtnBackground));
        OnPropertyChanged(nameof(InstallBtnForeground));
        OnPropertyChanged(nameof(InstallBtnBorderBrush));
        OnPropertyChanged(nameof(InstallOnlyBtnVisibility));
        OnPropertyChanged(nameof(ReinstallRowVisibility));
        OnPropertyChanged(nameof(IsNotInstalling));
        OnPropertyChanged(nameof(IsRdxInstalled));
        OnPropertyChanged(nameof(ExternalDisplayLabel));
        OnPropertyChanged(nameof(RdxStatusText));
        OnPropertyChanged(nameof(RdxStatusColor));
        OnPropertyChanged(nameof(RdxShortAction));
        OnPropertyChanged(nameof(CardRdxStatusDot));
        OnPropertyChanged(nameof(CardRdxInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
        // Combined card
        OnPropertyChanged(nameof(CombinedStatusDot));
        OnPropertyChanged(nameof(CombinedActionLabel));
        OnPropertyChanged(nameof(CanCombinedInstall));
        OnPropertyChanged(nameof(CombinedBtnBackground));
        OnPropertyChanged(nameof(CombinedBtnForeground));
        OnPropertyChanged(nameof(CombinedBtnBorderBrush));
        OnPropertyChanged(nameof(CombinedRowVisibility));
        OnPropertyChanged(nameof(ComponentExpandVisibility));
        OnPropertyChanged(nameof(ChevronCornerRadius));
        OnPropertyChanged(nameof(ChevronBorderThickness));
        OnPropertyChanged(nameof(R7bInstallCornerRadius));
        OnPropertyChanged(nameof(R7bInstallBorderThickness));
        OnPropertyChanged(nameof(R7bInstallMargin));
        // Managed state
        OnPropertyChanged(nameof(IsManaged));
        OnPropertyChanged(nameof(SidebarItemForeground));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
        OnPropertyChanged(nameof(CanCardInstall));
        // Visibility
        OnPropertyChanged(nameof(ExternalBtnVisibility));
        OnPropertyChanged(nameof(NoModVisibility));
        OnPropertyChanged(nameof(SwitchToLumaVisibility));
    }

    // ── Targeted notification: IsInstalling changed ───────────────────────────────
    private void NotifyIsInstallingDependents()
    {
        OnPropertyChanged(nameof(InstallActionLabel));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsNotInstalling));
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(CardRdxStatusDot));
        OnPropertyChanged(nameof(CardRdxInstallEnabled));
        OnPropertyChanged(nameof(RdxStatusText));
        OnPropertyChanged(nameof(RdxStatusColor));
        OnPropertyChanged(nameof(RdxShortAction));
        // Combined card
        OnPropertyChanged(nameof(CombinedActionLabel));
        OnPropertyChanged(nameof(CanCombinedInstall));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
        OnPropertyChanged(nameof(CanCardInstall));
    }

    partial void OnStatusChanged(GameStatus value) => NotifyStatusDependents();
    partial void OnIsInstallingChanged(bool value) => NotifyIsInstallingDependents();
    partial void OnInstalledAddonFileNameChanged(string? value)
    {
        OnPropertyChanged(nameof(InstalledFileLabel));
        OnPropertyChanged(nameof(InstalledFileLabelVisible));
        OnPropertyChanged(nameof(NoModVisibility));
        OnPropertyChanged(nameof(SwitchToLumaVisibility));
    }
    partial void OnRdxInstalledVersionChanged(string? value)
    {
        OnPropertyChanged(nameof(RdxStatusText));
    }
    partial void OnActionMessageChanged(string value) => OnPropertyChanged(nameof(MessageVisibility));
    partial void OnUseUeExtendedChanged(bool value) => OnPropertyChanged(nameof(GenericModLabel));
}
