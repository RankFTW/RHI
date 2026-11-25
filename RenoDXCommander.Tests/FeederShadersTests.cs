using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Issue #182: Feeder's effects are component files added to the game's own shaders, not a Select-mode pack list.</summary>
public class FeederShadersTests
{
    private static string NewGameDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-feeder-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void NoFeederInstall_NoFiles()
    {
        var dir = NewGameDir();
        try { Assert.Empty(FeederShaders.ShaderFilesFor(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FeederInstall_ListsFeedFx_AndLumeniteOnlyWhenRecorded()
    {
        var dir = NewGameDir();
        try
        {
            RhiInstallManifest.SetComponent(dir, "Feeder", new[] { "dlss5-feed.addon64", @"reshade-shaders\Shaders\DLSS5_Feed.fx" });
            var files = FeederShaders.ShaderFilesFor(dir);
            var feed = Assert.Single(files);
            Assert.StartsWith(@"Shaders\", feed.Rel);
            Assert.EndsWith("DLSS5_Feed.fx", feed.Rel);
            Assert.StartsWith(ShaderPackService.ShadersDir, feed.Source);

            RhiInstallManifest.SetComponent(dir, "Feeder", new[] { @"reshade-shaders\Shaders\DLSS5_Feed.fx", @"reshade-shaders\Shaders\lumenite_Kernel.fx" });
            files = FeederShaders.ShaderFilesFor(dir);
            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.Rel.EndsWith("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, true); }
    }
}
