// CustomGameCandidate.cs -- a game folder found in one of the user's custom game folders
// (Settings › Tools & Maintenance › Custom game folders), offered to the user before it's added.

namespace RenoDXCommander.Models;

/// <summary>One exe inside a candidate folder, with the score the exe picker gave it.</summary>
public sealed record ScoredExe(string FullPath, string RelativePath, long SizeBytes, int Score, string Reasons);

/// <summary>A folder that looks like a game RHI doesn't know about yet.</summary>
public sealed class CustomGameCandidate
{
    /// <summary>The game's root folder (a direct subfolder of the custom folder, or the custom folder itself).</summary>
    public string FolderPath { get; init; } = "";

    /// <summary>The custom folder (from settings) this candidate was found in.</summary>
    public string CustomFolder { get; init; } = "";

    /// <summary>Suggested display name (cleaned folder name, or the matching wiki / PCGW title).</summary>
    public string SuggestedName { get; init; } = "";

    public EngineType Engine { get; init; } = EngineType.Unknown;

    /// <summary>Exes ranked best first. Never empty for an offered candidate.</summary>
    public IReadOnlyList<ScoredExe> Exes { get; init; } = Array.Empty<ScoredExe>();

    /// <summary>True when the top two exes scored close together, so the user should check the pick.</summary>
    public bool IsAmbiguous { get; init; }
}
