using System.Text.Json;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>RtxEncoreConfig edits single values in rtx-encore.jsonc without losing comments or other lines.</summary>
public class RtxEncoreConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-encore-tests-" + Guid.NewGuid().ToString("N"));
    private string PathOf => System.IO.Path.Combine(_dir, RtxEncoreConfig.FileName);

    public RtxEncoreConfigTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string Sample =
        "{\r\n" +
        "  \"configVersion\": 4,                        // Settings apply live.\r\n" +
        "  \"renderingEngines\": {\r\n" +
        "    \"reconstructionEngine\": \"game\",          // game or xess. See https://example.com/a//b\r\n" +
        "    \"frameGenerationEngine\": \"game\"          // game, dlss-fg or xefg. Restart.\r\n" +
        "  },\r\n" +
        "  \"frameGeneration\": {\r\n" +
        "    \"mode\": \"game\",                          // fixed, dynamic or game.\r\n" +
        "    \"multiplier\": 4,                         // 2 to 6.\r\n" +
        "    \"flipMeteringBypass\": false              // Restart.\r\n" +
        "  },\r\n" +
        "  \"other\": {\r\n" +
        "    \"nested\": {                             // A nested object with a same-named key.\r\n" +
        "      \"multiplier\": 9\r\n" +
        "    },\r\n" +
        "    \"multiplier\": 7\r\n" +
        "  },\r\n" +
        "  \"menuState\": {\r\n" +
        "    \"menuWindow\": \"1634,71,1559,1860\"\r\n" +
        "  }\r\n" +
        "}\r\n";

    private static void AssertValidJsonc(string path)
    {
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = false };
        using var _ = JsonDocument.Parse(File.ReadAllText(path), opts);
    }

    [Fact]
    public void Set_ExistingKey_ChangesOnlyThatValue()
    {
        File.WriteAllText(PathOf, Sample);
        RtxEncoreConfig.SetString(PathOf, "renderingEngines", "frameGenerationEngine", "xefg");
        RtxEncoreConfig.SetInt(PathOf, "frameGeneration", "multiplier", 3);
        RtxEncoreConfig.SetBool(PathOf, "frameGeneration", "flipMeteringBypass", true);

        Assert.Equal("xefg", RtxEncoreConfig.Get(PathOf, "renderingEngines", "frameGenerationEngine"));
        Assert.Equal("3", RtxEncoreConfig.Get(PathOf, "frameGeneration", "multiplier"));
        Assert.Equal("true", RtxEncoreConfig.Get(PathOf, "frameGeneration", "flipMeteringBypass"));
        // Same key name elsewhere is untouched
        Assert.Equal("7", RtxEncoreConfig.Get(PathOf, "other", "multiplier"));

        var before = Sample.Split("\r\n");
        var after = File.ReadAllText(PathOf).Split("\r\n");
        Assert.Equal(before.Length, after.Length);
        int changed = before.Zip(after).Count(p => p.First != p.Second);
        Assert.Equal(3, changed);
        Assert.Contains("// game, dlss-fg or xefg. Restart.", after[4]); // comment kept
        Assert.Contains("https://example.com/a//b", after[3]);
        AssertValidJsonc(PathOf);
    }

    [Fact]
    public void Set_StringWithCommas_IsReadWhole()
    {
        File.WriteAllText(PathOf, Sample);
        Assert.Equal("1634,71,1559,1860", RtxEncoreConfig.Get(PathOf, "menuState", "menuWindow"));
    }

    [Fact]
    public void Set_MissingKey_IsAddedToItsSection()
    {
        File.WriteAllText(PathOf, Sample);
        RtxEncoreConfig.SetBool(PathOf, "frameGeneration", "dynamicExperimental56", true);
        Assert.Equal("true", RtxEncoreConfig.Get(PathOf, "frameGeneration", "dynamicExperimental56"));
        Assert.Equal("4", RtxEncoreConfig.Get(PathOf, "frameGeneration", "multiplier"));
        AssertValidJsonc(PathOf);
    }

    [Fact]
    public void Set_MissingSection_IsAdded()
    {
        File.WriteAllText(PathOf, Sample);
        RtxEncoreConfig.SetString(PathOf, "xefg", "xefgUiHandling", "game");
        RtxEncoreConfig.SetBool(PathOf, "xefg", "xefgTensorCores", true);
        Assert.Equal("game", RtxEncoreConfig.Get(PathOf, "xefg", "xefgUiHandling"));
        Assert.Equal("true", RtxEncoreConfig.Get(PathOf, "xefg", "xefgTensorCores"));
        AssertValidJsonc(PathOf);
    }

    [Fact]
    public void Set_NoFile_CreatesSmallValidFile()
    {
        RtxEncoreConfig.SetString(PathOf, "renderingEngines", "frameGenerationEngine", "xefg");
        RtxEncoreConfig.SetInt(PathOf, "frameGeneration", "multiplier", 3);
        RtxEncoreConfig.SetString(PathOf, "renderingEngines", "reconstructionEngine", "game");
        AssertValidJsonc(PathOf);
        Assert.Equal("xefg", RtxEncoreConfig.Get(PathOf, "renderingEngines", "frameGenerationEngine"));
        Assert.Equal("3", RtxEncoreConfig.Get(PathOf, "frameGeneration", "multiplier"));
        Assert.Equal("game", RtxEncoreConfig.Get(PathOf, "renderingEngines", "reconstructionEngine"));
        Assert.Contains("\"configVersion\": 4", File.ReadAllText(PathOf));
    }

    [Fact]
    public void Nested_ExistingKey_ChangesOnlyThatValue()
    {
        File.WriteAllText(PathOf, Sample);
        Assert.Equal("9", RtxEncoreConfig.Get(PathOf, "other/nested", "multiplier"));
        RtxEncoreConfig.SetInt(PathOf, "other/nested", "multiplier", 5);
        Assert.Equal("5", RtxEncoreConfig.Get(PathOf, "other/nested", "multiplier"));
        Assert.Equal("7", RtxEncoreConfig.Get(PathOf, "other", "multiplier"));
        Assert.Equal("4", RtxEncoreConfig.Get(PathOf, "frameGeneration", "multiplier"));
        AssertValidJsonc(PathOf);
    }

    [Fact]
    public void Nested_MissingSections_AreAdded()
    {
        File.WriteAllText(PathOf, Sample);
        RtxEncoreConfig.SetBool(PathOf, "other/nested", "flag", true);                 // key in an existing nested section
        RtxEncoreConfig.SetString(PathOf, "other/fresh", "mode", "fast");              // new nested section in an existing parent
        RtxEncoreConfig.SetString(PathOf, "neuralRendering/optimizations", "nrPrecision", "fast"); // new parent and child
        RtxEncoreConfig.SetBool(PathOf, "neuralRendering/core", "nrEnabled", true);    // new child in the new parent
        AssertValidJsonc(PathOf);
        Assert.Equal("true", RtxEncoreConfig.Get(PathOf, "other/nested", "flag"));
        Assert.Equal("fast", RtxEncoreConfig.Get(PathOf, "other/fresh", "mode"));
        Assert.Equal("fast", RtxEncoreConfig.Get(PathOf, "neuralRendering/optimizations", "nrPrecision"));
        Assert.Equal("true", RtxEncoreConfig.Get(PathOf, "neuralRendering/core", "nrEnabled"));
        Assert.Equal("9", RtxEncoreConfig.Get(PathOf, "other/nested", "multiplier"));
    }

    [Fact]
    public void Nested_NoFile_CreatesValidFile()
    {
        RtxEncoreConfig.SetString(PathOf, "neuralRendering/optimizations", "nrPrecision", "fast");
        RtxEncoreConfig.SetBool(PathOf, "neuralRendering/optimizations", "nrFastProcessing", false);
        RtxEncoreConfig.SetBool(PathOf, "neuralRendering/core", "nrEnabled", true);
        RtxEncoreConfig.SetInt(PathOf, "frameGeneration", "multiplier", 3);
        AssertValidJsonc(PathOf);
        Assert.Equal("fast", RtxEncoreConfig.Get(PathOf, "neuralRendering/optimizations", "nrPrecision"));
        Assert.Equal("false", RtxEncoreConfig.Get(PathOf, "neuralRendering/optimizations", "nrFastProcessing"));
        Assert.Equal("true", RtxEncoreConfig.Get(PathOf, "neuralRendering/core", "nrEnabled"));
        Assert.Equal("3", RtxEncoreConfig.Get(PathOf, "frameGeneration", "multiplier"));
    }

    [Fact]
    public void Get_MissingFileOrKey_ReturnsNull()
    {
        Assert.Null(RtxEncoreConfig.Get(PathOf, "frameGeneration", "multiplier"));
        File.WriteAllText(PathOf, Sample);
        Assert.Null(RtxEncoreConfig.Get(PathOf, "frameGeneration", "nope"));
        Assert.Null(RtxEncoreConfig.Get(PathOf, "nope", "multiplier"));
    }
}
