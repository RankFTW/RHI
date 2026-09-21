using System.Text.RegularExpressions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Tests for One-Click Optimize: status rules, preflight, undo planning, restore-point persistence, the shared defaults
/// applier, and "drift alarms" that fail when RHI's Neural Rendering source changes the rules this feature mirrors.
/// </summary>
public class OneClickOptimizeTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rhi-oneclick-" + Guid.NewGuid().ToString("N"));
    public OneClickOptimizeTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    // ── fakes / builders ──────────────────────────────────────────────────────────

    private sealed class FakeProfile : INvidiaGameProfile
    {
        public bool IsSupported { get; set; } = true;
        public uint Sr, Rr, Fg, Nr, SrScale, RrScale;
        public bool SrOv, RrOv, FgOv;
        public uint GetSrPreset(string g, string p) => Sr;
        public uint GetRrPreset(string g, string p) => Rr;
        public uint GetFgPreset(string g, string p) => Fg;
        public uint GetNrPreset(string g, string p) => Nr;
        public uint GetSrRenderScale(string g, string p) => SrScale;
        public uint GetRrRenderScale(string g, string p) => RrScale;
        public bool IsSrDriverOverrideActive(string g, string p) => SrOv;
        public bool IsRrDriverOverrideActive(string g, string p) => RrOv;
        public bool IsFgDriverOverrideActive(string g, string p) => FgOv;
        public bool SetSrPreset(string g, string p, uint v) { Sr = v; return true; }
        public bool SetRrPreset(string g, string p, uint v) { Rr = v; return true; }
        public bool SetFgPreset(string g, string p, uint v) { Fg = v; return true; }
        public bool SetNrPreset(string g, string p, uint v) { Nr = v; return true; }
        public bool SetSrRenderScale(string g, string p, uint v) { SrScale = v; return true; }
        public bool SetRrRenderScale(string g, string p, uint v) { RrScale = v; return true; }
        public bool SetSrDriverOverride(string g, string p, bool e) { SrOv = e; return true; }
        public bool SetRrDriverOverride(string g, string p, bool e) { RrOv = e; return true; }
        public bool SetFgDriverOverride(string g, string p, bool e) { FgOv = e; return true; }
    }

    private static OptimizationDefaults Defaults(uint sr = 11, uint rr = 5, uint fg = 2, uint scale = 67, bool srOv = true, bool rrOv = true, bool fgOv = true,
        string slVer = "2.14.1", string nrVer = "")
        => new() { SrPreset = sr, RrPreset = rr, FgPreset = fg, SrRenderScale = scale, SrOverride = srOv, RrOverride = rrOv, FgOverride = fgOv,
                   StreamlineVersion = slVer, NrVersion = nrVer };

    private static GameOptimizationSnapshot Snap(bool sr = true, bool rr = false, bool fg = false, bool sl = false, bool nrDll = false,
        uint srPreset = 0, uint srScale = 0, bool srOv = false, string? nrRoute = null, bool nrSupported = false, bool nrInstalled = false,
        string? dlssVer = "310.9.1", string? slVer = null, uint rrPreset = 0, bool rrOv = false, uint fgPreset = 0, bool fgOv = false, string? nrDllVer = null,
        bool reshade = true, bool? nrDllMatch = null)
        => new() { HasDlss = sr, HasDlssd = rr, HasDlssg = fg, HasStreamline = sl, HasDlssnr = nrDll, DlssVersion = dlssVer,
                   DlssdVersion = rr ? "310.9.1" : null, DlssgVersion = fg ? "310.9.1" : null, StreamlineVersion = slVer, DlssnrVersion = nrDllVer,
                   SrPreset = srPreset, SrRenderScale = srScale, SrOverride = srOv, RrPreset = rrPreset, RrOverride = rrOv, FgPreset = fgPreset, FgOverride = fgOv,
                   NrRoute = nrRoute, NrRouteSupported = nrSupported, NrInstalled = nrInstalled,
                   ReShadeInstalled = reshade, NrDllMatchesDefault = nrDllMatch };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Neural Rendering route
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    // hasDlss, 32bit, OpenGL, DX11, Vulkan -> route
    [InlineData(true, false, false, false, false, "ShortFuse")]          // native DLSS + DX12
    [InlineData(true, false, false, true, false, "DLSS5ToolBridge")]     // DX11 compatibility
    [InlineData(true, false, false, false, true, "DLSS5ToolBridge")]     // Vulkan
    [InlineData(false, false, false, false, false, "Feeder")]            // no native DLSS
    [InlineData(true, true, false, false, false, "Feeder")]              // 32-bit
    [InlineData(true, false, true, false, false, "Feeder")]              // OpenGL
    public void RecommendNrRoute_MatchesRhiAutoSelection(bool dlss, bool b32, bool gl, bool dx11, bool vk, string expected)
        => Assert.Equal(expected, OptimizationEvaluator.RecommendNrRoute(dlss, b32, gl, dx11, vk));

    [Theory]
    [InlineData("ShortFuse", true, false, false, false, false, true)]
    [InlineData("ShortFuse", true, true, false, false, false, false)]     // 32-bit
    [InlineData("ShortFuse", true, false, true, false, false, false)]     // OpenGL
    [InlineData("DLSS5Tool", false, false, false, false, false, false)]   // needs native DLSS
    [InlineData("DLSS5Tool", true, false, false, false, false, true)]
    [InlineData("DLSS5ToolBridge", true, false, false, true, false, true)]
    [InlineData("DLSS5ToolBridge", true, false, false, false, false, false)]   // DX12: no bridge
    [InlineData("Feeder", false, true, true, false, false, true)]         // always applicable
    [InlineData("Nonsense", true, false, false, false, false, false)]
    public void IsNrRouteSupported_FollowsMethodPickerRules(string method, bool dlss, bool b32, bool gl, bool dx11, bool vk, bool expected)
        => Assert.Equal(expected, OptimizationEvaluator.IsNrRouteSupported(method, dlss, b32, gl, dx11, vk));

    [Theory]
    [InlineData("ShortFuse", true)]
    [InlineData("DLSS5Tool", true)]
    [InlineData("DLSS5ToolBridge", true)]
    [InlineData("Feeder", false)]   // needs manual ReShade depth / motion-vector set-up
    [InlineData(null, false)]
    public void IsAutoInstallable_ExcludesFeeder(string? method, bool expected)
        => Assert.Equal(expected, OptimizationEvaluator.IsAutoInstallable(method));

    // ═══════════════════════════════════════════════════════════════════════════════
    // Status
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_NothingMatches_IsNotOptimized()
    {
        var r = OptimizationEvaluator.Evaluate(Defaults(), Snap(), null);
        Assert.Equal(OptimizationState.NotOptimized, r.State);
        Assert.All(r.Items, i => Assert.False(i.Satisfied));
    }

    [Fact]
    public void Evaluate_EverythingMatches_IsOptimized()
    {
        var r = OptimizationEvaluator.Evaluate(Defaults(), Snap(srOv: true, srPreset: 11, srScale: 67), null);
        Assert.Equal(OptimizationState.Optimized, r.State);
        Assert.Contains("Optimized", r.Summary);
    }

    [Fact]
    public void Evaluate_SomeMatch_IsPartial()
    {
        var r = OptimizationEvaluator.Evaluate(Defaults(), Snap(srOv: true, srPreset: 11, srScale: 0), null);
        Assert.Equal(OptimizationState.PartiallyOptimized, r.State);
        Assert.False(r.DefaultsChanged);
    }

    [Fact]
    public void Evaluate_DefaultsChangedSinceLastOptimize_IsFlagged_ButOnlyWhileNotFullyMatching()
    {
        var old = Defaults(sr: 10);                                    // the defaults used at the last optimize
        var now = Defaults(sr: 11);                                    // the user changed the SR preset since
        var snap = Snap(srOv: true, srPreset: 10, srScale: 67);        // game still has the old preset

        var changed = OptimizationEvaluator.Evaluate(now, snap, old.Fingerprint());
        Assert.Equal(OptimizationState.PartiallyOptimized, changed.State);
        Assert.True(changed.DefaultsChanged);
        Assert.StartsWith("Defaults changed", changed.Summary);

        var upToDate = OptimizationEvaluator.Evaluate(now, Snap(srOv: true, srPreset: 11, srScale: 67), old.Fingerprint());
        Assert.Equal(OptimizationState.Optimized, upToDate.State);
        Assert.False(upToDate.DefaultsChanged);
    }

    [Fact]
    public void Evaluate_ExplicitVersionDefault_ComparesInstalledVersion()
    {
        var d = new OptimizationDefaults { SrVersion = "310.9.1" };
        Assert.Equal(OptimizationState.Optimized, OptimizationEvaluator.Evaluate(d, Snap(dlssVer: "310.9.1"), null).State);
        Assert.Equal(OptimizationState.NotOptimized, OptimizationEvaluator.Evaluate(d, Snap(dlssVer: "3.8.10"), null).State);
        // with the driver override active on the game, Quick Apply skips the swap, so it counts as satisfied
        Assert.Equal(OptimizationState.Optimized, OptimizationEvaluator.Evaluate(d, Snap(dlssVer: "3.8.10", srOv: true), null).State);
    }

    [Fact]
    public void Evaluate_LegacyDlss1_IsIgnored_LikeQuickApply()
    {
        var r = OptimizationEvaluator.Evaluate(Defaults(), Snap(dlssVer: "1.0.12"), null);
        Assert.Empty(r.Items);
        Assert.Contains("Nothing to optimize", r.Summary);
    }

    [Fact]
    public void Evaluate_OnlyApplicableComponentsCount()
    {
        // RR/FG/Streamline defaults are configured, but this game only has SR
        var d = new OptimizationDefaults { RrPreset = 5, FgPreset = 2, StreamlineVersion = "2.14.1", SrPreset = 11 };
        var r = OptimizationEvaluator.Evaluate(d, Snap(srPreset: 11), null);
        Assert.Equal(OptimizationState.Optimized, r.State);
        Assert.Single(r.Items);
    }

    [Fact]
    public void Evaluate_NeuralRendering_CountsOnlyForAutoInstallableRoutes()
    {
        var d = new OptimizationDefaults { SrPreset = 11 };
        var notInstalled = OptimizationEvaluator.Evaluate(d, Snap(srPreset: 11, nrRoute: "ShortFuse", nrSupported: true, nrInstalled: false), null);
        Assert.Equal(OptimizationState.PartiallyOptimized, notInstalled.State);
        Assert.Contains(notInstalled.Items, i => i.Name == "Neural Rendering" && !i.Satisfied);

        var installed = OptimizationEvaluator.Evaluate(d, Snap(srPreset: 11, nrRoute: "ShortFuse", nrSupported: true, nrInstalled: true), null);
        Assert.Equal(OptimizationState.Optimized, installed.State);

        var feeder = OptimizationEvaluator.Evaluate(d, Snap(srPreset: 11, nrRoute: "Feeder", nrSupported: true, nrInstalled: false), null);
        Assert.Equal(OptimizationState.Optimized, feeder.State);            // Feeder is never demanded automatically
        Assert.DoesNotContain(feeder.Items, i => i.Name == "Neural Rendering");
    }

    [Fact]
    public void Evaluate_NrAddonWithoutReShade_IsNotOptimized()
    {
        // Regression: the ShortFuse addon was present but ReShade (which loads it) was not; the game reported as done.
        var d = new OptimizationDefaults { SrPreset = 11 };
        var r = OptimizationEvaluator.Evaluate(d, Snap(srPreset: 11, nrRoute: "ShortFuse", nrSupported: true, nrInstalled: true, reshade: false), null);
        Assert.Equal(OptimizationState.PartiallyOptimized, r.State);
        Assert.Contains(r.Items, i => i.Name.StartsWith("ReShade") && !i.Satisfied);
        Assert.Contains(r.Items, i => i.Name == "Neural Rendering" && i.Satisfied);
    }

    [Fact]
    public void Evaluate_ReShadeIsNotDemanded_WhenNoAutomaticRouteApplies()
    {
        var d = new OptimizationDefaults { SrPreset = 11 };
        var r = OptimizationEvaluator.Evaluate(d, Snap(srPreset: 11, nrRoute: "Feeder", nrSupported: true, reshade: false), null);
        Assert.Equal(OptimizationState.Optimized, r.State);
        Assert.DoesNotContain(r.Items, i => i.Name.StartsWith("ReShade"));
    }

    [Fact]
    public void Evaluate_NrDll_UsesContentMatch_NotTheVersionLabel()
    {
        // The default is the build label "310.8.SF-v2"; the DLL itself reports file version "310.8.2".
        var d = new OptimizationDefaults { NrVersion = "310.8.SF-v2" };
        var byVersion = OptimizationEvaluator.Evaluate(d, Snap(nrDll: true, nrDllVer: "310.8.2"), null);
        Assert.Equal(OptimizationState.NotOptimized, byVersion.State);        // no content info -> falls back to the version string

        var identical = OptimizationEvaluator.Evaluate(d, Snap(nrDll: true, nrDllVer: "310.8.2", nrDllMatch: true), null);
        Assert.Equal(OptimizationState.Optimized, identical.State);

        var different = OptimizationEvaluator.Evaluate(d, Snap(nrDll: true, nrDllVer: "310.8.2", nrDllMatch: false), null);
        Assert.Equal(OptimizationState.NotOptimized, different.State);
    }

    [Fact]
    public void NrDllMatchesCached_ComparesContent_AndReturnsNullWhenNothingToCompare()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-nrcmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var rnd = new Random(7);
            var data = new byte[3 * 1024 * 1024]; rnd.NextBytes(data);
            var a = Path.Combine(dir, "a.dll"); var b = Path.Combine(dir, "b.dll"); var c = Path.Combine(dir, "c.dll"); var d = Path.Combine(dir, "d.dll");
            File.WriteAllBytes(a, data); File.WriteAllBytes(b, data);
            var tail = (byte[])data.Clone(); tail[^10] ^= 0xFF; File.WriteAllBytes(c, tail);            // same size, different end
            File.WriteAllBytes(d, data.AsSpan(0, data.Length - 1).ToArray());                            // different size

            Assert.True(GameOptimizationService.NrDllMatchesCached(a, b));
            Assert.False(GameOptimizationService.NrDllMatchesCached(a, c));
            Assert.False(GameOptimizationService.NrDllMatchesCached(a, d));
            Assert.Null(GameOptimizationService.NrDllMatchesCached(a, Path.Combine(dir, "missing.dll")));
            Assert.Null(GameOptimizationService.NrDllMatchesCached(null, b));
            Assert.Null(GameOptimizationService.NrDllMatchesCached(a, GameOptimizationService.NrCachePath("")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Fingerprint_IsStable_AndChangesWithAnyDefault()
    {
        Assert.Equal(Defaults().Fingerprint(), Defaults().Fingerprint());
        Assert.NotEqual(Defaults().Fingerprint(), Defaults(sr: 12).Fingerprint());
        Assert.NotEqual(Defaults().Fingerprint(), Defaults(scale: 50).Fingerprint());
        Assert.NotEqual(Defaults().Fingerprint(), Defaults(slVer: "2.15.0").Fingerprint());
        Assert.NotEqual(Defaults().Fingerprint(), Defaults(nrVer: "310.8.0").Fingerprint());
        Assert.NotEqual(Defaults().Fingerprint(), Defaults(fgOv: false).Fingerprint());
    }

    [Fact]
    public void Defaults_From_SettingsUsesConfiguredValues_NotAnyHardwareAssumption()
    {
        var s = new SettingsViewModel { DefaultSrPreset = 12, DefaultRrPreset = 4, DefaultSrRenderScale = 58, DefaultSrDriverOverride = false,
                                        DefaultStreamlineVersion = "2.13.0", DefaultDlssnrVersion = "310.8.0", DefaultNrPreset = 3 };
        var d = OptimizationDefaults.From(s, nrFeatureEnabled: true);
        Assert.Equal(12u, d.SrPreset);
        Assert.Equal(4u, d.RrPreset);
        Assert.Equal(58u, d.SrRenderScale);
        Assert.False(d.SrOverride);
        Assert.Equal("310.8.0", d.NrVersion);
        Assert.Equal(3u, d.NrPreset);

        var noNr = OptimizationDefaults.From(s, nrFeatureEnabled: false);   // NR defaults are ignored when the feature flag is off
        Assert.Equal("", noNr.NrVersion);
        Assert.Equal(0u, noNr.NrPreset);
        Assert.False(OptimizationDefaults.From(new SettingsViewModel(), true).HasAny);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Preflight
    // ═══════════════════════════════════════════════════════════════════════════════

    private static PreflightInput Ok(Action<PreflightInputBuilder>? tweak = null)
    {
        var b = new PreflightInputBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    private sealed class PreflightInputBuilder
    {
        public bool Exists = true, Valid = true, Running, Api = true, Bitness = true, NrSupported = true, Defaults = true, NvNeeded = true, NvAvail = true, Elevated = true;
        public string? Exe = @"C:\g\game.exe", Route = "ShortFuse";
        public string[] Foreign = Array.Empty<string>();
        public PreflightInput Build() => new()
        {
            InstallPathExists = Exists, GameExecutable = Exe, ExecutableIsValidPe = Valid, GameIsRunning = Running, ApiDetected = Api,
            BitnessKnown = Bitness, NrRoute = Route, NrRouteSupported = NrSupported, DefaultsConfigured = Defaults,
            NvidiaProfileAccessNeeded = NvNeeded, NvidiaProfileAccessAvailable = NvAvail, Elevated = Elevated, ForeignComponents = Foreign,
        };
    }

    [Fact]
    public void Preflight_AllGood_HasNoIssues() => Assert.Empty(OptimizationEvaluator.Preflight(Ok()));

    [Theory]
    [InlineData("missing folder")]
    [InlineData("no exe")]
    [InlineData("invalid exe")]
    [InlineData("running")]
    [InlineData("no defaults, no auto route")]
    [InlineData("route unsupported")]
    [InlineData("api unknown with auto route")]
    [InlineData("nvidia unavailable")]
    [InlineData("foreign component")]
    public void Preflight_BlockingProblems_AreBlocking(string scenario)
    {
        var issues = OptimizationEvaluator.Preflight(Ok(b =>
        {
            switch (scenario)
            {
                case "missing folder": b.Exists = false; break;
                case "no exe": b.Exe = null; break;
                case "invalid exe": b.Valid = false; break;
                case "running": b.Running = true; break;
                case "no defaults, no auto route": b.Defaults = false; b.Route = "Feeder"; break;
                case "route unsupported": b.NrSupported = false; break;
                case "api unknown with auto route": b.Api = false; break;
                case "nvidia unavailable": b.NvAvail = false; break;
                case "foreign component": b.Foreign = new[] { "OptiScaler" }; break;
            }
        }));
        Assert.True(OptimizationEvaluator.HasBlocking(issues), scenario);
    }

    [Fact]
    public void Preflight_MissingFolder_StopsAtOnce()
        => Assert.Single(OptimizationEvaluator.Preflight(Ok(b => { b.Exists = false; b.Running = true; b.Api = false; })));

    [Fact]
    public void Preflight_UnknownApi_IsOnlyAWarning_WhenNoAutoNrRouteIsInvolved()
    {
        var issues = OptimizationEvaluator.Preflight(Ok(b => { b.Api = false; b.Route = "Feeder"; }));
        Assert.False(OptimizationEvaluator.HasBlocking(issues));
        Assert.Contains(issues, i => i.Severity == PreflightSeverity.Warning);
    }

    [Fact]
    public void Preflight_NotElevated_IsANonBlockingWarning()
    {
        var issues = OptimizationEvaluator.Preflight(Ok(b => b.Elevated = false));
        Assert.False(OptimizationEvaluator.HasBlocking(issues));
        Assert.Single(issues);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Undo planning
    // ═══════════════════════════════════════════════════════════════════════════════

    private static DllStateRecord Rec(string comp, string? ver, bool backup) => new() { Component = comp, Path = @"C:\g\" + comp + ".dll", VersionBefore = ver, HadBackupBefore = backup };

    [Fact]
    public void PlanDllUndo_Unchanged_DoesNothing()
    {
        var steps = OptimizationEvaluator.PlanDllUndo(new[] { Rec("Dlss", "3.8.10", false) },
            new Dictionary<string, (string?, bool)> { ["Dlss"] = ("3.8.10", false) });
        Assert.Equal(DllUndoAction.None, Assert.Single(steps).Action);
    }

    [Fact]
    public void PlanDllUndo_NoBackupBefore_RestoresRhisOwnOriginalBackup()
    {
        var steps = OptimizationEvaluator.PlanDllUndo(new[] { Rec("Dlss", "3.8.10", false) },
            new Dictionary<string, (string?, bool)> { ["Dlss"] = ("310.9.1", true) });
        Assert.Equal(DllUndoAction.RestoreOriginal, Assert.Single(steps).Action);
    }

    [Fact]
    public void PlanDllUndo_UserHadAlreadySwapped_SwapsBackToTheExactPreviousVersion()
    {
        var steps = OptimizationEvaluator.PlanDllUndo(new[] { Rec("Dlssg", "310.7.0", true) },
            new Dictionary<string, (string?, bool)> { ["Dlssg"] = ("310.9.1", true) });
        var s = Assert.Single(steps);
        Assert.Equal(DllUndoAction.SwapToPreviousVersion, s.Action);
        Assert.Equal("310.7.0", s.Version);
    }

    [Fact]
    public void PlanDllUndo_UnknownPreviousVersionAndNoBackup_ReportsTheLimitInsteadOfPretending()
    {
        var steps = OptimizationEvaluator.PlanDllUndo(new[] { Rec("Streamline", null, true) },
            new Dictionary<string, (string?, bool)> { ["Streamline"] = ("2.14.1", true) });
        Assert.Equal(DllUndoAction.CannotRestore, Assert.Single(steps).Action);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Restore-point persistence
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void StateStore_RoundTripsARestorePoint_AndOnlyTheLatestIsKept()
    {
        var path = Path.Combine(_tmp, "optimization_state.json");
        var store = new OptimizationStateStore(path);
        var key = OptimizationEvaluator.GameKey("Some Game", "Steam");

        var first = new OptimizationRestorePoint { GameKey = key, SrPreset = 10, SrOverride = true, NrStoredMethodBefore = "Feeder", NrMethodInstalledByOptimize = "ShortFuse" };
        first.Dlls.Add(Rec("Dlss", "3.8.10", true));
        store.Set(key, new OptimizationRecord { RestorePoint = first, LastAppliedFingerprint = "abc" });

        var loaded = new OptimizationStateStore(path).Get(key)!;        // fresh instance: really read from disk
        Assert.Equal(10u, loaded.RestorePoint!.SrPreset);
        Assert.True(loaded.RestorePoint.SrOverride);
        Assert.Equal("ShortFuse", loaded.RestorePoint.NrMethodInstalledByOptimize);
        Assert.Equal("3.8.10", loaded.RestorePoint.Dlls.Single().VersionBefore);
        Assert.Equal("abc", loaded.LastAppliedFingerprint);

        store.Set(key, new OptimizationRecord { RestorePoint = new OptimizationRestorePoint { GameKey = key, SrPreset = 99 } });
        Assert.Equal(99u, store.Get(key)!.RestorePoint!.SrPreset);
        Assert.Empty(store.Get(key)!.RestorePoint!.Dlls);
    }

    [Fact]
    public void StateStore_KeysAreCaseInsensitive_AndRemoveWorks()
    {
        var store = new OptimizationStateStore(Path.Combine(_tmp, "s.json"));
        store.Set("Game|Steam", new OptimizationRecord { LastAppliedFingerprint = "x" });
        Assert.NotNull(store.Get("GAME|steam"));
        store.Remove("game|STEAM");
        Assert.Null(store.Get("Game|Steam"));
    }

    [Fact]
    public void StateStore_CorruptFile_IsTreatedAsEmpty_AndDoesNotThrow()
    {
        var path = Path.Combine(_tmp, "bad.json");
        File.WriteAllText(path, "{ not json");
        var store = new OptimizationStateStore(path);
        Assert.Null(store.Get("x|y"));
        store.Set("x|y", new OptimizationRecord { LastAppliedFingerprint = "1" });   // and it can be written again
        Assert.Equal("1", store.Get("x|y")!.LastAppliedFingerprint);
    }

    [Fact]
    public void StateStore_DoesNotTouchAnyOtherRhiFile()
        => Assert.EndsWith("optimization_state.json", OptimizationStateStore.DefaultPath);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Restore point capture + profile undo (in-memory NVIDIA profile)
    // ═══════════════════════════════════════════════════════════════════════════════

    private static GameCardViewModel Card() => new()
    {
        GameName = "Some Game", Source = "Steam", InstallPath = @"C:\Games\Some Game",
        DlssDetection = new DlssDetectionResult { DlssPath = @"C:\Games\Some Game\nvngx_dlss.dll" },
    };

    [Fact]
    public void RestorePoint_CapturesTheNvidiaProfile_AndUndoPutsExactlyThatBack()
    {
        var profile = new FakeProfile { Sr = 10, Rr = 0, Fg = 1, SrScale = 88, SrOv = false, RrOv = true };
        var svc = new GameOptimizationService(profile, null!);
        var card = Card();

        var rp = svc.CaptureRestorePoint(card, Defaults(), nrStoredMethodBefore: "Feeder", nrInstalledBefore: true);
        Assert.True(rp.ProfileCaptured);
        Assert.Equal("Some Game|Steam", rp.GameKey);
        Assert.Equal("Feeder", rp.NrStoredMethodBefore);
        Assert.True(rp.NrWasInstalledBefore);

        // simulate an optimize changing everything
        profile.Sr = 11; profile.Rr = 5; profile.Fg = 2; profile.SrScale = 67; profile.SrOv = true; profile.RrOv = false; profile.FgOv = true;

        var log = svc.UndoProfile(card, rp);
        Assert.Equal((10u, 0u, 1u, 88u), (profile.Sr, profile.Rr, profile.Fg, profile.SrScale));
        Assert.False(profile.SrOv);
        Assert.True(profile.RrOv);
        Assert.False(profile.FgOv);
        Assert.NotEmpty(log);
    }

    [Fact]
    public void RestorePoint_RecordsEachDllWithItsVersionAndBackupState()
    {
        var card = Card();
        card.DlssInstalledVersion = "3.8.10";
        var rp = new GameOptimizationService(new FakeProfile(), null!).CaptureRestorePoint(card, Defaults(), null, false);
        var dll = Assert.Single(rp.Dlls);
        Assert.Equal("Dlss", dll.Component);
        Assert.Equal("3.8.10", dll.VersionBefore);
        Assert.False(dll.HadBackupBefore);
    }

    [Fact]
    public void Undo_WhenNvidiaProfileUnavailable_SaysSo_InsteadOfClaimingSuccess()
    {
        var profile = new FakeProfile { IsSupported = false };
        var svc = new GameOptimizationService(profile, null!);
        var rp = svc.CaptureRestorePoint(Card(), Defaults(), null, false);
        Assert.False(rp.ProfileCaptured);
        Assert.Contains("nothing was captured", svc.UndoProfile(Card(), rp).Single());
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Shared defaults applier (the logic behind Quick Apply and One-Click)
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Applier_AppliesOverridesPresetsAndScale_FromTheConfiguredDefaults()
    {
        var profile = new FakeProfile();
        var s = new SettingsViewModel { DefaultSrDriverOverride = true, DefaultSrPreset = 11, DefaultSrRenderScale = 67 };

        await new DlssDefaultsApplier(null!, profile).ApplyAsync(Card(), s);   // driver override => no DLL swap, so no DLSS service needed

        Assert.True(profile.SrOv);
        Assert.Equal(11u, profile.Sr);
        Assert.Equal(67u, profile.SrScale);
        Assert.Equal(0u, profile.Rr);                                           // RR not present in this game: untouched
    }

    [Fact]
    public async Task Applier_LeavesUnconfiguredValuesAlone()
    {
        var profile = new FakeProfile { Sr = 12, SrScale = 50 };
        await new DlssDefaultsApplier(null!, profile).ApplyAsync(Card(), new SettingsViewModel());
        Assert.Equal(12u, profile.Sr);
        Assert.Equal(50u, profile.SrScale);
    }

    [Fact]
    public async Task Applier_IsIdempotent()
    {
        var profile = new FakeProfile();
        var s = new SettingsViewModel { DefaultSrDriverOverride = true, DefaultSrPreset = 11, DefaultSrRenderScale = 67 };
        var applier = new DlssDefaultsApplier(null!, profile);
        await applier.ApplyAsync(Card(), s);
        await applier.ApplyAsync(Card(), s);
        Assert.Equal((true, 11u, 67u), (profile.SrOv, profile.Sr, profile.SrScale));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Drift alarms: the NR source this feature mirrors must not change silently
    // ═══════════════════════════════════════════════════════════════════════════════

    private static string? RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RenoDXCommander.sln"))) dir = dir.Parent;
        var path = dir == null ? null : Path.Combine(dir.FullName, relative);
        return path != null && File.Exists(path) ? path : null;
    }

    private static string Squash(string s) => Regex.Replace(s, @"\s+", " ");

    [Fact]
    public void Drift_NeuralRenderingMethodNames_AndRouteRulesStillMatchThisFeature()
    {
        var file = RepoFile(@"RenoDXCommander\DetailPanelBuilder.NeuralRendering.cs");
        if (file == null) return;   // source tree not available (e.g. tests run from a packaged output)
        var src = File.ReadAllText(file);

        foreach (var (name, value) in new[] { ("NrMethodShortFuse", OptimizationEvaluator.NrShortFuse), ("NrMethodDlss5Tool", OptimizationEvaluator.NrDlss5Tool),
                                              ("NrMethodDlss5ToolBridge", OptimizationEvaluator.NrDlss5ToolBridge), ("NrMethodFeeder", OptimizationEvaluator.NrFeeder) })
            Assert.Matches(new Regex($@"const string {name}\s*=\s*""{Regex.Escape(value)}""\s*;"), src);

        // Auto-selection rule
        Assert.Contains(Squash("is32Bit ? NrMethodFeeder : card.GraphicsApi == GraphicsApiType.OpenGL ? NrMethodFeeder : !hasDlss ? NrMethodFeeder : (isDx11 || isVulkan) ? NrMethodDlss5ToolBridge : NrMethodShortFuse"), Squash(src));

        // Method-enablement rules
        var s = Squash(src);
        Assert.Contains("Key = NrMethodShortFuse, Enabled = !is32Bit && card.GraphicsApi != GraphicsApiType.OpenGL", s);
        Assert.Contains("Key = NrMethodDlss5Tool, Enabled = hasDlss && !is32Bit", s);
        Assert.Contains("Key = NrMethodDlss5ToolBridge, Enabled = hasDlss && (isDx11 || isVulkan) && !is32Bit", s);
        Assert.Contains("Key = NrMethodFeeder, Enabled = true", s);
    }

    [Fact]
    public void Drift_NeuralRenderingInstallAndRemoveHelpers_StillExistWithTheSignaturesOneClickCalls()
    {
        var file = RepoFile(@"RenoDXCommander\DetailPanelBuilder.NeuralRendering.cs");
        if (file == null) return;
        var s = Squash(File.ReadAllText(file));
        Assert.Contains("private async Task InstallShortFuseAsync( GameCardViewModel card, Button statusBtn, ComboBox addonVersionCombo, Renodx5AddonService rdx5Svc, IDlssStreamlineService dlssSvc)", s);
        Assert.Contains("private async Task InstallDlss5ToolAsync( GameCardViewModel card, Button statusBtn, ComboBox addonVersionCombo, ComboBox nrVersionCombo, Renodx5AddonService rdx5Svc, IDlssStreamlineService dlssSvc, IAddonPackService addonSvc)", s);
        Assert.Contains("private async Task InstallBridgeAddonAsync( GameCardViewModel card, Button statusBtn, IAddonPackService addonSvc, ComboBox? packVersionCombo = null)", s);
        // the Install-button epilogue One-Click mirrors
        Assert.Contains("_window.ViewModel.SetNrMethodOverride(gameName, selKey, store);", s);
        Assert.Contains("Models.RhiInstallManifest.SetNrMethod(installPath, selKey);", s);
        Assert.Contains("_window.ViewModel.DeployAddonsForCard(gameName);", s);
        // the Remove-button steps One-Click undo mirrors
        Assert.Contains("rdx5Svc.UninstallSf(installPath, det.HasAny ? det : null);", s);
        Assert.Contains("_window.ViewModel.RevertSfAutoConfig(card);", s);
        Assert.Contains("RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);", s);
        Assert.Contains("Models.RhiInstallManifest.SetNrMethod(installPath, null);", s);
    }

    [Fact]
    public void Drift_QuickApplyButtonStillUsesTheSharedApplier()
    {
        var file = RepoFile(@"RenoDXCommander\DetailPanelBuilder.Overrides.NvidiaProfile.cs");
        if (file == null) return;
        Assert.Contains("new DlssDefaultsApplier(", File.ReadAllText(file));
    }
}
