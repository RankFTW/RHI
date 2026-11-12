using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>HSF (Capt-Hunter/HSF): release parsing, zip layout, the D3D12 route RHI writes up front, and DLSS detection skipping HSF's own copies.</summary>
public class HsfServiceTests
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    [Theory]
    [InlineData(true, false, false, null, HsfService.Route.BringIn)]
    [InlineData(true, false, true, "2.14.0", HsfService.Route.GameStreamline)]
    [InlineData(true, false, true, "1.5.2", HsfService.Route.None)]   // HSF leaves Streamline 1.x alone
    [InlineData(true, false, true, null, HsfService.Route.GameStreamline)]
    [InlineData(true, false, null, null, HsfService.Route.None)]      // DLSS scan not run yet: HSF picks
    [InlineData(true, true, false, null, HsfService.Route.None)]      // D3D12 routes are 64-bit only
    [InlineData(false, false, false, null, HsfService.Route.None)]
    public void PickRoute(bool dx12, bool is32, bool? hasSl, string? slVer, HsfService.Route expected) =>
        Assert.Equal(expected, HsfService.PickRoute(dx12, is32, hasSl, slVer));

    [Theory]
    [InlineData("hsf.addon64", "hsf.addon64")]
    [InlineData("HSF-0.9.6/hsf.addon32", "hsf.addon32")]
    [InlineData("HSF.ini", "HSF.ini")]
    [InlineData("reshade-shaders/Shaders/HSF.fx", "HSF.fx")]
    [InlineData("HSF_streamline/sl.interposer.dll", @"HSF_streamline\sl.interposer.dll")]
    [InlineData("HSF-0.9.6/HSF_streamline/hsf-dlssg-helper64.exe", @"HSF_streamline\hsf-dlssg-helper64.exe")]
    [InlineData("HSF_streamline/hsf_dlssg.log", null)]
    [InlineData("HSF_streamline/", null)]
    [InlineData("licenses/Streamline-license.txt", null)]
    [InlineData("README.txt", null)]
    [InlineData("LICENSE.txt", null)]
    public void StagedPathFor_KeepsWhatGamesNeed(string entry, string? expected) =>
        Assert.Equal(expected, HsfService.StagedPathFor(entry));

    [Fact]
    public void ParseNewestRelease_TakesPreReleasesAndSkipsDrafts()
    {
        const string json = """
        [
          { "tag_name": "v0.9.7", "draft": true, "prerelease": true,
            "assets": [ { "name": "HSF-0.9.7.zip", "url": "https://api.github.com/a/7", "browser_download_url": "https://x/7" } ] },
          { "tag_name": "v0.9.6", "draft": false, "prerelease": true,
            "assets": [ { "name": "notes.txt", "url": "https://api.github.com/a/n" },
                        { "name": "HSF-0.9.6.zip", "url": "https://api.github.com/a/6", "browser_download_url": "https://x/6" } ] }
        ]
        """;
        var rel = HsfService.ParseNewestRelease(json);
        Assert.NotNull(rel);
        Assert.Equal("0.9.6", rel!.Version);
        Assert.Equal("https://api.github.com/a/6", rel.AssetApiUrl);
        Assert.Null(HsfService.ParseNewestRelease("{}"));
    }

    [Fact]
    public void PresetRoute_WritesOnceAndNeverOverHsfsOwnChoice()
    {
        var game = Path.Combine(Path.GetTempPath(), "rhi-hsf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(game);
            var ini = Path.Combine(game, HsfService.IniName);
            File.WriteAllText(ini, "; HSF settings\r\n[HSF]\r\n");
            var svc = new HsfService(new HttpClient(), new NullReporter(), new GitHubETagCache());

            Assert.True(svc.PresetRoute(game, HsfService.Route.BringIn));
            Assert.Equal("2", Dlssg20_30Service.ReadIniValue(ini, "HSF", "SlBringIn"));
            // HSF (or the user) already has a route: left alone
            Assert.False(svc.PresetRoute(game, HsfService.Route.GameStreamline));
            Assert.Null(Dlssg20_30Service.ReadIniValue(ini, "HSF", "InGameDlssg"));
            Assert.False(svc.PresetRoute(game, HsfService.Route.None));

            File.WriteAllText(ini, "[HSF]\r\nInGameDlssg=0\r\n"); // switched off by hand
            Assert.False(svc.PresetRoute(game, HsfService.Route.BringIn));
            Assert.Null(Dlssg20_30Service.ReadIniValue(ini, "HSF", "SlBringIn"));
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    [Theory]
    [InlineData(@"C:\Games\X\HSF_streamline", true)]
    [InlineData(@"C:\Games\X\hsf_streamline\", true)]
    [InlineData(@"C:\Games\X\Streamline", false)]
    public void DlssScan_SkipsHsfStreamlineFolder(string dir, bool skipped) =>
        Assert.Equal(skipped, DlssStreamlineService.IsModOwnedFolder(dir));

    [Fact]
    public void DlssCache_PathInsideHsfStreamline_IsRejected()
    {
        Assert.True(DlssStreamlineService.IsInModOwnedFolder(@"C:\Games\X\HSF_streamline\nvngx_dlssg.dll"));
        Assert.False(DlssStreamlineService.IsInModOwnedFolder(@"C:\Games\X\bin\nvngx_dlssg.dll"));
        Assert.False(DlssStreamlineService.IsInModOwnedFolder(null));
    }

    [Theory]
    [InlineData(@"HSF_streamline\mods\MFGUnlocker.dll", true)]
    [InlineData(@"bin\x64\HSF_streamline\mods\MFGUnlocker.dll", true)]
    [InlineData(@"..\Binaries\hsf_streamline/mods/mfgunlocker.dll", true)]
    [InlineData("version.dll", false)]
    [InlineData(@"OptiScaler\plugins\MFGUnlocker.asi", false)]
    [InlineData(@"NotHSF_streamline\mods\MFGUnlocker.dll", false)]
    [InlineData(null, false)]
    public void MfgUnlocker_HsfModsInstall_IsRecognised(string? installedAs, bool expected) =>
        Assert.Equal(expected, Rtx40MfgService.IsHsfModsInstall(installedAs));

    [Fact]
    public void MfgUnlocker_HsfModsRelPath_IsInsideHsfStreamline()
    {
        var game = Path.Combine(Path.GetTempPath(), "rhi-hsf-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(game);
        try
        {
            var rel = Rtx40MfgService.HsfModsRelPath(game);
            Assert.Equal(Path.Combine(HsfService.StreamlineFolder, "mods", Rtx40MfgService.HsfModsFileName), rel);
            Assert.True(Rtx40MfgService.IsHsfModsInstall(rel));
        }
        finally { Directory.Delete(game, recursive: true); }
    }
}
