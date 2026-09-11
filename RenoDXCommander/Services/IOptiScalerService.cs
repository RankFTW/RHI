using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages OptiScaler lifecycle: download, staging, install, uninstall,
/// update detection, INI management, and ReShade coexistence.
/// </summary>
public interface IOptiScalerService
{
    /// <summary>Whether the staging folder contains a valid OptiScaler release.</summary>
    bool IsStagingReady { get; }

    /// <summary>Whether a newer OptiScaler release is available on GitHub.</summary>
    bool HasUpdate { get; }

    /// <summary>The currently staged version tag (e.g. "v0.8.1"), or null.</summary>
    string? StagedVersion { get; }

    /// <summary>Whether the nightly staging folder contains a valid OptiScaler nightly release.</summary>
    bool IsStagingReadyNightly { get; }

    /// <summary>Whether a newer OptiScaler nightly release is available on GitHub.</summary>
    bool HasUpdateNightly { get; }

    /// <summary>The currently staged nightly version (date string e.g. "20260813"), or null.</summary>
    string? StagedVersionNightly { get; }

    /// <summary>
    /// Whether the first-time warning has been acknowledged.
    /// Persisted so the dialog is only shown once across all installs.
    /// </summary>
    bool FirstTimeWarningAcknowledged { get; set; }

    // ── Staging and update ────────────────────────────────────────────────────

    /// <summary>
    /// Downloads and extracts the latest OptiScaler release to the staging folder.
    /// No-op if staging is already valid and up to date.
    /// </summary>
    Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Downloads OptiPatcher to the staging folder and auto-deploys to all installed games.
    /// No-op if already up to date.
    /// </summary>
    Task EnsureOptiPatcherStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Checks the GitHub rolling release for a newer OptiPatcher version.
    /// Returns true if a newer version is available.
    /// </summary>
    Task<bool> CheckOptiPatcherUpdateAsync();

    /// <summary>
    /// Checks the GitHub releases API for a newer version than the staged one.
    /// Sets <see cref="HasUpdate"/> accordingly.
    /// </summary>
    Task CheckForUpdateAsync();

    /// <summary>
    /// Removes the staging folder contents (called from Settings cache clear).
    /// </summary>
    void ClearStaging();

    /// <summary>Downloads and extracts the latest OptiScaler nightly to the nightly staging folder.</summary>
    Task EnsureNightlyStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>Checks the nightly GitHub releases API for a newer version. Sets <see cref="HasUpdateNightly"/>.</summary>
    Task CheckForNightlyUpdateAsync();

    /// <summary>Removes the nightly staging folder contents.</summary>
    void ClearNightlyStaging();

    /// <summary>Whether the DLSS NR staging folder contains a valid OptiScaler DLSS NR release.</summary>
    bool IsStagingReadyDlssNr { get; }

    /// <summary>Whether a newer OptiScaler DLSS NR release is available on GitHub.</summary>
    bool HasUpdateDlssNr { get; }

    /// <summary>The currently staged DLSS NR version (e.g. "0.7.7"), or null.</summary>
    string? StagedVersionDlssNr { get; }

    /// <summary>Downloads and extracts the latest OptiScaler DLSS NR release to the DLSS NR staging folder.</summary>
    Task EnsureDlssNrStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>Checks the DLSS NR GitHub releases API for a newer version. Sets <see cref="HasUpdateDlssNr"/>.</summary>
    Task CheckForDlssNrUpdateAsync();

    /// <summary>Removes the DLSS NR staging folder contents.</summary>
    void ClearDlssNrStaging();

    // ── DLSS DLL staging ──────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the latest nvngx_dlss.dll from the DLSS Swapper manifest to the staging folder.
    /// No-op if staging is already valid and up to date.
    /// </summary>
    Task EnsureDlssStagingAsync(IProgress<(string message, double percent)>? progress = null);

    // ── Install / Uninstall / Update ──────────────────────────────────────────

    /// <summary>
    /// Installs OptiScaler to the specified game folder.
    /// Handles first-time warning, DLL naming, INI seeding, LoadReshade enforcement,
    /// companion file deployment, and ReShade coexistence.
    /// </summary>
    Task<AuxInstalledRecord?> InstallAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null,
        string gpuType = "NVIDIA",
        bool dlssInputs = true,
        string? hotkey = null,
        string variant = "Stable");

    /// <summary>
    /// Uninstalls OptiScaler from the specified game folder.
    /// Removes DLL, INI, companion files, restores ReShade filename, removes tracking record.
    /// </summary>
    void Uninstall(GameCardViewModel card);

    /// <summary>
    /// Updates OptiScaler in a game folder: replaces DLL and companions, preserves INI.
    /// </summary>
    Task UpdateAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null);

    // ── INI management ────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds all 6 user-editable INI files in the inis folder from bundled templates,
    /// only if they don't already exist. Called once at startup.
    /// </summary>
    void SeedUserInis();

    /// <summary>
    /// Copies OptiScaler.ini from the INIs_Folder to the game folder,
    /// enforcing LoadReshade=true.
    /// </summary>
    void CopyIniToGame(GameCardViewModel card, string? hotkey = null);

    // ── Detection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects whether OptiScaler is installed in a game folder by checking
    /// binary signatures and OptiScaler.ini presence.
    /// Returns the detected DLL filename, or null if not found.
    /// </summary>
    string? DetectInstallation(string installPath);

    /// <summary>
    /// Returns true if the given DLL file contains OptiScaler binary signatures.
    /// Used by both detection and foreign DLL protection.
    /// </summary>
    bool IsOptiScalerFile(string filePath);

    // ── Tracking records ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads all persisted OptiScaler <see cref="AuxInstalledRecord"/> entries from disk.
    /// </summary>
    List<AuxInstalledRecord> LoadAllRecords();

    /// <summary>
    /// Finds the OptiScaler tracking record for a specific game.
    /// </summary>
    AuxInstalledRecord? FindRecord(string gameName, string installPath);

    // ── DLL naming ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective OptiScaler DLL filename for a game,
    /// following the priority chain: user override &gt; manifest override &gt; dxgi.dll.
    /// </summary>
    string GetEffectiveOsDllName(string gameName);

    // ── Hotkey ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the ShortcutKey= value to the OptiScaler.ini in the INIs_Folder.
    /// </summary>
    void SetHotkey(string hotkeyValue);

    /// <summary>
    /// Updates ShortcutKey= in all game folders where OptiScaler is installed.
    /// </summary>
    void ApplyHotkeyToAllGames(string hotkeyValue);

    /// <summary>
    /// Copies all Streamline DLLs from the RHI Streamline staging folder to
    /// <paramref name="installPath"/>\OptiScaler\Streamline\.
    /// </summary>
    void DeployStreamlineToGame(string installPath, string? version = null);

    /// <summary>
    /// Removes the OptiScaler\Streamline\ subfolder from the given game install path.
    /// </summary>
    void RemoveStreamlineFromGame(string installPath);
}
