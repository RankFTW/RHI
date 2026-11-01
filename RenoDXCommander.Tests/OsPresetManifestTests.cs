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
}
