using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Display modes: the store (modes, a game's Custom mode, rename) and switching with real ini files.</summary>
public class DisplayModeTests : IDisposable
{
    private const string Game = "Cyberpunk 2077";
    private const string Key = "Cyberpunk 2077|Steam";
    private readonly string _dir;

    public DisplayModeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rhi_dm_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private DisplayModeContext Ctx() => new() { GameName = Game, Store = "Steam", InstallPath = _dir, HasRenoDx = true };

    private string ReShadeIni => Path.Combine(_dir, "reshade.ini");
    private string ReLimiterIni => Path.Combine(_dir, "relimiter.ini");

    private void WriteFiles(int peak, int fps)
    {
        File.WriteAllText(ReShadeIni, $"[GENERAL]\nPresetPath=ReShadePreset.ini\n\n[renodx-preset1]\ntoneMapPeakNits={peak}\n\n[renodx-preset2]\ntoneMapPeakNits={peak}\n");
        File.WriteAllText(ReLimiterIni, $"[FrameLimiter]\ntarget_fps={fps}\nosd_toggle_key=F12\n");
    }

    private string? Peak() => DisplayModeItems.ReadRdx(ReShadeIni, "toneMapPeakNits");
    private string? Fps() => Dlssg20_30Service.ReadIniValue(ReLimiterIni, "FrameLimiter", "target_fps");

    // ── Store ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreMonitorAndTv_SwappingNothing()
    {
        var store = new DisplayModeStore();
        Assert.Equal(new[] { "Monitor", "TV" }, store.Modes.Select(m => m.Name));
        Assert.Empty(store.ItemsFor(Key));
        Assert.Null(store.ActiveModeId(Key)); // new games start Off
        Assert.False(store.IsManaged(Key));
    }

    [Fact]
    public void Modes_AddRenameMoveDelete()
    {
        var store = new DisplayModeStore();
        var id = store.AddMode("Handheld");
        Assert.NotNull(id);
        Assert.Null(store.AddMode("handheld")); // duplicate, any case
        Assert.NotNull(store.ValidateName(""));
        Assert.NotNull(store.ValidateName(new string('x', DisplayModeStore.MaxNameLength + 1)));
        Assert.Null(store.ValidateName("TV", "tv")); // renaming to its own name
        Assert.True(store.RenameMode(id!, "Deck"));
        Assert.True(store.MoveMode(id!, -1));
        Assert.Equal(new[] { "Monitor", "Deck", "TV" }, store.Modes.Select(m => m.Name));

        Assert.NotNull(store.ValidateName("custom")); // a game's own mode is called Custom
        store.SetActive(Key, id!, null);
        Assert.True(store.DeleteMode(id!));
        Assert.Null(store.ActiveModeId(Key)); // turned off
        Assert.False(store.HasGameState(Key)); // nothing left to save

        while (store.Modes.Count > 1) store.DeleteMode(store.Modes[^1].Id);
        Assert.False(store.DeleteMode(store.Modes[0].Id)); // one always stays
    }

    [Fact]
    public void MaxModes_IsEnforced()
    {
        var store = new DisplayModeStore();
        for (int i = store.Modes.Count; i < DisplayModeStore.MaxModes; i++) Assert.NotNull(store.AddMode("Mode " + i));
        Assert.Null(store.AddMode("One too many"));
    }

    [Fact]
    public void Custom_OnlyForTheGameThatMadeIt()
    {
        const string Other = "Other|Steam";
        var store = new DisplayModeStore();
        Assert.Equal(2, store.ModesFor(Key).Count);
        store.CreateCustom(Key);
        Assert.Equal(new[] { "Monitor", "TV", "Custom" }, store.ModesFor(Key).Select(m => m.Name));
        Assert.Equal(2, store.ModesFor(Other).Count); // no Custom for other games

        store.SetInCustom(Key, DisplayModeItemKeys.RdxPeakNits, true);
        store.SetCustomValue(Key, DisplayModeItemKeys.RdxPeakNits, "650");
        Assert.Equal("650", store.Effective(Key, DisplayModeStore.CustomId, DisplayModeItemKeys.RdxPeakNits));
        Assert.True(store.Swaps(Key, DisplayModeItemKeys.RdxPeakNits));
        Assert.False(store.Swaps(Other, DisplayModeItemKeys.RdxPeakNits));

        store.SetInCustom(Key, DisplayModeItemKeys.RdxPeakNits, false); // unticked: normal value, value kept for later
        Assert.Null(store.Effective(Key, DisplayModeStore.CustomId, DisplayModeItemKeys.RdxPeakNits));
        Assert.Equal("650", store.CustomValue(Key, DisplayModeItemKeys.RdxPeakNits));

        store.SetActive(Key, DisplayModeStore.CustomId, null);
        Assert.Equal(DisplayModeStore.CustomId, store.ActiveModeId(Key));
        Assert.Equal("Custom", store.NameOf(store.ActiveModeId(Key)));
        store.SetActive(Other, DisplayModeStore.CustomId, null); // no Custom mode: refused
        Assert.Null(store.ActiveModeId(Other));

        store.DeleteCustom(Key);
        Assert.Null(store.ActiveModeId(Key));
        Assert.False(store.HasCustom(Key));
        Assert.False(store.HasGameState(Key));
    }

    [Fact]
    public void Items_EverythingAModeSetsPlusCustom()
    {
        var store = new DisplayModeStore();
        Assert.False(store.Swaps(Key, DisplayModeItemKeys.RdxPeakNits));
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "1000"); // set in Settings: every game swaps it
        Assert.True(store.Swaps(Key, DisplayModeItemKeys.RdxPeakNits));
        Assert.True(store.Swaps("Other|Steam", DisplayModeItemKeys.RdxPeakNits));
        Assert.Equal("1000", store.Effective(Key, "tv", DisplayModeItemKeys.RdxPeakNits));
        Assert.Null(store.Effective(Key, "monitor", DisplayModeItemKeys.RdxPeakNits));
    }

    [Fact]
    public void Load_DropsOlderOwnValuesAndLists()
    {
        var data = new DisplayModeData
        {
            Modes = { new DisplayModeDefinition { Id = "monitor", Name = "Monitor" } },
            DefaultItems = new() { DisplayModeItemKeys.RdxPeakNits },
            Games =
            {
                [Key] = new GameDisplayModeState
                {
                    Mode = "monitor",
                    Items = new() { DisplayModeItemKeys.RdxPeakNits },
                    Values = { ["monitor"] = new() { [DisplayModeItemKeys.RdxPeakNits] = "500" } },
                },
            },
        };
        var store = new DisplayModeStore();
        store.Load(data);
        Assert.Equal("monitor", store.ActiveModeId(Key));
        Assert.Empty(store.ItemsFor(Key));
        Assert.False(store.HasCustom(Key));
        Assert.Null(store.Snapshot().DefaultItems);
        Assert.Null(store.Snapshot().Games[Key].Items);
        Assert.Empty(store.Snapshot().Games[Key].Values);
    }

    [Fact]
    public void Rename_MovesStateForEveryStore()
    {
        var store = new DisplayModeStore();
        store.SetActive(Key, "tv", new[] { _dir });
        store.SetActive("Cyberpunk 2077|GOG", "tv", null);
        store.MigrateGameRename(Game, "Cyberpunk");
        Assert.Equal("tv", store.ActiveModeId("Cyberpunk|Steam"));
        Assert.Equal("tv", store.ActiveModeId("Cyberpunk|GOG"));
        Assert.False(store.HasGameState(Key));
        Assert.Equal("Cyberpunk|Steam", store.FindManagedByFolder(_dir + "\\"));
    }

    [Fact]
    public void Snapshot_RoundTripsThroughJson()
    {
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.UlTargetFps, "117");
        store.CreateCustom(Key);
        store.SetInCustom(Key, DisplayModeItemKeys.RdxPeakNits, true);
        store.SetCustomValue(Key, DisplayModeItemKeys.RdxPeakNits, "650");
        store.SetActive(Key, "tv", new[] { _dir });

        var json = JsonSerializer.Serialize(store.Snapshot());
        var copy = new DisplayModeStore();
        copy.Load(JsonSerializer.Deserialize<DisplayModeData>(json));

        Assert.Equal("tv", copy.ActiveModeId(Key));
        Assert.Equal("117", copy.ModeDefault("TV", DisplayModeItemKeys.UlTargetFps)); // ids are case-insensitive
        Assert.True(copy.HasCustom("cyberpunk 2077|steam"));
        Assert.Equal("650", copy.CustomValue(Key, "rdxpeaknits"));
        Assert.Equal(new[] { DisplayModeItemKeys.UlTargetFps, DisplayModeItemKeys.RdxPeakNits }, copy.ItemsFor(Key));
    }

    [Fact]
    public void Load_NullOrEmptyGivesDefaults()
    {
        var store = new DisplayModeStore();
        store.Load(new DisplayModeData());
        Assert.Equal(2, store.Modes.Count);
        store.Load(null);
        Assert.Equal(2, store.Modes.Count);
    }

    // ── Switching ─────────────────────────────────────────────────────────────

    [Fact]
    public void Switch_WritesTheNewModeAndNormalValuesBack()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        store.SetModeDefault("tv", DisplayModeItemKeys.UlTargetFps, "117");
        var svc = new DisplayModeService(store);

        var r = svc.Switch(Ctx(), "tv");
        Assert.Equal(2, r.WrittenCount);
        Assert.Equal("700", Peak());
        Assert.Equal("117", Fps());
        Assert.Contains("toneMapPeakNits=700", File.ReadAllText(ReShadeIni).Split("[renodx-preset2]")[1]); // every preset
        // Coming from Off: the starting values are its values from before modes, not Monitor's
        Assert.Equal("1000", store.OriginalValue(Key, DisplayModeItemKeys.RdxPeakNits));
        Assert.Equal("141", store.OriginalValue(Key, DisplayModeItemKeys.UlTargetFps));
        Assert.True(store.IsManaged(Key));

        // Changed in game while on the TV, then back to Monitor and to TV again
        DisplayModeItems.WriteRdx(ReShadeIni, "toneMapPeakNits", "650");
        svc.Switch(Ctx(), "monitor"); // Monitor has no values: the game's normal values
        Assert.Equal("1000", Peak());
        Assert.Equal("141", Fps());
        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak()); // the mode's value again: in-game changes aren't kept
        Assert.Equal("117", Fps());
    }

    [Fact]
    public void Switch_InGameChange_GetsTheModesValueBack()
    {
        // Set in Settings: an in-game change isn't kept, the mode's value comes back
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        var svc = new DisplayModeService(store);
        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak());
        Assert.Equal("141", Fps()); // nothing sets the frame rate cap: left alone
        DisplayModeItems.WriteRdx(ReShadeIni, "toneMapPeakNits", "650");
        svc.Switch(Ctx(), "monitor");
        Assert.Equal("1000", Peak()); // Monitor sets nothing: its normal value
        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak());
    }

    [Fact]
    public void RenoDxKeys_BothSpellings_ReadRenoDxsOwnAndWriteAllLineByLine()
    {
        // RenoDX's own key plus an older RHI write with a capital T (ReShade's ini is case-sensitive)
        File.WriteAllText(ReShadeIni,
            "[renodx-preset1]\r\ntoneMapGameNits=203\r\ntoneMapPeakNits=1000\r\nToneMapPeakNits=1050\r\n; comment\r\n[renodx-preset2]\r\nOther=1\r\n");
        Assert.Equal("1000", Peak()); // RenoDX's value, not the stale capital-T one

        DisplayModeItems.WriteRdx(ReShadeIni, "toneMapPeakNits", "700");
        var text = File.ReadAllText(ReShadeIni);
        Assert.Contains("toneMapPeakNits=700\r\nToneMapPeakNits=700\r\n; comment", text); // both lines, nothing merged or lost
        Assert.Contains("[renodx-preset2]\r\nOther=1\r\ntoneMapPeakNits=700\r\nToneMapPeakNits=700\r\n", text); // both spellings added to a section without it
        Assert.DoesNotContain("\r\r", text);
    }

    [Fact]
    public void RenoDxKeys_NoPresetSection_CreatesPreset1()
    {
        File.WriteAllText(ReShadeIni, "[GENERAL]\nPresetPath=x.ini\n");
        DisplayModeItems.WriteRdx(ReShadeIni, "toneMapPeakNits", "800");
        Assert.Equal("[GENERAL]\nPresetPath=x.ini\n\n[renodx-preset1]\ntoneMapPeakNits=800\nToneMapPeakNits=800\n", File.ReadAllText(ReShadeIni));
    }

    [Fact]
    public void Switch_ModeWithoutAValue_DoesNotInheritThePreviousModesValue()
    {
        // TV gets a value only after the game was switched to it once (the bug: TV kept Monitor's value)
        WriteFiles(peak: 1050, fps: 141);
        var store = new DisplayModeStore();
        var svc = new DisplayModeService(store);
        svc.Switch(Ctx(), "tv");
        Assert.Equal("1050", Peak()); // nothing to write yet
        svc.Switch(Ctx(), "monitor");

        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "2000");
        svc.Switch(Ctx(), "tv");
        Assert.Equal("2000", Peak());
        svc.Switch(Ctx(), "monitor");
        Assert.Equal("1050", Peak());
        svc.Switch(Ctx(), "tv");
        Assert.Equal("2000", Peak());
    }

    [Fact]
    public void ReLimiterInstalledLater_GetsTheModesValueNotZero()
    {
        WriteFiles(peak: 1000, fps: 141);
        File.Delete(ReLimiterIni); // ReLimiter not installed yet
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.UlTargetFps, "118");
        var svc = new DisplayModeService(store);
        svc.Switch(Ctx(), "tv");

        // ReLimiter installed while in TV: the install writes the global cap into a fresh relimiter.ini
        var keep = svc.Keep(_dir, DisplayModeItemKeys.ReLimiterItems);
        Assert.NotNull(keep);
        File.WriteAllText(ReLimiterIni, "[FrameLimiter]\ntarget_fps=323\n");
        keep!();
        Assert.Equal("118", Fps());
    }

    [Fact]
    public void Switch_LeavesItemsWithoutAValueAlone()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore(); // TV has no defaults
        var svc = new DisplayModeService(store);
        var r = svc.Switch(Ctx(), "tv");
        Assert.Equal(0, r.WrittenCount);
        Assert.Equal("1000", Peak());
        Assert.Equal("141", Fps());
        Assert.Contains("Nothing to change", r.Summary);
    }

    [Fact]
    public void Switch_SkipsItemsTheGameCantUse()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.DcFpsLimit, "117.5");
        store.SetModeDefault("tv", DisplayModeItemKeys.UlTargetFps, "117");
        var r = new DisplayModeService(store).Switch(Ctx(), "tv");
        Assert.False(File.Exists(Path.Combine(_dir, "DisplayCommander.ini"))); // not created
        Assert.Contains(r.Items, i => i.Item.Key == DisplayModeItemKeys.DcFpsLimit && i.Problem != null && !i.Written);
        Assert.Equal("117", Fps());
    }

    [Fact]
    public void Switch_RenoDxItemsNeedRenoDx()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        var ctx = new DisplayModeContext { GameName = Game, Store = "Steam", InstallPath = _dir, HasRenoDx = false };
        new DisplayModeService(store).Switch(ctx, "tv");
        Assert.Equal("1000", Peak());
    }

    [Fact]
    public void Keep_PutsModeValuesBackAfterAGlobalWrite()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        var svc = new DisplayModeService(store);

        Assert.Null(svc.Keep(_dir, DisplayModeItemKeys.ReShadeIniItems)); // not managed yet: global writes go ahead
        svc.Switch(Ctx(), "tv");

        var keep = svc.Keep(_dir, DisplayModeItemKeys.ReShadeIniItems);
        Assert.NotNull(keep);
        DisplayModeItems.WriteRdx(ReShadeIni, "toneMapPeakNits", "400"); // e.g. "Write peak brightness to all games"
        keep!();
        Assert.Equal("700", Peak());

        // A fresh ini (ReShade reinstalled): the mode's value is written into it
        File.Delete(ReShadeIni);
        keep = svc.Keep(_dir, DisplayModeItemKeys.ReShadeIniItems);
        File.WriteAllText(ReShadeIni, "[GENERAL]\n");
        keep!();
        Assert.Equal("700", Peak());

        // Items it doesn't swap aren't kept
        Assert.Null(svc.Keep(_dir, new[] { DisplayModeItemKeys.RsOverlayKey }));
    }

    [Fact]
    public void LaunchItems_AreStoredNotWritten()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.Hdr, "On");
        store.SetModeDefault("tv", DisplayModeItemKeys.Resolution, "3840x2160@120");
        var r = new DisplayModeService(store).Switch(Ctx(), "tv");
        Assert.Equal(0, r.WrittenCount);
        Assert.Equal("On", store.ActiveValue(Key, DisplayModeItemKeys.Hdr));
        Assert.Equal("3840x2160@120", store.ActiveValue(Key, DisplayModeItemKeys.Resolution));
        Assert.Null(store.ActiveValue(Key, DisplayModeItemKeys.LaunchArgs)); // not swapped
        Assert.Contains("on launch", r.Summary);
    }

    [Fact]
    public void Keys_WrittenToEveryReShadeIni()
    {
        WriteFiles(peak: 1000, fps: 141);
        File.WriteAllText(Path.Combine(_dir, "ReShade2.ini"), "[INPUT]\nKeyOverlay=36,0,0,0\n");
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RsOverlayKey, "35,0,0,0");
        store.SetModeDefault("tv", DisplayModeItemKeys.UlOsdKey, "Ctrl+F11");
        new DisplayModeService(store).Switch(Ctx(), "tv");
        Assert.Equal("35,0,0,0", Dlssg20_30Service.ReadIniValue(ReShadeIni, "INPUT", "KeyOverlay"));
        Assert.Equal("35,0,0,0", Dlssg20_30Service.ReadIniValue(Path.Combine(_dir, "ReShade2.ini"), "INPUT", "KeyOverlay"));
        Assert.Equal("Ctrl+F11", Dlssg20_30Service.ReadIniValue(ReLimiterIni, "FrameLimiter", "osd_toggle_key"));
    }

    [Theory]
    [InlineData(DisplayModeItemKeys.UlTargetFps, "117", "117")]
    [InlineData(DisplayModeItemKeys.UlTargetFps, "0", "0")]
    [InlineData(DisplayModeItemKeys.UlTargetFps, "5", null)]
    [InlineData(DisplayModeItemKeys.RdxPeakNits, "0", null)]
    [InlineData(DisplayModeItemKeys.RdxPeakNits, "abc", null)]
    [InlineData(DisplayModeItemKeys.DcFpsLimit, "117,456", "117.456")]
    public void Normalize_ChecksRanges(string key, string text, string? expected)
        => Assert.Equal(expected, DisplayModeItems.Get(key)!.Normalize(text));

    [Fact]
    public void Format_ShowsReadableValues()
    {
        Assert.Equal("700 nits", DisplayModeItems.Get(DisplayModeItemKeys.RdxPeakNits)!.Format("700"));
        Assert.Equal("Off", DisplayModeItems.Get(DisplayModeItemKeys.UlTargetFps)!.Format("0"));
        Assert.Equal("4x", DisplayModeItems.Get(DisplayModeItemKeys.MfgMode)!.Format("fixed:3"));
        Assert.Equal("End", DisplayModeItems.Get(DisplayModeItemKeys.RsOverlayKey)!.Format("35,0,0,0"));
    }

    // ── Off ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TurnOff_WritesGlobalValuesElseTheValuesFromBeforeModes()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        store.SetModeDefault("tv", DisplayModeItemKeys.UlTargetFps, "117");
        var svc = new DisplayModeService(store)
        {
            // Only the frame rate cap has a global setting here
            GlobalValue = (_, item) => item == DisplayModeItemKeys.UlTargetFps ? "60" : null,
        };
        svc.Switch(Ctx(), "tv");
        DisplayModeItems.WriteRdx(ReShadeIni, "toneMapPeakNits", "650"); // changed in game

        var r = svc.TurnOff(Ctx());
        Assert.True(r.TurnedOff);
        Assert.Equal("1000", Peak()); // no global: what it had before modes
        Assert.Equal("60", Fps());    // the global setting
        Assert.Null(store.ActiveModeId(Key));
        Assert.False(store.IsManaged(Key));
        Assert.Null(store.OriginalValue(Key, DisplayModeItemKeys.RdxPeakNits));
        Assert.Null(svc.Keep(_dir, DisplayModeItemKeys.ReShadeIniItems)); // global writes reach it again

        // Back to TV: its value, and a fresh before-modes record
        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak());
        Assert.Equal("1000", store.OriginalValue(Key, DisplayModeItemKeys.RdxPeakNits));
    }

    [Fact]
    public void TurnOff_WhenAlreadyOff_DoesNothing()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        var svc = new DisplayModeService(store) { GlobalValue = (_, _) => "60" };
        var r = svc.TurnOff(Ctx());
        Assert.Empty(r.Items);
        Assert.Equal("141", Fps());
    }

    [Fact]
    public void ModeWithoutAValue_UsesTheNormalValue()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        var svc = new DisplayModeService(store);
        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak());
        svc.Switch(Ctx(), "monitor"); // Monitor has no peak value: back to the game's own, not TV's 700
        Assert.Equal("1000", Peak());
    }

    [Fact]
    public void LaunchItems_DontApplyWhileOff()
    {
        var store = new DisplayModeStore();
        store.SetModeDefault("monitor", DisplayModeItemKeys.Hdr, "On");
        Assert.Null(store.ActiveValue(Key, DisplayModeItemKeys.Hdr)); // Off: the HDR button's own rule
        store.SetActive(Key, "monitor", null);
        Assert.Equal("On", store.ActiveValue(Key, DisplayModeItemKeys.Hdr));
    }

    [Fact]
    public void ItemAddedWhileInAMode_ItsValueCountsAsTheBeforeModesValue()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        var svc = new DisplayModeService(store);
        svc.Switch(Ctx(), "tv");
        store.SetModeDefault("tv", DisplayModeItemKeys.UlTargetFps, "117"); // set in Settings while the game is in TV
        svc.TurnOff(Ctx());
        Assert.Equal("1000", Peak());
        Assert.Equal("141", Fps()); // never written by a mode: left as it is
    }

    [Fact]
    public void Switch_CustomMode_ChangesOnlyWhatItTicks()
    {
        WriteFiles(peak: 1000, fps: 141);
        var store = new DisplayModeStore();
        store.SetModeDefault("tv", DisplayModeItemKeys.RdxPeakNits, "700");
        store.CreateCustom(Key);
        store.SetInCustom(Key, DisplayModeItemKeys.UlTargetFps, true);
        store.SetCustomValue(Key, DisplayModeItemKeys.UlTargetFps, "60");
        var svc = new DisplayModeService(store);

        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak());
        Assert.Equal("141", Fps()); // TV doesn't set the cap: normal value

        svc.Switch(Ctx(), DisplayModeStore.CustomId);
        Assert.Equal("1000", Peak()); // Custom doesn't tick peak: normal value, not TV's
        Assert.Equal("60", Fps());

        svc.Switch(Ctx(), "tv");
        Assert.Equal("700", Peak());
        Assert.Equal("141", Fps());

        svc.TurnOff(Ctx());
        Assert.Equal("1000", Peak());
        Assert.Equal("141", Fps());
    }

    [Fact]
    public void Snapshot_KeepsTheBeforeModesValues()
    {
        var store = new DisplayModeStore();
        store.SetActive(Key, "tv", new[] { _dir });
        store.SetOriginal(Key, DisplayModeItemKeys.RdxPeakNits, "1000");
        var copy = new DisplayModeStore();
        copy.Load(JsonSerializer.Deserialize<DisplayModeData>(JsonSerializer.Serialize(store.Snapshot())));
        Assert.Equal("1000", copy.OriginalValue(Key, DisplayModeItemKeys.RdxPeakNits));
        copy.SetOff(Key);
        Assert.False(copy.HasGameState(Key));
    }

    [Fact]
    public void ItemKeys_AreUnique()
    {
        var keys = DisplayModeItems.All.Select(i => i.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(DisplayModeItems.All, i => Assert.Contains(i.Group, DisplayModeItems.Groups));
    }
}
