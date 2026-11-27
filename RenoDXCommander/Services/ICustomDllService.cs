using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// The user's own single-file DLLs (e.g. Special K) in <c>%LocalAppData%\RHI\Custom\DLLs\</c>, copied into
/// games under a name the user picks and kept up to date when a newer file is dropped into the folder.
/// </summary>
public interface ICustomDllService
{
    /// <summary>The custom DLL folder (created on first use).</summary>
    string Folder { get; }

    /// <summary>Every *.dll at the top level of the folder, with its bitness and whether it can be used.</summary>
    IReadOnlyList<CustomDllSource> ListSources();

    /// <summary>The custom DLLs recorded for a game (copies; empty when none).</summary>
    IReadOnlyList<CustomDllRecord> GetRecords(string gameName, string store);

    /// <summary>
    /// Copies <paramref name="source"/> into the game as <paramref name="targetName"/>. <paramref name="otherOwners"/> =
    /// DLL names other RHI components hold in the game (refused). Returns null on success, else why nothing was changed.
    /// </summary>
    string? Install(CustomDllGame game, string source, string targetName, IReadOnlyDictionary<string, string>? otherOwners = null);

    /// <summary>
    /// Removes <paramref name="source"/> from the game. Returns null on success, a note starting with "Note:" when
    /// the record was dropped but a file was left (changed outside RHI), or an error (nothing changed).
    /// </summary>
    string? Remove(CustomDllGame game, string source);

    /// <summary>The state of each record (hashes files: call off the UI thread).</summary>
    IReadOnlyList<(CustomDllRecord Record, CustomDllState State)> GetStates(string gameName, string store);

    /// <summary>
    /// Copies newer folder files over RHI's own copies in <paramref name="games"/> (hidden and running games skipped,
    /// hand-replaced copies left, deleted copies never re-created). Logs one summary line.
    /// </summary>
    CustomDllRedeployResult CheckAndRedeploy(IEnumerable<CustomDllGame> games, string caller);

    /// <summary>Moves records from a renamed game (only those at <paramref name="installPath"/> when given).</summary>
    int RenameRecords(string oldName, string newName, string? installPath = null);

    /// <summary>Points records of <paramref name="gameName"/> at <paramref name="oldPath"/> to <paramref name="newPath"/> (folder moves).</summary>
    int RepointRecords(string gameName, string oldPath, string newPath);

    /// <summary>Watches the folder; <paramref name="onChanged"/> runs about 2 s after the last change. Safe to call twice.</summary>
    void StartWatching(Action onChanged);
}
