using System.Text.Json;
using SharpCompress.Archives;

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
public class ShaderPackService : IShaderPackService
{
    private readonly HttpClient _http;

    public ShaderPackService(HttpClient http) => _http = http;
    // ── Public path constants (used by AuxInstallService) ─────────────────────────
    public static readonly string ShadersDir = Path.Combine(AuxInstallService.RsStagingDir, "Shaders");
    public static readonly string TexturesDir = Path.Combine(AuxInstallService.RsStagingDir, "Textures");

    public static readonly string DcReshadeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Display_Commander", "Reshade");
    public static readonly string DcShadersDir = Path.Combine(DcReshadeDir, "Shaders");
    public static readonly string DcTexturesDir = Path.Combine(DcReshadeDir, "Textures");

    // User-defined custom shaders — placed by the user, never auto-downloaded
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
        PackCategory Category = PackCategory.Extra // UI grouping
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
            Id          : "Azen",
            DisplayName : "Azen by Zenteon",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Zenteon/Azen/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Zenteon's casual shader collection — experimental effects"
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
    };

    // ── Main entry point ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks every pack. A pack is (re-)downloaded when:
    ///   • its version token has changed (new release / changed ETag), OR
    ///   • its cache zip is missing from the downloads folder, OR
    ///   • it has no extracted files in the staging Shaders/Textures tree.
    /// Failures in one pack are logged and skipped; others continue.
    /// </summary>
    public async Task EnsureLatestAsync(
        IProgress<string>? progress = null)
    {
        foreach (var pack in Packs)
        {
            try { await EnsurePackAsync(pack, progress); }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.EnsureLatestAsync] Unexpected error for '{pack.Id}' — {ex.Message}"); }
        }
    }

    // ── Per-pack download + extract ───────────────────────────────────────────────

    private async Task EnsurePackAsync(
        ShaderPack pack,
        IProgress<string>? progress)
    {
        string? downloadUrl;
        string versionToken;

        if (pack.Kind == SourceKind.GhRelease)
        {
            (downloadUrl, versionToken) = await ResolveGhRelease(pack);
            if (downloadUrl == null) return;
        }
        else
        {
            downloadUrl = pack.Url;
            versionToken = await ResolveDirectUrlVersion(pack);
        }

        // Derive the expected cache path so we can check physical existence
        var ext = Path.GetExtension(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrEmpty(ext)) ext = ".zip";
        var cachePath = Path.Combine(AuxInstallService.DownloadCacheDir, $"shaders_{pack.Id}{ext}");

        var stored = LoadStoredVersion(pack.Id);
        var versionMatch = stored == versionToken && versionToken != "unknown";
        var cacheExists = File.Exists(cachePath);
        var hasExtracted = PackHasExtractedFiles(pack.Id, cachePath);

        if (versionMatch && cacheExists && hasExtracted)
        {
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Up to date ({versionToken})");
            return;
        }

        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Need update — " +
            $"versionMatch={versionMatch} cacheExists={cacheExists} hasExtracted={hasExtracted}");

        // ── Download ──────────────────────────────────────────────────────────────
        progress?.Report($"Downloading {pack.DisplayName}...");
        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Downloading from {downloadUrl}");

        Directory.CreateDirectory(AuxInstallService.DownloadCacheDir);
        var tempPath = cachePath + ".tmp";

        try
        {
            var dlResp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!dlResp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Download failed ({dlResp.StatusCode})");
                return;
            }

            var total = dlResp.Content.Headers.ContentLength ?? -1L;
            long received = 0;
            var buf = new byte[1024 * 1024]; // 1 MB

            using (var net = await dlResp.Content.ReadAsStreamAsync())
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
            {
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, read));
                    received += read;
                    if (total > 0)
                        progress?.Report($"Downloading {pack.DisplayName}... {received / 1024} KB / {total / 1024} KB");
                }
            }

            if (File.Exists(cachePath)) File.Delete(cachePath);
            File.Move(tempPath, cachePath);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] Temp file cleanup failed — {cleanupEx.Message}"); }
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Download exception — {ex.Message}");
            return;
        }

        // ── Extract ───────────────────────────────────────────────────────────────
        progress?.Report($"Extracting {pack.DisplayName}...");
        try
        {
            Directory.CreateDirectory(ShadersDir);
            Directory.CreateDirectory(TexturesDir);

            using var archive = ArchiveFactory.Open(cachePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;

                var key = entry.Key?.Replace('\\', '/') ?? "";

                string? rootDir = null;
                string? relInRoot = null;

                foreach (var (token, dir) in new[]
                {
                    ("Shaders/",  ShadersDir),
                    ("Textures/", TexturesDir),
                })
                {
                    int idx = key.IndexOf("/" + token, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        rootDir = dir;
                        relInRoot = key.Substring(idx + 1 + token.Length);
                        break;
                    }
                    if (key.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        rootDir = dir;
                        relInRoot = key.Substring(token.Length);
                        break;
                    }
                }

                if (rootDir == null || string.IsNullOrEmpty(relInRoot)) continue;

                // Skip shaders that are known to fail compilation
                var fileName = Path.GetFileName(relInRoot);
                if (rootDir == ShadersDir && ExcludedShaderFiles.Contains(fileName)) continue;

                // Place each pack's files into a subdirectory named after the pack ID
                var relPath = Path.Combine(pack.Id, relInRoot.Replace('/', Path.DirectorySeparatorChar));
                var destPath = Path.Combine(rootDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using var entryStream = entry.OpenEntryStream();
                using var fileStream = File.Create(destPath);
                await entryStream.CopyToAsync(fileStream);
            }

            // Copy ReShade framework headers to the staging root so all packs can find them
            foreach (var header in ReShadeHeaders)
            {
                var packHeader = Path.Combine(ShadersDir, pack.Id, header);
                var rootHeader = Path.Combine(ShadersDir, header);
                if (File.Exists(packHeader))
                    try { File.Copy(packHeader, rootHeader, overwrite: true); }
                    catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] Failed to copy header '{header}' to root — {ex.Message}"); }
            }

            // Record which files this pack contributed so we can verify presence later
            RecordExtractedFiles(pack.Id, cachePath);
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Extracted successfully");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Extraction failed — {ex.Message}");
            return;
        }

        SaveStoredVersion(pack.Id, versionToken);
        progress?.Report($"{pack.DisplayName} updated.");
        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Done. Version = {versionToken}");
    }

    // ── Extracted-file tracking ───────────────────────────────────────────────────

    // Settings key that stores the list of files extracted by a pack.
    // Value is a JSON array of paths relative to RsStagingDir.
    private string FileListKey(string packId) => $"ShaderPack_{packId}_Files";

    /// <summary>
    /// Returns true when every file previously recorded for this pack still exists
    /// on disk AND the cache zip itself exists. Either condition missing → re-extract.
    /// </summary>
    private bool PackHasExtractedFiles(string packId, string cachePath)
    {
        if (!File.Exists(cachePath)) return false;
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null || !d.TryGetValue(FileListKey(packId), out var json) || string.IsNullOrEmpty(json))
                return false; // no record → treat as missing
            var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            if (files.Count == 0) return false;
            // All recorded files must still exist
            return files.All(rel => File.Exists(Path.Combine(AuxInstallService.RsStagingDir, rel)));
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PackHasExtractedFiles] Failed to check extracted files for '{packId}' — {ex.Message}"); return false; }
    }

    /// <summary>
    /// After a successful extraction, walks the archive again and records every
    /// extracted relative path so PackHasExtractedFiles can verify them next run.
    /// </summary>
    private void RecordExtractedFiles(string packId, string cachePath)
    {
        try
        {
            var files = new List<string>();
            using var archive = ArchiveFactory.Open(cachePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key?.Replace('\\', '/') ?? "";

                string? rootDir = null;
                string? relInRoot = null;
                foreach (var (token, dir) in new[]
                {
                    ("Shaders/",  ShadersDir),
                    ("Textures/", TexturesDir),
                })
                {
                    int idx = key.IndexOf("/" + token, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) { rootDir = dir; relInRoot = key.Substring(idx + 1 + token.Length); break; }
                    if (key.StartsWith(token, StringComparison.OrdinalIgnoreCase)) { rootDir = dir; relInRoot = key.Substring(token.Length); break; }
                }
                if (rootDir == null || string.IsNullOrEmpty(relInRoot)) continue;
                // Skip excluded shaders so they are never recorded or deployed
                var recFileName = Path.GetFileName(relInRoot);
                if (rootDir == ShadersDir && ExcludedShaderFiles.Contains(recFileName)) continue;
                // Store as relative path from RsStagingDir, with pack subdirectory
                var subDir = rootDir == ShadersDir ? "Shaders" : "Textures";
                files.Add(Path.Combine(subDir, packId, relInRoot.Replace('/', Path.DirectorySeparatorChar)));
            }

            Dictionary<string, string> d = new();
            if (File.Exists(SettingsPath))
                d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? new();
            d[FileListKey(packId)] = JsonSerializer.Serialize(files);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.RecordExtractedFiles] Failed for '{packId}' — {ex.Message}"); }
    }

    // ── Source resolution ─────────────────────────────────────────────────────────

    private async Task<(string? url, string version)> ResolveGhRelease(
        ShaderPack pack)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, pack.Url);
            req.Headers.Add("User-Agent", "RenoDXCommander");
            req.Headers.Add("Accept", "application/vnd.github+json");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[ShaderPackService.ResolveGhRelease] [{pack.Id}] GitHub API {resp.StatusCode}");
                return (null, "");
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    bool matches = pack.AssetExt == null ||
                                   name.EndsWith(pack.AssetExt, StringComparison.OrdinalIgnoreCase);
                    if (matches && !string.IsNullOrEmpty(url))
                        return (url, name);
                }
            }

            // Fall back to source code zipball
            if (root.TryGetProperty("zipball_url", out var zb))
            {
                var tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "unknown" : "unknown";
                var zbUrl = zb.GetString();
                if (!string.IsNullOrEmpty(zbUrl))
                    return (zbUrl, $"source_{tagName}.zip");
            }

            CrashReporter.Log($"[ShaderPackService.ResolveGhRelease] [{pack.Id}] No suitable asset found");
            return (null, "");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.ResolveGhRelease] [{pack.Id}] GH API error — {ex.Message}");
            return (null, "");
        }
    }

    private async Task<string> ResolveDirectUrlVersion(ShaderPack pack)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Head, pack.Url);
            req.Headers.Add("User-Agent", "RenoDXCommander");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return "unknown";
            var etag = resp.Headers.ETag?.Tag;
            var modified = resp.Content.Headers.LastModified?.ToString("O");
            return etag ?? modified ?? "unknown";
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.ResolveDirectUrlVersion] Failed to resolve version for URL — {ex.Message}"); return "unknown"; }
    }

    // ── Deployment helpers ────────────────────────────────────────────────────────

    // ── AvailablePacks (Task 5.1) ────────────────────────────────────────────────

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

    // ── Pack filtering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns packs whose Id is in the given set. Unknown IDs are silently ignored.
    /// </summary>
    private IEnumerable<ShaderPack> PacksForIds(IEnumerable<string> packIds)
    {
        var idSet = new HashSet<string>(packIds, StringComparer.OrdinalIgnoreCase);
        return Packs.Where(p => idSet.Contains(p.Id));
    }

    /// <summary>
    /// Deploys only the packs matching the given <paramref name="packIds"/>.
    /// Used to deploy the user's chosen subset of shader packs.
    /// </summary>
    private void DeployPacksIfAbsent(IEnumerable<string> packIds, string destShadersDir, string destTexturesDir)
    {
        var shadersFiles = new List<string>();
        var texturesFiles = new List<string>();

        foreach (var pack in PacksForIds(packIds))
        {
            try
            {
                if (!File.Exists(SettingsPath)) continue;
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                if (d == null || !d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json))
                    continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        shadersFiles.Add(rel.Substring("Shaders".Length + 1));
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        texturesFiles.Add(rel.Substring("Textures".Length + 1));
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService] Failed to read pack record — {ex.Message}"); }
        }

        bool hasRecords = shadersFiles.Count > 0 || texturesFiles.Count > 0;

        if (hasRecords)
        {
            EnsureReShadeHeaders(shadersFiles);
            DeployFileListIfAbsent(ShadersDir, destShadersDir, shadersFiles);
            DeployFileListIfAbsent(TexturesDir, destTexturesDir, texturesFiles);
        }
    }

    /// <summary>
    /// Deploys all known packs to the destination directories.
    /// Fallback used by <see cref="DeployToGameFolder"/> when no specific pack IDs are provided.
    /// </summary>
    private void DeployAllPacksIfAbsent(string destShadersDir, string destTexturesDir)
    {
        DeployPacksIfAbsent(Packs.Select(p => p.Id), destShadersDir, destTexturesDir);
    }

    /// <summary>
    /// ReShade framework headers that all shader packs depend on.
    /// These must always be deployed alongside any pack so that shaders can compile.
    /// </summary>
    private static readonly string[] ReShadeHeaders = { "ReShade.fxh", "ReShadeUI.fxh" };

    /// <summary>
    /// Ensures the ReShade framework headers (reshade.fxh, reshadeui.fxh) are included
    /// in the deploy list whenever any shader pack files are being deployed.
    /// These headers live in the staging Shaders folder but aren't tracked per-pack.
    /// </summary>
    private static void EnsureReShadeHeaders(List<string> shadersFiles)
    {
        foreach (var header in ReShadeHeaders)
        {
            if (!shadersFiles.Contains(header, StringComparer.OrdinalIgnoreCase))
                shadersFiles.Add(header);
        }
    }

    /// <summary>
    /// Collects relative paths for all files belonging to packs matching the given <paramref name="packIds"/>.
    /// </summary>
    private (List<string> shaders, List<string> textures) FilesForIds(IEnumerable<string> packIds)
    {
        var shaders = new List<string>();
        var textures = new List<string>();
        try
        {
            if (!File.Exists(SettingsPath)) return (shaders, textures);
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null) return (shaders, textures);
            foreach (var pack in PacksForIds(packIds))
            {
                if (!d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json)) continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        shaders.Add(rel.Substring("Shaders".Length + 1));
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        textures.Add(rel.Substring("Textures".Length + 1));
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService] Operation failed — {ex.Message}"); }
        return (shaders, textures);
    }

    // ── Marker / ownership helpers ────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the reshade-shaders folder in <paramref name="gameDir"/>
    /// was placed there by RDXC (contains our marker file).
    /// </summary>
    public bool IsManagedByRdxc(string gameDir)
    {
        var marker = Path.Combine(gameDir, GameReShadeShaders, ManagedMarkerFile);
        return File.Exists(marker);
    }

    /// <summary>
    /// Writes the RDXC ownership marker into the reshade-shaders folder.
    /// Call after creating the folder so future runs recognise it as ours.
    /// </summary>
    private void WriteMarker(string gameDir)
    {
        try
        {
            var rsDir = Path.Combine(gameDir, GameReShadeShaders);
            var marker = Path.Combine(rsDir, ManagedMarkerFile);
            Directory.CreateDirectory(rsDir);
            File.WriteAllText(marker, ManagedMarkerContent);
        }
        catch (Exception ex)
        { CrashReporter.Log($"[ShaderPackService.WriteMarkerFile] Failed to write marker — {ex.Message}"); }
    }

    /// <summary>
    /// If a user-owned reshade-shaders folder was previously renamed to
    /// reshade-shaders-original, rename it back. Called on RS/DC uninstall.
    /// </summary>
    public void RestoreOriginalIfPresent(string gameDir)
    {
        var orig = Path.Combine(gameDir, GameReShadeOriginal);
        var current = Path.Combine(gameDir, GameReShadeShaders);
        if (!Directory.Exists(orig)) return;
        // Only restore if our managed copy is gone
        if (!Directory.Exists(current))
        {
            try { Directory.Move(orig, current); }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.RestoreOriginalShaders] Failed to restore original — {ex.Message}"); }
        }
    }

    // ── Deployment helpers ────────────────────────────────────────────────────────

    public void DeployToDcFolder()
    {
        CrashReporter.Log("[ShaderPackService.DeployToDcFolder] No-op — local-only shader deployment");
    }

    /// <summary>
    /// One-time startup migration: renames legacy Shaders and Textures folders
    /// inside the DC AppData folder to *.old so Display Commander no longer loads
    /// stale global shaders. Skips the rename when the .old target already exists.
    /// Never throws — errors are logged via CrashReporter.
    /// </summary>
    public static void MigrateLegacyDcShaders()
        => MigrateLegacyDcShaders(DcShadersDir, DcTexturesDir);

    /// <summary>
    /// Testable overload that accepts explicit directory paths.
    /// </summary>
    internal static void MigrateLegacyDcShaders(string shadersDir, string texturesDir)
    {
        // Rename Shaders → Shaders.old
        try
        {
            var shadersOld = shadersDir + ".old";
            if (Directory.Exists(shadersDir) && !Directory.Exists(shadersOld))
            {
                Directory.Move(shadersDir, shadersOld);
                CrashReporter.Log("[ShaderPackService.MigrateLegacyDcShaders] Renamed Shaders → Shaders.old");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.MigrateLegacyDcShaders] Failed to rename Shaders — {ex.Message}");
        }

        // Rename Textures → Textures.old
        try
        {
            var texturesOld = texturesDir + ".old";
            if (Directory.Exists(texturesDir) && !Directory.Exists(texturesOld))
            {
                Directory.Move(texturesDir, texturesOld);
                CrashReporter.Log("[ShaderPackService.MigrateLegacyDcShaders] Renamed Textures → Textures.old");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.MigrateLegacyDcShaders] Failed to rename Textures — {ex.Message}");
        }
    }

    /// <summary>
    /// Deploys staging shaders to <c>gameDir\reshade-shaders\</c>.
    /// If a pre-existing non-RDXC reshade-shaders folder is found, it is renamed
    /// to reshade-shaders-original before creating our managed folder.
    /// When <paramref name="packIds"/> is null, all packs are deployed (fallback for LumaService).
    /// </summary>
    public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null)
    {
        var rsDir = Path.Combine(gameDir, GameReShadeShaders);

        // If an existing reshade-shaders folder is NOT ours, preserve it
        if (Directory.Exists(rsDir) && !IsManagedByRdxc(gameDir))
        {
            var origDir = Path.Combine(gameDir, GameReShadeOriginal);
            try
            {
                if (!Directory.Exists(origDir))
                    Directory.Move(rsDir, origDir);
                else
                    CrashReporter.Log($"[ShaderPackService.DeployToGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename");
            }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.DeployToGameFolder] Failed to rename existing reshade-shaders — {ex.Message}"); }
        }

        if (packIds != null)
            DeployPacksIfAbsent(packIds, Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"));
        else
            DeployAllPacksIfAbsent(Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"));
        WriteMarker(gameDir);
    }

    /// <summary>
    /// Removes the RDXC-managed reshade-shaders folder (only if our marker is present).
    /// If a pre-existing folder was renamed to reshade-shaders-original, it is left alone;
    /// RestoreOriginalIfPresent() handles restoring it on RS/DC uninstall.
    /// If the folder has no marker (user-owned), rename it to reshade-shaders-original.
    /// Called when DC is installed to the same folder (ReShade uses DC global path).
    /// </summary>
    public void RemoveFromGameFolder(string gameDir)
    {
        var rsDir = Path.Combine(gameDir, GameReShadeShaders);
        if (!Directory.Exists(rsDir)) return;

        if (IsManagedByRdxc(gameDir))
        {
            try { Directory.Delete(rsDir, recursive: true); }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.RemoveFromGameFolder] Failed to remove managed reshade-shaders — {ex.Message}"); }
        }
        else
        {
            // User-owned folder — rename to preserve it
            var origDir = Path.Combine(gameDir, GameReShadeOriginal);
            if (!Directory.Exists(origDir))
                try { Directory.Move(rsDir, origDir); }
                catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.RemoveFromGameFolder] Failed to rename user reshade-shaders — {ex.Message}"); }
            else
                CrashReporter.Log($"[ShaderPackService.RemoveFromGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename of user folder");
        }
    }

    // ── Global sync (called on Refresh) ──────────────────────────────────────────

    /// <summary>
    /// Builds the set of relative paths (relative to ShadersDir / TexturesDir)
    /// that were ever deployed by RDXC for ANY pack — used to identify stale files
    /// during sync.
    /// </summary>
    private (HashSet<string> shaders, HashSet<string> textures) AllKnownPackFiles()
    {
        var shaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(SettingsPath)) return (shaders, textures);
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null) return (shaders, textures);
            foreach (var pack in Packs)
            {
                if (!d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json)) continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        shaders.Add(rel.Substring("Shaders".Length + 1));
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        textures.Add(rel.Substring("Textures".Length + 1));
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService] Operation failed — {ex.Message}"); }
        return (shaders, textures);
    }

    /// <summary>
    /// Removes from <paramref name="destDir"/> every file in <paramref name="knownFiles"/>
    /// that is NOT in <paramref name="keepFiles"/>. Leaves directories in place.
    /// </summary>
    private void PruneFiles(string destDir, IEnumerable<string> knownFiles, IEnumerable<string> keepFiles)
    {
        if (!Directory.Exists(destDir)) return;
        var keepSet = new HashSet<string>(keepFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var rel in knownFiles)
        {
            if (keepSet.Contains(rel)) continue;
            var path = Path.Combine(destDir, rel);
            if (!File.Exists(path)) continue;
            try { File.Delete(path); }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PruneFiles] Failed for '{path}' — {ex.Message}"); }
        }
    }

    /// <summary>
    /// Synchronises the DC global Reshade folder to exactly match the current selection.
    /// </summary>
    public void SyncDcFolder(IEnumerable<string>? selectedPackIds = null)
    {
        CrashReporter.Log("[ShaderPackService.SyncDcFolder] No-op — local-only shader deployment");
    }


    /// <summary>
    /// Synchronises the game-local reshade-shaders folder to match the current selection.
    /// Null/empty selection → remove managed shaders and restore originals.
    /// Non-empty selection → prune unselected pack files and deploy selected packs.
    /// </summary>
    public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
    {
        var rsShaders = Path.Combine(gameDir, GameReShadeShaders, "Shaders");
        var rsTextures = Path.Combine(gameDir, GameReShadeShaders, "Textures");

        var (allKnownShaders, allKnownTextures) = AllKnownPackFiles();

        // Null/empty selection → remove managed shaders and restore originals
        if (selectedPackIds == null || !selectedPackIds.Any())
        {
            CrashReporter.Log($"[ShaderPackService.SyncGameFolder] No packs selected → removing managed shaders for {gameDir}");
            if (IsManagedByRdxc(gameDir))
                RemoveFromGameFolder(gameDir);
            RestoreOriginalIfPresent(gameDir);
            return;
        }

        // Non-empty selection → prune unselected and deploy selected
        CrashReporter.Log($"[ShaderPackService.SyncGameFolder] gameDir={gameDir}, managed={IsManagedByRdxc(gameDir)}");

        if (IsManagedByRdxc(gameDir))
        {
            var (selectShaders, selectTextures) = FilesForIds(selectedPackIds);
            PruneFiles(rsShaders, allKnownShaders, selectShaders);
            PruneFiles(rsTextures, allKnownTextures, selectTextures);

            DeployPacksIfAbsent(selectedPackIds, rsShaders, rsTextures);
            WriteMarker(gameDir);
        }
        else
        {
            // Not yet managed — handle rename + marker, then deploy selected packs
            var rsDir = Path.Combine(gameDir, GameReShadeShaders);
            if (Directory.Exists(rsDir))
            {
                var origDir = Path.Combine(gameDir, GameReShadeOriginal);
                try
                {
                    if (!Directory.Exists(origDir))
                        Directory.Move(rsDir, origDir);
                    else
                        CrashReporter.Log($"[ShaderPackService.SyncGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename");
                }
                catch (Exception ex)
                { CrashReporter.Log($"[ShaderPackService.SyncGameFolder] Failed to rename existing reshade-shaders — {ex.Message}"); }
            }

            DeployPacksIfAbsent(selectedPackIds, Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"));
            WriteMarker(gameDir);
        }
    }

    /// <summary>
    /// Synchronises shaders to every game that has ReShade installed.
    /// Called after ↻ Refresh so selection changes take effect immediately everywhere.
    /// Per-game overrides are resolved from <paramref name="locations"/> shaderModeOverride;
    /// otherwise the passed-in <paramref name="selectedPackIds"/> is used.
    /// </summary>
    public void SyncShadersToAllLocations(
        IEnumerable<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode, string? shaderModeOverride)> locations,
        IEnumerable<string>? selectedPackIds = null)
    {
        foreach (var loc in locations)
        {
            if (string.IsNullOrEmpty(loc.installPath) || !Directory.Exists(loc.installPath))
                continue;

            if (!loc.rsInstalled)
                continue;

            SyncGameFolder(loc.installPath, selectedPackIds);
        }
    }

    // ── Private copy helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the staged source file differs from the deployed destination file.
    /// Compares file size first (fast), then falls back to byte-level comparison if sizes match
    /// but the staging file is newer — covers edge cases where content changes without size change.
    /// </summary>
    private bool IsFileStale(string srcPath, string destPath)
    {
        try
        {
            var srcInfo  = new FileInfo(srcPath);
            var destInfo = new FileInfo(destPath);

            // Different size → definitely stale
            if (srcInfo.Length != destInfo.Length) return true;

            // Same size but source is newer → compare bytes
            if (srcInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc)
            {
                // Quick byte comparison (read in 8 KB chunks)
                using var fs1 = File.OpenRead(srcPath);
                using var fs2 = File.OpenRead(destPath);
                var buf1 = new byte[8192];
                var buf2 = new byte[8192];
                int read1;
                while ((read1 = fs1.Read(buf1, 0, buf1.Length)) > 0)
                {
                    var read2 = fs2.Read(buf2, 0, buf2.Length);
                    if (read1 != read2) return true;
                    if (!buf1.AsSpan(0, read1).SequenceEqual(buf2.AsSpan(0, read2))) return true;
                }
                return false; // Identical bytes
            }

            return false; // Same size and destination is same age or newer
        }
        catch
        {
            return false; // On error, don't overwrite
        }
    }

    private void DeployFileListIfAbsent(string sourceDir, string destDir, List<string> relPaths)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);
        foreach (var rel in relPaths)
        {
            var src = Path.Combine(sourceDir, rel);
            if (!File.Exists(src)) continue;
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (File.Exists(dest))
            {
                // Overwrite if the staged file differs from the deployed file
                if (!IsFileStale(src, dest)) continue;
                try { File.Copy(src, dest, overwrite: true); }
                catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.DeployFileListIfAbsent] Update '{rel}' failed — {ex.Message}"); }
            }
            else
            {
                File.Copy(src, dest, overwrite: false);
            }
        }
    }

    /// <summary>
    /// Full-folder copy fallback: copies all files from sourceDir → destDir,
    /// skipping any that already exist. Used when per-pack file records are absent.
    /// </summary>
    private void DeployFolderIfAbsent(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (File.Exists(destFile))
            {
                if (!IsFileStale(file, destFile)) continue;
                try { File.Copy(file, destFile, overwrite: true); }
                catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.DeployFolderIfAbsent] Update '{rel}' failed — {ex.Message}"); }
            }
            else
            {
                File.Copy(file, destFile, overwrite: false);
            }
        }
    }

    // ── Settings persistence ──────────────────────────────────────────────────────

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "settings.json");

    private string VersionKey(string packId) => $"ShaderPack_{packId}_Version";

    private string? LoadStoredVersion(string packId)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            return d != null && d.TryGetValue(VersionKey(packId), out var v) ? v : null;
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.LoadStoredVersion] Failed to load stored version for '{packId}' — {ex.Message}"); return null; }
    }

    private void SaveStoredVersion(string packId, string version)
    {
        try
        {
            Dictionary<string, string> d = new();
            if (File.Exists(SettingsPath))
                d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? new();
            d[VersionKey(packId)] = version;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.SaveStoredVersion] Failed to save version for '{packId}' — {ex.Message}"); }
    }
}
