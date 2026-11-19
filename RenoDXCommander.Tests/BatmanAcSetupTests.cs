using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Batman: Arkham City launcher settings written into BmEngine.ini / UserEngine.ini (line edits only).</summary>
public class BatmanAcSetupTests
{
    private static readonly (string, string)[] Values = { ("AllowD3D11", "True"), ("PostProcessAAType", "0"), ("DepthOfField", "True") };

    [Fact]
    public void Replaces_Keys_Only_In_SystemSettings()
    {
        var text = "[SystemSettings]\r\nAllowD3D11=False\r\nPostProcessAAType=2\r\nDepthOfField=False\r\n\r\n" +
                   "[SystemSettingsMobile]\r\nDepthOfField=FALSE\r\n";
        var result = BatmanAcSetup.SetInSection(text, "SystemSettings", Values);
        Assert.Equal("[SystemSettings]\r\nAllowD3D11=True\r\nPostProcessAAType=0\r\nDepthOfField=True\r\n\r\n" +
                     "[SystemSettingsMobile]\r\nDepthOfField=FALSE\r\n", result);
    }

    [Fact]
    public void Inserts_Missing_Keys_After_The_Header()
    {
        // UserEngine.ini as the launcher writes it: no PostProcessAAType / DepthOfField lines
        var text = "[Configuration]\nBasedOn=..\\BmGame\\Config\\DefaultEngine.ini\n\n[SystemSettings]\nAllowD3D11=True\nResX=3440\n\n[Engine.Engine]\nPhysXLevel=2\n";
        var result = BatmanAcSetup.SetInSection(text, "SystemSettings", Values);
        Assert.Equal("[Configuration]\nBasedOn=..\\BmGame\\Config\\DefaultEngine.ini\n\n[SystemSettings]\nPostProcessAAType=0\nDepthOfField=True\n" +
                     "AllowD3D11=True\nResX=3440\n\n[Engine.Engine]\nPhysXLevel=2\n", result);
    }

    [Fact]
    public void Adds_The_Section_When_Missing()
    {
        var result = BatmanAcSetup.SetInSection("[Engine.Engine]\nPhysXLevel=2\n", "SystemSettings", Values);
        Assert.Equal("[Engine.Engine]\nPhysXLevel=2\n\n[SystemSettings]\nAllowD3D11=True\nPostProcessAAType=0\nDepthOfField=True\n", result);
    }

    [Fact]
    public void Already_Set_Leaves_The_Text_Unchanged()
    {
        var text = "[SystemSettings]\r\nAllowD3D11=True\r\nPostProcessAAType=0\r\nDepthOfField=True\r\n";
        Assert.Equal(text, BatmanAcSetup.SetInSection(text, "SystemSettings", Values));
    }

    [Fact]
    public void Apply_Keeps_The_Files_UTF16_Encoding_And_Backs_Up_Once()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-bmac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // UE3 writes UTF-16 LE with a BOM; the launcher's DX9 + FXAA choice must be overwritten
            var utf16 = new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            var path = Path.Combine(dir, "UserEngine.ini");
            File.WriteAllText(path, "[SystemSettings]\r\nAllowD3D11=False\r\nMultisampleMode=FXAA-HI\r\n", utf16);

            BatmanAcSetup.ApplyLauncherSettings(dir);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xFE, bytes[1]);
            var text = File.ReadAllText(path); // detects the BOM
            Assert.Contains("AllowD3D11=True", text);
            Assert.Contains("MultisampleMode=1xMSAA", text);
            Assert.False(text.Contains('\uFEFF')); // no second BOM inside the text (ordinal: culture compare ignores U+FEFF)
            Assert.True(File.Exists(path + ".rhi-backup"));
            Assert.Contains("FXAA-HI", File.ReadAllText(path + ".rhi-backup"));
            Assert.False(File.Exists(Path.Combine(dir, "BmEngine.ini"))); // missing files aren't created
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Launcher_Settings_Are_What_The_Mod_Needs()
    {
        var map = BatmanAcSetup.LauncherSettings.ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("True", map["AllowD3D11"]);
        Assert.Equal("False", map["AllowD3D10"]);
        Assert.Equal("1xMSAA", map["MultisampleMode"]);
        Assert.Equal("0", map["PostProcessAAType"]);
        Assert.Equal("True", map["DepthOfField"]);
    }
}
