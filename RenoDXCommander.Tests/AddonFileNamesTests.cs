using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Duplicate-download addon names ("renodx-x (1).addon64") count as the addon itself.</summary>
public class AddonFileNamesTests
{
    [Theory]
    [InlineData("renodx-ue-extended (1).addon64", "renodx-ue-extended.addon64")]
    [InlineData("renodx-ue-extended(2).addon64", "renodx-ue-extended.addon64")]
    [InlineData("renodx-cyberpunk2077 (12).addon64", "renodx-cyberpunk2077.addon64")]
    [InlineData("renodx-game - Copy.addon64", "renodx-game.addon64")]
    [InlineData("renodx-game - Copy (2).addon32", "renodx-game.addon32")]
    [InlineData("RENODX-GAME (1).ADDON64", "RENODX-GAME.ADDON64")]
    public void StripsDuplicateSuffixes(string name, string expected) =>
        Assert.Equal(expected, AddonFileNames.Canonical(name));

    [Theory]
    [InlineData("renodx-ue-extended.addon64")]
    [InlineData("renodx-game (beta).addon64")]   // not a number: a real name
    [InlineData("renodx-game-1.addon64")]
    [InlineData("renodx-game (1).dll")]           // not an addon
    [InlineData("")]
    public void LeavesRealNamesAlone(string name) =>
        Assert.Equal(name, AddonFileNames.Canonical(name));

    [Fact]
    public void SameAddonIgnoresCaseAndSuffix() =>
        Assert.True(AddonFileNames.SameAddon("Renodx-UE-Extended (1).addon64", "renodx-ue-extended.addon64"));

    [Fact]
    public void DifferentAddonsDiffer() =>
        Assert.False(AddonFileNames.SameAddon("renodx-game2 (1).addon64", "renodx-game.addon64"));

    [Fact]
    public void RemoveDuplicatesKeepsTheRealFileAndOtherAddons()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-addon-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in new[] { "renodx-game.addon64", "renodx-game (1).addon64", "renodx-game - Copy.addon64", "renodx-other (1).addon64" })
                File.WriteAllText(Path.Combine(dir, f), "x");
            var removed = AddonFileNames.RemoveDuplicates(dir, "renodx-game.addon64");
            Assert.Equal(2, removed.Count);
            Assert.True(File.Exists(Path.Combine(dir, "renodx-game.addon64")));
            Assert.True(File.Exists(Path.Combine(dir, "renodx-other (1).addon64")));
            Assert.False(File.Exists(Path.Combine(dir, "renodx-game (1).addon64")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
