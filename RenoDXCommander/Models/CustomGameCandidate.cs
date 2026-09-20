using RenoDXCommander.Services;

namespace RenoDXCommander.Models;

/// <summary>How sure the custom-folder scanner is that a discovered folder is a game.</summary>
public enum CustomCandidateConfidence
{
    /// <summary>Strong engine marker or several supporting signals — safe to import automatically.</summary>
    High,
    /// <summary>Plausible but ambiguous — only imported after the user reviews it.</summary>
    Low,
}

/// <summary>
/// A game folder discovered inside a user-configured custom game folder.
/// Not yet part of the library; becomes a <see cref="DetectedGame"/> with
/// <see cref="CustomGameMerger.SourceName"/> as its source once imported.
/// </summary>
public class CustomGameCandidate
{
    /// <summary>Inferred display name (folder name, disambiguated when it would collide).</summary>
    public string Name { get; set; } = "";

    /// <summary>Game root folder. This is what enters the normal pipeline as the install path.</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>The configured custom folder this candidate was found under.</summary>
    public string ScanRoot { get; set; } = "";

    /// <summary>Executable chosen as the game's main executable (null if none was usable).</summary>
    public string? ExePath { get; set; }

    public EngineType Engine { get; set; } = EngineType.Unknown;
    public GraphicsApiType GraphicsApi { get; set; } = GraphicsApiType.Unknown;
    public MachineType Bitness { get; set; } = MachineType.Native;
    public CustomCandidateConfidence Confidence { get; set; } = CustomCandidateConfidence.Low;

    /// <summary>Short human-readable reason for the confidence rating (logged / shown as tooltip).</summary>
    public string Reason { get; set; } = "";

    public DetectedGame ToDetectedGame() => new()
    {
        Name = Name,
        InstallPath = InstallPath,
        Source = CustomGameMerger.SourceName,
    };
}
