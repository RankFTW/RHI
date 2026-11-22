using RenoDXCommander;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class ReLimiterPresetKeysTests
{
    [Theory]
    [InlineData(0x62, false, false, false, "Keypad2")]
    [InlineData(0xBA, true, false, false, "Shift+Semicolon")]
    [InlineData(0x21, false, false, false, "PageUp")]
    [InlineData(0x26, false, true, true, "Ctrl+Alt+UpArrow")]
    [InlineData(0x7B, false, false, false, "F12")]
    [InlineData(0x50, false, false, false, "P")]
    [InlineData(0x6A, false, false, false, "0x6A")] // numpad * has no ReLimiter name: hex, which it reads
    public void BuildUlHotkeyString_UsesReLimiterNames(int vk, bool shift, bool ctrl, bool alt, string expected)
        => Assert.Equal(expected, HotkeyManager.BuildUlHotkeyString(vk, shift, ctrl, alt));

    [Fact]
    public void SetKey_ReplacesInPlace_AndKeepsOtherSections()
    {
        var lines = new List<string> { "[Presets]", "prev_key=Keypad2", "next_key=Keypad3", "[OSD_Preset_1]", "name=Main" };
        var result = ReLimiterPresetKeys.SetKey(lines, "Presets", "next_key", "PageDown");
        Assert.Equal(new[] { "[Presets]", "prev_key=Keypad2", "next_key=PageDown", "[OSD_Preset_1]", "name=Main" }, result);
    }

    [Fact]
    public void SetKey_AddsMissingKeyAtEndOfSection()
    {
        var lines = new List<string> { "[Presets]", "prev_key=Keypad2", "", "[OSD_Preset_1]", "name=Main" };
        var result = ReLimiterPresetKeys.SetKey(lines, "Presets", "blackout_key", "F3");
        Assert.Equal(new[] { "[Presets]", "prev_key=Keypad2", "blackout_key=F3", "", "[OSD_Preset_1]", "name=Main" }, result);
    }

    [Fact]
    public void SetKey_AddsSectionWhenMissing()
    {
        var result = ReLimiterPresetKeys.SetKey(new List<string> { "[OSD_Preset_1]", "name=Main" }, "Presets", "oled_care_key", "");
        Assert.Equal(new[] { "[Presets]", "oled_care_key=", "[OSD_Preset_1]", "name=Main" }, result);
    }
}
