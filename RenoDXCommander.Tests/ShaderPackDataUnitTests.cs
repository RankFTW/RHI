using System.Reflection;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for shader pack data correctness (reshade-shader-packs feature).
/// Verifies specific pack URLs, branch assignments, and edge-case behaviour.
/// DeployMode enum was removed — PacksForMode tests removed (will be updated in Task 7).
/// </summary>
public class ShaderPackDataUnitTests
{
    private readonly ShaderPackService _service = new(new HttpClient(), new GitHubETagCache());

    // ── Reflection helpers ────────────────────────────────────────────────────────

    /// <summary>Gets the static Packs array via reflection.</summary>
    private static Array GetPacksArray()
    {
        var field = typeof(ShaderPackService).GetField(
            "Packs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (Array)field!.GetValue(null)!;
    }

    /// <summary>Gets a pack's property value by Id from the static Packs array.</summary>
    private static string GetPackProperty(string packId, string propertyName)
    {
        var packs = GetPacksArray();
        foreach (var pack in packs)
        {
            var idProp = pack.GetType().GetProperty("Id")!;
            var id = (string)idProp.GetValue(pack)!;
            if (id == packId)
            {
                var prop = pack.GetType().GetProperty(propertyName)!;
                return prop.GetValue(pack)!.ToString()!;
            }
        }
        throw new InvalidOperationException($"Pack '{packId}' not found");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verify exact pack count is 43.
    /// _Requirements: 1.1, 1.4_
    /// </summary>
    [Fact]
    public void PackCount_IsExactly43()
    {
        Assert.Equal(43, GetPacksArray().Length);
        Assert.Equal(43, _service.AvailablePacks.Count);
    }

    /// <summary>
    /// Verify MaxG2DSimpleHDR URL is updated to main branch archive.
    /// _Requirements: 1.1, 1.4_
    /// </summary>
    [Fact]
    public void MaxG2DSimpleHDR_UrlUpdatedToMainBranchArchive()
    {
        var url = GetPackProperty("MaxG2DSimpleHDR", "Url");
        Assert.Equal(
            "https://github.com/MaxG2D/ReshadeSimpleHDRShaders/archive/refs/heads/main.zip",
            url);
    }

    /// <summary>
    /// Verify PotatoFX URL still points to CreepySasquatch fork.
    /// _Requirements: 1.1, 1.4_
    /// </summary>
    [Fact]
    public void PotatoFX_UrlPointsToCreepySasquatch()
    {
        var url = GetPackProperty("PotatoFX", "Url");
        Assert.Equal(
            "https://github.com/CreepySasquatch/potatoFX/archive/refs/heads/main.zip",
            url);
    }

    /// <summary>
    /// Verify ClshortfuseShaders is still present (RDXC-only pack, not in ReShade installer).
    /// _Requirements: 1.4_
    /// </summary>
    [Fact]
    public void ClshortfuseShaders_StillPresent()
    {
        var ids = _service.AvailablePacks.Select(p => p.Id).ToList();
        Assert.Contains("ClshortfuseShaders", ids);
    }

    /// <summary>
    /// Verify empty selection behaves like Off mode (no files deployed).
    /// _Requirements: 5.2_
    /// </summary>
    [Fact]
    public void SyncGameFolder_EmptySelection_BehavesLikeOff()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rdxc_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Call SyncGameFolder with empty selection
            _service.SyncGameFolder(tempDir, Array.Empty<string>());

            // No reshade-shaders folder should be created (same as Off mode)
            var rsDir = Path.Combine(tempDir, "reshade-shaders");
            Assert.False(Directory.Exists(rsDir),
                "Empty selection should not create reshade-shaders folder");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
