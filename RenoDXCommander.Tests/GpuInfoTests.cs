using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>GPU names from Windows → generation (decides whether Universal MFG Unlocker is needed).</summary>
public class GpuInfoTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", GpuFamily.Rtx50)]
    [InlineData("NVIDIA GeForce RTX 5070 Ti Laptop GPU", GpuFamily.Rtx50)]
    [InlineData("NVIDIA GeForce RTX 5090 D", GpuFamily.Rtx50)]
    [InlineData("NVIDIA GeForce RTX 4090", GpuFamily.Rtx40)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", GpuFamily.Rtx40)]
    [InlineData("NVIDIA GeForce RTX 3080 Ti", GpuFamily.Rtx30)]
    [InlineData("NVIDIA GeForce RTX 3050 6GB Laptop GPU", GpuFamily.Rtx30)]
    [InlineData("NVIDIA GeForce RTX 2080 SUPER", GpuFamily.Rtx20)]
    [InlineData("NVIDIA GeForce RTX 2060", GpuFamily.Rtx20)]
    [InlineData("NVIDIA TITAN RTX", GpuFamily.Rtx20)]
    [InlineData("Quadro RTX 5000", GpuFamily.Rtx20)]
    [InlineData("NVIDIA RTX A4000", GpuFamily.Rtx30)]
    [InlineData("NVIDIA RTX A500 Laptop GPU", GpuFamily.Rtx30)]
    [InlineData("NVIDIA RTX 2000 Ada Generation", GpuFamily.Rtx40)]
    [InlineData("NVIDIA RTX 6000 Ada Generation", GpuFamily.Rtx40)]
    [InlineData("NVIDIA RTX PRO 6000 Blackwell Workstation Edition", GpuFamily.Rtx50)]
    [InlineData("NVIDIA GeForce RTX 6090", GpuFamily.Newer)]
    [InlineData("NVIDIA GeForce GTX 1080 Ti", GpuFamily.NotRtx)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", GpuFamily.NotRtx)]
    [InlineData("NVIDIA GeForce MX450", GpuFamily.NotRtx)]
    [InlineData("AMD Radeon RX 7900 XTX", GpuFamily.NotRtx)]
    [InlineData("", GpuFamily.NotRtx)]
    [InlineData(null, GpuFamily.NotRtx)]
    public void FromName(string? name, GpuFamily expected) =>
        Assert.Equal(expected, GpuInfoService.FromName(name));

    [Theory]
    [InlineData(GpuFamily.Rtx20, true)]
    [InlineData(GpuFamily.Rtx30, true)]
    [InlineData(GpuFamily.Rtx40, true)]
    [InlineData(GpuFamily.Rtx50, false)]
    [InlineData(GpuFamily.Newer, false)]
    [InlineData(GpuFamily.NotRtx, false)]
    public void NeedsMfgUnlocker(GpuFamily family, bool expected) =>
        Assert.Equal(expected, GpuInfoService.NeedsMfgUnlocker(family));

    [Fact]
    public void Primary_PicksTheNewestRtxCard()
    {
        var gpus = new[]
        {
            new NvidiaGpu("NVIDIA GeForce GTX 1050", GpuFamily.NotRtx),
            new NvidiaGpu("NVIDIA GeForce RTX 3060", GpuFamily.Rtx30),
            new NvidiaGpu("NVIDIA GeForce RTX 2070", GpuFamily.Rtx20),
        };
        Assert.Equal("NVIDIA GeForce RTX 3060", GpuInfoService.Primary(gpus)!.Name);
        Assert.Null(GpuInfoService.Primary(Array.Empty<NvidiaGpu>()));
    }
}
