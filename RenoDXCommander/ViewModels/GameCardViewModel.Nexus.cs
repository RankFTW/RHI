using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// Nexus Mods integration properties for GameCardViewModel.
/// Provides observable state for update detection and download progress.
/// </summary>
public partial class GameCardViewModel
{
    [ObservableProperty] private bool _isNexusUpdateAvailable;
    [ObservableProperty] private bool _isNexusDownloading;
    [ObservableProperty] private double _nexusDownloadProgress;
    [ObservableProperty] private string _nexusActionMessage = "";

    // ── Nexus computed properties ─────────────────────────────────────────────────

    /// <summary>
    /// Visible when a Nexus update is available. Follows the same pattern as
    /// <see cref="UpdateBadgeVisibility"/> and <see cref="LumaBadgeVisibility"/>.
    /// </summary>
    public Visibility NexusUpdateBadgeVisibility =>
        IsNexusUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True when a Nexus download is not in progress — used for IsEnabled bindings.</summary>
    public bool IsNexusNotDownloading => !IsNexusDownloading;

    /// <summary>Visibility for the Nexus download progress bar.</summary>
    public Visibility NexusProgressVisibility =>
        IsNexusDownloading ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visibility for the Nexus action message text.</summary>
    public Visibility NexusMessageVisibility =>
        string.IsNullOrEmpty(NexusActionMessage) ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: IsNexusUpdateAvailable changed ──────────────────────
    partial void OnIsNexusUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(NexusUpdateBadgeVisibility));
        OnPropertyChanged(nameof(InstallBtnBackground));
        OnPropertyChanged(nameof(InstallBtnForeground));
        OnPropertyChanged(nameof(InstallBtnBorderBrush));
    }

    // ── Targeted notification: IsNexusDownloading changed ─────────────────────────
    partial void OnIsNexusDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNexusNotDownloading));
        OnPropertyChanged(nameof(NexusProgressVisibility));
    }

    // ── Targeted notification: NexusActionMessage changed ─────────────────────────
    partial void OnNexusActionMessageChanged(string value)
    {
        OnPropertyChanged(nameof(NexusMessageVisibility));
    }
}
