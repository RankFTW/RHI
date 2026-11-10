using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Which build the shared Vulkan ReShade layer should hold, and how it's compared.</summary>
public class VulkanLayerChannelTests
{
    [Fact]
    public void NoOverrides_IsStable() =>
        Assert.Equal("Stable", VulkanLayerService.ResolveLayerChannel(new string?[] { null, null }));

    [Fact]
    public void ExplicitStable_DoesNotHideAnotherGamesOverride() =>
        // Update All's resolver returns "Stable" for games without an override; it must not win
        Assert.Equal("Nightly", VulkanLayerService.ResolveLayerChannel(new string?[] { "Stable", null, "Nightly" }));

    [Fact]
    public void LegacyVersion_IsUsed() =>
        Assert.Equal("6.7.3", VulkanLayerService.ResolveLayerChannel(new string?[] { null, "6.7.3" }));

    [Fact]
    public void SameSizeDifferentContent_IsNotIdentical()
    {
        // Stable and Nightly builds can be exactly the same size
        var dir = Path.Combine(Path.GetTempPath(), "rhi_vk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.dll");
            var b = Path.Combine(dir, "b.dll");
            var c = Path.Combine(dir, "c.dll");
            File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(b, new byte[] { 1, 2, 3, 5 });
            File.WriteAllBytes(c, new byte[] { 1, 2, 3, 4 });
            Assert.False(VulkanLayerService.FilesIdentical(a, b));
            Assert.True(VulkanLayerService.FilesIdentical(a, c));
        }
        finally { Directory.Delete(dir, true); }
    }
}
