namespace RenoDXCommander.Services;

/// <summary>
/// Downloads, extracts, and deploys HDR ReShade shader packs.
/// </summary>
public interface IShaderPackService
{
    /// <summary>
    /// Exposes pack metadata for the picker UI — returns every known pack's Id and DisplayName.
    /// </summary>
    IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; }

    /// <summary>
    /// Returns the short description for a pack, or null if none is set.
    /// </summary>
    string? GetPackDescription(string packId);

    /// <summary>
    /// Returns the IDs of packs that the given pack requires (dependencies).
    /// Returns empty if the pack has no dependencies.
    /// </summary>
    string[] GetRequiredPacks(string packId);

    /// <summary>
    /// Expands a set of pack IDs to include all transitive dependencies.
    /// </summary>
    IEnumerable<string> ExpandPackDependencies(IEnumerable<string> packIds);

    Task EnsureLatestAsync(IProgress<string>? progress = null);

    /// <summary>
    /// Downloads and extracts only the specified packs (on-demand).
    /// Packs that are already cached are skipped.
    /// </summary>
    Task EnsurePacksAsync(IEnumerable<string> packIds, IProgress<string>? progress = null);

    /// <summary>
    /// Returns true if the given pack's files are already cached locally
    /// (downloaded and extracted to the staging directory).
    /// </summary>
    bool IsPackCached(string packId);

    /// <summary>
    /// Returns true if the given pack's files are already cached locally (async-safe version).
    /// </summary>
    Task<bool> IsPackCachedAsync(string packId);

    void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null,
        Dictionary<string, HashSet<string>>? fileExclusions = null);

    void RemoveFromGameFolder(string gameDir);

    bool IsManagedByRdxc(string gameDir);

    void RestoreOriginalIfPresent(string gameDir);

    void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null,
        Dictionary<string, HashSet<string>>? fileExclusions = null);

    void SyncShadersToAllLocations(
        IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations,
        IEnumerable<string>? selectedPackIds = null);

    // ── Per-file include dependency map ──────────────────────────────────────────

    /// <summary>
    /// Scans all .fx and .fxh files in the staging Shaders directory and builds a map of
    /// filename → set of filenames it directly #includes (relative filename only, no path).
    /// Result is cached in-memory after first call; cleared on pack update.
    /// </summary>
    Dictionary<string, HashSet<string>> BuildIncludeMap();

    /// <summary>Clears the cached include map so it is rebuilt on the next call.</summary>
    void ClearIncludeCache();

    /// <summary>Returns all .fx filenames (leaf names) belonging to the given pack IDs, from the staging dir.</summary>
    IReadOnlyList<string> GetPackShaderFiles(IEnumerable<string> packIds);

    // ── Per-file exclusion storage ────────────────────────────────────────────────

    /// <summary>Gets the set of shader filenames explicitly excluded by the user for this pack.</summary>
    HashSet<string> GetExcludedFiles(string packId);

    /// <summary>Gets the set of shader filenames explicitly excluded by the user for this pack (async-safe version).</summary>
    Task<HashSet<string>> GetExcludedFilesAsync(string packId);

    /// <summary>Saves the excluded files for a pack.</summary>
    void ClearPackRegistration(string packId);
    void SetExcludedFiles(string packId, IEnumerable<string> excluded);

    /// <summary>Saves the excluded files for a pack (async-safe version).</summary>
    Task SetExcludedFilesAsync(string packId, IEnumerable<string> excluded);

    /// <summary>
    /// Scans the pack's staging subfolder and records all found files in settings.json.
    /// Used after importing shader files from an archive.
    /// </summary>
    void RecordExtractedFilesFromDir(string packId);

    /// <summary>
    /// Scans the pack's staging subfolder and records all found files in settings.json (async-safe version).
    /// Used after importing shader files from an archive.
    /// </summary>
    Task RecordExtractedFilesFromDirAsync(string packId);
}
