using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// Ultra Limiter status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── UL computed properties ─────────────────────────────────────────────────────

    /// <summary>Per-component status dot for Ultra Limiter.</summary>
    public string UlStatusDot => UlStatus == GameStatus.Installed ? "🟢" : "⚪";

    public string UlActionLabel => UlIsInstalling ? "Installing..."
        : UlStatus == GameStatus.Installed ? "↺  Reinstall Ultra Limiter"
        : "⬇  Install Ultra Limiter";

    public string UlBtnBackground  => "#182840";
    public string UlBtnForeground  => "#7AACDD";
    public string UlBtnBorderBrush => "#2A4468";

    public Visibility UlProgressVisibility => UlIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UlMessageVisibility  => string.IsNullOrEmpty(UlActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility UlDeleteVisibility   => UlStatus == GameStatus.Installed ? Visibility.Visible : Visibility.Collapsed;

    public string UlStatusText => UlIsInstalling ? "Installing…"
        : UlStatus == GameStatus.Installed ? "Installed"
        : "Ready";
    public string UlStatusColor => UlIsInstalling ? "#D4A856"
        : UlStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";
    public string UlShortAction => UlIsInstalling ? "…"
        : UlStatus == GameStatus.Installed ? "↺ Reinstall"
        : "⬇ Install";

    public bool IsUlNotInstalling => !UlIsInstalling;
    public bool IsUlInstalled => UlStatus == GameStatus.Installed;

    // ── Card grid properties ──────────────────────────────────────────────────────
    public string CardUlStatusDot => UlIsInstalling ? "#2196F3"
        : UlStatus == GameStatus.Installed ? "#4CAF50" : "#5A6880";
    public bool CardUlInstallEnabled => !UlIsInstalling;

    /// <summary>
    /// Ultra Limiter row is visible when NOT in DC Legacy Mode and NOT in Luma mode.
    /// </summary>
    public Visibility UlRowVisibility => DcLegacyMode ? Visibility.Collapsed
        : EffectiveLumaMode ? Visibility.Collapsed : Visibility.Visible;

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
    partial void OnUlActionMessageChanged(string value) => OnPropertyChanged(nameof(UlMessageVisibility));
}
