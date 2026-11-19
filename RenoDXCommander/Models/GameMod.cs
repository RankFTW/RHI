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
    /// <summary>The shared RenoDX Source engine addon (games in RenoDXdb-source.json); installs with the DXVK proxy setup.</summary>
    public bool IsGenericSource { get; set; }
    // Optional URL found in the game name cell on the wiki (points to per-game instructions)
    public string? NameUrl { get; set; }

    // A .zip snapshot (addon + helper files) installs the addon inside it: name that, not the zip
    [JsonIgnore] public string? AddonFileName   => NameFor(SnapshotUrl);
    [JsonIgnore] public string? AddonFileName32  => NameFor(SnapshotUrl32);
    private static string? NameFor(string? url) => url == null ? null
        : RenoDXCommander.Services.RenoDxZipPackage.IsZip(url) ? RenoDXCommander.Services.RenoDxZipPackage.AddonNameFor(Path.GetFileName(url))
        : Path.GetFileName(url);
    [JsonIgnore] public bool HasBothBitVersions  => SnapshotUrl != null && SnapshotUrl32 != null;
}
