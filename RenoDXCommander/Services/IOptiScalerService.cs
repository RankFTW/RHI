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
    /// Checks the GitHub releases API for a newer version than the staged one.
    /// Sets <see cref="HasUpdate"/> accordingly.
    /// </summary>
    Task CheckForUpdateAsync();

    /// <summary>
    /// Removes the staging folder contents (called from Settings cache clear).
    /// </summary>
    void ClearStaging();

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
        string? hotkey = null);

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
    /// Copies OptiScaler.ini from the INIs_Folder to the game folder,
    /// enforcing LoadReshade=true.
    /// </summary>
    void CopyIniToGame(GameCardViewModel card);

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
}
