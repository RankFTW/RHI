using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// RE Framework status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── Plain properties ──────────────────────────────────────────────────────────
    public REFrameworkInstalledRecord? RefRecord { get; set; }
    public bool IsREEngineGame { get; set; }
    public bool ExcludeFromUpdateAllRef { get; set; }

    // ── REF computed properties ───────────────────────────────────────────────────

    public string RefActionLabel => RefIsInstalling ? "Installing..."
        : RefStatus == GameStatus.UpdateAvailable ? "⬆  Update RE Framework"
        : RefStatus == GameStatus.Installed ? "↺  Reinstall RE Framework"
        : "⬇  Install RE Framework";

    public string RefBtnBackground  => RefStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string RefBtnForeground  => RefStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string RefBtnBorderBrush => RefStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility RefProgressVisibility => RefIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RefMessageVisibility  => string.IsNullOrEmpty(RefActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RefDeleteVisibility   => RefStatus == GameStatus.Installed || RefStatus == GameStatus.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

    public string RefStatusText => RefIsInstalling ? "Installing…"
        : RefStatus == GameStatus.UpdateAvailable ? "Update"
        : RefStatus == GameStatus.Installed ? (RefInstalledVersion ?? "Installed")
        : "Ready";
    public string RefStatusColor => RefIsInstalling ? "#D4A856"
        : RefStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : RefStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";
    public string RefShortAction => RefIsInstalling ? "…"
        : RefStatus == GameStatus.UpdateAvailable ? "⬆ Update"
        : RefStatus == GameStatus.Installed ? "↺ Reinstall"
        : "⬇ Install";

    public bool IsRefNotInstalling => !RefIsInstalling;
    public bool IsRefInstalled => RefStatus == GameStatus.Installed || RefStatus == GameStatus.UpdateAvailable;

    // ── Card grid properties ──────────────────────────────────────────────────────
    public string CardRefStatusDot => RefIsInstalling ? "#2196F3"
        : RefStatus == GameStatus.UpdateAvailable ? "#4CAF50"
        : RefStatus == GameStatus.Installed ? "#4CAF50" : "#5A6880";
    public bool CardRefInstallEnabled => !RefIsInstalling;

    /// <summary>
    /// RE Framework row is visible when the game is an RE Engine game and NOT in Luma mode.
    /// </summary>
    public Visibility RefRowVisibility => (IsREEngineGame && !EffectiveLumaMode) ? Visibility.Visible : Visibility.Collapsed;

    // ── Targeted notification: RefStatus changed ──────────────────────────────────
    private void NotifyRefStatusDependents()
    {
        OnPropertyChanged(nameof(RefActionLabel));
        OnPropertyChanged(nameof(RefBtnBackground));
        OnPropertyChanged(nameof(RefBtnForeground));
        OnPropertyChanged(nameof(RefBtnBorderBrush));
        OnPropertyChanged(nameof(RefDeleteVisibility));
        OnPropertyChanged(nameof(RefStatusText));
        OnPropertyChanged(nameof(RefStatusColor));
        OnPropertyChanged(nameof(RefShortAction));
        OnPropertyChanged(nameof(IsRefInstalled));
        OnPropertyChanged(nameof(CardRefStatusDot));
        OnPropertyChanged(nameof(CardRefInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
        // ReShade depends on IsRefInstalled for RE Engine games
        OnPropertyChanged(nameof(RsActionLabel));
        OnPropertyChanged(nameof(CardRsInstallEnabled));
    }

    // ── Targeted notification: RefIsInstalling changed ────────────────────────────
    private void NotifyRefIsInstallingDependents()
    {
        OnPropertyChanged(nameof(RefActionLabel));
        OnPropertyChanged(nameof(RefProgressVisibility));
        OnPropertyChanged(nameof(IsRefNotInstalling));
        OnPropertyChanged(nameof(RefStatusText));
        OnPropertyChanged(nameof(RefStatusColor));
        OnPropertyChanged(nameof(RefShortAction));
        OnPropertyChanged(nameof(CardRefStatusDot));
        OnPropertyChanged(nameof(CardRefInstallEnabled));
    }

    partial void OnRefStatusChanged(GameStatus value) => NotifyRefStatusDependents();
    partial void OnRefIsInstallingChanged(bool value) => NotifyRefIsInstallingDependents();
    partial void OnRefInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(RefStatusText));
    partial void OnRefActionMessageChanged(string value) => OnPropertyChanged(nameof(RefMessageVisibility));
}
