using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// DXVK status, install state, and computed properties
public partial class GameCardViewModel
{
    // ── DXVK observable properties ────────────────────────────────────────────────
    [ObservableProperty] private GameStatus _dxvkStatus = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _dxvkIsInstalling;
    [ObservableProperty] private double     _dxvkProgress;
    [ObservableProperty] private string     _dxvkActionMessage = "";
    [ObservableProperty] private string?    _dxvkInstalledVersion;

    // ── Per-game overrides ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _dxvkEnabled;
    public bool ExcludeFromUpdateAllDxvk { get; set; }

    // ── Tracking record ───────────────────────────────────────────────────────────
    public DxvkInstalledRecord? DxvkRecord { get; set; }

    // ── Manifest-driven state (set externally by ApplyManifestCardOverrides) ──────
    /// <summary>True when this game is on the DXVK blacklist in the manifest.</summary>
    public bool IsDxvkBlacklisted { get; set; }

    /// <summary>True when the manifest contains a dxvkApiOverrides entry for this game.</summary>
    public bool HasDxvkApiOverride { get; set; }

    // ── DXVK toggle eligibility ───────────────────────────────────────────────────

    /// <summary>
    /// DXVK toggle is visible only for games whose primary API is DX8/DX9/DX10/DX11.
    /// Games with DX12, Vulkan, OpenGL, or Unknown API do not show the toggle.
    /// </summary>
    /// <summary>
    /// Returns true if the detected APIs include DX12 or Vulkan, indicating the game
    /// likely runs on a newer API even if the primary detection picked DX11.
    /// Also returns true for Unreal Engine DX11 games — modern UE titles support DX12
    /// and the DX11 detection is often a fallback when PE scanning fails (e.g. WindowsApps).
    /// </summary>
    private bool HasHigherApiDetected =>
        DetectedApis.Contains(GraphicsApiType.DirectX12)
        || DetectedApis.Contains(GraphicsApiType.Vulkan)
        || (GraphicsApi == GraphicsApiType.DirectX11
            && (EngineHint.StartsWith("Unreal Engine", StringComparison.OrdinalIgnoreCase) || EngineHint is "Unreal (Legacy)"));

    public bool IsDxvkToggleVisible =>
        // DX11 is intentionally excluded — DXVK on Windows provides marginal to negative
        // performance for DX11 games, and causes overlay/fullscreen/VRR issues.
        // The DX11 install code is still in place; re-add DirectX11 here when ready.
        (GraphicsApi is GraphicsApiType.DirectX8
                     or GraphicsApiType.DirectX9
                     or GraphicsApiType.DirectX10)
        && !HasHigherApiDetected
        || DxvkRecord?.InstalledDlls.Contains("d3d9.dll") == true; // Keep visible when direct DX9 mode is active (API switched to Vulkan)

    /// <summary>
    /// DXVK toggle is enabled when visible AND not blacklisted AND the API is not
    /// Unknown (unless an API override exists in the manifest).
    /// </summary>
    public bool IsDxvkToggleEnabled =>
        IsDxvkToggleVisible
        && !IsDxvkBlacklisted
        && (GraphicsApi != GraphicsApiType.Unknown || HasDxvkApiOverride);

    /// <summary>
    /// Tooltip explaining why the DXVK toggle is disabled. Returns null when enabled.
    /// </summary>
    public string? DxvkToggleTooltip =>
        IsDxvkToggleEnabled ? null
        : IsDxvkBlacklisted ? "DXVK заблокирован для этой игры из-за античита."
        : GraphicsApi == GraphicsApiType.Unknown && !HasDxvkApiOverride
            ? "DXVK нельзя включить: не удалось определить версию DirectX игры."
        : GraphicsApi is GraphicsApiType.DirectX12 or GraphicsApiType.Vulkan or GraphicsApiType.OpenGL
            ? $"DXVK не поддерживает {GraphicsApi}. Он транслирует в Vulkan только DirectX 8/9/10/11."
        : null;

    // ── DXVK computed properties ──────────────────────────────────────────────────

    /// <summary>Per-component status dot for DXVK.</summary>
    public string DxvkStatusDot => DxvkStatus == GameStatus.UpdateAvailable ? "🟢"
        : DxvkStatus == GameStatus.Installed ? "🟢" : "⚪";

    public string DxvkActionLabel => DxvkIsInstalling ? "Installing..."
        : DxvkStatus == GameStatus.UpdateAvailable ? "⬆  Обновить DXVK"
        : DxvkStatus == GameStatus.Installed ? "↺  Переустановить DXVK"
        : "⬇  Установить DXVK";

    public string DxvkBtnBackground  => DxvkStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string DxvkBtnForeground  => DxvkStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string DxvkBtnBorderBrush => DxvkStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility DxvkProgressVisibility => DxvkIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DxvkDeleteVisibility   => DxvkStatus == GameStatus.Installed || DxvkStatus == GameStatus.UpdateAvailable
        ? Visibility.Visible : Visibility.Collapsed;

    public string DxvkStatusText => DxvkIsInstalling ? "Installing…"
        : DxvkStatus == GameStatus.UpdateAvailable ? "Update"
        : DxvkStatus == GameStatus.Installed ? (DxvkInstalledVersion ?? "Installed")
        : "Ready";
    public string DxvkStatusColor => DxvkIsInstalling ? "#D4A856"
        : DxvkStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : DxvkStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";
    public string DxvkShortAction => DxvkIsInstalling ? "…"
        : DxvkStatus == GameStatus.UpdateAvailable ? "⬆ Обновить"
        : DxvkStatus == GameStatus.Installed ? "↺ Переустановить"
        : "⬇ Установить";

    public bool IsDxvkNotInstalling => !DxvkIsInstalling;
    public bool IsDxvkInstalled => DxvkStatus == GameStatus.Installed || DxvkStatus == GameStatus.UpdateAvailable;
    public bool DxvkInstallEnabled => !DxvkIsInstalling;

    /// <summary>DXVK row is visible when installed OR when a variant has been selected (pending install).</summary>
    public Visibility DxvkRowVisibility => (DxvkEnabled || DxvkVariantPending) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True when a DXVK variant override is set but DXVK is not yet installed — shows the Install button.</summary>
    [ObservableProperty] private bool _dxvkVariantPending;

    /// <summary>
    /// Stores the Low Latency Mode value that was active before Smooth Motion was enabled.
    /// Used to restore the previous value when Smooth Motion is turned off.
    /// null = no saved value (smooth motion was off or latency was already Ultra).
    /// </summary>
    public uint? PreSmoothMotionLowLatency { get; set; }

    // ── Card grid properties ──────────────────────────────────────────────────────
    public string CardDxvkStatusDot => DxvkIsInstalling ? "#2196F3"
        : DxvkStatus == GameStatus.UpdateAvailable ? "#4CAF50"
        : DxvkStatus == GameStatus.Installed ? "#4CAF50" : "#5A6880";
    public bool CardDxvkInstallEnabled => !DxvkIsInstalling;

    // ── Targeted notification: DxvkStatus changed ─────────────────────────────────
    private void NotifyDxvkStatusDependents()
    {
        OnPropertyChanged(nameof(DxvkStatusDot));
        OnPropertyChanged(nameof(DxvkActionLabel));
        OnPropertyChanged(nameof(DxvkBtnBackground));
        OnPropertyChanged(nameof(DxvkBtnForeground));
        OnPropertyChanged(nameof(DxvkBtnBorderBrush));
        OnPropertyChanged(nameof(DxvkDeleteVisibility));
        OnPropertyChanged(nameof(DxvkStatusText));
        OnPropertyChanged(nameof(DxvkStatusColor));
        OnPropertyChanged(nameof(DxvkShortAction));
        OnPropertyChanged(nameof(IsDxvkInstalled));
        OnPropertyChanged(nameof(DxvkInstallEnabled));
        OnPropertyChanged(nameof(CardDxvkStatusDot));
        OnPropertyChanged(nameof(CardDxvkInstallEnabled));
        OnPropertyChanged(nameof(UpdateBadgeVisibility));
    }

    // ── Targeted notification: DxvkIsInstalling changed ───────────────────────────
    private void NotifyDxvkIsInstallingDependents()
    {
        OnPropertyChanged(nameof(DxvkActionLabel));
        OnPropertyChanged(nameof(DxvkProgressVisibility));
        OnPropertyChanged(nameof(IsDxvkNotInstalling));
        OnPropertyChanged(nameof(DxvkInstallEnabled));
        OnPropertyChanged(nameof(DxvkStatusText));
        OnPropertyChanged(nameof(DxvkStatusColor));
        OnPropertyChanged(nameof(DxvkShortAction));
        OnPropertyChanged(nameof(CardDxvkStatusDot));
        OnPropertyChanged(nameof(CardDxvkInstallEnabled));
    }

    partial void OnDxvkStatusChanged(GameStatus value) => NotifyDxvkStatusDependents();
    partial void OnDxvkIsInstallingChanged(bool value) => NotifyDxvkIsInstallingDependents();
    partial void OnDxvkInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(DxvkStatusText));
    partial void OnDxvkActionMessageChanged(string value) { }
    partial void OnDxvkEnabledChanged(bool value) => OnPropertyChanged(nameof(DxvkRowVisibility));
}
