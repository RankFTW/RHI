using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Recommended OptiScaler forks: picking the release and asset from a GitHub releases list.</summary>
public class OsForkTests
{
    private static string Rel(string tag, string published, bool pre = false, bool draft = false, params (string Name, string Updated)[] assets) =>
        "{\"tag_name\":\"" + tag + "\",\"published_at\":\"" + published + "\",\"prerelease\":" + (pre ? "true" : "false")
        + ",\"draft\":" + (draft ? "true" : "false") + ",\"assets\":["
        + string.Join(",", assets.Select(a => "{\"name\":\"" + a.Name + "\",\"updated_at\":\"" + a.Updated
            + "\",\"browser_download_url\":\"https://github.com/x/y/releases/download/" + tag + "/" + a.Name + "\"}"))
        + "]}";

    [Theory]
    [InlineData("OptiScaler-Venom_nightly-20261107.7z", true)]
    [InlineData("v0.1.37.2-Optiscaler-F5.zip", true)]
    [InlineData("OptiScaler-NR-v0.9.86.zip", true)]
    [InlineData("Optional-Vendor-Neutral-DLSS5-Forwarder.zip", false)]
    [InlineData("OptiScaler-Venom_nightly-20261107.7z.sha256", false)]
    [InlineData("OptiScaler-DLSSNR-Troubleshooting.md", false)]
    [InlineData("Source code.zip", false)]
    public void ScoreAsset_KeepsBuildsAndSkipsCompanions(string name, bool usable) =>
        Assert.Equal(usable, OsForkService.ScoreAsset(name) > 0);

    [Fact]
    public void PickRelease_NewestByDate_SkipsDraftsAndReleasesWithoutBuilds()
    {
        var json = "[" + string.Join(",",
            Rel("v0.8.10", "2026-11-05T10:00:00Z", pre: true, assets: ("OptiScaler-v0.8.10.zip", "2026-11-05T10:00:00Z")),
            Rel("v0.8.91", "2026-11-01T10:00:00Z", pre: true, assets: ("OptiScaler-v0.8.91.zip", "2026-11-01T10:00:00Z")),
            Rel("v0.9.0", "2026-11-06T10:00:00Z", draft: true, assets: ("OptiScaler-v0.9.0.zip", "2026-11-06T10:00:00Z")),
            Rel("notes", "2026-11-07T10:00:00Z", assets: ("readme.md", "2026-11-07T10:00:00Z"))) + "]";
        var r = OsForkService.PickRelease(json, new ManifestOsFork { Repo = "x/y" })!;
        Assert.Equal("v0.8.10", r.Tag);
        Assert.Equal("OptiScaler-v0.8.10.zip", r.AssetName);
    }

    [Fact]
    public void PickRelease_PrereleasesCanBeExcluded()
    {
        var json = "[" + Rel("v2-pre", "2026-11-05T00:00:00Z", pre: true, assets: ("OptiScaler.zip", "2026-11-05T00:00:00Z"))
                 + "," + Rel("v1", "2026-10-01T00:00:00Z", assets: ("OptiScaler.zip", "2026-10-01T00:00:00Z")) + "]";
        Assert.Equal("v2-pre", OsForkService.PickRelease(json, new ManifestOsFork { Repo = "x/y" })!.Tag);
        Assert.Equal("v1", OsForkService.PickRelease(json, new ManifestOsFork { Repo = "x/y", IncludePrereleases = false })!.Tag);
    }

    [Fact]
    public void PickRelease_OneReleaseManyBuilds_PicksNewestUpload_OrThePattern()
    {
        var json = "[" + Rel("nightly", "2026-09-05T00:00:00Z", pre: true, assets: new[]
        {
            ("OptiScaler_fork_20260901.7z", "2026-09-01T00:00:00Z"),
            ("OptiScaler_fork_20260905.7z", "2026-09-05T00:00:00Z"),
            ("OptiScaler_fork_20260905_with_DLSS.7z", "2026-09-05T00:01:00Z"),
        }) + "]";
        Assert.Equal("OptiScaler_fork_20260905_with_DLSS.7z", OsForkService.PickRelease(json, new ManifestOsFork { Repo = "x/y" })!.AssetName);
        var plain = OsForkService.PickRelease(json, new ManifestOsFork { Repo = "x/y", AssetPattern = @"_\d{8}\.7z$" })!;
        Assert.Equal("OptiScaler_fork_20260905.7z", plain.AssetName);
    }

    [Fact]
    public void ForkVariants_AreRecognised()
    {
        Assert.True(OsVariants.IsFork("Fork:venom"));
        Assert.False(OsVariants.IsFork("Custom:venom"));
        Assert.Equal("OptiScaler fork (removed)", OsVariants.Label("Fork:no-such-fork-" + Guid.NewGuid().ToString("N")[..8]));
    }
}
