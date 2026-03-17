using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

public class GameMod
{
    public string Name { get; set; } = "";
    public string Maintainer { get; set; } = "";
    public string? SnapshotUrl { get; set; }
    public string? SnapshotUrl32 { get; set; }
    public string? NexusUrl { get; set; }
    public string? DiscordUrl { get; set; }
    public string Status { get; set; } = "✅";
    public string? Notes { get; set; }
    public bool IsGenericUnreal { get; set; }
    public bool IsGenericUnity { get; set; }
    // Optional URL found in the game name cell on the wiki (points to per-game instructions)
    public string? NameUrl { get; set; }

    [JsonIgnore] public string? AddonFileName   => SnapshotUrl   != null ? Path.GetFileName(SnapshotUrl)   : null;
    [JsonIgnore] public string? AddonFileName32  => SnapshotUrl32 != null ? Path.GetFileName(SnapshotUrl32) : null;
    [JsonIgnore] public bool HasBothBitVersions  => SnapshotUrl != null && SnapshotUrl32 != null;
}
