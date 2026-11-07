namespace RenoDXCommander.Services;

/// <summary>
/// Moves a game's RHI installs (files, settings files, presets, records) from its old install
/// folder to a new one when the folder changes: manifest installPathMigrations / installPathOverrides,
/// Change install folder, Reset install folder.
/// </summary>
public interface IInstallFolderMigrationService
{
    /// <summary>What would move, without touching anything.</summary>
    FolderMovePlan BuildPlan(FolderMigrationRequest request);

    /// <summary>True when the old folder holds RHI installs for this game.</summary>
    bool HasInstalls(FolderMigrationRequest request);

    /// <summary>
    /// Moves everything (all-or-nothing) and repoints the install records and addon tracker.
    /// Nothing is moved when the game is running, a file is locked or a folder isn't writable.
    /// Blocking file work: call from a background thread.
    /// </summary>
    FolderMigrationOutcome Migrate(FolderMigrationRequest request);
}

/// <summary>Per-game facts the mover needs that live in settings rather than in install records.</summary>
public sealed class FolderMigrationRequest
{
    public required string GameName { get; init; }
    public string Store { get; init; } = "";
    public required string OldDir { get; init; }
    public required string NewDir { get; init; }
    /// <summary>The game's root (store folder), for the "game is running" check. Null = old and new folders only.</summary>
    public string? GameRoot { get; init; }
    /// <summary>GAC-symlink games: ReShade isn't in the game folder, leave its record alone.</summary>
    public bool SkipReShade { get; init; }
    /// <summary>Emulator bundles (several RenoDX addons on one card): their records aren't moved.</summary>
    public bool IsEmulator { get; init; }
    public string? RtxEncoreInstalledAs { get; init; }
    public bool RtxEncoreNrDllDeployed { get; init; }
    public string? Rtx40MfgInstalledAs { get; init; }
    public string? Dlssg2030InstalledAs { get; init; }
    public string? DeInstalledAs { get; init; }
    public bool DgVoodooStandalone { get; init; }
}

public sealed class FolderMigrationOutcome
{
    public bool Success { get; set; }
    /// <summary>The old folder had nothing of RHI's for this game.</summary>
    public bool NothingToMove { get; set; }
    /// <summary>Why nothing was moved (plain text, can be shown to the user).</summary>
    public string? Reason { get; set; }
    /// <summary>Component names that were moved or cleaned up, for notices and logs.</summary>
    public List<string> Components { get; } = new();
    /// <summary>A DLSS / NR DLL moved (the DLSS scan caches should forget the game).</summary>
    public bool MovedDlssFiles { get; set; }
    public FolderMoveResult? Result { get; set; }
}
