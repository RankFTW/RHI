using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Result of a FetchAllAsync call. Contains named mods, UE-Extended entries,
/// Unity entries and Source entries fetched from the four RenoDXdb JSON files.
/// </summary>
public record DbFetchResult(
    List<GameMod> Mods,
    Dictionary<string, RenoDXDbUnrealEntry> UnrealEntries,
    Dictionary<string, RenoDXDbUnityEntry> UnityEntries,
    Dictionary<string, RenoDXDbSourceEntry> SourceEntries);

public interface IRenoDXDbService
{
    /// <summary>
    /// Fetches RenoDXdb.json (named mods), RenoDXdb-unreal.json (UE-Extended games),
    /// RenoDXdb-unity.json (Unity games) and RenoDXdb-source.json (Source engine games).
    /// Uses ETag caching — returns cached data on 304 Not Modified.
    /// </summary>
    Task<DbFetchResult> FetchAllAsync();

    /// <summary>Clears the ETag cache for all DB URLs so the next fetch is unconditional.</summary>
    void InvalidateCache();
}
