using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// Luma status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── Luma computed properties ───────────────────────────────────────────────────

    /// <summary>True when a matching Luma mod exists in the wiki.</summary>
    public bool IsLumaAvailable => LumaMod != null;

    // ── Luma badge + button visibility ─────────────────────────────────────────────
    public Visibility LumaBadgeVisibility => (LumaFeatureEnabled && IsLumaAvailable) ? Visibility.Visible : Visibility.Collapsed;
    public string LumaBadgeLabel => IsLumaMode ? "Luma ON" : "Luma";
    public string LumaBadgeBackground => IsLumaMode ? "#122818" : "#1A2030";
    public string LumaBadgeForeground => IsLumaMode ? "#5ECB7D" : "#6B7A8E";
    public string LumaBadgeBorderBrush => IsLumaMode ? "#1E4028" : "#283240";

    // In Luma mode: hide RenoDX install, hide ReShade row, show Luma install
    private bool EffectiveLumaMode => LumaFeatureEnabled && IsLumaMode;
    public Visibility LumaInstallVisibility => (EffectiveLumaMode && LumaMod?.DownloadUrl != null
        && LumaStatus == GameStatus.NotInstalled) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LumaReinstallVisibility => (EffectiveLumaMode
        && (LumaStatus == GameStatus.Installed || LumaStatus == GameStatus.UpdateAvailable))
        ? Visibility.Visible : Visibility.Collapsed;
    public bool IsLumaNotInstalling => !IsLumaInstalling;
    public Visibility LumaProgressVisibility => IsLumaInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LumaMessageVisibility => string.IsNullOrEmpty(LumaActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public string LumaActionLabel => IsLumaInstalling ? "Installing..."
        : LumaStatus == GameStatus.UpdateAvailable ? "⬆  Update Luma"
        : LumaStatus == GameStatus.Installed ? "↺  Reinstall Luma"
        : "⬇  Install Luma";

    // Component table: Luma short status/action (consistent with RS/DC/RDX)
    public string LumaStatusText => IsLumaInstalling ? "Installing…"
        : LumaStatus == GameStatus.UpdateAvailable ? "Update"
        : LumaStatus == GameStatus.Installed
            ? (LumaRecord?.InstalledBuildNumber > 0 ? $"Build {LumaRecord.InstalledBuildNumber}" : "Installed")
        : "Ready";
    public string LumaStatusColor => IsLumaInstalling ? "#D4A856"
        : LumaStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : LumaStatus == GameStatus.Installed       ? "#5ECB7D"
        : "#A0AABB";
    public string LumaShortAction => IsLumaInstalling ? "…"
        : LumaStatus == GameStatus.UpdateAvailable ? "⬆ Update"
        : LumaStatus == GameStatus.Installed       ? "↺ Reinstall"
        : "⬇ Install";

    public string LumaBtnBackground  => LumaStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string LumaBtnForeground  => LumaStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string LumaBtnBorderBrush => LumaStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public bool IsLumaInstalled => LumaStatus is GameStatus.Installed or GameStatus.UpdateAvailable;

    // In Luma mode: hide RenoDX row (also hidden when IsExternalOnly)
    public Visibility RenoDxRowVisibility => (EffectiveLumaMode || IsExternalOnly) ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: LumaStatus changed ─────────────────────────────────
    private void NotifyLumaStatusDependents()
    {
        OnPropertyChanged(nameof(LumaActionLabel));
        OnPropertyChanged(nameof(LumaInstallVisibility));
        OnPropertyChanged(nameof(LumaReinstallVisibility));
        OnPropertyChanged(nameof(LumaStatusText));
        OnPropertyChanged(nameof(LumaStatusColor));
        OnPropertyChanged(nameof(LumaShortAction));
        OnPropertyChanged(nameof(LumaBtnBackground));
        OnPropertyChanged(nameof(LumaBtnForeground));
        OnPropertyChanged(nameof(LumaBtnBorderBrush));
        OnPropertyChanged(nameof(IsLumaInstalled));
        OnPropertyChanged(nameof(CardLumaStatusDot));
        OnPropertyChanged(nameof(CardLumaInstallEnabled));
        // Managed state
        OnPropertyChanged(nameof(IsManaged));
        OnPropertyChanged(nameof(SidebarItemForeground));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
    }

    // ── Targeted notification: IsLumaInstalling changed ───────────────────────────
    private void NotifyIsLumaInstallingDependents()
    {
        OnPropertyChanged(nameof(LumaActionLabel));
        OnPropertyChanged(nameof(IsLumaNotInstalling));
        OnPropertyChanged(nameof(LumaProgressVisibility));
        OnPropertyChanged(nameof(LumaStatusText));
        OnPropertyChanged(nameof(LumaStatusColor));
        OnPropertyChanged(nameof(LumaShortAction));
        OnPropertyChanged(nameof(CardLumaStatusDot));
        OnPropertyChanged(nameof(CardLumaInstallEnabled));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
        OnPropertyChanged(nameof(CanCardInstall));
    }

    // ── Targeted notification: LumaFeatureEnabled changed ─────────────────────────
    private void NotifyLumaFeatureEnabledDependents()
    {
        // All Luma-related visibility properties
        OnPropertyChanged(nameof(LumaBadgeVisibility));
        OnPropertyChanged(nameof(LumaInstallVisibility));
        OnPropertyChanged(nameof(LumaReinstallVisibility));
        OnPropertyChanged(nameof(CardLumaVisible));
        OnPropertyChanged(nameof(R7bLumaSwitchVisibility));
        OnPropertyChanged(nameof(R7bLumaSwitchCornerRadius));
        OnPropertyChanged(nameof(R7bLumaSwitchBorderThickness));
        OnPropertyChanged(nameof(R7bLumaSwitchMargin));
        OnPropertyChanged(nameof(R7bInstallCornerRadius));
        OnPropertyChanged(nameof(R7bInstallBorderThickness));
        OnPropertyChanged(nameof(R7bInstallMargin));
        // Row visibility
        OnPropertyChanged(nameof(RenoDxRowVisibility));
        OnPropertyChanged(nameof(ReShadeRowVisibility));
        // Combined card
        OnPropertyChanged(nameof(CombinedRowVisibility));
        OnPropertyChanged(nameof(ComponentExpandVisibility));
        // Visibility
        OnPropertyChanged(nameof(ExternalBtnVisibility));
        OnPropertyChanged(nameof(InstalledFileLabelVisible));
        OnPropertyChanged(nameof(NoModVisibility));
        OnPropertyChanged(nameof(SwitchToLumaVisibility));
        // Wiki status (hidden in Luma mode)
        OnPropertyChanged(nameof(WikiStatusIcon));
        OnPropertyChanged(nameof(WikiStatusIconVisible));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
        OnPropertyChanged(nameof(HasExtraLinks));
        OnPropertyChanged(nameof(ExtraLinkVisibility));
    }

    // ── Targeted notification: IsLumaMode changed ─────────────────────────────────
    private void NotifyIsLumaModeDependents()
    {
        // Luma badge styling
        OnPropertyChanged(nameof(LumaBadgeLabel));
        OnPropertyChanged(nameof(LumaBadgeBackground));
        OnPropertyChanged(nameof(LumaBadgeForeground));
        OnPropertyChanged(nameof(LumaBadgeBorderBrush));
        // Luma visibility
        OnPropertyChanged(nameof(LumaInstallVisibility));
        OnPropertyChanged(nameof(LumaReinstallVisibility));
        OnPropertyChanged(nameof(CardLumaVisible));
        // Row visibility
        OnPropertyChanged(nameof(RenoDxRowVisibility));
        OnPropertyChanged(nameof(ReShadeRowVisibility));
        // Combined card
        OnPropertyChanged(nameof(CombinedRowVisibility));
        OnPropertyChanged(nameof(ComponentExpandVisibility));
        // Visibility
        OnPropertyChanged(nameof(ExternalBtnVisibility));
        OnPropertyChanged(nameof(InstalledFileLabelVisible));
        OnPropertyChanged(nameof(NoModVisibility));
        OnPropertyChanged(nameof(SwitchToLumaVisibility));
        // Wiki status (hidden in Luma mode)
        OnPropertyChanged(nameof(WikiStatusIcon));
        OnPropertyChanged(nameof(WikiStatusIconVisible));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
        OnPropertyChanged(nameof(HasExtraLinks));
        OnPropertyChanged(nameof(ExtraLinkVisibility));
        // Author badge (switches between RenoDX and Luma author)
        OnPropertyChanged(nameof(AuthorList));
        OnPropertyChanged(nameof(HasAuthors));
    }

    partial void OnLumaStatusChanged(GameStatus value) => NotifyLumaStatusDependents();
    partial void OnIsLumaInstallingChanged(bool value) => NotifyIsLumaInstallingDependents();
    partial void OnLumaFeatureEnabledChanged(bool value) => NotifyLumaFeatureEnabledDependents();
    partial void OnIsLumaModeChanged(bool value) => NotifyIsLumaModeDependents();
    partial void OnLumaActionMessageChanged(string value) => OnPropertyChanged(nameof(LumaMessageVisibility));
}
