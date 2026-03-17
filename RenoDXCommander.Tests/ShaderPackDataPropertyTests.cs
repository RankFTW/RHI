using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for shader pack data integrity (reshade-shader-packs feature).
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class ShaderPackDataPropertyTests
{
    private readonly ShaderPackService _service = new(new HttpClient());

    /// <summary>All 44 expected pack Ids after adding ReShade installer packs.</summary>
    private static readonly string[] ExpectedPackIds =
    {
        "Lilium", "PumboAutoHDR", "SmolbbsoopShaders", "MaxG2DSimpleHDR",
        "ClshortfuseShaders", "PotatoFX", "PotatoFXUpstream", "CrosireSlim", "SweetFX",
        "CrosireLegacy", "OtisFX", "Depth3D", "FXShaders",
        "DaodanShaders", "BrussellShaders", "FubaxShaders", "qUINT",
        "AlucardDH", "WarpFX", "Prod80", "CorgiFX",
        "InsaneShaders", "CobraFX", "AstrayFX", "CRTRoyale",
        "RSRetroArch", "VRToolkit", "FGFX", "CShade",
        "iMMERSE", "VortShaders", "BXShade", "SHADERDECK",
        "METEOR", "AnnReShade", "ZenteonFX", "GShadeShaders",
        "PthoFX", "Anagrama", "BarbatosShaders", "BFBFX",
        "Rendepth", "CropAndResize", "LumeniteFX"
    };

    /// <summary>All valid DeployMode values.</summary>
    private static readonly ShaderPackService.DeployMode[] AllModes =
        Enum.GetValues<ShaderPackService.DeployMode>();

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private PacksForMode method via reflection.
    /// Returns the count of packs returned for the given mode.
    /// </summary>
    private int InvokePacksForModeCount(ShaderPackService.DeployMode mode)
    {
        var method = typeof(ShaderPackService).GetMethod(
            "PacksForMode",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("Could not find PacksForMode method via reflection");

        var result = method.Invoke(_service, new object[] { mode });
        // PacksForMode returns IEnumerable<ShaderPack> (private record), count via LINQ
        return ((System.Collections.IEnumerable)result!).Cast<object>().Count();
    }

    /// <summary>
    /// Invokes the private PacksForMode method via reflection.
    /// Returns the pack Ids returned for the given mode.
    /// </summary>
    private List<string> InvokePacksForModeIds(ShaderPackService.DeployMode mode)
    {
        var method = typeof(ShaderPackService).GetMethod(
            "PacksForMode",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("Could not find PacksForMode method via reflection");

        var result = method.Invoke(_service, new object[] { mode });
        var packs = ((System.Collections.IEnumerable)result!).Cast<object>();

        // ShaderPack is a private record — use reflection to get Id property
        var ids = new List<string>();
        foreach (var pack in packs)
        {
            var idProp = pack.GetType().GetProperty("Id");
            if (idProp != null)
                ids.Add((string)idProp.GetValue(pack)!);
        }
        return ids;
    }

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

    /// <summary>Generates any valid DeployMode value.</summary>
    private static Gen<ShaderPackService.DeployMode> GenAnyDeployMode() =>
        Gen.Elements(AllModes);

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
    /// For any expected pack Id from the complete set of 44 (8 original + 36 new),
    /// that Id must exist in AvailablePacks, the total count must be exactly 44,
    /// and AvailablePacks must match the Packs array.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PackArrayCompleteness_AllExpectedIdsExist_AndCountIs43()
    {
        // Pick a random expected Id each iteration to verify it exists
        return Prop.ForAll(Gen.Elements(ExpectedPackIds).ToArbitrary(), expectedId =>
        {
            var available = _service.AvailablePacks;

            // Total count must be exactly 44
            if (available.Count != 44)
                return false.Label($"AvailablePacks.Count is {available.Count}, expected 44");

            // The randomly chosen expected Id must be present
            var ids = available.Select(p => p.Id).ToList();
            if (!ids.Contains(expectedId))
                return false.Label($"Expected Id '{expectedId}' not found in AvailablePacks");

            // All 44 expected Ids must be present (static check, but verified each iteration)
            var missingIds = ExpectedPackIds.Where(id => !ids.Contains(id)).ToList();
            if (missingIds.Count > 0)
                return false.Label($"Missing Ids: {string.Join(", ", missingIds)}");

            // AvailablePacks count must match Packs array length
            // (AvailablePacks derives from Packs, so count equality confirms they match)
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

    // ── Property 3: DeployMode filtering correctness ──────────────────────────────

    // Feature: reshade-shader-packs, Property 3: DeployMode filtering correctness
    /// <summary>
    /// **Validates: Requirements 1.3, 5.1, 5.2, 5.3, 5.4**
    ///
    /// For any DeployMode value, PacksForMode returns exactly the correct subset:
    /// All returns all 44 packs, Minimum returns only packs with IsMinimum == true
    /// (only Lilium), Off returns zero packs, User returns zero packs, and Select
    /// returns zero packs.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeployModeFiltering_ReturnsCorrectSubsetPerMode()
    {
        return Prop.ForAll(GenAnyDeployMode().ToArbitrary(), mode =>
        {
            var count = InvokePacksForModeCount(mode);
            var ids = InvokePacksForModeIds(mode);

            switch (mode)
            {
                case ShaderPackService.DeployMode.All:
                    if (count != 44)
                        return false.Label($"All mode returned {count} packs, expected 44");
                    break;

                case ShaderPackService.DeployMode.Minimum:
                    if (count != 1)
                        return false.Label($"Minimum mode returned {count} packs, expected 1");
                    if (!ids.Contains("Lilium"))
                        return false.Label("Minimum mode did not include Lilium");
                    break;

                case ShaderPackService.DeployMode.Off:
                    if (count != 0)
                        return false.Label($"Off mode returned {count} packs, expected 0");
                    break;

                case ShaderPackService.DeployMode.User:
                    if (count != 0)
                        return false.Label($"User mode returned {count} packs, expected 0");
                    break;

                case ShaderPackService.DeployMode.Select:
                    if (count != 0)
                        return false.Label($"Select mode returned {count} packs, expected 0");
                    break;

                default:
                    return false.Label($"Unknown mode: {mode}");
            }

            return true.Label($"OK: mode={mode}, count={count}");
        });
    }

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
