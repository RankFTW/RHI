using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>DLSS5 Feeder preset handling, matching Feeder 2.0.0's Install-DLSS5Feeder.ps1.</summary>
public class FeederPresetTests
{
    private const string Fx200 = "float4 BaseLevel(float2 uv) { return 0; }";
    private const string Fx119 = "float4 Something(float2 uv) { return 0; }";

    [Fact]
    public void NewInstall_UsesBuiltIn_WithNewShader_ElseLumenite()
    {
        Assert.Equal(5, FeederPreset.ChooseProvider(null, Fx200));
        Assert.Equal(3, FeederPreset.ChooseProvider(null, Fx119));
        Assert.Equal(5, FeederPreset.ChooseProvider("Techniques=Foo@foo.fx\r\n", Fx200));
    }

    [Fact]
    public void ExistingProvider_IsKept()
    {
        var preset = "Techniques=DLSS5_Feed@DLSS5_Feed.fx\r\n\r\n[DLSS5_Feed.fx]\r\nPreprocessorDefinitions=FOO=1,DLSS5_MV_PROVIDER=3\r\n";
        Assert.Equal(3, FeederPreset.ReadProvider(preset));
        Assert.Equal(3, FeederPreset.ChooseProvider(preset, Fx200));
    }

    [Fact]
    public void Merge_NewPreset_BuiltIn()
    {
        var p = FeederPreset.Merge(null, 5);
        Assert.Equal("DLSS5_Feed@DLSS5_Feed.fx", FeederPreset.GetKey(p, "", "Techniques"));
        Assert.Equal("DLSS5_Feed@DLSS5_Feed.fx,DLSS5_Feed_Debug@DLSS5_Feed.fx", FeederPreset.GetKey(p, "", "TechniqueSorting"));
        Assert.Equal(5, FeederPreset.ReadProvider(p));
    }

    [Fact]
    public void Merge_NewPreset_Lumenite_PutsKernelFirst()
    {
        var p = FeederPreset.Merge(null, 3);
        Assert.Equal("Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx", FeederPreset.GetKey(p, "", "Techniques"));
        Assert.Equal(3, FeederPreset.ReadProvider(p));
    }

    [Fact]
    public void Merge_KeepsUsersEffectsAndValues_AndDropsLumeniteForBuiltIn()
    {
        var old = "Techniques=SMAA@SMAA.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n"
                + "TechniqueSorting=SMAA@SMAA.fx,DLSS5_Feed_Debug@DLSS5_Feed.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n"
                + "\r\n[DLSS5_Feed.fx]\r\nMV_SCALE=1.250000\r\n\r\n[SMAA.fx]\r\nEdgeDetectionThreshold=0.050000\r\n";
        var p = FeederPreset.Merge(old, 5);
        Assert.Equal("SMAA@SMAA.fx,DLSS5_Feed@DLSS5_Feed.fx", FeederPreset.GetKey(p, "", "Techniques"));
        Assert.Equal("SMAA@SMAA.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx,DLSS5_Feed_Debug@DLSS5_Feed.fx",
            FeederPreset.GetKey(p, "", "TechniqueSorting"));
        Assert.Equal("1.250000", FeederPreset.GetKey(p, "DLSS5_Feed.fx", "MV_SCALE"));
        Assert.Equal("0.050000", FeederPreset.GetKey(p, "SMAA.fx", "EdgeDetectionThreshold"));
        Assert.Equal("DLSS5_MV_PROVIDER=5", FeederPreset.GetKey(p, "DLSS5_Feed.fx", "PreprocessorDefinitions"));
    }

    [Fact]
    public void Merge_BuiltIn_KeepsLumeniteFlow_WhenAnotherLumeniteEffectUsesIt()
    {
        var old = "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,Lumenite_GI@lumenite_GI.fx\r\n";
        var p = FeederPreset.Merge(old, 5);
        Assert.Equal("Lumenite_Kernel@lumenite_Kernel.fx,Lumenite_GI@lumenite_GI.fx,DLSS5_Feed@DLSS5_Feed.fx",
            FeederPreset.GetKey(p, "", "Techniques"));
    }

    [Fact]
    public void Merge_ReplacesOnlyTheProviderDefinition()
    {
        var old = "Techniques=DLSS5_Feed@DLSS5_Feed.fx\r\n\r\n[DLSS5_Feed.fx]\r\nPreprocessorDefinitions=FOO=1,DLSS5_MV_PROVIDER=0\r\n";
        var p = FeederPreset.Merge(old, 5);
        Assert.Equal("FOO=1,DLSS5_MV_PROVIDER=5", FeederPreset.GetKey(p, "DLSS5_Feed.fx", "PreprocessorDefinitions"));
    }

    [Fact]
    public void RemoveGlobalProvider_FromReShadeIni()
    {
        var ini = "[GENERAL]\r\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\r\nPreprocessorDefinitions=FOO=1,DLSS5_MV_PROVIDER=3\r\n\r\n[INPUT]\r\nKeyOverlay=36,0,0,0\r\n";
        var r = FeederPreset.RemoveGlobalProvider(ini);
        Assert.Equal("FOO=1", FeederPreset.GetKey(r, "GENERAL", "PreprocessorDefinitions"));
        Assert.Equal("36,0,0,0", FeederPreset.GetKey(r, "INPUT", "KeyOverlay"));
        var clean = ini.Replace(",DLSS5_MV_PROVIDER=3", "");
        Assert.Same(clean, FeederPreset.RemoveGlobalProvider(clean)); // nothing to remove: unchanged
    }

    [Fact]
    public void SetKey_AddsMissingSectionAndKey()
    {
        var t = FeederPreset.SetKey("[GENERAL]\r\nA=1\r\n", "GENERAL", "PresetPath", ".\\ReShadePreset.ini");
        Assert.Equal(".\\ReShadePreset.ini", FeederPreset.GetKey(t, "GENERAL", "PresetPath"));
        Assert.Equal("1", FeederPreset.GetKey(t, "GENERAL", "A"));
        var u = FeederPreset.SetKey("Techniques=X@x.fx\r\n", "DLSS5_Feed.fx", "PreprocessorDefinitions", "DLSS5_MV_PROVIDER=5");
        Assert.Equal("DLSS5_MV_PROVIDER=5", FeederPreset.GetKey(u, "DLSS5_Feed.fx", "PreprocessorDefinitions"));
        Assert.Equal("X@x.fx", FeederPreset.GetKey(u, "", "Techniques"));
    }
}
