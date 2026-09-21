using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Pure rules behind "One-Click Optimize": which Neural Rendering route applies, what "optimized" means for a game,
/// whether it is safe to start, and how to plan an undo. No UI, no I/O, no hardware assumptions — everything is
/// derived from the user's configured RHI defaults and what is installed for the game.
/// </summary>
public static class OptimizationEvaluator
{
    // ── Neural Rendering route ────────────────────────────────────────────────────

    public const string NrShortFuse = "ShortFuse";
    public const string NrDlss5Tool = "DLSS5Tool";
    public const string NrDlss5ToolBridge = "DLSS5ToolBridge";
    public const string NrFeeder = "Feeder";

    /// <summary>
    /// The method RHI auto-selects when the user has not chosen one. This mirrors the "Auto-select best method" rule in
    /// DetailPanelBuilder.NeuralRendering.cs; a unit test compares the two so a change upstream is noticed immediately.
    /// </summary>
    public static string RecommendNrRoute(bool hasNativeDlss, bool is32Bit, bool isOpenGl, bool isDx11, bool isVulkan)
        => is32Bit ? NrFeeder
         : isOpenGl ? NrFeeder
         : !hasNativeDlss ? NrFeeder
         : (isDx11 || isVulkan) ? NrDlss5ToolBridge
         : NrShortFuse;

    /// <summary>Whether a method is applicable to the game (same enablement rules as the Neural Rendering method picker).</summary>
    public static bool IsNrRouteSupported(string method, bool hasNativeDlss, bool is32Bit, bool isOpenGl, bool isDx11, bool isVulkan)
        => method switch
        {
            NrShortFuse => !is32Bit && !isOpenGl,
            NrDlss5Tool => hasNativeDlss && !is32Bit,
            NrDlss5ToolBridge => hasNativeDlss && (isDx11 || isVulkan) && !is32Bit,
            NrFeeder => true,
            _ => false,
        };

    /// <summary>
    /// One-Click installs the routes that need no manual set-up. The Feeder route (games without native DLSS) needs
    /// ReShade depth / motion-vector shaders configured by the user, so it is reported but never installed automatically.
    /// </summary>
    public static bool IsAutoInstallable(string? method) => method is NrShortFuse or NrDlss5Tool or NrDlss5ToolBridge;

    // ── Status ────────────────────────────────────────────────────────────────────

    private static bool IsLegacy(string? version) => version != null && version.StartsWith("1.", StringComparison.Ordinal);

    private static bool SameVersion(string? installed, string wanted)
        => installed != null && string.Equals(installed.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <param name="lastAppliedFingerprint">Fingerprint of the defaults used the last time this game was optimized (null if never).</param>
    public static OptimizationReport Evaluate(OptimizationDefaults d, GameOptimizationSnapshot s, string? lastAppliedFingerprint)
    {
        var items = new List<OptimizationItem>();

        // Same applicability rules as Quick Apply: DLSS 1.x DLLs are never touched.
        if (s.HasDlss && !IsLegacy(s.DlssVersion))
        {
            if (d.SrOverride) items.Add(new("SR version", s.SrOverride, "NVIDIA Override"));
            else if (d.SrVersion.Length > 0) items.Add(new("SR version", s.SrOverride || SameVersion(s.DlssVersion, d.SrVersion), d.SrVersion));
            if (d.SrPreset != 0) items.Add(new("SR preset", s.SrPreset == d.SrPreset, $"0x{d.SrPreset:X}"));
            if (d.SrRenderScale != 0) items.Add(new("SR render scale", s.SrRenderScale == d.SrRenderScale, $"{d.SrRenderScale}%"));
        }
        if (s.HasDlssd && !IsLegacy(s.DlssdVersion))
        {
            if (d.RrOverride) items.Add(new("RR version", s.RrOverride, "NVIDIA Override"));
            else if (d.RrVersion.Length > 0) items.Add(new("RR version", s.RrOverride || SameVersion(s.DlssdVersion, d.RrVersion), d.RrVersion));
            if (d.RrPreset != 0) items.Add(new("RR preset", s.RrPreset == d.RrPreset, $"0x{d.RrPreset:X}"));
            if (d.RrRenderScale != 0) items.Add(new("RR render scale", s.RrRenderScale == d.RrRenderScale, $"{d.RrRenderScale}%"));
        }
        if (s.HasDlssg)
        {
            if (d.FgOverride) items.Add(new("FG version", s.FgOverride, "NVIDIA Override"));
            else if (d.FgVersion.Length > 0) items.Add(new("FG version", s.FgOverride || SameVersion(s.DlssgVersion, d.FgVersion), d.FgVersion));
            if (d.FgPreset != 0) items.Add(new("FG preset", s.FgPreset == d.FgPreset, $"0x{d.FgPreset:X}"));
        }
        if (s.HasStreamline && d.StreamlineVersion.Length > 0)
            items.Add(new("Streamline version", SameVersion(s.StreamlineVersion, d.StreamlineVersion), d.StreamlineVersion));
        if (s.HasDlssnr)
        {
            if (d.NrVersion.Length > 0) items.Add(new("Neural Rendering DLL", s.NrDllMatchesDefault ?? SameVersion(s.DlssnrVersion, d.NrVersion), d.NrVersion));
            if (d.NrPreset != 0) items.Add(new("Neural Rendering preset", s.NrPreset == d.NrPreset, $"0x{d.NrPreset:X}"));
        }
        if (s.NrRouteSupported && IsAutoInstallable(s.NrRoute))
        {
            items.Add(new("Neural Rendering", s.NrInstalled, s.NrRoute!));
            // The addon does nothing without ReShade to load it, so an install without ReShade is not "done".
            items.Add(new("ReShade (required by Neural Rendering)", s.ReShadeInstalled, s.ReShadeInstalled ? "installed" : "missing"));
        }

        int ok = items.Count(i => i.Satisfied);
        var state = items.Count == 0 ? OptimizationState.NotOptimized
                  : ok == items.Count ? OptimizationState.Optimized
                  : ok == 0 ? OptimizationState.NotOptimized
                  : OptimizationState.PartiallyOptimized;

        bool defaultsChanged = lastAppliedFingerprint != null
                               && !string.Equals(lastAppliedFingerprint, d.Fingerprint(), StringComparison.Ordinal)
                               && state != OptimizationState.Optimized;

        string summary = items.Count == 0 ? "Nothing to optimize for this game with your current defaults"
            : state == OptimizationState.Optimized ? "Optimized — matches your defaults"
            : defaultsChanged ? $"Defaults changed — {ok}/{items.Count} up to date"
            : state == OptimizationState.PartiallyOptimized ? $"Partially optimized — {ok}/{items.Count} match your defaults"
            : "Not optimized";

        return new OptimizationReport { State = state, Items = items, DefaultsChanged = defaultsChanged, Summary = summary };
    }

    // ── Preflight ─────────────────────────────────────────────────────────────────

    public static IReadOnlyList<PreflightIssue> Preflight(PreflightInput p)
    {
        var issues = new List<PreflightIssue>();
        void Block(string m) => issues.Add(new(PreflightSeverity.Blocking, m));
        void Warn(string m) => issues.Add(new(PreflightSeverity.Warning, m));

        if (!p.InstallPathExists) { Block("The game folder no longer exists (drive unplugged or game moved)."); return issues; }
        if (string.IsNullOrEmpty(p.GameExecutable)) Block("No game executable was found in the game folder.");
        else if (!p.ExecutableIsValidPe) Block("The game executable is not a valid Windows program.");
        if (p.GameIsRunning) Block("The game is running — close it first.");

        bool autoNr = p.NrRouteSupported && IsAutoInstallable(p.NrRoute);
        if (!p.DefaultsConfigured && !autoNr) Block("No defaults are configured yet — set them in “DLSS & Streamline Defaults” first.");
        if (!string.IsNullOrEmpty(p.NrRoute) && !p.NrRouteSupported) Block($"The Neural Rendering method “{p.NrRoute}” is not applicable to this game.");
        if (!p.ApiDetected || !p.BitnessKnown)
        {
            var msg = "Graphics API / bitness could not be detected — press Refresh and try again.";
            if (autoNr) Block(msg); else Warn(msg);
        }
        if (p.NvidiaProfileAccessNeeded && !p.NvidiaProfileAccessAvailable)
            Block("NVIDIA driver profile access is unavailable (no supported NVIDIA driver/GPU detected).");
        else if (p.NvidiaProfileAccessNeeded && !p.Elevated)
            Warn("RHI is not running as administrator. Per-game DLSS profile settings work without it, but Admin Mode is recommended.");
        foreach (var f in p.ForeignComponents)
            Block($"{f} is installed for this game and may conflict — review it in the game's panel before optimizing.");
        return issues;
    }

    public static bool HasBlocking(IEnumerable<PreflightIssue> issues) => issues.Any(i => i.Severity == PreflightSeverity.Blocking);

    // ── Undo planning for DLL components ──────────────────────────────────────────

    /// <summary>
    /// Decides how to put each DLL back. RHI already keeps ".original" backups, so when none existed before the optimize the
    /// backup it created IS the pre-optimize state and is simply restored. When the user had already swapped a DLL (a backup
    /// existed), the exact earlier version is swapped back in. If neither is possible the limitation is reported, not hidden.
    /// </summary>
    public static IReadOnlyList<DllUndoStep> PlanDllUndo(
        IEnumerable<DllStateRecord> before, IReadOnlyDictionary<string, (string? version, bool hasBackup)> now)
    {
        var steps = new List<DllUndoStep>();
        foreach (var r in before)
        {
            now.TryGetValue(r.Component, out var cur);
            bool changed = !string.Equals(cur.version ?? "", r.VersionBefore ?? "", StringComparison.OrdinalIgnoreCase);
            if (!changed) steps.Add(new(r.Component, r.Path, DllUndoAction.None, null, "unchanged"));
            else if (!r.HadBackupBefore && cur.hasBackup) steps.Add(new(r.Component, r.Path, DllUndoAction.RestoreOriginal, r.VersionBefore, "restored from RHI's original backup"));
            else if (!string.IsNullOrEmpty(r.VersionBefore)) steps.Add(new(r.Component, r.Path, DllUndoAction.SwapToPreviousVersion, r.VersionBefore, $"swapped back to {r.VersionBefore}"));
            else steps.Add(new(r.Component, r.Path, DllUndoAction.CannotRestore, null, "previous version unknown and no RHI backup available"));
        }
        return steps;
    }

    public static string GameKey(string gameName, string? store) => $"{gameName}|{store ?? ""}";
}
