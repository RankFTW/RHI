using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches and parses the RenoDX wiki mod list.
/// </summary>
public interface IWikiService
{
    Task<(List<GameMod> Mods, Dictionary<string, string> GenericNotes)>
        FetchAllAsync(IProgress<string>? progress = null);

    Task<DateTime?> GetSnapshotLastModifiedAsync(string url);
}
