using RenoDXCommander.Services;
using Color = Windows.UI.Color;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Colour themes: the colour mapping, presets and the saved Custom string.</summary>
public class ThemePaletteTests
{
    private static Color H(string hex) => ThemePalette.Hex(hex);

    [Theory]
    [InlineData("#121518")]
    [InlineData("#5ECB7D")]
    [InlineData("#1A2840")]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    public void Default_maps_every_colour_to_itself(string hex)
    {
        var m = new ThemeColorMapper(ThemePalette.Default);
        Assert.Equal(H(hex), m.Map(H(hex)));
    }

    [Fact]
    public void Theme_anchor_colours_map_to_the_theme_colours()
    {
        var ember = ThemePresets.Find("Ember")!.Palette;
        var m = new ThemeColorMapper(ember);
        var d = ThemePalette.Default;
        foreach (var key in new[] { "Background", "Panel", "Border", "Muted", "Text", "Accent", "Buttons", "Installed", "Update", "Warning", "Error" })
            AssertClose(ember.Get(key), m.Map(d.Get(key)), key);
    }

    [Fact]
    public void Map_keeps_alpha_and_leaves_transparent_alone()
    {
        var m = new ThemeColorMapper(ThemePresets.Find("MidnightBlue")!.Palette);
        var half = Color.FromArgb(0x80, 0x12, 0x15, 0x18);
        Assert.Equal(0x80, m.Map(half).A);
        var clear = Color.FromArgb(0, 0x12, 0x15, 0x18);
        Assert.Equal(clear, m.Map(clear));
    }

    [Fact]
    public void Oled_black_makes_the_background_black()
    {
        var m = new ThemeColorMapper(ThemePresets.Find("OledBlack")!.Palette);
        Assert.Equal(H("#000000"), m.Map(ThemePalette.Default.Background));
        // Status bar / header (darker than the background) also go (near) black
        var bar = m.Map(H("#0D1015"));
        Assert.True(bar.R <= 3 && bar.G <= 3 && bar.B <= 3, ThemePalette.ToHex(bar));
    }

    [Theory]
    [InlineData("#0B0F17")] // TabsTheme page
    [InlineData("#0E131D")] // TabsTheme top bar / sidebar
    [InlineData("#121518")] // SurfaceBase
    public void Oled_black_turns_the_tabs_navies_black_too(string hex)
    {
        // The Tabs / Spotlight chrome navies are greys, not tinted "button blue" (they stayed navy in OLED Black)
        var c = new ThemeColorMapper(ThemePresets.Find("OledBlack")!.Palette).Map(H(hex));
        Assert.True(c.R <= 4 && c.G <= 4 && c.B <= 4, $"{hex} -> {ThemePalette.ToHex(c)}");
    }

    [Theory]
    [InlineData("#283240")] // BorderDefault
    [InlineData("#141820")] // CardBackground
    [InlineData("#6B7A8E")] // TextTertiary
    public void Blue_grey_neutrals_follow_the_neutral_stops_not_the_button_colour(string hex)
    {
        // Ember turns buttons orange; the blue-grey greys must stay greys (no orange tint)
        var c = new ThemeColorMapper(ThemePresets.Find("Ember")!.Palette).Map(H(hex));
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        Assert.True(max - min < 40, $"{hex} -> {ThemePalette.ToHex(c)}");
    }

    [Theory]
    [InlineData("#96BEE6")] // SettingsButtonBlue
    [InlineData("#1A2840")] // selected row / card highlight
    public void Blue_accent_variants_follow_the_button_colour(string hex)
    {
        var c = new ThemeColorMapper(ThemePresets.Find("Ember")!.Palette).Map(H(hex));
        Assert.True(c.R > c.B, $"{hex} -> {ThemePalette.ToHex(c)} (expected orange-ish)");
    }

    [Fact]
    public void Tinted_variants_follow_their_accent()
    {
        // Colour-blind: installed green -> blue, so the dark green install tint turns bluish
        var m = new ThemeColorMapper(ThemePresets.Find("ColourBlind")!.Palette);
        var c = m.Map(H("#122818")); // AccentGreenBg
        Assert.True(c.B > c.G, $"expected blue tint, got {ThemePalette.ToHex(c)}");
    }

    [Fact]
    public void Custom_string_round_trips()
    {
        var p = ThemePresets.Find("Violet")!.Palette;
        var back = ThemePalette.ParseCustom(p.ToCustomString(), ThemePalette.Default);
        foreach (var (key, _) in ThemePalette.CustomKeys)
            Assert.Equal(p.Get(key), back.Get(key));
    }

    [Fact]
    public void Custom_string_ignores_bad_parts()
    {
        var p = ThemePalette.ParseCustom("Background=#000000;Nonsense=#FFFFFF;Text=notacolour;Accent", ThemePalette.Default);
        Assert.Equal(H("#000000"), p.Background);
        Assert.Equal(ThemePalette.Default.Text, p.Text);
        Assert.Equal(ThemePalette.Default.Accent, p.Accent);
    }

    [Fact]
    public void Resolve_unknown_or_empty_falls_back_to_default()
    {
        Assert.Equal(ThemePalette.Default, ThemePresets.Resolve("NoSuchTheme", null));
        Assert.Equal(ThemePalette.Default, ThemePresets.Resolve(null, null));
        Assert.Equal(ThemePalette.Default, ThemePresets.Resolve("Custom", ""));
    }

    [Fact]
    public void Changing_background_rederives_border_and_muted()
    {
        var p = ThemePalette.Default.With("Panel", H("#202020")).With("Text", H("#F0F0F0"));
        Assert.True(ThemeColorMapper.Lightness(p.Border) > ThemeColorMapper.Lightness(p.Panel));
        Assert.True(ThemeColorMapper.Lightness(p.Muted) < ThemeColorMapper.Lightness(p.Text));
    }

    [Fact]
    public void Presets_have_unique_ids_and_readable_text()
    {
        Assert.Equal(ThemePresets.All.Count, ThemePresets.All.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var p in ThemePresets.All)
            Assert.True(ThemeColorMapper.Contrast(p.Palette.Text, p.Palette.Background) >= 7, p.Id);
    }

    private static void AssertClose(Color expected, Color actual, string what)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= 3 && Math.Abs(expected.G - actual.G) <= 3 && Math.Abs(expected.B - actual.B) <= 3,
            $"{what}: expected {ThemePalette.ToHex(expected)}, got {ThemePalette.ToHex(actual)}");
    }
}
