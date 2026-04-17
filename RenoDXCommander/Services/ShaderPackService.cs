// ShaderPackService.cs — Class declaration, constructor, path constants, pack definitions, enums, and ShaderPack record

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads, extracts and deploys HDR ReShade shader packs from multiple sources.
///
/// All packs are merged into a single shared staging tree:
///   %LocalAppData%\RenoDXCommander\reshade\Shaders\
///   %LocalAppData%\RenoDXCommander\reshade\Textures\
///
/// Each pack's extracted files are tracked individually. If a pack's cache zip is
/// deleted — or extracted files are missing from the staging folder — the pack is
/// re-downloaded and re-extracted on the next launch.
///
/// Source types:
///   GhRelease — GitHub Releases API, picks first matching asset extension
///   DirectUrl — Any static URL; versioned by ETag / Last-Modified header
/// </summary>
public partial class ShaderPackService : IShaderPackService
{
    private readonly HttpClient _http;

    public ShaderPackService(HttpClient http) => _http = http;
    // ── Public path constants (used by AuxInstallService) ─────────────────────────
    public static readonly string ShadersDir = Path.Combine(AuxInstallService.RsStagingDir, "Shaders");
    public static readonly string TexturesDir = Path.Combine(AuxInstallService.RsStagingDir, "Textures");

    // User-defined custom shaders — placed by the user, never auto-downloaded
    public const string CustomShaderSentinel = "__custom__";
    public static readonly string CustomDir = Path.Combine(AuxInstallService.RsStagingDir, "Custom");
    public static readonly string CustomShadersDir = Path.Combine(CustomDir, "Shaders");
    public static readonly string CustomTexturesDir = Path.Combine(CustomDir, "Textures");

    public const string GameReShadeShaders = "reshade-shaders";
    public const string GameReShadeOriginal = "reshade-shaders-original";
    private const string ManagedMarkerFile = "Managed by RDXC.txt";
    private const string ManagedMarkerContent = "This folder is managed by RenoDXCommander. Do not edit manually.\n"
                                                  + "Deleting this file will cause RDXC to treat the folder as user-managed.";

    // ── Pack definitions ──────────────────────────────────────────────────────────

    private enum SourceKind { GhRelease, DirectUrl }

    /// <summary>
    /// UI grouping for the shader picker dialog.
    /// Essential — always deployed, shown at the top.
    /// Recommended — suggested packs shown in the second group.
    /// Extra — everything else.
    /// </summary>
    public enum PackCategory { Essential, Recommended, Extra }

    /// <summary>
    /// Shader files that fail to compile and should never be extracted or deployed.
    /// Matched against the filename (leaf) of each archive entry, case-insensitive.
    /// </summary>
    private static readonly HashSet<string> ExcludedShaderFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "BX_XIV_ChromakeyPlus.fx",
        "GrainSpread.fx",
        "NTSCCustom.fx",
        "NTSC_XOT.fx",
    };

    private record ShaderPack(
        string Id,           // unique key — used in settings.json and cache filenames
        string DisplayName,  // shown in progress messages and logs
        SourceKind Kind,
        string Url,          // API url (GhRelease) or direct download url (DirectUrl)
        bool IsMinimum,    // true for packs included in the Lilium/minimum set
        string? AssetExt = null,  // GhRelease: required file extension of the release asset
        string? Description = null, // short description shown in the shader picker dialog
        PackCategory Category = PackCategory.Extra, // UI grouping
        string[]? Requires = null // IDs of packs that must also be selected when this pack is enabled
    );

    // Packs in order of download. IsMinimum=true → included in Minimum mode.
    private static readonly ShaderPack[] Packs =
    {
        new(
            Id          : "Lilium",
            DisplayName : "Lilium HDR Shaders",
            Kind        : SourceKind.GhRelease,
            Url         : "https://api.github.com/repos/EndlesslyFlowering/ReShade_HDR_shaders/releases/latest",
            IsMinimum   : true,
            AssetExt    : ".7z",
            Description : "HDR tone mapping and inverse tone mapping shaders",
            Category    : PackCategory.Essential
        ),
        new(
            Id          : "CrosireMaster",
            DisplayName : "crosire reshade-shaders (master)",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/crosire/reshade-shaders/archive/refs/heads/master.zip",
            IsMinimum   : true,
            Description : "Official ReShade standard effects — full master branch",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "PumboAutoHDR",
            DisplayName : "PumboAutoHDR",
            Kind        : SourceKind.GhRelease,
            Url         : "https://api.github.com/repos/Filoppi/PumboAutoHDR/releases/latest",
            IsMinimum   : false,
            AssetExt    : ".zip",
            Description : "Automatic HDR conversion for SDR games",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "SmolbbsoopShaders",
            DisplayName : "smolbbsoop shaders",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/smolbbsoop/smolbbsoopshaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "HDR utility shaders and effects",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "MaxG2DSimpleHDR",
            DisplayName : "MaxG2D Simple HDR Shaders",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/MaxG2D/ReshadeSimpleHDRShaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Simple HDR bloom, lens flare, and tone mapping",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "ClshortfuseShaders",
            DisplayName : "clshortfuse ReShade shaders",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/clshortfuse/reshade-shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "HDR and color correction shaders for RenoDX",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "PotatoFX",
            DisplayName : "potatoFX (CreepySasquatch)",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/CreepySasquatch/potatoFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Lightweight post-processing effects for low-end hardware",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "Azen",
            DisplayName : "Azen by Zenteon",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Zenteon/Azen/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Zenteon's casual shader collection — experimental effects",
            Requires    : new[] { "SmolbbsoopShaders" }
        ),
        new(
            Id          : "SweetFX",
            DisplayName : "SweetFX by CeeJay.dk",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/CeeJayDK/SweetFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Classic color grading, sharpening, and bloom effects"
        ),
        new(
            Id          : "OtisFX",
            DisplayName : "OtisFX by Otis_Inf",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/FransBouma/OtisFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Cinematic depth of field, light rays, and camera effects"
        ),
        new(
            Id          : "Depth3D",
            DisplayName : "Depth3D by BlueSkyDefender",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/BlueSkyDefender/Depth3D/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Stereoscopic 3D and depth-based visual effects"
        ),
        new(
            Id          : "DaodanShaders",
            DisplayName : "reshade-shaders by Daodan",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Daodan317081/reshade-shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Comic, crosshatch, and artistic style effects"
        ),
        new(
            Id          : "BrussellShaders",
            DisplayName : "Shaders by brussell",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/brussell1/Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Halftone, sketch, and stylized rendering effects"
        ),
        new(
            Id          : "FubaxShaders",
            DisplayName : "fubax-shaders by Fubaxiusz",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Fubaxiusz/fubax-shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "VR-friendly lens distortion and chromatic aberration"
        ),
        new(
            Id          : "qUINT",
            DisplayName : "qUINT by Marty McFly",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/martymcmodding/qUINT/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "MXAO, ADOF, lightroom, and screen-space reflections"
        ),
        new(
            Id          : "AlucardDH",
            DisplayName : "dh-reshade-shaders by AlucardDH",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/AlucardDH/dh-reshade-shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Ambient occlusion, undither, and color enhancement"
        ),
        new(
            Id          : "WarpFX",
            DisplayName : "Warp-FX by Radegast",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Radegast-FFXIV/Warp-FX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Screen warp, swirl, and distortion effects"
        ),
        new(
            Id          : "Prod80",
            DisplayName : "Color effects by prod80",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/prod80/prod80-ReShade-Repository/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Professional color grading, curves, and tone tools"
        ),
        new(
            Id          : "CorgiFX",
            DisplayName : "CorgiFX by originalnicodr",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/originalnicodr/CorgiFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Screenshot and virtual photography tools"
        ),
        new(
            Id          : "InsaneShaders",
            DisplayName : "Insane-Shaders by Lord of Lunacy",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/LordOfLunacy/Insane-Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Advanced dithering, fog removal, and edge detection"
        ),
        new(
            Id          : "CobraFX",
            DisplayName : "CobraFX by SirCobra",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/LordKobra/CobraFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Gravity, auto-focus, and real-time ray tracing effects"
        ),
        new(
            Id          : "AstrayFX",
            DisplayName : "AstrayFX by BlueSkyDefender",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/BlueSkyDefender/AstrayFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Depth-based fog, haze, and atmospheric effects"
        ),
        new(
            Id          : "CRTRoyale",
            DisplayName : "CRT-Royale-ReShade by akgunter",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/akgunter/crt-royale-reshade/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "CRT monitor simulation with phosphor and scanline emulation"
        ),
        new(
            Id          : "RSRetroArch",
            DisplayName : "RSRetroArch by Matsilagi",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Matsilagi/RSRetroArch/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "RetroArch shader ports — CRT, LCD, and retro filters"
        ),
        new(
            Id          : "VRToolkit",
            DisplayName : "VRToolkit by retroluxfilm",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/retroluxfilm/reshade-vrtoolkit/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Sharpening and clarity tools optimized for VR headsets"
        ),
        new(
            Id          : "FGFX",
            DisplayName : "FGFX by AlexTuduran",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/AlexTuduran/FGFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Film grain, multi-LUT, and cinematic post-processing"
        ),
        new(
            Id          : "CShade",
            DisplayName : "CShade by papadanku",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/papadanku/CShade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Optical flow, motion blur, and convolution effects"
        ),
        new(
            Id          : "iMMERSE",
            DisplayName : "iMMERSE by Marty McFly",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/martymcmodding/iMMERSE/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Next-gen RTGI, MXAO, and anti-aliasing suite"
        ),
        new(
            Id          : "VortShaders",
            DisplayName : "vort_Shaders by vortigern11",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/vortigern11/vort_Shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Sharpening, color correction, and depth effects"
        ),
        new(
            Id          : "BXShade",
            DisplayName : "BX-Shade by BarricadeMKXX",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/liuxd17thu/BX-Shade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Bloom, exposure, and color enhancement effects"
        ),
        new(
            Id          : "SHADERDECK",
            DisplayName : "SHADERDECK by TreyM",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/IAmTreyM/SHADERDECK/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Curated collection of color and lighting effects"
        ),
        new(
            Id          : "METEOR",
            DisplayName : "METEOR by Marty McFly",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/martymcmodding/METEOR/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Advanced denoiser and image reconstruction"
        ),
        new(
            Id          : "AnnReShade",
            DisplayName : "Ann-ReShade by Anastasia Bouwsma",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/AnastasiaGals/Ann-ReShade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Soft bloom, color grading, and ambient light presets"
        ),
        new(
            Id          : "ZenteonFX",
            DisplayName : "ZenteonFX Shaders by Zenteon",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Zenteon/ZenteonFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Global illumination, SSR, and path tracing effects"
        ),
        new(
            Id          : "GShadeShaders",
            DisplayName : "GShade-Shaders by Marot",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Mortalitas/GShade-Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Large collection of community shaders from GShade"
        ),
        new(
            Id          : "PthoFX",
            DisplayName : "Ptho-FX by PthoEastCoast",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/PthoEastCoast/Ptho-FX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Cinematic color grading and film emulation"
        ),
        new(
            Id          : "Anagrama",
            DisplayName : "The Anagrama Collection by nullfractal",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/nullfrctl/reshade-shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Artistic and experimental visual effects"
        ),
        new(
            Id          : "BarbatosShaders",
            DisplayName : "reshade-shaders by Barbatos",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/BarbatosBachiko/Reshade-Shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Ambient occlusion, bloom, and color effects"
        ),
        new(
            Id          : "BFBFX",
            DisplayName : "BFBFX by yaboi BFB",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/yplebedev/BFBFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Stylized and artistic post-processing effects"
        ),
        new(
            Id          : "Rendepth",
            DisplayName : "Rendepth by cybereality",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/outmode/rendepth-reshade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Depth-based 3D rendering and stereo effects"
        ),
        new(
            Id          : "CropAndResize",
            DisplayName : "Crop and Resize by P0NYSLAYSTATION",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/P0NYSLAYSTATION/Scaling-Shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Screen cropping, scaling, and aspect ratio tools"
        ),
        new(
            Id          : "FXShaders",
            DisplayName : "FXShaders by luluco250",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/luluco250/FXShaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Bloom, grain, dithering, and utility shader library"
        ),
        new(
            Id          : "LumeniteFX",
            DisplayName : "LumeniteFX by Kaido",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/umar-afzaal/LumeniteFX/archive/refs/heads/mainline.zip",
            IsMinimum   : false,
            Description : "Lighting, bloom, and atmospheric glow effects"
        ),
        new(
            Id          : "NNShaders",
            DisplayName : "NN-Shaders by Sarenya",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Sarenya/NN-Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Neural network-based image processing shaders"
        ),
        new(
            Id          : "QdOledAplFixer",
            DisplayName : "QD-OLED APL Fixer by mspeedo",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/mspeedo/QD-OLED-APL-FIXER/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "HDR brightness boost to compensate for QD-OLED ABL dimming"
        ),
        new(
            Id          : "GlamaryeFX",
            DisplayName : "Glamarye Fast Effects by rj200",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/rj200/Glamarye_Fast_Effects_for_ReShade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Lightweight all-in-one: sharpening, AO, indirect lighting, and color correction in a single pass"
        ),
    };

    // ── AvailablePacks ───────────────────────────────────────────────────────────

    /// <summary>
    /// Exposes pack metadata for the picker UI — returns every known pack's Id and DisplayName.
    /// </summary>
    public IReadOnlyList<(string Id, string DisplayName, PackCategory Category)> AvailablePacks { get; } =
        Packs.Select(p => (p.Id, p.DisplayName, p.Category)).ToList().AsReadOnly();

    /// <summary>
    /// Returns the short description for a pack, or null if none is set.
    /// </summary>
    public string? GetPackDescription(string packId) =>
        Packs.FirstOrDefault(p => p.Id == packId)?.Description;

    /// <summary>
    /// Returns the IDs of packs that the given pack requires (dependencies).
    /// </summary>
    public string[] GetRequiredPacks(string packId) =>
        Packs.FirstOrDefault(p => p.Id == packId)?.Requires ?? Array.Empty<string>();
}

