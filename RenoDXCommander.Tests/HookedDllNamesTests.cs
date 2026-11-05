using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// "&lt;name&gt;Hooked.dll" names for extras installed beside Ultimate ASI Loader, and the 20/30 FG
/// Unlock's name validation and build mapping for them.
/// </summary>
public class HookedDllNamesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-hooked-" + Guid.NewGuid().ToString("N"));

    public HookedDllNamesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; left for the OS to clean */ }
    }

    [Theory]
    [InlineData("version.dll", "versionHooked.dll")]
    [InlineData("winmm.dll", "winmmHooked.dll")]
    [InlineData("xinput1_3.dll", "xinput1_3Hooked.dll")]
    public void ToHooked_AddsSuffixBeforeExtension(string name, string expected)
        => Assert.Equal(expected, HookedDllNames.ToHooked(name));

    [Theory]
    [InlineData("versionHooked.dll", true, "version.dll")]
    [InlineData("VERSIONHOOKED.DLL", true, "VERSION.DLL")]
    [InlineData("version.dll", false, "")]
    [InlineData("Hooked.dll", false, "")]          // no base name
    [InlineData("versionHooked.asi", false, "")]   // only DLLs are chained
    [InlineData(null, false, "")]
    public void TryGetBase_RecognisesHookedNames(string? name, bool expected, string expectedBase)
    {
        Assert.Equal(expected, HookedDllNames.TryGetBase(name, out var b));
        Assert.Equal(expectedBase, b);
    }

    [Fact]
    public void BaseOf_LeavesPlainNamesAlone()
    {
        Assert.Equal("version.dll", HookedDllNames.BaseOf("versionHooked.dll"));
        Assert.Equal("dxgi.dll", HookedDllNames.BaseOf("dxgi.dll"));
    }

    [Fact]
    public void WithHooked_InsertsAfterAsiLoaderName()
    {
        var names = HookedDllNames.WithHooked(new[] { "version.dll", "winmm.dll", "dxgi.dll" }, "winmm.dll");
        Assert.Equal(new[] { "version.dll", "winmm.dll", "winmmHooked.dll", "dxgi.dll" }, names);
    }

    [Fact]
    public void WithHooked_NoAsiLoaderOrUnsupportedName_Unchanged()
    {
        var baseNames = new[] { "version.dll", "winmm.dll" };
        Assert.Equal(baseNames, HookedDllNames.WithHooked(baseNames, null));
        Assert.Equal(baseNames, HookedDllNames.WithHooked(baseNames, "dinput8.dll"));
    }

    [Fact]
    public void WithHooked_ExcludedName_Unchanged()
    {
        var baseNames = new[] { "version.dll", "binkw64.dll" };
        Assert.Equal(baseNames, HookedDllNames.WithHooked(baseNames, "binkw64.dll", n => n.StartsWith("bink")));
    }

    [Theory]
    [InlineData("version.dll", "versionHooked.dll")]
    [InlineData("winmm.dll", "version.dll")]
    [InlineData(null, "version.dll")]
    public void Recommended_PrefersVersionHookedWhenAsiLoaderIsVersion(string? ual, string expected)
        => Assert.Equal(expected, HookedDllNames.Recommended(ual));

    [Fact]
    public void IsHookedFor_MatchesOnlyAsiLoadersName()
    {
        Assert.True(HookedDllNames.IsHookedFor("versionHooked.dll", "version.dll"));
        Assert.False(HookedDllNames.IsHookedFor("winmmHooked.dll", "version.dll"));
        Assert.False(HookedDllNames.IsHookedFor("version.dll", "version.dll"));
        Assert.False(HookedDllNames.IsHookedFor("versionHooked.dll", null));
    }

    [Fact]
    public void MoveSentinel_MovesWhenTargetFree()
    {
        var from = Path.Combine(_root, "version.dll");
        var to   = Path.Combine(_root, "versionHooked.dll");
        File.WriteAllBytes(from + ".original", Array.Empty<byte>());
        HookedDllNames.MoveSentinel(from, to);
        Assert.False(File.Exists(from + ".original"));
        Assert.True(File.Exists(to + ".original"));
    }

    [Fact]
    public void MoveSentinel_KeepsRealBackupAtTarget()
    {
        var from = Path.Combine(_root, "versionHooked.dll");
        var to   = Path.Combine(_root, "version.dll");
        File.WriteAllBytes(from + ".original", Array.Empty<byte>());   // RHI's 0-byte marker
        File.WriteAllBytes(to + ".original", new byte[] { 1, 2, 3 });  // the game's real file
        HookedDllNames.MoveSentinel(from, to);
        Assert.False(File.Exists(from + ".original"));
        Assert.Equal(3, new FileInfo(to + ".original").Length);
    }

    [Fact]
    public void MoveSentinel_TwoRealBackups_LeavesBoth()
    {
        var from = Path.Combine(_root, "a.dll");
        var to   = Path.Combine(_root, "b.dll");
        File.WriteAllBytes(from + ".original", new byte[] { 9 });
        File.WriteAllBytes(to + ".original", new byte[] { 1, 2 });
        HookedDllNames.MoveSentinel(from, to);
        Assert.True(File.Exists(from + ".original"));
        Assert.Equal(2, new FileInfo(to + ".original").Length);
    }

    // ── 20/30 FG Unlock ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("version.dll", true)]
    [InlineData("versionHooked.dll", true)]
    [InlineData("winmmHooked.dll", true)]
    [InlineData("winhttp.dll", false)]
    [InlineData("winhttpHooked.dll", false)]
    public void Dlssg2030_AcceptsHookedFormOfSupportedNames(string name, bool expected)
        => Assert.Equal(expected, Dlssg20_30Service.IsSupportedProxyName(name));

    [Theory]
    [InlineData("version.dll", "version.dll")]
    [InlineData("versionHooked.dll", "version.dll")]
    [InlineData("winmmHooked.dll", @"alternatives\winmm.dll")]
    [InlineData("dxgi.dll", @"alternatives\dxgi.dll")]
    public void Dlssg2030_HookedNameUsesBaseBuild(string name, string expectedRel)
        => Assert.Equal(expectedRel, Dlssg20_30Service.StagedProxyRelPath(name));

    [Fact]
    public void Dlssg2030_UnsupportedName_HasNoBuild()
        => Assert.Null(Dlssg20_30Service.StagedProxyRelPath("winhttp.dll"));
}
