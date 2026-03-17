namespace RenoDXCommander.Services;

/// <summary>
/// Downloads, extracts, and deploys HDR ReShade shader packs.
/// </summary>
public interface IShaderPackService
{
    /// <summary>
    /// Exposes pack metadata for the picker UI — returns every known pack's Id and DisplayName.
    /// </summary>
    IReadOnlyList<(string Id, string DisplayName)> AvailablePacks { get; }

    /// <summary>
    /// Returns the short description for a pack, or null if none is set.
    /// </summary>
    string? GetPackDescription(string packId);

    Task EnsureLatestAsync(IProgress<string>? progress = null);

    void DeployToDcFolder(ShaderPackService.DeployMode? mode = null);

    void DeployToGameFolder(string gameDir, ShaderPackService.DeployMode? mode = null);

    void RemoveFromGameFolder(string gameDir);

    bool IsManagedByRdxc(string gameDir);

    void RestoreOriginalIfPresent(string gameDir);

    void SyncDcFolder(ShaderPackService.DeployMode m, IEnumerable<string>? selectedPackIds = null);

    void SyncGameFolder(string gameDir, ShaderPackService.DeployMode m, IEnumerable<string>? selectedPackIds = null);

    void SyncShadersToAllLocations(
        IEnumerable<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode, string? shaderModeOverride)> locations,
        ShaderPackService.DeployMode? mode = null,
        IEnumerable<string>? selectedPackIds = null);
}
