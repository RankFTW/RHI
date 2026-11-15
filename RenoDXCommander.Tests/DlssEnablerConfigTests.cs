using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class DlssEnablerConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-de-config-" + Guid.NewGuid().ToString("N"));

    public DlssEnablerConfigTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string Sample =
        "; header\r\n[Performance]\r\n; Force-load comment\r\nForceLoadDLSSG=false\r\nMFGOverrideMode=0\r\n\r\n[UI]\r\nToggleKey=Tilde\r\n";

    [Fact]
    public void Get_ReadsValueInItsSection_IgnoringComments()
    {
        Assert.Equal("false", DlssEnablerConfig.GetFromText(Sample, "Performance", "ForceLoadDLSSG"));
        Assert.Equal("Tilde", DlssEnablerConfig.GetFromText(Sample, "ui", "togglekey"));
        Assert.Null(DlssEnablerConfig.GetFromText(Sample, "UI", "ForceLoadDLSSG")); // other section
        Assert.Null(DlssEnablerConfig.GetFromText(Sample, "Reflex", "Vsync"));
    }

    [Fact]
    public void Set_ReplacesOnlyThatLine_KeepsCommentsAndCrlf()
    {
        var s = DlssEnablerConfig.SetInText(Sample, "Performance", "ForceLoadDLSSG", "true");
        Assert.Equal(Sample.Replace("ForceLoadDLSSG=false", "ForceLoadDLSSG=true"), s);
    }

    [Fact]
    public void Set_AddsMissingKeyAtEndOfItsSection()
    {
        var s = DlssEnablerConfig.SetInText(Sample, "Performance", "DynamicMFG", "true");
        Assert.Contains("MFGOverrideMode=0\r\nDynamicMFG=true\r\n\r\n[UI]", s);
        Assert.Equal("true", DlssEnablerConfig.GetFromText(s, "Performance", "DynamicMFG"));
    }

    [Fact]
    public void Set_AddsMissingSection()
    {
        var s = DlssEnablerConfig.SetInText(Sample, "GhostBuster", "Enabled", "false");
        Assert.EndsWith("ToggleKey=Tilde\r\n\r\n[GhostBuster]\r\nEnabled=false\r\n", s);
    }

    [Fact]
    public void DefaultTemplate_HasTheSettingsTheCogUses_AtTheirDefaults()
    {
        var t = DlssEnablerConfig.DefaultTemplate;
        Assert.Equal("4.10.0.23", DlssEnablerConfig.GetFromText(t, "Meta", "configVersion"));
        Assert.Equal("false", DlssEnablerConfig.GetFromText(t, "Performance", "ForceLoadDLSSG"));
        Assert.Equal("0", DlssEnablerConfig.GetFromText(t, "Performance", "MFGOverrideMode"));
        Assert.Equal("false", DlssEnablerConfig.GetFromText(t, "Performance", "DynamicMFG"));
        Assert.Equal("120", DlssEnablerConfig.GetFromText(t, "Performance", "DFGTargetFps"));
        Assert.Equal("false", DlssEnablerConfig.GetFromText(t, "Performance", "MFGHotkeys"));
        Assert.Equal("false", DlssEnablerConfig.GetFromText(t, "Debug", "UseFsrOnly"));
        Assert.Equal("0", DlssEnablerConfig.GetFromText(t, "NGX", "HudDetectionMode"));
        Assert.Equal("true", DlssEnablerConfig.GetFromText(t, "GhostBuster", "Enabled"));
        Assert.Equal("true", DlssEnablerConfig.GetFromText(t, "UI", "Monitoring"));
        Assert.Equal("Tilde", DlssEnablerConfig.GetFromText(t, "UI", "ToggleKey"));
    }

    [Fact]
    public void CreateIfMissing_WritesCrlfWithoutBom_AndNeverReplacesAFile()
    {
        Assert.True(DlssEnablerConfig.CreateIfMissing(_dir));
        var path = DlssEnablerConfig.PathIn(_dir);
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));

        DlssEnablerConfig.Set(path, "Performance", "ForceLoadDLSSG", "true");
        Assert.False(DlssEnablerConfig.CreateIfMissing(_dir));
        Assert.Equal("true", DlssEnablerConfig.Get(path, "Performance", "ForceLoadDLSSG"));
    }
}
