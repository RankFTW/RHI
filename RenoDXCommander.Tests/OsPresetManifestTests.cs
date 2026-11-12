using System.Text.Json;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>optiscaler_presets.json (live on GitHub) must parse and match the app's built-in copies.</summary>
public class OsPresetManifestTests
{
    private static string RepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, name))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, name);
    }

    [Fact]
    public void RepoManifest_ParsesAndMatchesBuiltIns()
    {
        var parsed = OsPresetService.Parse(File.ReadAllText(RepoFile("optiscaler_presets.json")));
        Assert.NotNull(parsed);
        Assert.Equal(
            JsonSerializer.Serialize(OsPresetService.BuiltInPresets),
            JsonSerializer.Serialize(parsed));
    }

    [Fact]
    public void Parse_DropsUnnamedPresets_AndRejectsBadJson()
    {
        Assert.Null(OsPresetService.Parse("not json"));
        Assert.Null(OsPresetService.Parse("{\"version\":1,\"presets\":[{\"fgOutput\":\"xefg\"}]}"));
        var one = OsPresetService.Parse("{\"version\":1,\"presets\":[{\"name\":\"A\"},{\"fgOutput\":\"xefg\"}]}");
        Assert.Single(one!);
    }

    [Fact]
    public void Parse_ReadsPresetsV2_AfterPresets()
    {
        var both = OsPresetService.Parse(
            "{\"version\":1,\"presets\":[{\"name\":\"A\"}],\"presetsV2\":[{\"name\":\"B\",\"installAnrFg\":true}]}");
        Assert.Equal(new[] { "A", "B" }, both!.Select(p => p.Name));
        Assert.True(both[1].InstallAnrFg);
        Assert.Single(OsPresetService.Parse("{\"version\":1,\"presetsV2\":[{\"name\":\"B\"}]}")!);
    }

    [Theory]
    [InlineData("auto", "dlssg", true)]
    [InlineData("nvngxfg", "auto", true)]
    [InlineData("NvngxFG", "auto", true)]
    [InlineData("upscaler", "xefg", false)]
    [InlineData(null, null, false)]
    public void UsesNvngxReplacement_WithDlssgOutputOrNvngxInput(string? fgInput, string? fgOutput, bool expected) =>
        Assert.Equal(expected, OptiScalerService.UsesNvngxReplacement(fgInput, fgOutput));

    [Fact]
    public void OptiScalerUpdate_KeepsPluginsAndCustomFolders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "plugins"));
            Directory.CreateDirectory(Path.Combine(dir, "custom", AnrFgService.ProviderName, "dlssg"));
            Directory.CreateDirectory(Path.Combine(dir, "Streamline"));
            File.WriteAllText(Path.Combine(dir, "custom", AnrFgService.ProviderName, "dlssg", "nvngx_dlssg.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "old.dll"), "x");

            OptiScalerService.DeleteDirKeepingPlugins(dir);

            Assert.True(Directory.Exists(Path.Combine(dir, "plugins")));
            Assert.True(File.Exists(Path.Combine(dir, "custom", AnrFgService.ProviderName, "dlssg", "nvngx_dlssg.dll")));
            Assert.False(Directory.Exists(Path.Combine(dir, "Streamline")));
            Assert.False(File.Exists(Path.Combine(dir, "old.dll")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SupportsCustomFgProviders_LooksForTheBuildsCustomDlssgText()
    {
        var with = Path.GetTempFileName();
        var without = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(with, new byte[] { 0, 1 }.Concat("xxCustom DLSSG {} initializedxx"u8.ToArray()).ToArray());
            File.WriteAllText(without, "OptiScaler 0.9.4");
            Assert.True(OptiScalerService.SupportsCustomFgProviders(with));
            Assert.False(OptiScalerService.SupportsCustomFgProviders(without));
            Assert.False(OptiScalerService.SupportsCustomFgProviders(with + ".missing"));
        }
        finally { File.Delete(with); File.Delete(without); }
    }

    [Fact]
    public void FindCustomFgProviders_ListsFoldersWithADlssgDll()
    {
        var game = Path.Combine(Path.GetTempPath(), "rhi-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var custom = Path.Combine(game, "OptiScaler", "custom");
            Directory.CreateDirectory(Path.Combine(custom, AnrFgService.ProviderName, "dlssg"));
            File.WriteAllText(Path.Combine(custom, AnrFgService.ProviderName, "dlssg", "nvngx_dlssg.dll"), "x");
            Directory.CreateDirectory(Path.Combine(custom, "UpscalerOnly", "ffx"));
            File.WriteAllText(Path.Combine(custom, "UpscalerOnly", "ffx", "a.dll"), "x");
            Directory.CreateDirectory(Path.Combine(custom, "Empty", "dlssg"));

            Assert.Equal(new[] { AnrFgService.ProviderName }, OptiScalerService.FindCustomFgProviders(game));
            Assert.True(AnrFgService.IsInstalledIn(game));
            Assert.Empty(OptiScalerService.FindCustomFgProviders(Path.Combine(game, "missing")));
        }
        finally { Directory.Delete(game, recursive: true); }
    }
}
