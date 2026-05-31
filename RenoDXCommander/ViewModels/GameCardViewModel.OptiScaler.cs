using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// OptiScaler status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── OptiScaler observable properties ───────────────────────────────────────────
    [ObservableProperty] private GameStatus _osStatus = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _osIsInstalling;
    [ObservableProperty] private double     _osProgress;
    [ObservableProperty] private string     _osActionMessage = "";
    [ObservableProperty] private string?    _osInstalledFile;
    [ObservableProperty] private string?    _osInstalledVersion;

    // Per-game update exclusion
    public bool ExcludeFromUpdateAllOs { get; set; }

    // ── OptiScaler computed properties ────────────────────────────────────────────

    /// <summary>Per-component status dot for OptiScaler.</summary>
    public string OsStatusDot => OsStatus == GameStatus.UpdateAvailable ? "🟢"
        : OsStatus == GameStatus.Installed ? "🟢" : "⚪";

    public string OsActionLabel => OsIsInstalling ? L("Installing...")
        : OsStatus == GameStatus.UpdateAvailable ? L("⬆  Update OptiScaler")
        : OsStatus == GameStatus.Installed ? L("↺  Reinstall OptiScaler")
        : L("⬇  Install OptiScaler");

    public string OsBtnBackground  => OsStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string OsBtnForeground  => OsStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string OsBtnBorderBrush => OsStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility OsProgressVisibility => OsIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OsMessageVisibility  => string.IsNullOrEmpty(OsActionMessage)
        ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OsDeleteVisibility   => OsStatus == GameStatus.Installed
        || OsStatus == GameStatus.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

    public string OsStatusText => OsIsInstalling ? L("Installing…")
        : OsStatus == GameStatus.UpdateAvailable ? L("Update")
        : OsStatus == GameStatus.Installed ? (OsInstalledVersion ?? L("Installed"))
        : L("Ready");
    public string OsStatusColor => OsIsInstalling ? "#D4A856"
        : OsStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : OsStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";
    public string OsShortAction => OsIsInstalling ? "…"
        : OsStatus == GameStatus.UpdateAvailable ? L("⬆ Update")
        : OsStatus == GameStatus.Installed ? L("↺ Reinstall")
        : L("⬇ Install");

    public bool IsOsNotInstalling => !OsIsInstalling;
    public bool IsOsInstalled => OsStatus == GameStatus.Installed
        || OsStatus == GameStatus.UpdateAvailable;

    /// <summary>
    /// OptiScaler install enabled when: not installing, not 32-bit.
    /// No mutual exclusion with other components (unlike UL/DC).
    /// </summary>
    public bool OsInstallEnabled => !OsIsInstalling && !Is32Bit;

    /// <summary>True when OptiScaler.ini exists in the inis folder.</summary>
    public bool OsIniExists => File.Exists(Path.Combine(Services.AuxInstallService.InisDir, "OptiScaler.ini"));

    /// <summary>OptiScaler row is always visible for all games.</summary>
    public Visibility OsRowVisibility => Visibility.Visible;

    // ── Card grid properties ──────────────────────────────────────────────────────
    public string CardOsStatusDot => OsIsInstalling ? "#2196F3"
        : OsStatus == GameStatus.UpdateAvailable ? "#4CAF50"
        : OsStatus == GameStatus.Installed ? "#4CAF50" : "#5A6880";
    public bool CardOsInstallEnabled => !OsIsInstalling && !Is32Bit;

    // ── Targeted notification: OsStatus changed ───────────────────────────────────
    private void NotifyOsStatusDependents()
    {
        OnPropertyChanged(nameof(OsStatusDot));
        OnPropertyChanged(nameof(OsActionLabel));
        OnPropertyChanged(nameof(OsBtnBackground));
        OnPropertyChanged(nameof(OsBtnForeground));
        OnPropertyChanged(nameof(OsBtnBorderBrush));
        OnPropertyChanged(nameof(OsDeleteVisibility));
        OnPropertyChanged(nameof(OsStatusText));
        OnPropertyChanged(nameof(OsStatusColor));
        OnPropertyChanged(nameof(OsShortAction));
        OnPropertyChanged(nameof(IsOsInstalled));
        OnPropertyChanged(nameof(OsInstallEnabled));
        OnPropertyChanged(nameof(CardOsStatusDot));
        OnPropertyChanged(nameof(CardOsInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
    }

    // ── Targeted notification: OsIsInstalling changed ─────────────────────────────
    private void NotifyOsIsInstallingDependents()
    {
        OnPropertyChanged(nameof(OsActionLabel));
        OnPropertyChanged(nameof(OsProgressVisibility));
        OnPropertyChanged(nameof(IsOsNotInstalling));
        OnPropertyChanged(nameof(OsInstallEnabled));
        OnPropertyChanged(nameof(OsStatusText));
        OnPropertyChanged(nameof(OsStatusColor));
        OnPropertyChanged(nameof(OsShortAction));
        OnPropertyChanged(nameof(CardOsStatusDot));
        OnPropertyChanged(nameof(CardOsInstallEnabled));
    }

    partial void OnOsStatusChanged(GameStatus value) => NotifyOsStatusDependents();
    partial void OnOsIsInstallingChanged(bool value) => NotifyOsIsInstallingDependents();
    partial void OnOsInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(OsStatusText));
    partial void OnOsActionMessageChanged(string value) => OnPropertyChanged(nameof(OsMessageVisibility));
}
