using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// Display Commander status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── DC computed properties ─────────────────────────────────────────────────────

    /// <summary>Per-component status dot for Display Commander.</summary>
    public string DcStatusDot => DcStatus == GameStatus.UpdateAvailable ? "🟢"
        : DcStatus == GameStatus.Installed ? "🟢" : "⚪";

    public string DcActionLabel => DcIsInstalling ? L("Installing...")
        : !IsRsInstalled ? L("⚠  ReShade required")
        : DcStatus == GameStatus.UpdateAvailable ? L("⬆  Update DC")
        : DcStatus == GameStatus.Installed ? L("↺  Reinstall DC")
        : L("⬇  Install DC");

    public string DcBtnBackground  => DcStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string DcBtnForeground  => DcStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string DcBtnBorderBrush => DcStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility DcProgressVisibility => DcIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DcMessageVisibility  => string.IsNullOrEmpty(DcActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DcDeleteVisibility   => DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable
        ? Visibility.Visible : Visibility.Collapsed;

    public string DcStatusText => DcIsInstalling ? L("Installing…")
        : DcStatus == GameStatus.UpdateAvailable ? L("Update")
        : DcStatus == GameStatus.Installed ? (DcInstalledVersion ?? L("Installed"))
        : L("Ready");
    public string DcStatusColor => DcIsInstalling ? "#D4A856"
        : DcStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : DcStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";
    public string DcShortAction => DcIsInstalling ? "…"
        : DcStatus == GameStatus.UpdateAvailable ? L("⬆ Update")
        : DcStatus == GameStatus.Installed ? L("↺ Reinstall")
        : L("⬇ Install");

    public bool IsDcNotInstalling => !DcIsInstalling;
    public bool IsDcInstalled => DcStatus == GameStatus.Installed || DcStatus == GameStatus.UpdateAvailable;

    /// <summary>True when DisplayCommander.ini is present in the inis folder — enables the 📋 button.</summary>
    public bool DcIniExists => File.Exists(Services.AuxInstallService.DcIniPath);

    /// <summary>DC install button disabled when installing, when ReLimiter is installed (mutual exclusion), when normal ReShade is active, or when ReShade is not installed.</summary>
    public bool DcInstallEnabled => !DcIsInstalling && !IsUlInstalled && !UseNormalReShade && IsRsInstalled;

    // ── Card grid properties ──────────────────────────────────────────────────────
    public string CardDcStatusDot => DcIsInstalling ? "#2196F3"
        : DcStatus == GameStatus.UpdateAvailable ? "#4CAF50"
        : DcStatus == GameStatus.Installed ? "#4CAF50" : "#5A6880";
    public bool CardDcInstallEnabled => !DcIsInstalling && IsRsInstalled;

    /// <summary>
    /// DC row is always visible (available in both standard and Luma modes).
    /// </summary>
    public Visibility DcRowVisibility => Visibility.Visible;

    // ── Targeted notification: DcStatus changed ───────────────────────────────────
    private void NotifyDcStatusDependents()
    {
        OnPropertyChanged(nameof(DcStatusDot));
        OnPropertyChanged(nameof(DcActionLabel));
        OnPropertyChanged(nameof(DcBtnBackground));
        OnPropertyChanged(nameof(DcBtnForeground));
        OnPropertyChanged(nameof(DcBtnBorderBrush));
        OnPropertyChanged(nameof(DcDeleteVisibility));
        OnPropertyChanged(nameof(DcStatusText));
        OnPropertyChanged(nameof(DcStatusColor));
        OnPropertyChanged(nameof(DcShortAction));
        OnPropertyChanged(nameof(IsDcInstalled));
        OnPropertyChanged(nameof(DcInstallEnabled));
        OnPropertyChanged(nameof(CardDcStatusDot));
        OnPropertyChanged(nameof(CardDcInstallEnabled));
        OnPropertyChanged(nameof(UlInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
        // Managed state (DC is now part of IsManaged)
        OnPropertyChanged(nameof(IsManaged));
        OnPropertyChanged(nameof(SidebarItemForeground));
        // Card grid
        OnPropertyChanged(nameof(CardPrimaryActionLabel));
    }

    // ── Targeted notification: DcIsInstalling changed ─────────────────────────────
    private void NotifyDcIsInstallingDependents()
    {
        OnPropertyChanged(nameof(DcActionLabel));
        OnPropertyChanged(nameof(DcProgressVisibility));
        OnPropertyChanged(nameof(IsDcNotInstalling));
        OnPropertyChanged(nameof(DcInstallEnabled));
        OnPropertyChanged(nameof(DcStatusText));
        OnPropertyChanged(nameof(DcStatusColor));
        OnPropertyChanged(nameof(DcShortAction));
        OnPropertyChanged(nameof(CardDcStatusDot));
        OnPropertyChanged(nameof(CardDcInstallEnabled));
        OnPropertyChanged(nameof(CanCardInstall));
    }

    partial void OnDcStatusChanged(GameStatus value) => NotifyDcStatusDependents();
    partial void OnDcIsInstallingChanged(bool value) => NotifyDcIsInstallingDependents();
    partial void OnDcInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(DcStatusText));
    partial void OnDcActionMessageChanged(string value) => OnPropertyChanged(nameof(DcMessageVisibility));
}
