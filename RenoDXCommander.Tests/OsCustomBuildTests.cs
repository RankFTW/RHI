using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Custom OptiScaler builds: the version list helpers, finding OptiScaler.dll and name suggestions.</summary>
public class OsCustomBuildTests
{
    [Fact]
    public void BuiltInVersions_KeepTheirLabelsAndTypes()
    {
        Assert.Equal("F5 DLSSNR", OsVariants.Label("F5"));
        Assert.Equal("DLSS NR", OsVariants.Label("DlssNr"));
        Assert.Equal("Stable", OsVariants.Label(null));
        Assert.Equal("F5", OsVariants.FromLabel("F5 DLSSNR"));
        Assert.Equal("DlssNr", OsVariants.FromLabel("DLSS NR"));
        Assert.Equal("Nightly", OsVariants.FromLabel("Nightly"));
        Assert.True(OsVariants.IsOs10("Nightly"));
        Assert.False(OsVariants.IsOs10("Stable"));
        Assert.True(OsVariants.NeedsNrDll("F5"));
        Assert.False(OsVariants.NeedsNrDll("Nightly"));
        Assert.True(OsVariants.IsKnown("Stable"));
        Assert.False(OsVariants.IsKnown("Bogus"));
    }

    [Fact]
    public void CustomIds_AreRecognised_AndMissingOnesAreUnknown()
    {
        Assert.True(OsVariants.IsCustom("Custom:venom"));
        Assert.False(OsVariants.IsCustom("Stable"));
        Assert.False(OsVariants.IsKnown("Custom:does-not-exist-" + Guid.NewGuid().ToString("N")));
        Assert.Equal("Custom build (removed)", OsVariants.Label("Custom:does-not-exist-" + Guid.NewGuid().ToString("N")));
        Assert.Equal("", OptiScalerService.IniSuffix("Custom:does-not-exist")); // unknown custom = flat = Stable templates
        Assert.Equal("_f5", OptiScalerService.IniSuffix("F5"));
    }

    [Fact]
    public void FindOptiScalerDll_PicksTheShallowestWithinThreeFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "b", "OptiScaler"));
            File.WriteAllText(Path.Combine(root, "a", "b", "OptiScaler.dll"), "x");
            File.WriteAllText(Path.Combine(root, "a", "b", "OptiScaler", "OptiScaler.dll"), "deeper");
            Assert.Equal(Path.Combine(root, "a", "b", "OptiScaler.dll"), OsCustomBuildService.FindOptiScalerDll(root));

            var deep = Path.Combine(root, "deep", "1", "2", "3", "4");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "OptiScaler.dll"), "x");
            Assert.Null(OsCustomBuildService.FindOptiScalerDll(Path.Combine(root, "deep")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/Venom-1107.7z?ex=abc&is=def", "Venom-1107")]
    [InlineData(@"C:\Downloads\v0.1.37.2-Optiscaler-F5.zip", "v0.1.37.2-Optiscaler-F5")]
    [InlineData(@"C:\Downloads\OptiScaler.dll", "OptiScaler build")]
    public void SuggestName_UsesTheFileName(string source, string expected) =>
        Assert.Equal(expected, OsCustomBuildService.SuggestName(source));

    [Fact]
    public void IsUrl_OnlyAcceptsWebLinks()
    {
        Assert.True(OsCustomBuildService.IsUrl("https://github.com/x/y/releases/download/v1/a.zip", out _));
        Assert.False(OsCustomBuildService.IsUrl(@"C:\builds\a.zip", out _));
        Assert.False(OsCustomBuildService.IsUrl("file:///C:/a.zip", out _));
    }
}
