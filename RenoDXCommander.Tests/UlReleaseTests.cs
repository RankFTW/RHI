using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>ReLimiter release list for the per-game Version picker.</summary>
public class UlReleaseTests
{
    private static string Rel(string tag, string published, bool prerelease = false, bool draft = false, params string[] assets) =>
        "{\"tag_name\":\"" + tag + "\",\"published_at\":\"" + published + "\",\"prerelease\":" + (prerelease ? "true" : "false")
        + ",\"draft\":" + (draft ? "true" : "false") + ",\"assets\":["
        + string.Join(",", assets.Select(a => "{\"name\":\"" + a
            + "\",\"browser_download_url\":\"https://github.com/RankFTW/ReLimiter/releases/download/" + tag + "/" + a + "\"}"))
        + "]}";

    [Fact]
    public void Parse_NewestFirst_WithBothBitnesses()
    {
        var json = "[" + Rel("v3.3.0", "2026-06-27T12:00:00Z", assets: "relimiter.addon64")
                 + "," + Rel("v3.3.5", "2026-08-10T00:00:00Z", assets: new[] { "relimiter.addon32", "relimiter.addon64" }) + "]";
        var list = UlReleaseService.ParseReleases(json);
        Assert.Equal(new[] { "v3.3.5", "v3.3.0" }, list.Select(r => r.Tag));
        Assert.NotNull(list[0].UrlFor(true));
        Assert.NotNull(list[0].UrlFor(false));
        Assert.Null(list[1].UrlFor(true)); // no 32-bit build before v3.3.1
        Assert.EndsWith("/v3.3.0/relimiter.addon64", list[1].UrlFor(false));
    }

    [Fact]
    public void Parse_SkipsDraftsPrereleasesAndReleasesWithoutTheAddon()
    {
        var json = "[" + Rel("v4.0.0-beta", "2026-09-01T00:00:00Z", prerelease: true, assets: "relimiter.addon64")
                 + "," + Rel("v3.9.9", "2026-09-02T00:00:00Z", draft: true, assets: "relimiter.addon64")
                 + "," + Rel("v3.3.6", "2026-09-03T00:00:00Z", assets: "source.zip")
                 + "," + Rel("v3.3.5", "2026-08-10T00:00:00Z", assets: "relimiter.addon64") + "]";
        Assert.Equal(new[] { "v3.3.5" }, UlReleaseService.ParseReleases(json).Select(r => r.Tag));
    }

    [Fact]
    public void Parse_BadJson_IsEmpty() => Assert.Empty(UlReleaseService.ParseReleases("{not json"));

    [Theory]
    [InlineData("v3.3.5", "v3.3.5")]
    [InlineData("..\\..\\evil", "_.._evil")]
    [InlineData("", "_")]
    public void SafeTag_KeepsPathsInsideThePinnedFolder(string tag, string expected) =>
        Assert.Equal(expected, UlReleaseService.SafeTag(tag));
}
