using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for shader pack data integrity (reshade-shader-packs feature).
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// NOTE: DeployMode enum was removed. Property 3 (DeployMode filtering) removed — will be
/// replaced with pack-ID-based logic in Task 7.
/// </summary>
public class ShaderPackDataPropertyTests
{
    private readonly ShaderPackService _service = new(new HttpClient(), new GitHubETagCache());

    /// <summary>All 44 expected pack Ids after adding ReShade installer packs.</summary>
    private static readonly string[] ExpectedPackIds =
    {
        "Lilium", "CrosireMaster", "PumboAutoHDR", "SmolbbsoopShaders", "MaxG2DSimpleHDR",
        "ClshortfuseShaders", "PotatoFX", "SweetFX",
        "OtisFX", "Depth3D", "FXShaders",
        "DaodanShaders", "BrussellShaders", "FubaxShaders", "qUINT",
        "AlucardDH", "WarpFX", "Prod80", "CorgiFX",
        "InsaneShaders", "CobraFX", "AstrayFX", "CRTRoyale",
        "RSRetroArch", "VRToolkit", "FGFX", "CShade",
        "iMMERSE", "VortShaders", "BXShade", "SHADERDECK",
        "METEOR", "AnnReShade", "ZenteonFX", "GShadeShaders",
        "PthoFX", "Anagrama", "BarbatosShaders", "BFBFX",
        "Rendepth", "CropAndResize", "LumeniteFX", "Azen",
        "NNShaders", "QdOledAplFixer"
    };

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private FileListKey method via reflection.
    /// </summary>
    private string InvokeFileListKey(string packId)
    {
        var method = typeof(ShaderPackService).GetMethod(
            "FileListKey",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("Could not find FileListKey method via reflection");

        return (string)method.Invoke(_service, new object[] { packId })!;
    }

    /// <summary>
    /// Invokes the private VersionKey method via reflection.
    /// </summary>
    private string InvokeVersionKey(string packId)
    {
        var method = typeof(ShaderPackService).GetMethod(
            "VersionKey",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("Could not find VersionKey method via reflection");

        return (string)method.Invoke(_service, new object[] { packId })!;
    }

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty alphanumeric string suitable as a pack Id.
    /// </summary>
    private static Gen<string> GenAlphanumericPackId()
    {
        return Gen.Choose(1, 30).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"
                    .ToCharArray()))
            .Select(chars => new string(chars)));
    }

    // ── Property 1: Pack array completeness ─────────────────────────────────────

    // Feature: reshade-shader-packs, Property 1: Pack array completeness
    /// <summary>
    /// **Validates: Requirements 1.1, 1.4, 4.1**
    ///
    /// For any expected pack Id from the complete set of 43,
    /// that Id must exist in AvailablePacks, the total count must be exactly 43,
    /// and AvailablePacks must match the Packs array.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PackArrayCompleteness_AllExpectedIdsExist_AndCountIs43()
    {
        // Pick a random expected Id each iteration to verify it exists
        return Prop.ForAll(Gen.Elements(ExpectedPackIds).ToArbitrary(), expectedId =>
        {
            var available = _service.AvailablePacks;

            // Total count must be exactly 43
            if (available.Count != 43)
                return false.Label($"AvailablePacks.Count is {available.Count}, expected 43");

            // The randomly chosen expected Id must be present
            var ids = available.Select(p => p.Id).ToList();
            if (!ids.Contains(expectedId))
                return false.Label($"Expected Id '{expectedId}' not found in AvailablePacks");

            // All 43 expected Ids must be present (static check, but verified each iteration)
            var missingIds = ExpectedPackIds.Where(id => !ids.Contains(id)).ToList();
            if (missingIds.Count > 0)
                return false.Label($"Missing Ids: {string.Join(", ", missingIds)}");

            return true.Label($"OK: verified '{expectedId}', total={available.Count}");
        });
    }

    // ── Property 2: Pack definition validity ──────────────────────────────────────

    // Feature: reshade-shader-packs, Property 2: Pack definition validity
    /// <summary>
    /// **Validates: Requirements 1.2**
    ///
    /// For any pack in the Packs array, its Id must be unique (no two packs share
    /// the same Id), its DisplayName must be non-empty, and its Url must be a valid
    /// absolute URI.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PackDefinitionValidity_UniqueIds_NonEmptyNames_ValidUrls()
    {
        // Pick a random pack from AvailablePacks each iteration
        var available = _service.AvailablePacks;
        return Prop.ForAll(Gen.Choose(0, available.Count - 1).ToArbitrary(), index =>
        {
            var pack = available[index];

            // Id must be non-empty
            if (string.IsNullOrWhiteSpace(pack.Id))
                return false.Label($"Pack at index {index} has empty Id");

            // DisplayName must be non-empty
            if (string.IsNullOrWhiteSpace(pack.DisplayName))
                return false.Label($"Pack '{pack.Id}' has empty DisplayName");

            // All Ids must be unique
            var allIds = available.Select(p => p.Id).ToList();
            var duplicates = allIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
                return false.Label($"Duplicate Ids found: {string.Join(", ", duplicates)}");

            // Verify URL validity via reflection on the private Packs array
            var packsField = typeof(ShaderPackService).GetField(
                "Packs", BindingFlags.NonPublic | BindingFlags.Static);
            if (packsField == null)
                return false.Label("Could not find Packs field via reflection");

            var packs = (Array)packsField.GetValue(null)!;
            foreach (var p in packs)
            {
                var idProp = p.GetType().GetProperty("Id")!;
                var urlProp = p.GetType().GetProperty("Url")!;
                var displayNameProp = p.GetType().GetProperty("DisplayName")!;

                var id = (string)idProp.GetValue(p)!;
                var url = (string)urlProp.GetValue(p)!;
                var displayName = (string)displayNameProp.GetValue(p)!;

                if (string.IsNullOrWhiteSpace(displayName))
                    return false.Label($"Pack '{id}' has empty DisplayName");

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "https" && uri.Scheme != "http"))
                    return false.Label($"Pack '{id}' has invalid URL: '{url}'");
            }

            return true.Label($"OK: pack '{pack.Id}' at index {index}");
        });
    }

    // ── Property 3: DeployMode filtering — REMOVED (DeployMode enum deleted) ──
    // Will be replaced with pack-ID-based logic in Task 7.

    // ── Property 4: Settings key naming convention ────────────────────────────────

    // Feature: reshade-shader-packs, Property 4: Settings key naming convention
    /// <summary>
    /// **Validates: Requirements 2.3, 2.4, 2.5**
    ///
    /// For any valid pack Id string, the file list key must equal
    /// ShaderPack_{Id}_Files, and the version key must equal
    /// ShaderPack_{Id}_Version.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SettingsKeyNaming_ProducesCorrectFormat()
    {
        return Prop.ForAll(GenAlphanumericPackId().ToArbitrary(), packId =>
        {
            var expectedFileListKey = $"ShaderPack_{packId}_Files";
            var expectedVersionKey = $"ShaderPack_{packId}_Version";

            var actualFileListKey = InvokeFileListKey(packId);
            var actualVersionKey = InvokeVersionKey(packId);

            if (actualFileListKey != expectedFileListKey)
                return false.Label($"FileListKey for '{packId}': expected '{expectedFileListKey}', got '{actualFileListKey}'");

            if (actualVersionKey != expectedVersionKey)
                return false.Label($"VersionKey for '{packId}': expected '{expectedVersionKey}', got '{actualVersionKey}'");

            return true.Label($"OK: packId='{packId}'");
        });
    }
}
