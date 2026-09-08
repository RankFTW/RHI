using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class Dlss5AioServiceTests
{
    private static readonly Dlss5AioService.ReleaseAsset[] Assets =
    {
        new("DLSS5-ReShade-AIO-v2.1.3-32-bit.zip", "https://example.invalid/x86"),
        new("DLSS5-ReShade-AIO-v2.1.3-SHA256.txt", "https://example.invalid/checksum"),
        new("DLSS5-ReShade-AIO-v2.1.3-64-bit.zip", "https://example.invalid/x64"),
    };

    [Fact]
    public void SelectArchitectureAsset_Selects32BitZip()
    {
        var result = Dlss5AioService.SelectArchitectureAsset(Assets, is32Bit: true);
        Assert.NotNull(result);
        Assert.EndsWith("-32-bit.zip", result.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectArchitectureAsset_Selects64BitZip()
    {
        var result = Dlss5AioService.SelectArchitectureAsset(Assets, is32Bit: false);
        Assert.NotNull(result);
        Assert.EndsWith("-64-bit.zip", result.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectArchitectureAsset_RejectsUnrelatedZip()
    {
        var result = Dlss5AioService.SelectArchitectureAsset(
            new[] { new Dlss5AioService.ReleaseAsset("another-mod-64-bit.zip", "https://example.invalid") },
            is32Bit: false);
        Assert.Null(result);
    }

    [Fact]
    public void TryResolveUnderRoot_AcceptsPackageSubpath()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-aio-test");
        Assert.True(Dlss5AioService.TryResolveUnderRoot(
            root, "host64/reshade-shaders/Shaders/DLSS5_AIO_Feed.fx", out var result));
        Assert.StartsWith(Path.GetFullPath(root), result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveUnderRoot_RejectsZipTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-aio-test");
        Assert.False(Dlss5AioService.TryResolveUnderRoot(root, "../outside.dll", out _));
    }
}
