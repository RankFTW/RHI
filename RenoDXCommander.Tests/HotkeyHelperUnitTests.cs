using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for hotkey helper methods in HotkeyManager.
/// Requirements: 2.1, 2.2, 7.1, 7.2, 7.3
/// </summary>
public class HotkeyHelperUnitTests
{
    // ── ParseHotkeyString ─────────────────────────────────────────────

    [Fact]
    public void ParseHotkeyString_InvalidInput_ReturnsDefault()
    {
        var result = HotkeyManager.ParseHotkeyString("not,a,valid,hotkey");
        Assert.Equal((36, false, false, false), result);
    }

    [Fact]
    public void ParseHotkeyString_EmptyString_ReturnsDefault()
    {
        var result = HotkeyManager.ParseHotkeyString("");
        Assert.Equal((36, false, false, false), result);
    }

    [Fact]
    public void ParseHotkeyString_TooFewParts_ReturnsDefault()
    {
        var result = HotkeyManager.ParseHotkeyString("36,0,0");
        Assert.Equal((36, false, false, false), result);
    }

    // ── FormatHotkeyDisplay ───────────────────────────────────────────

    [Fact]
    public void FormatHotkeyDisplay_DefaultHomeKey_ReturnsHome()
    {
        var result = HotkeyManager.FormatHotkeyDisplay("36,0,0,0");
        Assert.Equal("Home", result);
    }

    [Fact]
    public void FormatHotkeyDisplay_CtrlF2_ReturnsCtrlPlusF2()
    {
        var result = HotkeyManager.FormatHotkeyDisplay("113,0,1,0");
        Assert.Equal("Ctrl + F2", result);
    }

    // ── IsDefaultHotkey ───────────────────────────────────────────────

    [Fact]
    public void IsDefaultHotkey_DefaultValue_ReturnsTrue()
    {
        Assert.True(HotkeyManager.IsDefaultHotkey("36,0,0,0"));
    }

    [Fact]
    public void IsDefaultHotkey_NonDefaultValue_ReturnsFalse()
    {
        Assert.False(HotkeyManager.IsDefaultHotkey("113,0,1,0"));
    }
}
