using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

public class RemoteManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("wikiNameOverrides")]
    public Dictionary<string, string>? WikiNameOverrides { get; set; }

    [JsonPropertyName("ueExtendedGames")]
    public List<string>? UeExtendedGames { get; set; }

    [JsonPropertyName("nativeHdrGames")]
    public List<string>? NativeHdrGames { get; set; }

    [JsonPropertyName("blacklist")]
    public List<string>? Blacklist { get; set; }

    [JsonPropertyName("thirtyTwoBitGames")]
    public List<string>? ThirtyTwoBitGames { get; set; }

    [JsonPropertyName("sixtyFourBitGames")]
    public List<string>? SixtyFourBitGames { get; set; }

    [JsonPropertyName("gameNotes")]
    public Dictionary<string, GameNoteEntry>? GameNotes { get; set; }

    [JsonPropertyName("forceExternalOnly")]
    public Dictionary<string, ForceExternalEntry>? ForceExternalOnly { get; set; }

    [JsonPropertyName("installPathOverrides")]
    public Dictionary<string, string>? InstallPathOverrides { get; set; }

    [JsonPropertyName("wikiStatusOverrides")]
    public Dictionary<string, string>? WikiStatusOverrides { get; set; }

    /// <summary>
    /// Per-game snapshot URL overrides. When a game's matched mod has no SnapshotUrl
    /// (or the wiki parser fails to capture it), this provides a direct download URL.
    /// Key = game name, Value = direct addon download URL.
    /// </summary>
    [JsonPropertyName("snapshotOverrides")]
    public Dictionary<string, string>? SnapshotOverrides { get; set; }

    /// <summary>
    /// Games that should default to Luma mode when first detected.
    /// If the user has never toggled Luma for the game, it will be auto-enabled.
    /// </summary>
    [JsonPropertyName("lumaDefaultGames")]
    public List<string>? LumaDefaultGames { get; set; }

    /// <summary>
    /// Custom notes for games in Luma mode (shown in the info dialog when Luma is active).
    /// Supplements or replaces wiki-provided LumaMod notes.
    /// </summary>
    [JsonPropertyName("lumaGameNotes")]
    public Dictionary<string, GameNoteEntry>? LumaGameNotes { get; set; }

    /// <summary>
    /// Games in this list are unlinked from any fuzzy wiki match.
    /// They will fall through to the generic engine addon (Unreal or Unity)
    /// instead of being incorrectly associated with a named wiki mod.
    /// </summary>
    [JsonPropertyName("wikiUnlinks")]
    public List<string>? WikiUnlinks { get; set; }

    /// <summary>
    /// Per-game engine overrides. Allows the manifest to force a specific engine label
    /// for a game, overriding auto-detection.
    /// 
    /// Special values that affect filtering and mod behaviour:
    ///   "Unreal"         → treated as Unreal Engine 4/5 (filters into Unreal, eligible for UE-Extended)
    ///   "Unreal (Legacy)"→ treated as Unreal Engine 3 (filters into Unreal)
    ///   "Unity"          → treated as Unity (filters into Unity, eligible for generic Unity addon)
    /// 
    /// Any other string (e.g. "Silk", "Source 2", "Creation Engine") is stored as-is and
    /// displayed in the engine badge. The game filters into Other, not Unreal or Unity.
    /// Key = game name, Value = engine label string.
    /// </summary>
    [JsonPropertyName("engineOverrides")]
    public Dictionary<string, string>? EngineOverrides { get; set; }

    /// <summary>
    /// Per-game DLL filename overrides. Allows the manifest to remotely set the filename
    /// that ReShade and Display Commander are installed as for specific games.
    /// Key = game name, Value = object with "reshade" and/or "dc" filename strings.
    /// Either field may be empty/null — an empty string means that file keeps its default name.
    /// Example: "Mirror's Edge": { "reshade": "d3d9.dll", "dc": "winmm.dll" }
    /// </summary>
    [JsonPropertyName("dllNameOverrides")]
    public Dictionary<string, ManifestDllNames>? DllNameOverrides { get; set; }

    /// <summary>
    /// Per-game OptiScaler DLL filename overrides. When a game requires a specific
    /// proxy DLL name for OptiScaler (e.g. games where dxgi.dll conflicts with
    /// another tool), this provides a direct mapping.
    /// Key = game name, Value = DLL filename string (e.g. "winmm.dll").
    /// </summary>
    [JsonPropertyName("optiScalerDllOverrides")]
    public Dictionary<string, string>? OptiScalerDllOverrides { get; set; }

    /// <summary>
    /// Per-game graphics API overrides. Allows the manifest to force a specific
    /// graphics API badge for games where auto-detection fails (e.g. games that
    /// load DirectX entirely at runtime with no static PE imports).
    /// Key = game name, Value = API string or comma-separated list.
    /// Single: "DX12", "Vulkan", "OpenGL"
    /// Multi:  "DX12, VLK" (marks the game as dual-API)
    /// Valid tokens: "DX8","DX9","DX10","DX11","DX12","Vulkan","VLK","OpenGL","OGL".
    /// </summary>
    [JsonPropertyName("graphicsApiOverrides")]
    public Dictionary<string, string>? GraphicsApiOverrides { get; set; }

    /// <summary>
    /// Author donation URLs keyed by display name.
    /// Merged into the hardcoded dictionary at startup — manifest entries
    /// take priority so links can be added/updated without a new build.
    /// </summary>
    [JsonPropertyName("donationUrls")]
    public Dictionary<string, string>? DonationUrls { get; set; }

    /// <summary>
    /// Games that require ReShade to be symlinked into a GAC (Global Assembly Cache)
    /// directory instead of the game folder. Used for XNA Framework games like Terraria
    /// where the graphics DLL is loaded from a system directory.
    /// Key = game name, Value = the GAC directory path where symlinks should be created.
    /// The reshade.ini will have [INSTALL] BasePath set to the game's install directory.
    /// Requires admin privileges for symlink creation.
    /// </summary>
    [JsonPropertyName("gacSymlinkGames")]
    public Dictionary<string, string>? GacSymlinkGames { get; set; }

    /// <summary>
    /// Author display-name overrides keyed by wiki maintainer handle.
    /// Merged into the hardcoded dictionary at startup.
    /// Example: { "oopydoopy": "Jon" }
    /// </summary>
    [JsonPropertyName("authorDisplayNames")]
    public Dictionary<string, string>? AuthorDisplayNames { get; set; }

    /// <summary>
    /// Per-game Nexus Mods URL overrides. When automatic name matching fails,
    /// this provides a direct mapping from game name to Nexus Mods page URL.
    /// Key = game name, Value = Nexus Mods URL string.
    /// </summary>
    [JsonPropertyName("nexusUrlOverrides")]
    public Dictionary<string, string>? NexusUrlOverrides { get; set; }

    /// <summary>
    /// Per-game Steam AppID overrides. When automatic AppID resolution fails,
    /// this provides a direct mapping from game name to Steam AppID.
    /// Key = game name, Value = integer Steam AppID.
    /// </summary>
    [JsonPropertyName("steamAppIdOverrides")]
    public Dictionary<string, int>? SteamAppIdOverrides { get; set; }

    /// <summary>
    /// Per-game PCGW URL overrides. When automatic PCGW resolution fails or
    /// resolves incorrectly, this provides a direct mapping from game name to
    /// the correct PCGamingWiki page URL.
    /// Key = game name, Value = PCGW URL string.
    /// </summary>
    [JsonPropertyName("pcgwUrlOverrides")]
    public Dictionary<string, string>? PcgwUrlOverrides { get; set; }

    /// <summary>
    /// Per-game author overrides. Sets the mod author for games that have no wiki entry
    /// but have a known mod author (e.g. mods distributed via Discord or Nexus only).
    /// The author name is displayed as a badge with a donation link if available.
    /// Key = game name, Value = author display name (or "&amp;"-separated for multiple).
    /// </summary>
    [JsonPropertyName("authorOverrides")]
    public Dictionary<string, string>? AuthorOverrides { get; set; }
}
