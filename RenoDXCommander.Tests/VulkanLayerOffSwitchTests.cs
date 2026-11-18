using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>The Vulkan layer's off-switch variable is renamed so DOOM Eternal can't turn ReShade off.</summary>
public class VulkanLayerOffSwitchTests
{
    private const string Stock = "{\n\t\"layer\": {\n\t\t\"name\": \"VK_LAYER_reshade\",\n\t\t\"disable_environment\": {\n\t\t\t\"DISABLE_VK_LAYER_reshade_1\": \"1\"\n\t\t}\n\t}\n}";

    [Fact]
    public void StockManifest_RenamesOnlyTheVariable()
    {
        var result = VulkanLayerService.RenameOffSwitch(Stock);
        Assert.Equal(Stock.Replace("DISABLE_VK_LAYER_reshade_1", "DISABLE_VK_LAYER_reshade_2"), result);
    }

    [Fact]
    public void AlreadyRenamed_NoChange() =>
        Assert.Null(VulkanLayerService.RenameOffSwitch(Stock.Replace("_reshade_1", "_reshade_2")));

    [Fact]
    public void GeneratedManifest_HasRenamedOffSwitch()
    {
        var json = VulkanLayerService.GenerateManifestJson();
        Assert.Contains("\"DISABLE_VK_LAYER_reshade_2\": \"1\"", json);
        Assert.DoesNotContain("DISABLE_VK_LAYER_reshade_1", json);
        System.Text.Json.JsonDocument.Parse(json).Dispose();
    }

    [Fact]
    public void BundledManifest_HasRenamedOffSwitch()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d != null && path == null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "RenoDXCommander", "ReShade64.json");
            if (File.Exists(p)) path = p;
        }
        Assert.NotNull(path);
        var json = File.ReadAllText(path!);
        Assert.Contains("\"DISABLE_VK_LAYER_reshade_2\"", json);
        Assert.Null(VulkanLayerService.RenameOffSwitch(json));
    }
}
