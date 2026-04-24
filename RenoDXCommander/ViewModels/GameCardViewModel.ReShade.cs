using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

// ReShade status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── Testable seam for VulkanLayerService.IsLayerInstalled() ────────────────────
    /// <summary>
    /// Delegate used by computed properties to check Vulkan layer status.
    /// Defaults to <see cref="VulkanLayerService.IsLayerInstalled()"/>.
    /// Tests can replace this with a custom func to control the result.
    /// </summary>
    internal static Func<bool> IsLayerInstalledFunc = VulkanLayerService.IsLayerInstalled;

    // ── RS computed properties ─────────────────────────────────────────────────────

    /// <summary>Per-component status dot for ReShade.</summary>
    public string RsStatusDot => RsStatus == GameStatus.UpdateAvailable ? "🟠"
        : (RsStatus == GameStatus.Installed) ? "🟢" : "⚪";

    public string RsActionLabel
    {
        get
        {
            if (RsIsInstalling) return "Installing...";
            // RE Engine games require REFramework before ReShade can be installed
            // (unless user has excluded REF via Update Inclusion toggle)
            if (IsREEngineGame && !IsRefInstalled && !EffectiveLumaMode && !ExcludeFromUpdateAllRef)
                return "⚠  RE Framework required";
            if (RequiresVulkanInstall)
            {
                bool layerInstalled = IsLayerInstalledFunc();
                if (layerInstalled && IsVulkanRsActive) return "↺  Reinstall Vulkan ReShade";
                if (layerInstalled) return "⬇  Install Vulkan ReShade";
                return "⬇  Install Vulkan Layer";
            }
            return RsStatus == GameStatus.UpdateAvailable ? "⬆  Update ReShade"
                 : RsStatus == GameStatus.Installed       ? "↺  Reinstall ReShade"
                 : "⬇  Install ReShade";
        }
    }

    // Background colours for RS buttons (purple tint when update available, blue otherwise)
    public string RsBtnBackground  => RsStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string RsBtnForeground  => RsStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string RsBtnBorderBrush => RsStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility RsProgressVisibility => RsIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RsMessageVisibility  => string.IsNullOrEmpty(RsActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RsInstalledVisible   => !string.IsNullOrEmpty(RsInstalledFile) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RsDeleteVisibility   => RsStatus == GameStatus.Installed || RsStatus == GameStatus.UpdateAvailable
                                               ? Visibility.Visible : Visibility.Collapsed;

    // Component table: RS short status text + short action labels
    public string RsStatusText => RsIsInstalling ? "Installing…"
        : RsStatus == GameStatus.UpdateAvailable ? (RsInstalledVersion ?? "Update")
        : RsStatus == GameStatus.Installed       ? (RsInstalledVersion ?? "Installed")
        : "Ready";
    public string RsStatusColor => RsIsInstalling ? "#D4A856"
        : RsStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : RsStatus == GameStatus.Installed       ? "#5ECB7D"
        : "#A0AABB";
    /// <summary>True when this is a Vulkan game and reshade.ini already exists in the game folder.</summary>
    private bool IsVulkanRsActive => RequiresVulkanInstall
        && File.Exists(Path.Combine(InstallPath, "reshade.ini"));

    public string RsShortAction
    {
        get
        {
            if (RsIsInstalling) return "…";
            if (RequiresVulkanInstall)
            {
                bool layerInstalled = IsLayerInstalledFunc();
                if (layerInstalled && IsVulkanRsActive) return "↺ Reinstall";
                if (layerInstalled) return "⬇ Vulkan RS";
                return "⬇ Install";
            }
            return RsStatus == GameStatus.UpdateAvailable ? "⬆ Update"
                 : RsStatus == GameStatus.Installed       ? "↺ Reinstall"
                 : "⬇ Install";
        }
    }

    public bool IsRsNotInstalling => !RsIsInstalling;
    public bool IsRsInstalled   => RsStatus is GameStatus.Installed or GameStatus.UpdateAvailable;

    // ── Dynamic corner radius for RS install buttons ─────────────────────────────
    public string RsInstallCornerRadius => (RsStatus == GameStatus.Installed || RsStatus == GameStatus.UpdateAvailable)
        ? "10,0,0,10" : "10";
    public string RsInstallBorderThickness => (RsStatus == GameStatus.Installed || RsStatus == GameStatus.UpdateAvailable)
        ? "1,1,0,1" : "1";
    public string RsInstallMargin => (RsStatus == GameStatus.Installed || RsStatus == GameStatus.UpdateAvailable)
        ? "0,0,1,0" : "0";

    // ── INI preset existence for RS ───────────────────────────────────────────────
    /// <summary>True when reshade.ini is present in the inis folder — enables the 📋 button.</summary>
    public bool RsIniExists => File.Exists(AuxInstallService.RsIniPath);

    // INI button corner radius: rounded right when it is the rightmost button (delete hidden)
    private bool RsDeleteVisible => RsStatus == GameStatus.Installed || RsStatus == GameStatus.UpdateAvailable;
    public string RsIniCornerRadius    => RsDeleteVisible ? "0"        : "0,10,10,0";
    public string RsIniBorderThickness => RsDeleteVisible ? "0,1,0,1"  : "0,1,1,1";
    public string RsIniMargin          => RsDeleteVisible ? "0,0,1,0"  : "0";

    // In Luma mode: hide ReShade row
    public Visibility ReShadeRowVisibility => EffectiveLumaMode ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: RsStatus changed ───────────────────────────────────
    private void NotifyRsStatusDependents()
    {
        OnPropertyChanged(nameof(RsStatusDot));
        OnPropertyChanged(nameof(RsActionLabel));
        OnPropertyChanged(nameof(RsBtnBackground));
        OnPropertyChanged(nameof(RsBtnForeground));
        OnPropertyChanged(nameof(RsBtnBorderBrush));
        OnPropertyChanged(nameof(RsDeleteVisibility));
        OnPropertyChanged(nameof(RsInstalledVisible));
        OnPropertyChanged(nameof(RsStatusText));
        OnPropertyChanged(nameof(RsStatusColor));
        OnPropertyChanged(nameof(RsShortAction));
        OnPropertyChanged(nameof(RsInstallCornerRadius));
        OnPropertyChanged(nameof(RsInstallBorderThickness));
        OnPropertyChanged(nameof(RsInstallMargin));
        OnPropertyChanged(nameof(RsIniCornerRadius));
        OnPropertyChanged(nameof(RsIniBorderThickness));
        OnPropertyChanged(nameof(RsIniMargin));
        OnPropertyChanged(nameof(IsRsInstalled));
        OnPropertyChanged(nameof(CardRsStatusDot));
        OnPropertyChanged(nameof(CardRsInstallEnabled));
        // Combined card
        OnPropertyChanged(nameof(CombinedStatusDot));
        OnPropertyChanged(nameof(CombinedActionLabel));
        OnPropertyChanged(nameof(CanCombinedInstall));
        OnPropertyChanged(nameof(CombinedBtnBackground));
        OnPropertyChanged(nameof(CombinedBtnForeground));
        OnPropertyChanged(nameof(CombinedBtnBorderBrush));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
        // Managed state
        OnPropertyChanged(nameof(IsManaged));
        OnPropertyChanged(nameof(SidebarItemForeground));
        // RenoDX / ReLimiter / DC depend on IsRsInstalled
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CardRdxInstallEnabled));
        OnPropertyChanged(nameof(RdxStatusText));
        OnPropertyChanged(nameof(RdxStatusColor));
        OnPropertyChanged(nameof(UlInstallEnabled));
        OnPropertyChanged(nameof(CardUlInstallEnabled));
        OnPropertyChanged(nameof(UlStatusText));
        OnPropertyChanged(nameof(UlStatusColor));
        OnPropertyChanged(nameof(DcInstallEnabled));
        OnPropertyChanged(nameof(CardDcInstallEnabled));
        OnPropertyChanged(nameof(DcStatusText));
        OnPropertyChanged(nameof(DcStatusColor));
        // Action labels depend on IsRsInstalled
        OnPropertyChanged(nameof(InstallActionLabel));
        OnPropertyChanged(nameof(UlActionLabel));
        OnPropertyChanged(nameof(DcActionLabel));
    }

    // ── Targeted notification: RsIsInstalling changed ─────────────────────────────
    private void NotifyRsIsInstallingDependents()
    {
        // RsStatusDot removed — it depends on RsStatus, not RsIsInstalling
        OnPropertyChanged(nameof(RsActionLabel));
        OnPropertyChanged(nameof(RsProgressVisibility));
        OnPropertyChanged(nameof(IsRsNotInstalling));
        OnPropertyChanged(nameof(RsStatusText));
        OnPropertyChanged(nameof(RsStatusColor));
        OnPropertyChanged(nameof(RsShortAction));
        OnPropertyChanged(nameof(CardRsStatusDot));
        OnPropertyChanged(nameof(CardRsInstallEnabled));
        // Combined card
        OnPropertyChanged(nameof(CombinedActionLabel));
        OnPropertyChanged(nameof(CanCombinedInstall));
        // Card grid
        OnPropertyChanged(nameof(CanCardInstall));
    }

    partial void OnRsStatusChanged(GameStatus value) => NotifyRsStatusDependents();
    partial void OnRsIsInstallingChanged(bool value) => NotifyRsIsInstallingDependents();
    partial void OnRsActionMessageChanged(string value) => OnPropertyChanged(nameof(RsMessageVisibility));
}
