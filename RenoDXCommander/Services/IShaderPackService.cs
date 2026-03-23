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

    Task EnsureLatestAsync(IProgress<string>? progress = null);

    void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null);

    void RemoveFromGameFolder(string gameDir);

    bool IsManagedByRdxc(string gameDir);

    void RestoreOriginalIfPresent(string gameDir);

    void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null);

    void SyncShadersToAllLocations(
        IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations,
        IEnumerable<string>? selectedPackIds = null);
}
