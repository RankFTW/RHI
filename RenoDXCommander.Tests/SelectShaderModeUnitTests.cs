using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;
using ShaderDeployMode = RenoDXCommander.Services.ShaderPackService.DeployMode;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for the Select Shader Mode feature.
/// Covers enum ordinals, version display, available packs,
/// settings defaults, and unknown pack ID handling.
/// </summary>
public class SelectShaderModeUnitTests
{
    /// <summary>
    /// 10.1 — DeployMode.Select has ordinal value 4.
    /// Validates: Requirement 2.2
    /// </summary>
    [Fact]
    public void DeployMode_Select_HasOrdinalValue4()
    {
        Assert.Equal(4, (int)ShaderDeployMode.Select);
    }

    /// <summary>
    /// 10.2 — RdxcVersion(1, 4, 8, 2).ToDisplayString() returns "1.4.8 beta 2".
    /// Validates: Requirement 1.2
    /// </summary>
    [Fact]
    public void RdxcVersion_1_4_8_2_ToDisplayString_ReturnsBeta2()
    {
        var version = new RdxcVersion(1, 4, 8, 2);
        Assert.Equal("1.4.8 beta 2", version.ToDisplayString());
    }

    /// <summary>
    /// 10.3 — AvailablePacks returns exactly 7 packs with non-empty DisplayNames.
    /// Validates: Requirement 4.2
    /// </summary>
    [Fact]
    public void AvailablePacks_Returns7PacksWithNonEmptyDisplayNames()
    {
        var service = new ShaderPackService(new HttpClient());
        var packs = service.AvailablePacks;

        Assert.Equal(7, packs.Count);
        Assert.All(packs, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id));
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
        });
    }

    /// <summary>
    /// 10.4 — Missing SelectedShaderPacks key defaults to empty list.
    /// Validates: Requirement 6.3
    /// </summary>
    [Fact]
    public void LoadSettingsFromDict_MissingSelectedShaderPacks_DefaultsToEmptyList()
    {
        var vm = new SettingsViewModel();
        var dict = new Dictionary<string, string>(); // no SelectedShaderPacks key

        vm.LoadSettingsFromDict(dict);

        Assert.NotNull(vm.SelectedShaderPacks);
        Assert.Empty(vm.SelectedShaderPacks);
    }

    /// <summary>
    /// 10.5 — Unknown pack ID in selection is silently ignored during deployment.
    /// SyncGameFolder with Select mode and a list containing an unknown pack ID should not throw.
    /// Validates: Design error handling — unrecognized pack IDs are silently ignored.
    /// </summary>
    [Fact]
    public void SyncGameFolder_UnknownPackId_SilentlyIgnored()
    {
        var service = new ShaderPackService(new HttpClient());
        var tempDir = Path.Combine(Path.GetTempPath(), $"rdxc_test_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Should not throw — unknown IDs are silently ignored
            var exception = Record.Exception(() =>
                service.SyncGameFolder(tempDir, ShaderDeployMode.Select, new[] { "NonExistentPack_XYZ" }));

            Assert.Null(exception);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
