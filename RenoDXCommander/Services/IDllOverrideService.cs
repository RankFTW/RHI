using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Defines the contract for DLL override CRUD operations and the backing data store.
/// Manages per-game DLL naming overrides, manifest-driven overrides, and user opt-outs.
/// </summary>
public interface IDllOverrideService
{
    /// <summary>Raised when the overrides dictionary changes and settings should be persisted.</summary>
    Action? OverridesChanged { get; set; }

    /// <summary>
    /// Games where a manifest-driven DLL override was injected by ApplyManifestDllRenames.
    /// These are NOT persisted — they are re-evaluated on every refresh.
    /// </summary>
    HashSet<string> ManifestDllOverrideGames { get; }

    /// <summary>
    /// Games where the user has explicitly disabled a manifest-driven DLL override.
    /// These are persisted to settings.json so the opt-out survives refreshes.
    /// </summary>
    HashSet<string> ManifestDllOverrideOptOuts { get; }

    // ── Data access for serialization ─────────────────────────────────────────

    /// <summary>Returns the full dictionary of per-game DLL naming overrides.</summary>
    Dictionary<string, DllOverrideConfig> GetAllOverrides();

    /// <summary>
    /// Replaces the current overrides and opt-outs with values loaded from settings.
    /// Clears manifest-injected overrides so they can be re-evaluated.
    /// </summary>
    void SetOverridesFromSettings(
        Dictionary<string, DllOverrideConfig> overrides,
        HashSet<string> optOuts);

    /// <summary>Clears manifest-injected overrides so they can be re-evaluated on refresh.</summary>
    void ClearManifestOverrides();

    /// <summary>Prunes opt-outs for games no longer in the manifest's dllNameOverrides.</summary>
    void PruneOptOuts(HashSet<string> manifestKeys, Func<string, string> normalizer);

    // ── CRUD ──────────────────────────────────────────────────────────────────

    /// <summary>Returns true if a DLL override exists for the specified game.</summary>
    bool HasDllOverride(string gameName);

    /// <summary>Returns the DLL override config for the specified game, or null if none exists.</summary>
    DllOverrideConfig? GetDllOverride(string gameName);

    /// <summary>Sets or updates the DLL override for the specified game.</summary>
    void SetDllOverride(string gameName, string reshadeFileName, string dcFileName);

    /// <summary>Removes the DLL override for the specified game.</summary>
    void RemoveDllOverride(string gameName);

    /// <summary>
    /// Called when DLL override is toggled ON — renames existing ReShade and DC
    /// files in the game folder to the custom filenames so they stay installed.
    /// </summary>
    void EnableDllOverride(GameCardViewModel card, string reshadeFileName, string dcFileName);

    /// <summary>
    /// Called when DLL override is already ON and the filenames are updated —
    /// renames existing files on disk to the new custom names.
    /// </summary>
    void UpdateDllOverrideNames(GameCardViewModel card, string newRsName, string newDcName);

    /// <summary>
    /// Called when DLL override is toggled OFF — removes the custom-named DLL files
    /// from the game folder.
    /// </summary>
    void DisableDllOverride(GameCardViewModel card);

    /// <summary>Migrates a DLL override entry when a game is renamed.</summary>
    void MigrateOverride(string oldName, string newName);

    /// <summary>
    /// Returns a filtered dictionary excluding manifest-injected overrides (for persistence).
    /// </summary>
    Dictionary<string, DllOverrideConfig> GetUserOverridesForSave();
}
