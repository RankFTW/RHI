using System.Text.Json.Serialization;

namespace RHI.ManifestEditor.Models;

public class RemoteManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    // ── Game Detection ────────────────────────────────────────────────────────
    [JsonPropertyName("blacklist")]
    public List<string>? Blacklist { get; set; }

    [JsonPropertyName("blacklistPrefixes")]
    public List<string>? BlacklistPrefixes { get; set; }

    [JsonPropertyName("wikiUnlinks")]
    public List<string>? WikiUnlinks { get; set; }

    [JsonPropertyName("wikiNameOverrides")]
    public Dictionary<string, string>? WikiNameOverrides { get; set; }

    [JsonPropertyName("lumaNameOverrides")]
    public Dictionary<string, string>? LumaNameOverrides { get; set; }

    [JsonPropertyName("installPathOverrides")]
    public Dictionary<string, string>? InstallPathOverrides { get; set; }

    [JsonPropertyName("splitGames")]
    public Dictionary<string, List<SplitGameEntry>>? SplitGames { get; set; }

    [JsonPropertyName("emulatorGames")]
    public Dictionary<string, EmulatorConfig>? EmulatorGames { get; set; }

    [JsonPropertyName("dlssSkipGames")]
    public List<string>? DlssSkipGames { get; set; }

    [JsonPropertyName("steamAppIdOverrides")]
    public Dictionary<string, int>? SteamAppIdOverrides { get; set; }

    // ── Engine & Graphics API ─────────────────────────────────────────────────
    [JsonPropertyName("engineOverrides")]
    public Dictionary<string, string>? EngineOverrides { get; set; }

    [JsonPropertyName("engineHintOverrides")]
    public Dictionary<string, string>? EngineHintOverrides { get; set; }

    [JsonPropertyName("engineIniPathOverrides")]
    public Dictionary<string, string>? EngineIniPathOverrides { get; set; }

    [JsonPropertyName("graphicsApiOverrides")]
    public Dictionary<string, string>? GraphicsApiOverrides { get; set; }

    [JsonPropertyName("thirtyTwoBitGames")]
    public List<string>? ThirtyTwoBitGames { get; set; }

    [JsonPropertyName("sixtyFourBitGames")]
    public List<string>? SixtyFourBitGames { get; set; }

    [JsonPropertyName("pdUpscalerGames")]
    public Dictionary<string, string>? PdUpscalerGames { get; set; }

    // ── UE-Extended / HDR ─────────────────────────────────────────────────────
    [JsonPropertyName("nativeHdrGames")]
    public List<string>? NativeHdrGames { get; set; }

    [JsonPropertyName("ueExtendedCompatibility")]
    public Dictionary<string, UeExtendedCompatEntry>? UeExtendedCompatibility { get; set; }

    [JsonPropertyName("ueExtendedGames")]
    public List<string>? UeExtendedGames { get; set; }

    [JsonPropertyName("noUeExtendedGames")]
    public List<string>? NoUeExtendedGames { get; set; }

    [JsonPropertyName("lumaRenodxCompat")]
    public List<string>? LumaRenodxCompat { get; set; }

    [JsonPropertyName("lumaDefaultGames")]
    public List<string>? LumaDefaultGames { get; set; }

    [JsonPropertyName("lumaRequiresDgVoodoo")]
    public List<string>? LumaRequiresDgVoodoo { get; set; }

    [JsonPropertyName("dgVoodooVersions")]
    public Dictionary<string, string>? DgVoodooVersions { get; set; }

    // ── Install Behaviour ─────────────────────────────────────────────────────
    [JsonPropertyName("installWarnings")]
    public Dictionary<string, Dictionary<string, string>>? InstallWarnings { get; set; }

    [JsonPropertyName("forceExternalOnly")]
    public Dictionary<string, ForceExternalEntry>? ForceExternalOnly { get; set; }

    [JsonPropertyName("snapshotOverrides")]
    public Dictionary<string, string>? SnapshotOverrides { get; set; }

    [JsonPropertyName("dllNameOverrides")]
    public Dictionary<string, ManifestDllNames>? DllNameOverrides { get; set; }

    [JsonPropertyName("optiScalerDllOverrides")]
    public Dictionary<string, string>? OptiScalerDllOverrides { get; set; }

    [JsonPropertyName("gacSymlinkGames")]
    public Dictionary<string, string>? GacSymlinkGames { get; set; }

    [JsonPropertyName("launchExeOverrides")]
    public Dictionary<string, string>? LaunchExeOverrides { get; set; }

    [JsonPropertyName("renodxIniOverrides")]
    public Dictionary<string, Dictionary<string, string>>? RenodxIniOverrides { get; set; }

    /// <summary>
    /// Maps game name → filename of a custom Engine.ini file hosted in the rhi-repo engine-files/ folder.
    /// When present, the file is fetched and merged into the game's Engine.ini instead of the standard HDR keys.
    /// Multiple games can point to the same filename. Any valid Engine.ini key/section content is supported.
    /// </summary>
    [JsonPropertyName("engineIniFiles")]
    public Dictionary<string, string>? EngineIniFiles { get; set; }

    [JsonPropertyName("legacyReShadeVersions")]
    public Dictionary<string, string>? LegacyReShadeVersions { get; set; }

    [JsonPropertyName("legacyReShadeAvailable")]
    public List<string>? LegacyReShadeAvailable { get; set; }

    // ── Per-Game Content & Notes ──────────────────────────────────────────────
    [JsonPropertyName("gameNotes")]
    public Dictionary<string, GameNoteEntry>? GameNotes { get; set; }

    [JsonPropertyName("reshadeGameInfo")]
    public Dictionary<string, GameNoteEntry>? ReshadeGameInfo { get; set; }

    [JsonPropertyName("relimiterGameInfo")]
    public Dictionary<string, GameNoteEntry>? RelimiterGameInfo { get; set; }

    [JsonPropertyName("displayCommanderGameInfo")]
    public Dictionary<string, GameNoteEntry>? DisplayCommanderGameInfo { get; set; }

    [JsonPropertyName("reframeworkGameInfo")]
    public Dictionary<string, GameNoteEntry>? ReframeworkGameInfo { get; set; }

    [JsonPropertyName("optiScalerGameInfo")]
    public Dictionary<string, GameNoteEntry>? OptiScalerGameInfo { get; set; }

    [JsonPropertyName("lumaGameInfo")]
    public Dictionary<string, GameNoteEntry>? LumaGameInfo { get; set; }

    [JsonPropertyName("lumaGameNotes")]
    public Dictionary<string, GameNoteEntry>? LumaGameNotes { get; set; }

    // ── DXVK ─────────────────────────────────────────────────────────────────
    [JsonPropertyName("dxvkBlacklist")]
    public List<string>? DxvkBlacklist { get; set; }

    [JsonPropertyName("dxvkGameNotes")]
    public Dictionary<string, GameNoteEntry>? DxvkGameNotes { get; set; }

    [JsonPropertyName("dxvkApiOverrides")]
    public Dictionary<string, string>? DxvkApiOverrides { get; set; }

    // ── Wiki Status ───────────────────────────────────────────────────────────
    [JsonPropertyName("wikiStatusOverrides")]
    public Dictionary<string, string>? WikiStatusOverrides { get; set; }

    // ── Authors & URLs ────────────────────────────────────────────────────────
    [JsonPropertyName("authorDisplayNames")]
    public Dictionary<string, string>? AuthorDisplayNames { get; set; }

    [JsonPropertyName("donationUrls")]
    public Dictionary<string, string>? DonationUrls { get; set; }

    [JsonPropertyName("authorOverrides")]
    public Dictionary<string, string>? AuthorOverrides { get; set; }

    [JsonPropertyName("nexusUrlOverrides")]
    public Dictionary<string, string>? NexusUrlOverrides { get; set; }

    [JsonPropertyName("pcgwUrlOverrides")]
    public Dictionary<string, string>? PcgwUrlOverrides { get; set; }

    [JsonPropertyName("pcgwUseAppId")]
    public bool? PcgwUseAppId { get; set; }

    [JsonPropertyName("pcgwUrlCacheVersion")]
    public int? PcgwUrlCacheVersion { get; set; }

    [JsonPropertyName("uwFixUrlOverrides")]
    public Dictionary<string, string>? UwFixUrlOverrides { get; set; }

    [JsonPropertyName("ultraPlusUrlOverrides")]
    public Dictionary<string, string>? UltraPlusUrlOverrides { get; set; }

    [JsonPropertyName("optiScalerWikiNames")]
    public Dictionary<string, string>? OptiScalerWikiNames { get; set; }

    [JsonPropertyName("rtxHdrInfoUrl")]
    public string? RtxHdrInfoUrl { get; set; }

    // ── NVIDIA / DLSS ─────────────────────────────────────────────────────────
    [JsonPropertyName("profileNameOverrides")]
    public Dictionary<string, string>? ProfileNameOverrides { get; set; }

    [JsonPropertyName("profileExeExclusions")]
    public List<string>? ProfileExeExclusions { get; set; }

    [JsonPropertyName("dlssPresets")]
    public ManifestDlssPresets? DlssPresets { get; set; }

    [JsonPropertyName("dlssPresetsDev")]
    public ManifestDlssPresets? DlssPresetsDev { get; set; }

    // ── DOF Fix ───────────────────────────────────────────────────────────────
    [JsonPropertyName("dofFixForceGames")]
    public List<string>? DofFixForceGames { get; set; }

    [JsonPropertyName("dofFixSkipGames")]
    public List<string>? DofFixSkipGames { get; set; }

    // ── Packs & Component URLs ────────────────────────────────────────────────
    [JsonPropertyName("shaderPacks")]
    public Dictionary<string, ManifestShaderPack>? ShaderPacks { get; set; }

    [JsonPropertyName("addonPacks")]
    public Dictionary<string, ManifestAddonPack>? AddonPacks { get; set; }

    [JsonPropertyName("componentUrls")]
    public Dictionary<string, string>? ComponentUrls { get; set; }

    [JsonPropertyName("renodxExtraSettings")]
    public List<RenodxExtraSetting>? RenodxExtraSettings { get; set; }

    [JsonPropertyName("featureFlags")]
    public ManifestFeatureFlags? FeatureFlags { get; set; }
}

public class SplitGameEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("subPath")]
    public string SubPath { get; set; } = "";
}

public class EmulatorConfig
{
    [JsonPropertyName("addons")]
    public List<string> Addons { get; set; } = new();

    [JsonPropertyName("addonUrls")]
    public Dictionary<string, string>? AddonUrls { get; set; }
}

public class UeExtendedCompatEntry
{
    [JsonPropertyName("hdr")]
    public bool? Hdr { get; set; }

    [JsonPropertyName("lut")]
    public bool? Lut { get; set; }
}

public class ManifestDlssPresets
{
    [JsonPropertyName("sr")]
    public List<ManifestPresetEntry>? Sr { get; set; }

    [JsonPropertyName("rr")]
    public List<ManifestPresetEntry>? Rr { get; set; }

    [JsonPropertyName("fg")]
    public List<ManifestPresetEntry>? Fg { get; set; }

    [JsonPropertyName("nr")]
    public List<ManifestPresetEntry>? Nr { get; set; }
}

public class ManifestPresetEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }
}

public class RenodxExtraSetting
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("default")]
    public string Default { get; set; } = "0";

    [JsonPropertyName("options")]
    public List<RenodxExtraOption>? Options { get; set; }
}

public class RenodxExtraOption
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "0";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class ManifestShaderPack
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("isMinimum")]
    public bool? IsMinimum { get; set; }

    [JsonPropertyName("assetExt")]
    public string? AssetExt { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("requires")]
    public string[]? Requires { get; set; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }
}

public class ManifestAddonPack
{
    [JsonPropertyName("packageName")]
    public string? PackageName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("downloadUrl32")]
    public string? DownloadUrl32 { get; set; }

    [JsonPropertyName("downloadUrl64")]
    public string? DownloadUrl64 { get; set; }

    [JsonPropertyName("repositoryUrl")]
    public string? RepositoryUrl { get; set; }

    [JsonPropertyName("effectInstallPath")]
    public string? EffectInstallPath { get; set; }

    [JsonPropertyName("deployFileName")]
    public string? DeployFileName { get; set; }

    [JsonPropertyName("releaseApiUrl")]
    public string? ReleaseApiUrl { get; set; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }
}

public class ManifestFeatureFlags
{
    [JsonPropertyName("dlssNr")]
    public bool? DlssNr { get; set; }

    [JsonPropertyName("nexusMods")]
    public bool? NexusMods { get; set; }

    [JsonPropertyName("resolutionControl")]
    public bool? ResolutionControl { get; set; }
}
