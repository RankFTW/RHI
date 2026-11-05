using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>What the custom game folders scan needs to know about the library (a snapshot).</summary>
public sealed class CustomFolderScanRequest
{
    /// <summary>Custom folders from settings (absolute paths).</summary>
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();

    /// <summary>Candidate folders the user ignored (normalized paths, case-insensitive).</summary>
    public IReadOnlySet<string> IgnoredFolders { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Install paths of games already in the library (cards and manual games).</summary>
    public IReadOnlyList<string> LibraryPaths { get; init; } = Array.Empty<string>();

    /// <summary>Normalized names of games already in the library, plus games removed from RHI.</summary>
    public IReadOnlySet<string> KnownNames { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Exact (normalized) title lookup: wiki mods, then PCGW. Returns null when unknown.</summary>
    public Func<string, string?>? CanonicalTitle { get; init; }
}

/// <summary>Finds games in the user's custom game folders that RHI doesn't know about yet.</summary>
public interface ICustomFolderScanService
{
    /// <summary>Scans every folder off the UI thread. Never throws for one bad folder; logs and moves on.</summary>
    Task<IReadOnlyList<CustomGameCandidate>> ScanAsync(CustomFolderScanRequest request, CancellationToken ct);
}
