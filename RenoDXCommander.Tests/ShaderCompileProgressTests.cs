using System.Text;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Compile shaders now: cache header names and following NvOSC through the list of app caches.</summary>
public class ShaderCompileProgressTests
{
    private static byte[] Header(string magic, string name)
    {
        var b = new byte[64];
        Encoding.ASCII.GetBytes(magic).CopyTo(b, 0);
        Encoding.ASCII.GetBytes(name).CopyTo(b, 17);
        return b;
    }

    [Fact]
    public void ParseExeName_ReadsNameAfterMagic()
        => Assert.Equal("pbz.exe", NvShaderCompilerService.ParseExeName(Header("nvph", "pbz.exe")));

    [Fact]
    public void ParseExeName_RejectsOtherFiles()
    {
        Assert.Null(NvShaderCompilerService.ParseExeName(Header("abcd", "pbz.exe")));
        Assert.Null(NvShaderCompilerService.ParseExeName(Header("nvph", "")));
        Assert.Null(NvShaderCompilerService.ParseExeName(new byte[10]));
    }

    [Fact]
    public void Advance_SkipsFilesFinishedBetweenChecks()
        => Assert.Equal(3, NvShaderCompilerService.Advance(0, 6, i => i == 3));

    [Fact]
    public void Advance_StaysPutWhenNothingIsOpen()
        => Assert.Equal(2, NvShaderCompilerService.Advance(2, 6, _ => false));

    [Fact]
    public void Advance_NeverGoesBack()
        => Assert.Equal(4, NvShaderCompilerService.Advance(4, 6, i => i == 1));

    private static readonly List<ShaderCacheEntry> Plan =
    [
        new("a", "code.exe", 100),
        new("b", "msedgewebview2.exe", 100),
        new("c", "pbz.exe", 800),
    ];

    [Fact]
    public void Describe_NotStarted()
    {
        var p = NvShaderCompilerService.Describe(Plan, 0, started: false);
        Assert.Equal((0, 3), (p.Done, p.Total));
        Assert.Null(p.CurrentApp);
    }

    [Fact]
    public void Describe_ThirdApp_WeightsBarBySize()
    {
        var p = NvShaderCompilerService.Describe(Plan, 2, started: true);
        Assert.Equal((3, 3), (p.Done, p.Total));
        Assert.Equal("pbz.exe", p.CurrentApp);
        Assert.Equal(0.2, p.Fraction!.Value, 3); // 200 of 1000 bytes done
    }

    [Fact]
    public void Describe_EmptyPlan_HasNoTotal()
        => Assert.Equal(0, NvShaderCompilerService.Describe([], 0, started: true).Total);
}
