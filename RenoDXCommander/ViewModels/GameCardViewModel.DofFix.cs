using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// DOF Fix status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── DOF Fix observable properties ─────────────────────────────────────────────
    [ObservableProperty] private GameStatus _dofFixStatus = GameStatus.NotInstalled;
    [ObservableProperty] private string?    _dofFixInstalledVersion;
    [ObservableProperty] private bool       _dofFixIsInstalling;
    [ObservableProperty] private double     _dofFixProgress;
    [ObservableProperty] private string     _dofFixActionMessage = "";

    // ── DOF Fix per-game exclusion ────────────────────────────────────────────────
    public bool ExcludeFromUpdateAllDofFix { get; set; }

    // ── DOF Fix eligibility (set externally during card build) ─────────────────────
    public bool IsDofFixEligible { get; set; }

    // ── DOF Fix computed properties ───────────────────────────────────────────────

    /// <summary>DOF Fix row is visible when the game is eligible for DOF Fix.</summary>
    public Visibility DofFixRowVisibility =>
        IsDofFixEligible ? Visibility.Visible : Visibility.Collapsed;

    public string DofFixActionLabel => DofFixIsInstalling ? "Installing..."
        : DofFixStatus == GameStatus.UpdateAvailable ? "⬆  Update DOF Fix"
        : DofFixStatus == GameStatus.Installed ? "↺  Reinstall DOF Fix"
        : "⬇  Install DOF Fix";

    public string DofFixBtnBackground  => DofFixStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string DofFixBtnForeground  => DofFixStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string DofFixBtnBorderBrush => DofFixStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility DofFixProgressVisibility => DofFixIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DofFixDeleteVisibility   => DofFixStatus == GameStatus.Installed || DofFixStatus == GameStatus.UpdateAvailable
        ? Visibility.Visible : Visibility.Collapsed;

    public string DofFixStatusText => DofFixIsInstalling ? "Installing…"
        : DofFixStatus == GameStatus.UpdateAvailable ? "Update"
        : DofFixStatus == GameStatus.Installed ? (DofFixInstalledVersion ?? "Installed")
        : "Ready";
    public string DofFixStatusColor => DofFixIsInstalling ? "#D4A856"
        : DofFixStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : DofFixStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";

    public bool IsDofFixNotInstalling => !DofFixIsInstalling;
    public bool IsDofFixInstalled => DofFixStatus == GameStatus.Installed || DofFixStatus == GameStatus.UpdateAvailable;
    public bool DofFixInstallEnabled => !DofFixIsInstalling;

    public Visibility DofFixMessageVisibility => string.IsNullOrEmpty(DofFixActionMessage) ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: DofFixStatus changed ───────────────────────────────
    private void NotifyDofFixStatusDependents()
    {
        OnPropertyChanged(nameof(DofFixActionLabel));
        OnPropertyChanged(nameof(DofFixBtnBackground));
        OnPropertyChanged(nameof(DofFixBtnForeground));
        OnPropertyChanged(nameof(DofFixBtnBorderBrush));
        OnPropertyChanged(nameof(DofFixDeleteVisibility));
        OnPropertyChanged(nameof(DofFixStatusText));
        OnPropertyChanged(nameof(DofFixStatusColor));
        OnPropertyChanged(nameof(IsDofFixInstalled));
        OnPropertyChanged(nameof(DofFixInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
    }

    // ── Targeted notification: DofFixIsInstalling changed ─────────────────────────
    private void NotifyDofFixIsInstallingDependents()
    {
        OnPropertyChanged(nameof(DofFixActionLabel));
        OnPropertyChanged(nameof(DofFixProgressVisibility));
        OnPropertyChanged(nameof(IsDofFixNotInstalling));
        OnPropertyChanged(nameof(DofFixInstallEnabled));
        OnPropertyChanged(nameof(DofFixStatusText));
        OnPropertyChanged(nameof(DofFixStatusColor));
    }

    partial void OnDofFixStatusChanged(GameStatus value) => NotifyDofFixStatusDependents();
    partial void OnDofFixIsInstallingChanged(bool value) => NotifyDofFixIsInstallingDependents();
    partial void OnDofFixInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(DofFixStatusText));
    partial void OnDofFixActionMessageChanged(string value) => OnPropertyChanged(nameof(DofFixMessageVisibility));
}
