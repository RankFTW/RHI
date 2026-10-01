// ShaderPackService.cs — Class declaration, constructor, path constants, pack definitions, enums, and ShaderPack record

using RenoDXCommander.Models;

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
public partial class ShaderPackService : ShaderPackCatalog, IShaderPackService
{
    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    public ShaderPackService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }
    // ── Public path constants (used by AuxInstallService) ─────────────────────────
    public static readonly string ShadersDir = Path.Combine(AuxInstallService.RsStagingDir, "Shaders");
    public static readonly string TexturesDir = Path.Combine(AuxInstallService.RsStagingDir, "Textures");

    // User-defined custom shaders — placed by the user, never auto-downloaded
    public const string CustomShaderSentinel = "__custom__";
    /// <summary>Virtual pack ID for individual custom shader file selection in the picker.</summary>
    public const string CustomFilePackId = "__custom_files__";
    public static readonly string CustomDir = Path.Combine(AuxInstallService.RsStagingDir, "Custom");
    public static readonly string CustomShadersDir = Path.Combine(CustomDir, "Shaders");
    public static readonly string CustomTexturesDir = Path.Combine(CustomDir, "Textures");

    /// <summary>
    /// Returns all shader and texture files from the Custom folder as relative paths
    /// suitable for display in the picker (e.g. "Shaders/MyShader.fx", "Textures/LUT.png").
    /// Returns an empty list if the Custom folder doesn't exist or is empty.
    /// </summary>
    public static IReadOnlyList<string> GetCustomPackFiles()
    {
        var files = new List<string>();
        try
        {
            if (Directory.Exists(CustomShadersDir))
                foreach (var f in Directory.EnumerateFiles(CustomShadersDir, "*", SearchOption.AllDirectories))
                    files.Add(Path.Combine("Shaders", Path.GetRelativePath(CustomShadersDir, f)));
            if (Directory.Exists(CustomTexturesDir))
                foreach (var f in Directory.EnumerateFiles(CustomTexturesDir, "*", SearchOption.AllDirectories))
                    files.Add(Path.Combine("Textures", Path.GetRelativePath(CustomTexturesDir, f)));
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.GetCustomPackFiles] Failed — {ex.Message}"); }
        return files;
    }

    /// <summary>Returns true when the Custom shader folder contains at least one file.</summary>
    public static bool CustomPackHasFiles() => GetCustomPackFiles().Count > 0;

    public const string GameReShadeShaders = "reshade-shaders";
    public const string GameReShadeOriginal = "reshade-shaders-original";
    internal const string ManagedMarkerFileName = "Managed by RDXC.txt";
    private const string ManagedMarkerContent = "This folder is managed by RenoDXCommander. Do not edit manually.\n"
                                                  + "Deleting this file will cause RDXC to treat the folder as user-managed.";

}
