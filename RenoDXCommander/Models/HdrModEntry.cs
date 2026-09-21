namespace RenoDXCommander.Models;

/// <summary>
/// A single row in the Available HDR Mods dialog.
/// Represents a game that has a RenoDX mod, a UE-Extended entry, a Luma mod, or any combination.
/// </summary>
public record HdrModEntry(
    string  Name,
    /// <summary>"Done", "WIP", or null (not available).</summary>
    string? RenoDXStatus,
    string? RenoDXUrl,
    /// <summary>"Done", "WIP", or null (not available).</summary>
    string? LumaStatus,
    string? LumaUrl)
{
    public bool HasRenoDX => RenoDXStatus != null;
    public bool HasLuma   => LumaStatus   != null;
}
