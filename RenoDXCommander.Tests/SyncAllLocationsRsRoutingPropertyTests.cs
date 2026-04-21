using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests verifying that <c>SyncShadersToAllLocations</c> always
/// routes RS-installed games to <c>SyncGameFolder</c>, regardless of DC status.
///
/// NOTE: DeployMode enum was removed. Tests use pack-ID-based selection.
/// Will be fully updated in Task 7.
/// </summary>
public class SyncAllLocationsRsRoutingPropertyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ShaderPackService _service;
    private readonly List<string> _stagedFiles = new();

    /// <summary>Known pack IDs for generating selections.</summary>
    private static readonly string[] KnownPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    public SyncAllLocationsRsRoutingPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcRsRoute_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new ShaderPackService(new HttpClient(), new GitHubETagCache());
        EnsureStagingFiles();
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

        var shaderFile = Path.Combine(ShaderPackService.ShadersDir, "_rdxc_test_rsroute.fx");
        if (!File.Exists(shaderFile))
        {
            File.WriteAllText(shaderFile, "// test shader for RS routing property tests");
            _stagedFiles.Add(shaderFile);
        }

        var textureFile = Path.Combine(ShaderPackService.TexturesDir, "_rdxc_test_rsroute.png");
        if (!File.Exists(textureFile))
        {
            File.WriteAllBytes(textureFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            _stagedFiles.Add(textureFile);
        }
    }

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty subset of known pack IDs (replaces GenNonOffDeployMode).
    /// </summary>
    private static Gen<string[]> GenNonEmptyPackSelection()
    {
        if (KnownPackIds.Length == 0)
            return Gen.Constant(new[] { "Lilium" });

        return Gen.NonEmptyListOf(Gen.Elements(KnownPackIds))
            .Select(list => list.Distinct().ToArray());
    }

    // ── Property: RS-installed games always get SyncGameFolder ────────────────

    /// <summary>
    /// **Validates: Requirements 8.1, 8.4**
    ///
    /// For any game location tuple where <c>rsInstalled=true</c>,
    /// <c>SyncShadersToAllLocations</c>
    /// SHALL call <c>SyncGameFolder</c> with the game's install path — verified
    /// by the presence of the RDXC-managed <c>reshade-shaders</c> folder with
    /// the managed marker file.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RsInstalled_AlwaysGetsSyncGameFolder()
    {
        var gen = from packIds in GenNonEmptyPackSelection()
                  from suffix in Gen.Choose(1, 999999)
                  select (packIds, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (packIds, suffix) = tuple;

            var gameDir = Path.Combine(_tempRoot, $"game_{suffix}");
            Directory.CreateDirectory(gameDir);

            try
            {
                var locations = new[]
                {
                    (installPath: gameDir, rsInstalled: true,
                     shaderModeOverride: (string?)null)
                };

                _service.SyncShadersToAllLocations(locations, packIds);

                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
                if (!Directory.Exists(rsDir))
                    return false.Label(
                        $"reshade-shaders folder missing — SyncGameFolder was not called " +
                        $"(packs={packIds.Length})");

                var markerPath = Path.Combine(rsDir, "Managed by RDXC.txt");
                if (!File.Exists(markerPath))
                    return false.Label(
                        $"Managed marker missing — SyncGameFolder did not complete deployment " +
                        $"(packs={packIds.Length})");

                return true.Label($"OK: packs={packIds.Length}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }
}
