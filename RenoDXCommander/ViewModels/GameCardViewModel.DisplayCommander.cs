using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

// Display Commander status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── DC computed properties ─────────────────────────────────────────────────────

    /// <summary>Per-component status dot for Display Commander.</summary>
    public string DcStatusDot => DcStatus == GameStatus.UpdateAvailable ? "🟠"
        : (DcStatus == GameStatus.Installed) ? "🟢" : "⚪";

    public string DcActionLabel
    {
        get
        {
            if (DcIsInstalling) return "Installing...";
            return DcStatus == GameStatus.UpdateAvailable ? "⬆  Update Display Commander"
                 : DcStatus == GameStatus.Installed       ? "↺  Reinstall Display Commander"
                 : "⬇  Install Display Commander";
        }
    }

    // Background colours for DC buttons (purple tint when update available, blue otherwise)
    public string DcBtnBackground  => DcStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string DcBtnForeground  => DcStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string DcBtnBorderBrush => DcStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility DcProgressVisibility => DcIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DcMessageVisibility  => string.IsNullOrEmpty(DcActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DcInstalledVisible   => !string.IsNullOrEmpty(DcInstalledFile) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DcDeleteVisibility   => DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable
                                               ? Visibility.Visible : Visibility.Collapsed;

    // Component table: DC short status text + short action labels
    public string DcStatusText => DcIsInstalling ? "Installing…"
        : DcStatus == GameStatus.UpdateAvailable ? (DcInstalledVersion ?? "Update")
        : DcStatus == GameStatus.Installed       ? (DcInstalledVersion ?? "Installed")
        : "Ready";
    public string DcStatusColor => DcIsInstalling ? "#D4A856"
        : DcStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : DcStatus == GameStatus.Installed       ? "#5ECB7D"
        : "#A0AABB";
    public string DcShortAction => DcIsInstalling ? "…"
        : DcStatus == GameStatus.UpdateAvailable ? "⬆ Update"
        : DcStatus == GameStatus.Installed       ? "↺ Reinstall"
        : "⬇ Install";

    public bool IsDcNotInstalling => !DcIsInstalling;
    public bool IsDcInstalled   => DcStatus is GameStatus.Installed or GameStatus.UpdateAvailable;

    // ── Dynamic corner radius for DC install buttons ─────────────────────────────
    public string DcInstallCornerRadius => (DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable)
        ? "10,0,0,10" : "10";
    public string DcInstallBorderThickness => (DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable)
        ? "1,1,0,1" : "1";
    public string DcInstallMargin => (DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable)
        ? "0,0,1,0" : "0";

    // ── INI preset existence for DC ───────────────────────────────────────────────
    /// <summary>True when DisplayCommander.toml is present in the inis folder — enables the 📋 button.</summary>
    public bool DcIniExists => File.Exists(AuxInstallService.DcIniPath);

    // INI button corner radius: rounded right when it is the rightmost button (delete hidden)
    private bool DcDeleteVisible => DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable;
    public string DcIniCornerRadius    => DcDeleteVisible ? "0"        : "0,10,10,0";
    public string DcIniBorderThickness => DcDeleteVisible ? "0,1,0,1"  : "0,1,1,1";
    public string DcIniMargin          => DcDeleteVisible ? "0,0,1,0"  : "0";

    // In Luma mode: hide DC row
    public Visibility DcRowVisibility => EffectiveLumaMode ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: DcStatus changed ───────────────────────────────────
    private void NotifyDcStatusDependents()
    {
        OnPropertyChanged(nameof(DcStatusDot));
        OnPropertyChanged(nameof(DcActionLabel));
        OnPropertyChanged(nameof(DcBtnBackground));
        OnPropertyChanged(nameof(DcBtnForeground));
        OnPropertyChanged(nameof(DcBtnBorderBrush));
        OnPropertyChanged(nameof(DcDeleteVisibility));
        OnPropertyChanged(nameof(DcInstalledVisible));
        OnPropertyChanged(nameof(DcStatusText));
        OnPropertyChanged(nameof(DcStatusColor));
        OnPropertyChanged(nameof(DcShortAction));
        OnPropertyChanged(nameof(DcInstallCornerRadius));
        OnPropertyChanged(nameof(DcInstallBorderThickness));
        OnPropertyChanged(nameof(DcInstallMargin));
        OnPropertyChanged(nameof(DcIniCornerRadius));
        OnPropertyChanged(nameof(DcIniBorderThickness));
        OnPropertyChanged(nameof(DcIniMargin));
        OnPropertyChanged(nameof(IsDcInstalled));
        OnPropertyChanged(nameof(CardDcStatusDot));
        OnPropertyChanged(nameof(CardDcInstallEnabled));
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
    }

    // ── Targeted notification: DcIsInstalling changed ─────────────────────────────
    private void NotifyDcIsInstallingDependents()
    {
        // DcStatusDot removed — it depends on DcStatus, not DcIsInstalling
        OnPropertyChanged(nameof(DcActionLabel));
        OnPropertyChanged(nameof(DcProgressVisibility));
        OnPropertyChanged(nameof(IsDcNotInstalling));
        OnPropertyChanged(nameof(DcStatusText));
        OnPropertyChanged(nameof(DcStatusColor));
        OnPropertyChanged(nameof(DcShortAction));
        OnPropertyChanged(nameof(CardDcStatusDot));
        OnPropertyChanged(nameof(CardDcInstallEnabled));
        // Combined card
        OnPropertyChanged(nameof(CombinedActionLabel));
        OnPropertyChanged(nameof(CanCombinedInstall));
        // Card grid
        OnPropertyChanged(nameof(CanCardInstall));
    }

    partial void OnDcStatusChanged(GameStatus value) => NotifyDcStatusDependents();
    partial void OnDcIsInstallingChanged(bool value) => NotifyDcIsInstallingDependents();
    partial void OnDcActionMessageChanged(string value) => OnPropertyChanged(nameof(DcMessageVisibility));
}
