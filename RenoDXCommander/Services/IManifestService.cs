using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches, caches, and provides the remote game manifest.
/// </summary>
public interface IManifestService
{
    Task<RemoteManifest?> FetchAsync();

    RemoteManifest? LoadCached();
}
