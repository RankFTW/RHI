namespace RenoDXCommander.Models;

/// <summary>
/// Represents a single emulator game entry from the manifest's emulatorGames section.
/// </summary>
public class EmulatorGameEntry
{
    public string Name { get; set; } = "";
    public string SnapshotUrl { get; set; } = "";
    public string Author { get; set; } = "";
}

/// <summary>
/// Metadata read from Ryubing's per-game metadata.json file.
/// </summary>
public class RyubingGameMetadata
{
    public string Title { get; set; } = "";
    public string TitleId { get; set; } = "";
    public bool Favorite { get; set; }
    public string? TimespanPlayed { get; set; }
    public string? LastPlayedUtc { get; set; }
}
