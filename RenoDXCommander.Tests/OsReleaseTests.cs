using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>OptiScaler release lists: preferred assets, version matching and which versions can be pinned.</summary>
public class OsReleaseTests
{
    private static string Rel(string tag, string published, params string[] assets) =>
        "{\"tag_name\":\"" + tag + "\",\"published_at\":\"" + published + "\",\"prerelease\":true,\"draft\":false,\"assets\":["
        + string.Join(",", assets.Select(a => "{\"name\":\"" + a + "\",\"updated_at\":\"" + published
            + "\",\"browser_download_url\":\"https://github.com/x/y/releases/download/" + tag + "/" + a + "\"}"))
        + "]}";

    [Fact]
    public void PreferredAsset_FallsBackWhenAReleaseDoesntHaveIt()
    {
        var json = "[" + Rel("v0.8.10", "2026-10-26T00:00:00Z", "OptiScaler-NR-v0.8.10.zip", "OptiScaler-NR-v0.8.10-rtx40-mfg.zip", "OptiScaler-NR-v0.8.10-SHA256SUMS.txt")
                 + "," + Rel("v0.7", "2026-09-01T00:00:00Z", "OptiScaler-NR-v0.7.zip") + "]";
        var list = OsReleaseService.ListReleases(json, @"-rtx40-mfg\.zip$", true);
        Assert.Equal(new[] { "v0.8.10", "v0.7" }, list.Select(r => r.Tag));
        Assert.Equal("OptiScaler-NR-v0.8.10-rtx40-mfg.zip", list[0].AssetName);
        Assert.Equal("OptiScaler-NR-v0.7.zip", list[1].AssetName);
    }

    [Theory]
    [InlineData("v0.1.37", "0.1.37", true)]
    [InlineData("0.1.37.2", "v0.1.37.2", true)]
    [InlineData("0.1.37", "0.1.37.2", false)]
    [InlineData(null, "1", false)]
    public void SameVersion_IgnoresTheVPrefix(string? a, string? b, bool same) =>
        Assert.Equal(same, OsReleaseService.SameVersion(a, b));

    [Fact]
    public void Pinning_IsForReleaseBasedVersionsOnly()
    {
        Assert.True(OsReleaseService.SupportsPinning("Stable"));
        Assert.True(OsReleaseService.SupportsPinning("F5"));
        Assert.True(OsReleaseService.SupportsPinning("DlssNr"));
        Assert.False(OsReleaseService.SupportsPinning("Nightly")); // has its own build picker
        Assert.False(OsReleaseService.SupportsPinning("Custom:anything"));
    }
}
