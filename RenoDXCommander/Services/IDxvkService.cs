using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DXVK lifecycle: download, staging, install, uninstall,
/// update detection, dxvk.conf management, binary signature detection,
/// OptiScaler coexistence, and variant selection.
/// </summary>
public interface IDxvkService
{
    // ── Staging properties ─────────────────────────────────────────────

    /// <summary>Whether the staging folder contains a valid DXVK release.</summary>
    bool IsStagingReady { get; }

    /// <summary>Whether a newer DXVK release is available on GitHub.</summary>
    bool HasUpdate { get; }

    /// <summary>The currently staged version tag (e.g. "v2.7.1"), or null.</summary>
    string? StagedVersion { get; }

    /// <summary>
    /// Whether the first-time DXVK warning has been acknowledged this session.
    /// Reset each session so the dialog is shown once per session.
    /// </summary>
    bool FirstTimeWarningAcknowledged { get; set; }

    // ── Staging and update ─────────────────────────────────────────────

    /// <summary>
    /// Downloads and extracts the latest DXVK release to the staging folder.
    /// No-op if staging is already valid and up to date.
    /// </summary>
    Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Checks the GitHub releases API for a newer version than the staged one.
    /// Sets <see cref="HasUpdate"/> accordingly.
    /// </summary>
    Task CheckForUpdateAsync();

    /// <summary>
    /// Removes the staging folder contents (called when variant changes or from Settings cache clear).
    /// </summary>
    void ClearStaging();

    // ── Install / Uninstall / Update ───────────────────────────────────

    /// <summary>
    /// Installs DXVK to the specified game folder.
    /// Handles DLL selection by API/bitness, OptiScaler coexistence routing,
    /// backup of existing files, dxvk.conf deployment, and ReShade mode switching.
    /// </summary>
    Task InstallAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Uninstalls DXVK from the specified game folder.
    /// Removes deployed DLLs, restores backups, deletes dxvk.conf if deployed,
    /// and removes the tracking record.
    /// </summary>
    void Uninstall(GameCardViewModel card);

    /// <summary>
    /// Updates DXVK in a game folder: re-stages if needed, replaces DLLs,
    /// updates the tracking record version.
    /// </summary>
    Task UpdateAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null);

    // ── Configuration ──────────────────────────────────────────────────

    /// <summary>
    /// Copies the dxvk.conf template from the INI presets directory to the game folder.
    /// </summary>
    void CopyConfToGame(GameCardViewModel card);

    // ── Detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given DLL file contains DXVK binary signatures.
    /// Used by both detection and foreign DLL protection.
    /// </summary>
    bool IsDxvkFile(string filePath);

    /// <summary>
    /// Detects whether DXVK is installed in a game folder by scanning for
    /// DXVK binary signatures in known DLL filenames.
    /// Returns the detected version string, or null if not found.
    /// </summary>
    string? DetectInstallation(string installPath, GraphicsApiType api);

    // ── Tracking records ───────────────────────────────────────────────

    /// <summary>
    /// Loads all persisted <see cref="DxvkInstalledRecord"/> entries from disk.
    /// </summary>
    List<DxvkInstalledRecord> LoadAllRecords();

    /// <summary>
    /// Finds the DXVK tracking record for a specific game.
    /// </summary>
    DxvkInstalledRecord? FindRecord(string gameName, string installPath);

    // ── Variant ────────────────────────────────────────────────────────

    /// <summary>
    /// The selected DXVK variant (Development or Stable).
    /// Changing the variant clears the staging cache.
    /// </summary>
    DxvkVariant SelectedVariant { get; set; }
}
