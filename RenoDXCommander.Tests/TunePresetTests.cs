using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Tune presets: the store (built-in preset, names, order, hide, delete, rename migration, save round trip) and the planner.</summary>
public class TunePresetTests
{
    private static readonly IReadOnlyList<string> SrList = new[] { "310.5.3", "310.4.0", "310.1.0" };
    private static readonly IReadOnlyList<string> SlList = new[] { "2.9.0", "2.8.0" };

    // ── Store ──

    [Fact]
    public void NewStore_HasBuiltInRhiTuned()
    {
        var store = new TunePresetStore();
        var p = Assert.Single(store.Presets);
        Assert.Equal(TunePresetStore.BuiltInId, p.Id);
        Assert.Equal("RHI Tuned", p.Name);
        Assert.True(p.ReShade);
        Assert.Equal(TuneHdrMod.RenoDx, p.HdrMod);
        Assert.Equal(TuneLimiter.ReLimiter, p.FrameLimiter);
        Assert.Equal(TuneDll.Newest, p.DlssSr);
        Assert.Equal(TuneDll.Newest, p.DlssRr);
        Assert.Equal(TuneDll.Newest, p.DlssFg);
        Assert.Equal(TuneDll.Newest, p.Streamline);
        Assert.Equal("13", p.Nvidia[TuneItemKeys.SrPreset]); // M
        Assert.Equal("6", p.Nvidia[TuneItemKeys.RrPreset]);  // F
        Assert.Equal("2", p.Nvidia[TuneItemKeys.FgPreset]);  // B
    }

    [Fact]
    public void BuiltIn_CantBeDeleted_ButCanBeReset()
    {
        var store = new TunePresetStore();
        Assert.False(store.Delete(TunePresetStore.BuiltInId));
        var p = store.Get(TunePresetStore.BuiltInId)!;
        p.ReShade = false;
        p.Nvidia.Clear();
        Assert.True(store.Update(p));
        Assert.False(store.Get(TunePresetStore.BuiltInId)!.ReShade);
        Assert.True(store.Rename(TunePresetStore.BuiltInId, "Mine"));
        Assert.True(store.ResetBuiltIn());
        var reset = store.Get(TunePresetStore.BuiltInId)!;
        Assert.True(reset.ReShade);
        Assert.Equal("Mine", reset.Name); // name kept
        Assert.Equal(3, reset.Nvidia.Count);
    }

    [Fact]
    public void Names_AreValidated()
    {
        var store = new TunePresetStore();
        Assert.NotNull(store.ValidateName("", null));
        Assert.NotNull(store.ValidateName("rhi tuned", null)); // duplicate, any case
        Assert.Null(store.ValidateName("rhi tuned", TunePresetStore.BuiltInId)); // renaming itself
        Assert.NotNull(store.ValidateName(new string('x', TunePresetStore.MaxNameLength + 1), null));
        Assert.Null(store.Add("  Fast  ") is { } id ? null : "failed");
        Assert.Equal("Fast", store.Presets[1].Name);
    }

    [Fact]
    public void Add_CopiesSettings_MoveHideDelete()
    {
        var store = new TunePresetStore();
        var id = store.Add("Competitive")!;
        var p = store.Get(id)!;
        Assert.True(p.ReShade); // copy of RHI Tuned
        Assert.False(TunePresetStore.IsBuiltIn(id));

        Assert.True(store.Move(id, -1));
        Assert.Equal(id, store.Presets[0].Id);
        Assert.False(store.Move(id, -1)); // already first

        Assert.True(store.SetHidden(id, true));
        Assert.DoesNotContain(store.Visible, v => v.Id == id);
        Assert.Contains(store.Presets, v => v.Id == id);

        Assert.True(store.Delete(id));
        Assert.Single(store.Presets);
    }

    [Fact]
    public void MaxPresets_IsEnforced()
    {
        var store = new TunePresetStore();
        for (int i = 1; i < TunePresetStore.MaxPresets; i++) Assert.NotNull(store.Add($"P{i}"));
        Assert.Null(store.Add("One too many"));
    }

    [Fact]
    public void Snapshot_RoundTripsThroughJson_AndRestoresBuiltIn()
    {
        var store = new TunePresetStore();
        var id = store.Add("Mine")!;
        store.SetGameState("Cyberpunk 2077|Steam", new TunedGameState
        {
            PresetId = id, PresetName = "Mine", Installed = { BackupComponents.ReShade },
            Dlls = { ["SR"] = new TunedDll { Path = @"C:\g\nvngx_dlss.dll", PreviousVersion = "310.1.0", NewVersion = "310.5.3" } },
            NvidiaBefore = { [TuneItemKeys.SrPreset] = "0" },
        });
        var json = JsonSerializer.Serialize(store.Snapshot());

        var loaded = new TunePresetStore();
        loaded.Load(JsonSerializer.Deserialize<TuneData>(json));
        Assert.Equal(2, loaded.Presets.Count);
        var st = loaded.GameState("cyberpunk 2077|steam")!; // case-insensitive keys survive the round trip
        Assert.Equal("310.1.0", st.Dlls["sr"].PreviousVersion);
        Assert.Equal("0", st.NvidiaBefore[TuneItemKeys.SrPreset.ToLowerInvariant()]);
        Assert.Equal("13", loaded.Get(TunePresetStore.BuiltInId)!.Nvidia["dlsssrpreset"]);

        // A save without the built-in preset gets it back
        var noBuiltIn = new TuneData { Presets = { new TunePreset { Id = "x", Name = "X" } } };
        loaded.Load(noBuiltIn);
        Assert.Equal(TunePresetStore.BuiltInId, loaded.Presets[0].Id);
    }

    [Fact]
    public void Rename_MovesTunedGameRecord()
    {
        var store = new TunePresetStore();
        store.SetGameState("Old Name|Steam", new TunedGameState { PresetId = TunePresetStore.BuiltInId });
        store.SetGameState("Old Name|Epic", new TunedGameState { PresetId = TunePresetStore.BuiltInId });
        store.MigrateGameRename("Old Name", "New Name");
        Assert.True(store.IsTuned("New Name|Steam"));
        Assert.True(store.IsTuned("New Name|Epic"));
        Assert.False(store.IsTuned("Old Name|Steam"));
        Assert.Equal(2, store.CountTunedWith(TunePresetStore.BuiltInId));
    }

    // ── Planner ──

    private static TuneGameFacts Facts(
        bool rs = false, bool rdxMod = true, bool rdx = false, bool lumaMod = false, bool luma = false,
        bool ul = false, bool dc = false, bool noAddons = false, string? blocker = null, string? rsBlocked = null,
        string? sr = "310.1.0", bool srOverride = false, string? sl = "2.8.0", bool nvidia = true,
        Dictionary<string, string>? now = null) => new()
    {
        GameName = "Game",
        Blocker = blocker,
        RsInstalled = rs,
        RsBlocked = rsBlocked,
        NoAddons = noAddons,
        HasRenoDxMod = rdxMod,
        RdxInstalled = rdx,
        HasLumaMod = lumaMod,
        LumaInstalled = luma,
        UlInstalled = ul,
        DcInstalled = dc,
        Dlls = new(StringComparer.OrdinalIgnoreCase)
        {
            [TuneDllSlots.Sr] = new TuneDllFacts(sr != null, sr, srOverride, SrList),
            [TuneDllSlots.Rr] = new TuneDllFacts(false, null, false, SrList),
            [TuneDllSlots.Fg] = new TuneDllFacts(false, null, false, SrList),
            [TuneDllSlots.Sl] = new TuneDllFacts(sl != null, sl, false, SlList),
        },
        NvidiaReady = nvidia,
        IsAdmin = false,
        NvidiaNow = now ?? new(StringComparer.OrdinalIgnoreCase),
    };

    private static TuneStep Step(TunePlan plan, string id) => plan.Steps.Single(s => s.Id == id);

    [Fact]
    public void RhiTuned_FreshGame_InstallsEverythingAndUpdatesFiles()
    {
        var plan = TunePlanner.Build(TunePresetStore.CreateBuiltIn(), Facts(now: new() { [TuneItemKeys.SrPreset] = "0" }));
        Assert.Null(plan.Blocker);
        Assert.True(Step(plan, BackupComponents.ReShade).Runs);
        Assert.True(Step(plan, BackupComponents.RenoDx).Runs);
        Assert.True(Step(plan, BackupComponents.ReLimiter).Runs);
        var sr = Step(plan, TuneDllSlots.Sr);
        Assert.True(sr.Runs);
        Assert.Equal("310.5.3", sr.Target); // newest = first in the list
        Assert.Equal("310.1.0 → 310.5.3", sr.Detail);
        Assert.True(Step(plan, TuneDllSlots.Sl).Runs);
        Assert.False(Step(plan, TuneDllSlots.Rr).Runs); // the game has no RR file
        Assert.True(Step(plan, TuneItemKeys.SrPreset).Runs);
        Assert.Contains("→", Step(plan, TuneItemKeys.SrPreset).Detail);
        Assert.False(Step(plan, TuneItemKeys.RrPreset).Runs); // no RR
        Assert.False(Step(plan, TuneItemKeys.FgPreset).Runs); // no FG
        // Order: installs, then files, then NVIDIA
        var kinds = plan.Steps.Select(s => s.Kind).ToList();
        Assert.Equal(kinds.OrderBy(k => k).ToList(), kinds);
    }

    [Fact]
    public void Blocker_StopsEverything()
    {
        var plan = TunePlanner.Build(TunePresetStore.CreateBuiltIn(), Facts(blocker: "The game is running."));
        Assert.Equal("The game is running.", plan.Blocker);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void AlreadyInstalledAndCurrent_IsSkipped()
    {
        var plan = TunePlanner.Build(TunePresetStore.CreateBuiltIn(),
            Facts(rs: true, rdx: true, ul: true, sr: "310.5.3.0", sl: "2.9.0", now: new() { [TuneItemKeys.SrPreset] = "13" }));
        Assert.False(plan.Running.Any());
        Assert.Equal("Already installed", Step(plan, BackupComponents.ReShade).Skip);
        Assert.Equal("Already 310.5.3", Step(plan, TuneDllSlots.Sr).Skip);
        Assert.StartsWith("Already", Step(plan, TuneItemKeys.SrPreset).Skip);
    }

    [Fact]
    public void NoRenoDxMod_SkipsIt_ElseLumaFallsBackToLuma()
    {
        var p = TunePresetStore.CreateBuiltIn();
        var plan = TunePlanner.Build(p, Facts(rdxMod: false));
        Assert.Equal("No RenoDX mod for this game", Step(plan, BackupComponents.RenoDx).Skip);

        p.HdrMod = TuneHdrMod.RenoDxElseLuma;
        plan = TunePlanner.Build(p, Facts(rdxMod: false, lumaMod: true));
        Assert.True(Step(plan, BackupComponents.Luma).Runs);
        Assert.DoesNotContain(plan.Steps, s => s.Id == BackupComponents.RenoDx);

        plan = TunePlanner.Build(p, Facts(rdxMod: true, lumaMod: true));
        Assert.True(Step(plan, BackupComponents.RenoDx).Runs);
    }

    [Fact]
    public void ConflictingComponents_AreSkipped()
    {
        var p = TunePresetStore.CreateBuiltIn();
        var plan = TunePlanner.Build(p, Facts(luma: true, dc: true));
        Assert.Equal("Luma is installed", Step(plan, BackupComponents.RenoDx).Skip);
        Assert.Equal("Display Commander is installed", Step(plan, BackupComponents.ReLimiter).Skip);

        p.HdrMod = TuneHdrMod.Luma;
        p.FrameLimiter = TuneLimiter.DisplayCommander;
        plan = TunePlanner.Build(p, Facts(rdx: true, lumaMod: true, ul: true));
        Assert.Equal("RenoDX is installed", Step(plan, BackupComponents.Luma).Skip);
        Assert.Equal("ReLimiter is installed", Step(plan, BackupComponents.DisplayCommander).Skip);
    }

    [Fact]
    public void AddOns_NeedReShadeWithAddons()
    {
        var p = TunePresetStore.CreateBuiltIn();
        p.ReShade = false;
        var plan = TunePlanner.Build(p, Facts());
        Assert.Equal("Needs ReShade", Step(plan, BackupComponents.RenoDx).Skip);
        Assert.Equal("Needs ReShade", Step(plan, BackupComponents.ReLimiter).Skip);

        plan = TunePlanner.Build(TunePresetStore.CreateBuiltIn(), Facts(rs: true, noAddons: true));
        Assert.StartsWith("Needs ReShade with add-ons", Step(plan, BackupComponents.RenoDx).Skip);

        plan = TunePlanner.Build(TunePresetStore.CreateBuiltIn(), Facts(rsBlocked: "Install RE Framework first"));
        Assert.Equal("Install RE Framework first", Step(plan, BackupComponents.ReShade).Skip);
        Assert.Equal("Needs ReShade", Step(plan, BackupComponents.RenoDx).Skip);
    }

    [Fact]
    public void Dlls_DriverOverrideAndV1AndSpecificVersions()
    {
        var p = TunePresetStore.CreateBuiltIn();
        Assert.Equal("NVIDIA Override is on for it", Step(TunePlanner.Build(p, Facts(srOverride: true)), TuneDllSlots.Sr).Skip);
        Assert.Equal("Version 1 can't be updated", Step(TunePlanner.Build(p, Facts(sr: "1.0.13")), TuneDllSlots.Sr).Skip);

        p.DlssSr = "310.4.0";
        Assert.Equal("310.4.0", Step(TunePlanner.Build(p, Facts()), TuneDllSlots.Sr).Target);
        p.DlssSr = "999.0.0";
        Assert.Equal("Version 999.0.0 isn't available", Step(TunePlanner.Build(p, Facts()), TuneDllSlots.Sr).Skip);
        p.DlssSr = TuneDll.Leave;
        Assert.DoesNotContain(TunePlanner.Build(p, Facts()).Steps, s => s.Id == TuneDllSlots.Sr);
    }

    [Fact]
    public void Nvidia_NeedsCardAndAdminForReBar()
    {
        var p = new TunePreset { Id = "x", Name = "X" };
        p.Nvidia[TuneItemKeys.VSync] = "1199655232"; // Force On
        p.Nvidia[TuneItemKeys.ReBar] = "2";
        var plan = TunePlanner.Build(p, Facts(nvidia: false));
        Assert.All(plan.Steps, s => Assert.Equal("No NVIDIA graphics card / driver found", s.Skip));

        plan = TunePlanner.Build(p, Facts());
        Assert.True(Step(plan, TuneItemKeys.VSync).Runs);
        Assert.Equal("Needs Admin Mode", Step(plan, TuneItemKeys.ReBar).Skip);
    }

    [Fact]
    public void DescribeUndo_ListsWhatGoesBack()
    {
        var st = new TunedGameState
        {
            Installed = { BackupComponents.ReShade, BackupComponents.RenoDx },
            Dlls = { [TuneDllSlots.Sr] = new TunedDll { HadBackup = false }, [TuneDllSlots.Sl] = new TunedDll { HadBackup = true, PreviousVersion = "2.8.0" } },
            NvidiaBefore = { [TuneItemKeys.SrPreset] = "0" },
        };
        var lines = TunePlanner.DescribeUndo(st);
        Assert.Equal("Remove RenoDX", lines[0]); // newest first
        Assert.Equal("Remove ReShade", lines[1]);
        Assert.Contains("DLSS Super Resolution back to the game's own file", lines);
        Assert.Contains("Streamline back to 2.8.0", lines);
        Assert.Contains(lines, l => l.StartsWith("DLSS Super Resolution preset back to"));
    }

    [Fact]
    public void OptiScaler_InstallsWithDllName_SkipsTakenNames()
    {
        var p = new TunePreset { Id = "x", Name = "X", OptiScaler = true, OsDllName = "winmm.dll" };
        var step = Step(TunePlanner.Build(p, Facts()), BackupComponents.OptiScaler);
        Assert.True(step.Runs);
        Assert.Equal("winmm.dll", step.Target);
        Assert.Equal("as winmm.dll", step.Detail);

        var g = new TuneGameFacts { RsInstalled = true, RsFileName = "winmm.dll" };
        Assert.Equal("winmm.dll is ReShade's file name in this game", Step(TunePlanner.Build(p, g), BackupComponents.OptiScaler).Skip);
        Assert.Equal("Already installed", Step(TunePlanner.Build(p, new TuneGameFacts { OsInstalled = true }), BackupComponents.OptiScaler).Skip);
    }

    [Fact]
    public void NeuralRendering_AutoInstalledAndBlocked()
    {
        var p = new TunePreset { Id = "x", Name = "X", NrMethod = TuneNr.Auto, NrDllVersion = "310.9.16" };
        var step = Step(TunePlanner.Build(p, new TuneGameFacts { NrAuto = "ShortFuse" }), TuneComponentIds.NeuralRendering);
        Assert.True(step.Runs);
        Assert.Equal("ShortFuse", step.Target);
        Assert.Contains("NR DLL 310.9.16", step.Detail);

        Assert.Equal("Already installed",
            Step(TunePlanner.Build(p, new TuneGameFacts { NrAuto = "ShortFuse", NrInstalled = "ShortFuse" }), TuneComponentIds.NeuralRendering).Skip);
        Assert.StartsWith("GITC Uplift is installed",
            Step(TunePlanner.Build(p, new TuneGameFacts { NrAuto = "ShortFuse", NrInstalled = "Uplift" }), TuneComponentIds.NeuralRendering).Skip);

        p.NrMethod = "DLSS5Tool";
        p.NrAddonVersion = "1.2.3";
        var blocked = new TuneGameFacts { NrBlocked = { ["DLSS5Tool"] = "Needs a game with DLSS" } };
        Assert.Equal("DLSS5 Tool: Needs a game with DLSS", Step(TunePlanner.Build(p, blocked), TuneComponentIds.NeuralRendering).Skip);
        var ok = Step(TunePlanner.Build(p, new TuneGameFacts()), TuneComponentIds.NeuralRendering);
        Assert.Contains("version 1.2.3", ok.Detail);
        Assert.Contains("NR DLL 310.9.16", ok.Detail);
    }

    [Fact]
    public void TunedState_KeepsSettingsAndNrMethodThroughSave()
    {
        var store = new TunePresetStore();
        store.SetGameState("G|Steam", new TunedGameState
        {
            Installed = { TuneComponentIds.NeuralRendering }, NrMethodInstalled = "ShortFuse",
            SettingsBefore = { [TuneSettingKeys.NrDllVersion] = "", [TuneSettingKeys.OsDllName] = "dxgi.dll" },
        });
        var loaded = new TunePresetStore();
        loaded.Load(JsonSerializer.Deserialize<TuneData>(JsonSerializer.Serialize(store.Snapshot())));
        var st = loaded.GameState("G|Steam")!;
        Assert.Equal("ShortFuse", st.NrMethodInstalled);
        Assert.Equal("dxgi.dll", st.SettingsBefore["osdllname"]);
        Assert.Equal("Remove Neural Rendering (ShortFuse DLSS Tool)", TunePlanner.DescribeUndo(st)[0]);
    }

    [Fact]
    public void Extras_OrderSkipsAndOptions()
    {
        var p = new TunePreset { Id = "x", Name = "X", OptiScaler = true };
        p.Extras["UniversalMfg"] = "";
        p.Extras["DXVK"] = "Development";
        p.Extras["AsiLoader"] = "winmm.dll";
        p.Extras["HSF"] = "";
        var g = new TuneGameFacts
        {
            ExtrasInstalled = { "HSF" },
            ExtrasOption = { ["AsiLoader"] = ("winmm.dll", null), ["DXVK"] = ("Development", null), ["UniversalMfg"] = (null, "version.dll is used by ReShade in this game") },
        };
        var plan = TunePlanner.Build(p, g);
        var ids = plan.Steps.Select(s => s.Id).ToList();
        // DXVK before OptiScaler, the frame generation mods after it
        Assert.True(ids.IndexOf("Extra:DXVK") < ids.IndexOf(BackupComponents.OptiScaler));
        Assert.True(ids.IndexOf(BackupComponents.OptiScaler) < ids.IndexOf("Extra:UniversalMfg"));
        Assert.True(ids.IndexOf("Extra:AsiLoader") < ids.IndexOf("Extra:UniversalMfg"));
        Assert.Equal("as winmm.dll", Step(plan, "Extra:AsiLoader").Detail);
        Assert.Equal("variant Development", Step(plan, "Extra:DXVK").Detail);
        Assert.Equal("Already installed", Step(plan, "Extra:HSF").Skip);
        Assert.Equal("version.dll is used by ReShade in this game", Step(plan, "Extra:UniversalMfg").Skip);

        g.ExtrasBlocked["DXVK"] = "Not available for this game";
        Assert.Equal("Not available for this game", Step(TunePlanner.Build(p, g), "Extra:DXVK").Skip);
    }
}
