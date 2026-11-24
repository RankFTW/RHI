using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>FrameWarp install helpers: the Unreal Engine cvar, finding Engine.ini, manifest overrides.</summary>
public class FrameWarpTests
{
    [Fact]
    public void Cvar_AddedUnderConsoleVariables_KeepsOtherLines()
    {
        var text = "[Core.System]\r\nPaths=a\r\n[ConsoleVariables]\r\nr.Foo=1\r\n";
        var on = FrameWarpService.SetEngineCvarInText(text, enable: true);
        Assert.Equal("[Core.System]\r\nPaths=a\r\n[ConsoleVariables]\r\nr.Streamline.ForceTagging=1\r\nr.Foo=1\r\n", on);
        Assert.Equal(on, FrameWarpService.SetEngineCvarInText(on, enable: true)); // no second copy
        Assert.Equal(text, FrameWarpService.SetEngineCvarInText(on, enable: false));
    }

    [Fact]
    public void Cvar_AddsSectionWhenMissing_AndToEmptyFile()
    {
        Assert.Equal("[Core.System]\nPaths=a\n\n[ConsoleVariables]\nr.Streamline.ForceTagging=1\n",
            FrameWarpService.SetEngineCvarInText("[Core.System]\nPaths=a\n", enable: true));
        Assert.Equal("[ConsoleVariables]\nr.Streamline.ForceTagging=1\n", FrameWarpService.SetEngineCvarInText("", enable: true));
        Assert.Equal("", FrameWarpService.SetEngineCvarInText("", enable: false));
    }

    [Fact]
    public void EngineIni_FoundForUnrealLayout_IncludingStoreSubfolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-fw-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var local = Path.Combine(root, "Local");
            var bin = Path.Combine(root, "Game", "Returnal", "Binaries", "Win64");
            Directory.CreateDirectory(bin);
            Assert.Null(FrameWarpService.FindEngineIni(bin, local)); // no Saved folder yet

            var cfg = Path.Combine(local, "Returnal", "Steam", "Saved", "Config", "Windows");
            Directory.CreateDirectory(cfg);
            Assert.Equal(Path.Combine(cfg, "Engine.ini"), FrameWarpService.FindEngineIni(bin, local));

            Assert.Null(FrameWarpService.FindEngineIni(Path.Combine(root, "Game", "bin", "x64"), local)); // not Unreal
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ManifestOverride_NeedsHttpsAndAFullHash()
    {
        var good = new Dictionary<string, string>
        {
            ["latewarp"] = "https://example.com/lw.zip",
            ["latewarpSha256"] = new string('a', 64),
        };
        try
        {
            FrameWarpService.ApplyManifestUrls(good);
            Assert.Equal("https://example.com/lw.zip", FrameWarpService.LatewarpUrl);
            Assert.Equal(new string('A', 64), FrameWarpService.LatewarpSha256);

            FrameWarpService.ApplyManifestUrls(new Dictionary<string, string> { ["latewarp"] = "https://example.com/lw.zip" }); // no hash
            Assert.Equal(FrameWarpService.DefaultLatewarpUrl, FrameWarpService.LatewarpUrl);

            FrameWarpService.ApplyManifestUrls(new Dictionary<string, string> { ["latewarp"] = "http://example.com/lw.zip", ["latewarpSha256"] = new string('a', 64) });
            Assert.Equal(FrameWarpService.DefaultLatewarpUrl, FrameWarpService.LatewarpUrl);
            Assert.Equal(FrameWarpService.DefaultLatewarpSha256, FrameWarpService.LatewarpSha256);
        }
        finally { FrameWarpService.ApplyManifestUrls(null); }
    }

    [Theory]
    [InlineData("v2.4.0", "2.4.0")]
    [InlineData("2.4.0", "2.4.0")]
    public void VersionFromTag(string tag, string expected) => Assert.Equal(expected, FrameWarpService.VersionFromTag(tag));
}
