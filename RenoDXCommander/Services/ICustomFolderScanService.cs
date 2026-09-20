using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>Result of scanning the user's custom game folders.</summary>
public sealed class CustomFolderScanResult
{
    /// <summary>New game candidates (already de-duplicated against known games), best first.</summary>
    public List<CustomGameCandidate> Candidates { get; } = new();

    /// <summary>Configured folders that could not be read right now (offline drive, permissions). Kept, never deleted.</summary>
    public List<string> UnavailableRoots { get; } = new();

    /// <summary>Configured folders refused because they are system locations.</summary>
    public List<string> SkippedRoots { get; } = new();

    /// <summary>True when a per-folder safety cap ended a scan early.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Finds game folders inside explicitly configured custom folders. It never scans anything the
/// user did not choose, and it reuses <see cref="IGameDetectionService"/> for engine detection.
/// </summary>
public interface ICustomFolderScanService
{
    /// <param name="roots">Configured custom folders (normalised or not; offline ones are reported, not failed).</param>
    /// <param name="existingPaths">Install paths of games already known (stores, manual, earlier custom imports).</param>
    /// <param name="reservedNames">Names that new candidates must not reuse (manual and custom games).</param>
    Task<CustomFolderScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> existingPaths,
        IReadOnlyCollection<string> reservedNames,
        CancellationToken cancellationToken = default);
}
