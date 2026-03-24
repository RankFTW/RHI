using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// ReLimiter status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── UL computed properties ─────────────────────────────────────────────────────

    /// <summary>Per-component status dot for ReLimiter.</summary>
    public string UlStatusDot => UlStatus == GameStatus.UpdateAvailable ? "🟢"
        : UlStatus == GameStatus.Installed ? "🟢" : "⚪";

    public string UlActionLabel => UlIsInstalling ? "Installing..."
        : UlStatus == GameStatus.UpdateAvailable ? "⬆  Update ReLimiter"
        : UlStatus == GameStatus.Installed ? "↺  Reinstall ReLimiter"
        : "⬇  Install ReLimiter";

    public string UlBtnBackground  => "#182840";
    public string UlBtnForeground  => "#7AACDD";
    public string UlBtnBorderBrush => "#2A4468";

    public Visibility UlProgressVisibility => UlIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UlMessageVisibility  => string.IsNullOrEmpty(UlActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility UlDeleteVisibility   => UlStatus == GameStatus.Installed || UlStatus == GameStatus.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

    public string UlStatusText => UlIsInstalling ? "Installing…"
        : UlStatus == GameStatus.UpdateAvailable ? "Update"
        : UlStatus == GameStatus.Installed ? (UlInstalledVersion ?? "Installed")
        : "Ready";
    public string UlStatusColor => UlIsInstalling ? "#D4A856"
        : UlStatus == GameStatus.UpdateAvailable ? "#E8A33D"
        : UlStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";
    public string UlShortAction => UlIsInstalling ? "…"
        : UlStatus == GameStatus.UpdateAvailable ? "⬆ Update"
        : UlStatus == GameStatus.Installed ? "↺ Reinstall"
        : "⬇ Install";

    public bool IsUlNotInstalling => !UlIsInstalling;
    public bool IsUlInstalled => UlStatus == GameStatus.Installed || UlStatus == GameStatus.UpdateAvailable;

    /// <summary>True when relimiter.ini is present in the inis folder — enables the 📋 button.</summary>
    public bool UlIniExists => File.Exists(Services.AuxInstallService.UlIniPath);

    // ── Card grid properties ──────────────────────────────────────────────────────
    public string CardUlStatusDot => UlIsInstalling ? "#2196F3"
        : UlStatus == GameStatus.UpdateAvailable ? "#4CAF50"
        : UlStatus == GameStatus.Installed ? "#4CAF50" : "#5A6880";
    public bool CardUlInstallEnabled => !UlIsInstalling;

    /// <summary>
    /// ReLimiter row is visible when NOT in Luma mode.
    /// </summary>
    public Visibility UlRowVisibility => EffectiveLumaMode ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: UlStatus changed ───────────────────────────────────
    private void NotifyUlStatusDependents()
    {
        OnPropertyChanged(nameof(UlStatusDot));
        OnPropertyChanged(nameof(UlActionLabel));
        OnPropertyChanged(nameof(UlDeleteVisibility));
        OnPropertyChanged(nameof(UlStatusText));
        OnPropertyChanged(nameof(UlStatusColor));
        OnPropertyChanged(nameof(UlShortAction));
        OnPropertyChanged(nameof(IsUlInstalled));
        OnPropertyChanged(nameof(CardUlStatusDot));
        OnPropertyChanged(nameof(CardUlInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
    }

    // ── Targeted notification: UlIsInstalling changed ─────────────────────────────
    private void NotifyUlIsInstallingDependents()
    {
        OnPropertyChanged(nameof(UlActionLabel));
        OnPropertyChanged(nameof(UlProgressVisibility));
        OnPropertyChanged(nameof(IsUlNotInstalling));
        OnPropertyChanged(nameof(UlStatusText));
        OnPropertyChanged(nameof(UlStatusColor));
        OnPropertyChanged(nameof(UlShortAction));
        OnPropertyChanged(nameof(CardUlStatusDot));
        OnPropertyChanged(nameof(CardUlInstallEnabled));
    }

    partial void OnUlStatusChanged(GameStatus value) => NotifyUlStatusDependents();
    partial void OnUlIsInstallingChanged(bool value) => NotifyUlIsInstallingDependents();
    partial void OnUlInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(UlStatusText));
    partial void OnUlActionMessageChanged(string value) => OnPropertyChanged(nameof(UlMessageVisibility));
}
