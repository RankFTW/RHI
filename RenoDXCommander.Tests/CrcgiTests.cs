using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class CrcgiTests
{
    [Fact]
    public void TechniqueToken_ReadsTheFirstTechnique()
    {
        const string fx = "#include \"ReShade.fxh\"\nuniform float x;\n\ntechnique CRCGI_v2 <\n  ui_label = \"CRCGI v2\";\n> { pass {} }\n";
        Assert.Equal("CRCGI_v2@CRCGI_v2.fx", CrcgiService.TechniqueToken(fx, "CRCGI_v2.fx"));
    }

    [Fact]
    public void TechniqueToken_FallsBackToTheFileName()
    {
        Assert.Equal("CRCGI_v3@CRCGI_v3.fx", CrcgiService.TechniqueToken("// no technique here", "CRCGI_v3.fx"));
    }

    [Theory]
    [InlineData("v2", "2")]
    [InlineData("v1.2", "1.2")]
    [InlineData("3", "3")]
    public void VersionFromTag(string tag, string expected) => Assert.Equal(expected, CrcgiService.VersionFromTag(tag));

    private const string Preset =
        "Techniques=CRCGI_v2@CRCGI_v2.fx,SMAA@SMAA.fx\r\nTechniqueSorting=CRCGI_v2@CRCGI_v2.fx,SMAA@SMAA.fx\r\n\r\n"
        + "[CRCGI_v2.fx]\r\nIntensity=1.000000\r\nMultibounce=1\r\n\r\n[SMAA.fx]\r\nEdgeDetectionThreshold=0.100000\r\n";

    [Fact]
    public void SetValue_ChangesOnlyTheEffectsKey()
    {
        var t = CrcgiPreset.SetValue(Preset, "CRCGI_v2.fx", "Intensity", "2.000000");
        Assert.Equal("2.000000", CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "Intensity"));
        Assert.Equal("0.100000", CrcgiPreset.GetValue(t, "SMAA.fx", "EdgeDetectionThreshold"));
        Assert.Contains("SMAA@SMAA.fx", t);
    }

    [Fact]
    public void Defines_AreSetKeptAndRemoved()
    {
        var t = CrcgiPreset.SetDefine(Preset, "CRCGI_v2.fx", "CRCGI_RESOLUTION_DIVISOR", "2");
        t = CrcgiPreset.SetDefine(t, "CRCGI_v2.fx", "CRCGI_CASCADES", "6");
        Assert.Equal("CRCGI_RESOLUTION_DIVISOR=2,CRCGI_CASCADES=6", CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "PreprocessorDefinitions"));
        t = CrcgiPreset.SetDefine(t, "CRCGI_v2.fx", "CRCGI_RESOLUTION_DIVISOR", "3");
        Assert.Equal("3", CrcgiPreset.GetDefine(t, "CRCGI_v2.fx", "CRCGI_RESOLUTION_DIVISOR"));
        t = CrcgiPreset.SetDefine(t, "CRCGI_v2.fx", "CRCGI_RESOLUTION_DIVISOR", null);
        Assert.Null(CrcgiPreset.GetDefine(t, "CRCGI_v2.fx", "CRCGI_RESOLUTION_DIVISOR"));
        Assert.Equal("6", CrcgiPreset.GetDefine(t, "CRCGI_v2.fx", "CRCGI_CASCADES"));
    }

    [Fact]
    public void RemoveKeys_ResetsOnlyThoseKeys()
    {
        var t = CrcgiPreset.RemoveKeys(Preset, "CRCGI_v2.fx", new[] { "Intensity" });
        Assert.Null(CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "Intensity"));
        Assert.Equal("1", CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "Multibounce"));
    }

    [Fact]
    public void ReplaceSection_SwapsTheEffectsValuesAndKeepsOtherEffects()
    {
        var author = "Techniques=CRCGI_v2@CRCGI_v2.fx\r\n\r\n[CRCGI_v2.fx]\r\nIntensity=1.400000\r\nShadowAmount=0.600000\r\n";
        var body = CrcgiPreset.GetSection(author, "CRCGI_v2.fx")!;
        var t = CrcgiPreset.ReplaceSection(Preset, "CRCGI_v2.fx", body);
        Assert.Equal("1.400000", CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "Intensity"));
        Assert.Equal("0.600000", CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "ShadowAmount"));
        Assert.Null(CrcgiPreset.GetValue(t, "CRCGI_v2.fx", "Multibounce")); // the author's section is taken whole
        Assert.Equal("0.100000", CrcgiPreset.GetValue(t, "SMAA.fx", "EdgeDetectionThreshold"));
        Assert.Contains("SMAA@SMAA.fx", t);

        var added = CrcgiPreset.ReplaceSection("Techniques=SMAA@SMAA.fx\r\n", "CRCGI_v2.fx", body);
        Assert.Equal("1.400000", CrcgiPreset.GetValue(added, "CRCGI_v2.fx", "Intensity"));
    }

    [Theory]
    [InlineData("Batman Arkham Knight - CRCGI v2 (Not tested).ini", true)]
    [InlineData("The Witcher 3 Remastered - BVCLM - CRCGI v2.ini", true)]
    [InlineData("The Witcher 3 Remastered - BVCLM - CRCGI v1.2.ini", false)]
    [InlineData("Batman Arkham Knight - CRCGI v1.5 (Not tested) .ini", false)]
    [InlineData("Something - CRCGI v20.ini", false)]
    public void PresetFilesAreMatchedToTheMajorVersion(string file, bool expected)
        => Assert.Equal(expected, CrcgiService.IsPresetForMajor(file, "2"));

    [Theory]
    [InlineData("Batman™: Arkham Knight", "Batman Arkham Knight - With Add-on", true)]
    [InlineData("The Witcher 3: Wild Hunt", "The Witcher 3 Remastered - Better Vanilla Changes Lighting Mod", true)]
    [InlineData("Batman: Arkham City", "Batman Arkham Knight - With Add-on", false)]
    [InlineData("Cyberpunk 2077", "The Witcher 3 Remastered - Promotional Atmosphere Lighting Mod", false)]
    public void GameMatchesPresetFolder(string game, string folder, bool expected)
        => Assert.Equal(expected, CrcgiPreset.GameMatchesPresetFolder(game, folder));

    [Fact]
    public void ShaderFiles_OnlyWhereRhiInstalledIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-crcgi-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Empty(CrcgiService.ShaderFilesFor(dir));
            Assert.False(CrcgiService.IsInstalledIn(dir));

            // An RHI install: the record, the add-on and the marker naming the effect
            RhiInstallManifest.SetComponent(dir, CrcgiService.Component, new[] { CrcgiService.Addon64, CrcgiService.MarkerName });
            File.WriteAllText(Path.Combine(dir, CrcgiService.Addon64), "x");
            File.WriteAllText(Path.Combine(dir, CrcgiService.MarkerName), "version=2\r\neffect=CRCGI_v2.fx\r\ntechnique=CRCGI_v2@CRCGI_v2.fx\r\n");
            Assert.True(CrcgiService.IsInstalledIn(dir));
            Assert.Equal("2", CrcgiService.InstalledVersion(dir));
            var file = Assert.Single(CrcgiService.ShaderFilesFor(dir));
            Assert.StartsWith(@"Shaders\CRCGI", file.Rel);
            Assert.EndsWith(".fx", file.Rel);
        }
        finally { Directory.Delete(dir, true); }
    }
}
