using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Result of a FetchAllAsync call. Contains named mods, UE-Extended entries,
/// and Unity entries fetched from the three RenoDXdb JSON files.
/// </summary>
public record DbFetchResult(
    List<GameMod> Mods,
    Dictionary<string, RenoDXDbUnrealEntry> UnrealEntries,
    Dictionary<string, RenoDXDbUnityEntry> UnityEntries);

public interface IRenoDXDbService
{
    /// <summary>
    /// Fetches RenoDXdb.json (named mods), RenoDXdb-unreal.json (UE-Extended games),
    /// and RenoDXdb-unity.json (Unity games).
    /// Uses ETag caching — returns cached data on 304 Not Modified.
    /// </summary>
    Task<DbFetchResult> FetchAllAsync();

    /// <summary>Clears the ETag cache for all DB URLs so the next fetch is unconditional.</summary>
    void InvalidateCache();

    /// <summary>Named mod list from the last successful fetch. Empty until FetchAllAsync completes.</summary>
    IReadOnlyList<GameMod> CachedMods { get; }

    /// <summary>UE-Extended entry dict from the last successful fetch. Empty until FetchAllAsync completes.</summary>
    IReadOnlyDictionary<string, RenoDXDbUnrealEntry> CachedUnrealEntries { get; }

    /// <summary>Unity entry dict from the last successful fetch. Empty until FetchAllAsync completes.</summary>
    IReadOnlyDictionary<string, RenoDXDbUnityEntry> CachedUnityEntries { get; }
}
