using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests verifying that <c>SyncShadersToAllLocations</c> never
/// invokes <c>SyncDcFolder</c> — i.e. no shader or texture files are ever
/// written to the DC AppData folder during a global sync.
///
/// **Validates: Requirements 8.2**
///
/// NOTE: DeployMode enum was removed. Tests use pack-ID-based selection.
/// Will be fully updated in Task 7.
/// </summary>
public class SyncAllLocationsDcNeverCalledPropertyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ShaderPackService _service;
    private readonly List<string> _stagedFiles = new();

    private readonly HashSet<string> _dcShadersSnapshot;
    private readonly HashSet<string> _dcTexturesSnapshot;

    /// <summary>Known pack IDs for generating selections.</summary>
    private static readonly string[] KnownPackIds =
        new ShaderPackService(new HttpClient()).AvailablePacks.Select(p => p.Id).ToArray();

    public SyncAllLocationsDcNeverCalledPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcDcNever_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new ShaderPackService(new HttpClient());
        EnsureStagingFiles();

        _dcShadersSnapshot = SnapshotDirectory(ShaderPackService.DcShadersDir);
        _dcTexturesSnapshot = SnapshotDirectory(ShaderPackService.DcTexturesDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        foreach (var f in _stagedFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    private void EnsureStagingFiles()
    {
        Directory.CreateDirectory(ShaderPackService.ShadersDir);
        Directory.CreateDirectory(ShaderPackService.TexturesDir);

        var shaderFile = Path.Combine(ShaderPackService.ShadersDir, "_rdxc_test_dcnever.fx");
        if (!File.Exists(shaderFile))
        {
            File.WriteAllText(shaderFile, "// test shader for DC-never-called property tests");
            _stagedFiles.Add(shaderFile);
        }

        var textureFile = Path.Combine(ShaderPackService.TexturesDir, "_rdxc_test_dcnever.png");
        if (!File.Exists(textureFile))
        {
            File.WriteAllBytes(textureFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            _stagedFiles.Add(textureFile);
        }
    }

    private static HashSet<string> SnapshotDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => f.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a pack selection (null for empty, or a non-empty subset of known packs).
    /// Replaces GenAnyDeployMode.
    /// </summary>
    private static Gen<string[]?> GenAnyPackSelection()
    {
        if (KnownPackIds.Length == 0)
            return Gen.Constant<string[]?>(null);

        return Gen.OneOf(
            Gen.Constant<string[]?>(null),
            Gen.NonEmptyListOf(Gen.Elements(KnownPackIds))
                .Select(list => (string[]?)list.Distinct().ToArray()));
    }

    // ── Property: SyncDcFolder is never called ───────────────────────────────

    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// For any set of game locations passed to <c>SyncShadersToAllLocations</c>,
    /// the DC AppData folder's Shaders and Textures directories SHALL NOT have
    /// any new files created.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SyncShadersToAllLocations_NeverWritesToDcFolder()
    {
        var gen = from selection in GenAnyPackSelection()
                  from count in Gen.Choose(1, 5)
                  from suffix in Gen.Choose(1, 999999)
                  from dcInstalled in Arb.Default.Bool().Generator
                  from rsInstalled in Arb.Default.Bool().Generator
                  from dcMode in Arb.Default.Bool().Generator
                  select (selection, count, suffix, dcInstalled, rsInstalled, dcMode);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (selection, count, baseSuffix, dcInstalled, rsInstalled, dcMode) = tuple;

            var locations = new List<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode, string? shaderModeOverride)>();
            var gameDirs = new List<string>();

            for (int i = 0; i < count; i++)
            {
                var gameDir = Path.Combine(_tempRoot, $"game_{baseSuffix}_{i}_{dcInstalled}_{rsInstalled}_{dcMode}");
                Directory.CreateDirectory(gameDir);
                gameDirs.Add(gameDir);
                locations.Add((gameDir, dcInstalled, rsInstalled, dcMode, (string?)null));
            }

            try
            {
                _service.SyncShadersToAllLocations(locations, selection);

                var currentDcShaders = SnapshotDirectory(ShaderPackService.DcShadersDir);
                var newShaderFiles = currentDcShaders.Except(_dcShadersSnapshot).ToList();
                if (newShaderFiles.Count > 0)
                    return false.Label(
                        $"DC Shaders folder was modified — new files: {string.Join(", ", newShaderFiles)}");

                var currentDcTextures = SnapshotDirectory(ShaderPackService.DcTexturesDir);
                var newTextureFiles = currentDcTextures.Except(_dcTexturesSnapshot).ToList();
                if (newTextureFiles.Count > 0)
                    return false.Label(
                        $"DC Textures folder was modified — new files: {string.Join(", ", newTextureFiles)}");

                return true.Label(
                    $"OK: selection={selection?.Length ?? 0}, locations={count}");
            }
            finally
            {
                foreach (var dir in gameDirs)
                    try { Directory.Delete(dir, recursive: true); } catch { }
            }
        });
    }
}
