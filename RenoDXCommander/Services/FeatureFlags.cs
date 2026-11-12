// FeatureFlags.cs — Manifest-driven feature flag system.
// Each flag is enabled when either:
//   (a) DevUnlockService.IsUnlocked (local unlock.txt for dev preview), OR
//   (b) The manifest sets the flag to true (remote release without an app update).
//
// Usage:  if (FeatureFlags.DlssNr)  { ... }
// To ungate a feature globally: set the corresponding field in manifest.json featureFlags.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public static class FeatureFlags
{
    private static ManifestFeatureFlags? _manifestFlags;

    /// <summary>
    /// Called once when the manifest is fetched. Stores the manifest feature flags
    /// so they can supplement DevUnlockService.IsUnlocked checks at runtime.
    /// Safe to call multiple times (latest manifest always wins).
    /// </summary>
    public static void ApplyManifest(ManifestFeatureFlags? flags)
    {
        _manifestFlags = flags;
    }

    // ── Per-feature properties ────────────────────────────────────────────────

    /// <summary>DLSS Neural Rendering column and preset support.</summary>
    public static bool DlssNr
        => DevUnlockService.IsUnlocked || _manifestFlags?.DlssNr == true;

    /// <summary>Nexus Mods direct download / NXM protocol integration.</summary>
    public static bool NexusMods
        => DevUnlockService.IsUnlocked || _manifestFlags?.NexusMods == true;

    /// <summary>Resolution auto-toggle feature (Settings card + per-game toggle).</summary>
    public static bool ResolutionControl => true;

    /// <summary>
    /// HSF row in Extras › MFG Unlocks. Off for users until HSF's GitHub repo is public (the download
    /// needs repo access until then); turned on for everyone with "hsf": true in the manifest.
    /// </summary>
    public static bool Hsf
        => DevUnlockService.IsUnlocked || _manifestFlags?.Hsf == true;

    /// <summary>ANR FG preset in the OptiScaler ⚙. Turned on for everyone with "anrFg": true in the manifest.</summary>
    public static bool AnrFg
        => DevUnlockService.IsUnlocked || _manifestFlags?.AnrFg == true;
}
