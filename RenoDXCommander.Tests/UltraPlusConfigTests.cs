using RenoDXCommander.Services.UltraPlus;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Ultra+ settings files (samples from Phantom Blade Zero, Ultra+ 1.3.8).</summary>
public class UltraPlusConfigTests
{
    private const string Schema =
        "AutoApplyConfig.Comment=\"off/on; automatically reload settings when config changes - primarily to be used with the UPM overlay\"\r\n" +
        "AutoApplyConfig.Type=enum\r\nAutoApplyConfig.Default=off\r\nAutoApplyConfig.Values=off,on\r\n\r\n" +
        "Bloom.Comment=\"Bloom intensity (0.0 = off, 1.0 = default, 2.0 = double, game = use game default)\"\r\n" +
        "Bloom.Type=numeric\r\nBloom.Default=game\r\nBloom.ValueType=float\r\nBloom.Min=0\r\nBloom.Max=3\r\nBloom.Step=0.1\r\n\r\n" +
        "ColorGrading.Comment=\"game/off/on; allows disabling developer implemented color grading\"\r\n" +
        "ColorGrading.Type=enum\r\nColorGrading.Default=game\r\nColorGrading.Category=Advanced\r\nColorGrading.Values=game,off,on\r\n\r\n" +
        "CustomEyeAdaptation.Comment=\"!game/on; more stable and generally more exposed eye adaptation\"\r\n" +
        "CustomEyeAdaptation.Type=enum\r\nCustomEyeAdaptation.Default=game\r\nCustomEyeAdaptation.Values=game,on\r\n" +
        "LightingAO.Comment=\"lumen/ssao; AO method\"\r\nLightingAO.Type=enum\r\nLightingAO.Default=lumen\r\nLightingAO.Values=lumen,ssao\r\nLightingAO.Shortcut=F7\r\n";

    private const string Config =
        "; Ultra+ Configuration File\r\n; Press F12 to reload configuration in-game\r\n\r\n" +
        "; off/on; automatically reload settings when config changes\r\nAutoApplyConfig=off\r\n\r\n" +
        "; Bloom intensity (0.0 = off, 1.0 = default)\r\nBloom=0.60\r\n\r\n" +
        "; F7\r\n; lumen/ssao; AO method\r\nLightingAO=lumen ; keep this\r\n";

    [Fact]
    public void ParseSchema_ReadsEveryProperty()
    {
        var s = UltraPlusConfig.ParseSchema(Schema);
        Assert.Equal(new[] { "AutoApplyConfig", "Bloom", "ColorGrading", "CustomEyeAdaptation", "LightingAO" }, s.Select(x => x.Key));
        var bloom = s[1];
        Assert.True(bloom.IsNumeric);
        Assert.Equal(0, bloom.Min); Assert.Equal(3, bloom.Max); Assert.Equal(0.1, bloom.Step);
        Assert.Equal("game", bloom.Default);
        Assert.Equal("Bloom intensity (0.0 = off, 1.0 = default, 2.0 = double, game = use game default)", bloom.ModDescription);
        Assert.Equal(new[] { "game", "off", "on" }, s[2].Options);
        Assert.True(s[2].IsAdvanced);
        Assert.Equal("allows disabling developer implemented color grading", s[2].ModDescription);
        Assert.True(s[3].PreferModDescription);
        Assert.False(s[2].PreferModDescription);
        Assert.Equal("F7", s[4].Shortcut);
    }

    [Fact]
    public void ParseValues_StripsCommentsAndInlineComments()
    {
        var v = UltraPlusConfig.ParseValues(Config);
        Assert.Equal("off", v["AutoApplyConfig"]);
        Assert.Equal("0.60", v["Bloom"]);
        Assert.Equal("lumen", v["LightingAO"]);
        Assert.Equal(3, v.Count);
    }

    [Fact]
    public void SetValues_ChangesOnlyThoseLines_KeepsCommentsAndLineEndings()
    {
        var updated = UltraPlusConfig.SetValues(Config, new Dictionary<string, string>
        {
            ["AutoApplyConfig"] = "on",
            ["LightingAO"] = "ssao",
        });
        Assert.Equal(Config.Replace("AutoApplyConfig=off", "AutoApplyConfig=on").Replace("LightingAO=lumen ; keep this", "LightingAO=ssao ; keep this"), updated);
        Assert.DoesNotContain("\n\n\n", updated.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SetValues_AppendsMissingKey_AndKeepsLfFiles()
    {
        var updated = UltraPlusConfig.SetValues("A=1\nB=2\n", new Dictionary<string, string> { ["C"] = "3", ["a"] = "9" });
        Assert.Equal("A=9\nB=2\nC=3\n", updated);
        Assert.Equal("X=1", UltraPlusConfig.SetValues("", new Dictionary<string, string> { ["X"] = "1" }));
    }

    [Fact]
    public void SetValues_NeverTouchesCommentedKeys()
    {
        var text = "; Bloom=1\r\nBloom=0.5\r\n";
        Assert.Equal("; Bloom=1\r\nBloom=2\r\n", UltraPlusConfig.SetValues(text, new Dictionary<string, string> { ["Bloom"] = "2" }));
    }

    [Theory]
    [InlineData("0.60", 0.1, "0.6")]
    [InlineData("1.0", 0.1, "1")]
    [InlineData("0.35000001", 0.05, "0.35")]
    [InlineData("2", null, "2")]
    [InlineData("game", 0.1, "game")]
    [InlineData("-0.0", 0.1, "0")]
    public void NormalizeNumeric(string value, double? step, string expected) =>
        Assert.Equal(expected, UltraPlusConfig.NormalizeNumeric(value, step));

    [Fact]
    public void NumericChoices_LikeUpm()
    {
        var bloom = UltraPlusConfig.ParseSchema(Schema)[1];
        var c = UltraPlusConfig.NumericChoices(bloom);
        Assert.Equal("game", c[0]);
        Assert.Equal("0", c[1]);
        Assert.Equal("0.1", c[2]);
        Assert.Equal("3", c[^1]);
        Assert.Equal(32, c.Count); // game + 0..3 by 0.1
        var ints = UltraPlusConfig.NumericChoices(new UltraPlusSetting { Key = "N", Type = "numeric", Min = -1, Max = 2, Step = 1, ValueType = "int" });
        Assert.Equal(new[] { "-1", "0", "1", "2" }, ints);
    }

    [Fact]
    public void IsAllowed_ChecksOptionsAndRange()
    {
        var s = UltraPlusConfig.ParseSchema(Schema);
        Assert.True(UltraPlusConfig.IsAllowed(s[2], "off"));
        Assert.False(UltraPlusConfig.IsAllowed(s[2], "ultra"));
        Assert.True(UltraPlusConfig.IsAllowed(s[1], "2.5"));
        Assert.True(UltraPlusConfig.IsAllowed(s[1], "game"));
        Assert.False(UltraPlusConfig.IsAllowed(s[1], "3.5"));
    }

    [Fact]
    public void Presets_NamesRankAndCustomFormat()
    {
        Assert.Equal("high", UltraPlusConfig.PresetStorageName("preset_high.ini"));
        Assert.Null(UltraPlusConfig.PresetStorageName("presets.ini"));
        Assert.Null(UltraPlusConfig.PresetStorageName("preset_.ini"));
        Assert.Equal("U+ Recommended", UltraPlusConfig.PresetDisplayName("uplus_defaults"));
        Assert.Equal("Very High", UltraPlusConfig.PresetDisplayName("veryhigh"));
        Assert.Equal("My RT", UltraPlusConfig.PresetDisplayName("My RT"));

        var shipped = UltraPlusConfig.ParsePreset("high", "; Ultra+ HIGH Preset\r\n; game/on; x\r\nClothStutterFix=on\r\n");
        Assert.False(shipped.IsCustom);
        Assert.Equal("on", shipped.Values["ClothStutterFix"]);

        var text = UltraPlusConfig.BuildCustomPreset("Sharp\nand fast", new Dictionary<string, string> { ["Bloom"] = "0.6", ["LightingAO"] = "ssao" });
        var custom = UltraPlusConfig.ParsePreset("Mine", text);
        Assert.True(custom.IsCustom);
        Assert.Equal("Sharp and fast", custom.Description);
        Assert.Equal("ssao", custom.Values["LightingAO"]);
        Assert.True(UltraPlusConfig.PresetRank(custom) > UltraPlusConfig.PresetRank(shipped));
        Assert.True(UltraPlusConfig.PresetRank(UltraPlusConfig.ParsePreset("uplus_defaults", "")) < UltraPlusConfig.PresetRank(shipped));
    }

    [Theory]
    [InlineData("My RT", "My RT")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("bad/name", null)]
    [InlineData("..", null)]
    [InlineData("uplus_defaults", null)]
    [InlineData("", null)]
    public void SafePresetName(string name, string? expected) =>
        Assert.Equal(expected, UltraPlusConfig.SafePresetName(name));

    [Fact]
    public void Files_WriteChangesOneLine_BacksUpOnce_AndFlagsReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-uplus-" + Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(new[] { dir, "ue4ss" }.Concat(UltraPlusConfig.ConfigRelativeSegments).ToArray());
        Directory.CreateDirectory(cfgDir);
        try
        {
            var cfg = Path.Combine(cfgDir, UltraPlusConfig.ConfigFileName);
            File.WriteAllText(cfg, Config);
            File.WriteAllText(Path.Combine(cfgDir, UltraPlusConfig.SchemaFileName), Schema);
            File.WriteAllText(Path.Combine(cfgDir, "preset_high.ini"), "; HIGH\r\nBloom=1\r\n");
            File.WriteAllText(Path.Combine(cfgDir, "preset_uplus_defaults.ini"), "; DEFAULTS\r\nBloom=0.8\r\n");

            var state = UltraPlusFiles.Load(dir)!;
            Assert.Equal(cfg, state.ConfigPath);
            Assert.Equal("0.6", state.ValueOf(state.Settings[1]));
            Assert.Equal("game", state.ValueOf(state.Settings[2])); // not in the file: schema default
            Assert.False(state.AutoApplyOn);
            Assert.Equal(new[] { "U+ Recommended", "High" }, state.Presets.Select(p => p.DisplayName));

            Assert.True(UltraPlusFiles.Write(cfg, new Dictionary<string, string> { ["Bloom"] = "1.2" }));
            Assert.Equal(Config.Replace("Bloom=0.60", "Bloom=1.2"), File.ReadAllText(cfg));
            Assert.Equal(Config, File.ReadAllText(cfg + UltraPlusFiles.BackupSuffix));
            Assert.True(File.Exists(Path.Combine(cfgDir, UltraPlusConfig.ModifiedFlagFileName)));

            Assert.True(UltraPlusFiles.Write(cfg, new Dictionary<string, string> { ["Bloom"] = "0.3" }));
            Assert.Equal(Config, File.ReadAllText(cfg + UltraPlusFiles.BackupSuffix)); // first backup kept

            Assert.Null(UltraPlusFiles.SavePreset(cfgDir, "Mine", null, new Dictionary<string, string> { ["Bloom"] = "0.3" }));
            Assert.NotNull(UltraPlusFiles.SavePreset(cfgDir, "high", null, new Dictionary<string, string>())); // shipped: refused
            Assert.False(UltraPlusFiles.DeletePreset(cfgDir, "high"));
            Assert.True(UltraPlusFiles.DeletePreset(cfgDir, "Mine"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>UPM's data has keys that differ only by case (RTSkyLight / RTSkylight): that must not throw, and each
    /// exact key keeps its own text.</summary>
    [Fact]
    public void DataLookup_CaseOnlyDuplicates()
    {
        var lookup = new UltraPlusData.Lookup(new Dictionary<string, string>
        {
            ["RTSkyLight"] = "Sky light A",
            ["RTSkylight"] = "Sky light B",
        });
        Assert.Equal("Sky light A", lookup.Get("RTSkyLight"));
        Assert.Equal("Sky light B", lookup.Get("RTSkylight"));
        Assert.Equal("Sky light A", lookup.Get("rtskylight"));
        Assert.Null(lookup.Get("Other"));
        Assert.Null(new UltraPlusData.Lookup(null).Get("X"));
    }

    [Fact]
    public void Files_NoUltraPlus_ReturnsNull()
    {
        Assert.Null(UltraPlusFiles.Load(Path.GetTempPath()));
        Assert.Null(UltraPlusFiles.Load(null));
    }

    /// <summary>The developer's Phantom Blade Zero install, when present: every value must read back, and every
    /// shipped preset value must be one the schema allows.</summary>
    [Fact]
    public void RealInstall_PhantomBladeZero_WhenPresent()
    {
        const string game = @"E:\SteamLibrary\steamapps\common\PBZ\platform\steam";
        if (!Directory.Exists(game)) return;
        var state = UltraPlusFiles.Load(game);
        Assert.NotNull(state);
        Assert.True(state!.Settings.Count > 50);
        Assert.Contains(state.Presets, p => p.DisplayName == "U+ Recommended");
        foreach (var s in state.Settings)
            Assert.True(UltraPlusConfig.IsAllowed(s, state.ValueOf(s)) || s.Options.Count == 0, $"{s.Key}={state.ValueOf(s)}");
        foreach (var p in state.Presets)
            foreach (var kv in p.Values)
                if (state.Settings.FirstOrDefault(s => s.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)) is { } s)
                    Assert.True(UltraPlusConfig.IsAllowed(s, UltraPlusConfig.Normalize(s, kv.Value)), $"{p.StorageName}: {kv.Key}={kv.Value}");
    }
}
