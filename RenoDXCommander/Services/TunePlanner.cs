// TunePlanner.cs -- what tuning a game with a preset will do (pure, unit tested in TunePresetTests). Built from plain
// facts about the game (read on the UI thread, plus the NVIDIA values read in the background), it lists every step
// with what changes, or why it's skipped. MainWindow.Tune.cs shows the plan, then runs the steps that aren't skipped.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>DLL slots Tune can swap. Never rename: stored in settings.json (TunedGameState.Dlls).</summary>
public static class TuneDllSlots
{
    public const string Sr = "SR";
    public const string Rr = "RR";
    public const string Fg = "FG";
    public const string Sl = "SL";
    public static readonly IReadOnlyList<string> All = new[] { Sr, Rr, Fg, Sl };

    public static string Label(string slot) => slot switch
    {
        Sr => "DLSS Super Resolution",
        Rr => "DLSS Ray Reconstruction",
        Fg => "DLSS Frame Generation",
        Sl => "Streamline",
        _ => slot,
    };
}

/// <summary>Component ids Tune uses besides BackupComponents'. Never rename: stored in settings.json.</summary>
public static class TuneComponentIds
{
    public const string NeuralRendering = "NeuralRendering";
    /// <summary>Installed-list prefix for Extras ("Extra:HSF").</summary>
    public const string ExtraPrefix = "Extra:";

    public static bool IsExtra(string id) => id.StartsWith(ExtraPrefix, StringComparison.Ordinal);
    public static string ExtraOf(string id) => id[ExtraPrefix.Length..];

    public static string Label(string id) => id == NeuralRendering ? "Neural Rendering"
        : IsExtra(id) ? DetailPanelBuilder.TuneExtraLabel(ExtraOf(id))
        : BackupComponents.Label(id);

    /// <summary>The order Extras install in: API wrappers first (before OptiScaler moves DXVK), frame generation after OptiScaler.</summary>
    public static readonly IReadOnlyList<string> ExtrasBeforeOptiScaler = new[] { "DXVK", "dgVoodoo2" };

    /// <summary>Every extra a preset can install, in install order (ASI Loader before the mods that can load through it).</summary>
    public static readonly IReadOnlyList<string> ExtraOrder = new[]
        { "DXVK", "dgVoodoo2", "AsiLoader", "HSF", "RtxEncore", "DlssEnabler", "UniversalMfg", "MfgUnlock", "Dlssg2030", "BfiPresenter" };
}

/// <summary>One DLSS / Streamline file as the game has it now.</summary>
public sealed record TuneDllFacts(bool Present, string? Version, bool DriverOverride, IReadOnlyList<string> Versions);

/// <summary>Everything the planner needs to know about a game.</summary>
public sealed class TuneGameFacts
{
    public string GameName { get; init; } = "";
    /// <summary>Null when Tune can work in the game's folder, else why not.</summary>
    public string? Blocker { get; init; }

    public bool RsInstalled { get; init; }
    /// <summary>Null when ReShade can be installed, else why not (e.g. RE Framework needed first).</summary>
    public string? RsBlocked { get; init; }
    /// <summary>The game is on the No Addons ReShade channel (RenoDX, ReLimiter and Display Commander need add-ons).</summary>
    public bool NoAddons { get; init; }
    public bool IsVulkan { get; init; }

    public bool HasRenoDxMod { get; init; }
    public bool RdxInstalled { get; init; }
    public bool HasLumaMod { get; init; }
    public bool LumaInstalled { get; init; }
    public bool UlInstalled { get; init; }
    public bool DcInstalled { get; init; }

    public bool OsInstalled { get; init; }
    /// <summary>File names ReShade / Display Commander use in the game (OptiScaler can't take them).</summary>
    public string? RsFileName { get; init; }
    public string? DcFileName { get; init; }

    /// <summary>Extras: id → why it can't be installed now (missing = it can).</summary>
    public Dictionary<string, string> ExtrasBlocked { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extras already in the game.</summary>
    public HashSet<string> ExtrasInstalled { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extras: id → (resolved option, error when the wanted option can't be used).</summary>
    public Dictionary<string, (string? Option, string? Error)> ExtrasOption { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Neural Rendering method installed in the game, or null.</summary>
    public string? NrInstalled { get; init; }
    /// <summary>The method the Neural Rendering section picks for the game ("Auto").</summary>
    public string NrAuto { get; init; } = "";
    /// <summary>Method key → why it can't be used in the game (missing = it can).</summary>
    public Dictionary<string, string> NrBlocked { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, TuneDllFacts> Dlls { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool NvidiaReady { get; init; }
    public bool IsAdmin { get; init; }
    /// <summary>The game's current NVIDIA values (TuneItems key → value); missing = couldn't be read.</summary>
    public Dictionary<string, string> NvidiaNow { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum TuneStepKind { Install, Dll, Nvidia }

public sealed class TuneStep
{
    public TuneStepKind Kind { get; init; }
    /// <summary>BackupComponents id, TuneDllSlots slot or TuneItems key.</summary>
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    /// <summary>What changes ("310.1.0 → 310.5.3", "Default → M - TF2"); empty for installs.</summary>
    public string Detail { get; init; } = "";
    /// <summary>Version / value to apply.</summary>
    public string Target { get; init; } = "";
    /// <summary>Null = will run; else why it's skipped.</summary>
    public string? Skip { get; init; }
    public bool Runs => Skip == null;
}

public sealed class TunePlan
{
    public string? Blocker { get; init; }
    public List<TuneStep> Steps { get; } = new();
    public IEnumerable<TuneStep> Running => Steps.Where(s => s.Runs);
    public IEnumerable<TuneStep> Skipped => Steps.Where(s => !s.Runs);
}

public static class TunePlanner
{
    /// <summary>NVIDIA items that write settings needing admin rights (the driver page greys them out without it).</summary>
    public static readonly IReadOnlyList<string> AdminItems = new[]
        { TuneItemKeys.ReBar, TuneItemKeys.ReBarMode, TuneItemKeys.ReBarSize, TuneItemKeys.SmoothMotionApis, TuneItemKeys.FlipPacing };

    public static TunePlan Build(TunePreset preset, TuneGameFacts g)
    {
        var plan = new TunePlan { Blocker = g.Blocker };
        if (g.Blocker != null) return plan;

        // ── ReShade ──
        bool rsWillBe = g.RsInstalled;
        if (preset.ReShade)
        {
            string? skip = g.RsInstalled ? "Already installed" : g.RsBlocked;
            Add(plan, TuneStepKind.Install, BackupComponents.ReShade, "ReShade", skip);
            if (skip == null) rsWillBe = true;
        }

        // Add-on based components need ReShade with add-on support
        string? AddonNeeds() => g.NoAddons ? "Needs ReShade with add-ons (this game uses the No Addons channel)"
            : !rsWillBe ? "Needs ReShade" : null;

        // ── HDR mod ──
        string hdr = preset.HdrMod ?? "";
        if (hdr == TuneHdrMod.RenoDxElseLuma)
            hdr = g.HasRenoDxMod || g.RdxInstalled ? TuneHdrMod.RenoDx : TuneHdrMod.Luma;
        if (hdr == TuneHdrMod.RenoDx)
        {
            string? skip = g.RdxInstalled ? "Already installed"
                : !g.HasRenoDxMod ? "No RenoDX mod for this game"
                : g.LumaInstalled ? "Luma is installed"
                : AddonNeeds();
            Add(plan, TuneStepKind.Install, BackupComponents.RenoDx, "RenoDX", skip);
        }
        else if (hdr == TuneHdrMod.Luma)
        {
            string? skip = g.LumaInstalled ? "Already installed"
                : !g.HasLumaMod ? "No Luma mod for this game"
                : g.RdxInstalled ? "RenoDX is installed"
                : null; // Luma installs ReShade itself
            Add(plan, TuneStepKind.Install, BackupComponents.Luma, "Luma", skip);
        }

        // ── Frame limiter ──
        if (preset.FrameLimiter == TuneLimiter.ReLimiter)
        {
            string? skip = g.UlInstalled ? "Already installed"
                : g.DcInstalled ? "Display Commander is installed"
                : AddonNeeds();
            Add(plan, TuneStepKind.Install, BackupComponents.ReLimiter, "ReLimiter", skip);
        }
        else if (preset.FrameLimiter == TuneLimiter.DisplayCommander)
        {
            string? skip = g.DcInstalled ? "Already installed"
                : g.UlInstalled ? "ReLimiter is installed"
                : AddonNeeds();
            Add(plan, TuneStepKind.Install, BackupComponents.DisplayCommander, "Display Commander", skip);
        }

        // ── Extras: API wrappers (before OptiScaler, which moves DXVK aside) ──
        foreach (var id in TuneComponentIds.ExtrasBeforeOptiScaler) AddExtra(plan, preset, g, id);

        // ── OptiScaler (Extras) ──
        if (preset.OptiScaler)
        {
            string name = preset.OsDllName ?? "";
            string? skip = g.OsInstalled ? "Already installed"
                : name.Length > 0 && name.Equals(g.RsFileName, StringComparison.OrdinalIgnoreCase) ? $"{name} is ReShade's file name in this game"
                : name.Length > 0 && name.Equals(g.DcFileName, StringComparison.OrdinalIgnoreCase) ? $"{name} is Display Commander's file name in this game"
                : null;
            plan.Steps.Add(new TuneStep
            {
                Kind = TuneStepKind.Install, Id = BackupComponents.OptiScaler, Label = "OptiScaler",
                Detail = name.Length > 0 ? $"as {name}" : "", Target = name, Skip = skip,
            });
        }

        // ── Other Extras ──
        foreach (var id in TuneComponentIds.ExtraOrder)
            if (!TuneComponentIds.ExtrasBeforeOptiScaler.Contains(id, StringComparer.OrdinalIgnoreCase)) AddExtra(plan, preset, g, id);

        // ── Neural Rendering ──
        if (!string.IsNullOrEmpty(preset.NrMethod))
        {
            bool auto = preset.NrMethod.Equals(TuneNr.Auto, StringComparison.OrdinalIgnoreCase);
            string method = auto ? g.NrAuto : preset.NrMethod;
            string methodLabel = DetailPanelBuilder.NrMethodLabel(method);
            string? skip = g.NrInstalled != null
                    ? (g.NrInstalled == method ? "Already installed" : $"{DetailPanelBuilder.NrMethodLabel(g.NrInstalled)} is installed")
                : string.IsNullOrEmpty(method) ? "No method for this game"
                : g.NrBlocked.TryGetValue(method, out var why) ? $"{methodLabel}: {why}"
                : g.NoAddons ? "Needs ReShade with add-ons (this game uses the No Addons channel)"
                : null;
            var parts = new List<string> { methodLabel };
            if (!auto && !string.IsNullOrEmpty(preset.NrAddonVersion)) parts.Add($"version {preset.NrAddonVersion}");
            parts.Add($"NR DLL {(string.IsNullOrEmpty(preset.NrDllVersion) ? "latest" : preset.NrDllVersion)}");
            plan.Steps.Add(new TuneStep
            {
                Kind = TuneStepKind.Install, Id = TuneComponentIds.NeuralRendering, Label = "Neural Rendering",
                Detail = string.Join(", ", parts), Target = method, Skip = skip,
            });
        }

        // ── DLSS / Streamline DLLs ──
        foreach (var slot in TuneDllSlots.All)
        {
            var want = slot switch
            {
                TuneDllSlots.Sr => preset.DlssSr,
                TuneDllSlots.Rr => preset.DlssRr,
                TuneDllSlots.Fg => preset.DlssFg,
                _ => preset.Streamline,
            };
            if (string.IsNullOrEmpty(want)) continue;
            plan.Steps.Add(DllStep(slot, want, g.Dlls.TryGetValue(slot, out var f) ? f : null));
        }

        // ── NVIDIA settings ──
        foreach (var item in TuneItems.All)
        {
            if (preset.Nvidia == null || !preset.Nvidia.TryGetValue(item.Key, out var value)) continue;
            string target = item.Format(value);
            string? skip = null;
            string detail = "";
            if (!g.NvidiaReady) skip = "No NVIDIA graphics card / driver found";
            else if (DlssSlotOf(item.Key) is { } slot && !HasUsableDll(g, slot))
                skip = $"The game has no {TuneDllSlots.Label(slot)}";
            else if (!g.IsAdmin && AdminItems.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
                skip = "Needs Admin Mode";
            else if (g.NvidiaNow.TryGetValue(item.Key, out var now))
            {
                if (now.Equals(value, StringComparison.OrdinalIgnoreCase)) skip = $"Already {target}";
                else detail = $"{item.Format(now)} → {target}";
            }
            else detail = target;
            plan.Steps.Add(new TuneStep
            {
                Kind = TuneStepKind.Nvidia, Id = item.Key, Label = NvidiaLabel(item), Detail = detail, Target = value, Skip = skip,
            });
        }
        return plan;
    }

    public static string NvidiaLabel(DisplayModeItem item) => item.Group == TuneItems.GroupDriver ? item.Label : "DLSS " + item.Label;

    /// <summary>The DLL a DLSS item depends on, or null for driver settings.</summary>
    public static string? DlssSlotOf(string key) => key switch
    {
        TuneItemKeys.SrPreset or TuneItemKeys.SrScale => TuneDllSlots.Sr,
        TuneItemKeys.RrPreset or TuneItemKeys.RrScale => TuneDllSlots.Rr,
        TuneItemKeys.FgPreset => TuneDllSlots.Fg,
        _ => null,
    };

    private static bool HasUsableDll(TuneGameFacts g, string slot)
        => g.Dlls.TryGetValue(slot, out var f) && f.Present
           && !(slot != TuneDllSlots.Fg && IsV1(f.Version));

    private static bool IsV1(string? version) => version?.StartsWith("1.", StringComparison.Ordinal) == true;

    private static TuneStep DllStep(string slot, string want, TuneDllFacts? f)
    {
        string label = TuneDllSlots.Label(slot);
        string? target = null;
        string? skip = null;
        if (f == null || !f.Present) skip = $"The game has no {label} file";
        else if (slot != TuneDllSlots.Fg && IsV1(f.Version)) skip = "Version 1 can't be updated";
        else if (f.DriverOverride) skip = "NVIDIA Override is on for it";
        else
        {
            target = want.Equals(TuneDll.Newest, StringComparison.OrdinalIgnoreCase) ? f.Versions.FirstOrDefault() : want;
            if (string.IsNullOrEmpty(target)) skip = "No versions available (DLSS list not loaded)";
            else if (!want.Equals(TuneDll.Newest, StringComparison.OrdinalIgnoreCase)
                     && !f.Versions.Contains(target, StringComparer.OrdinalIgnoreCase)) skip = $"Version {target} isn't available";
            else if (SameVersion(f.Version, target)) skip = $"Already {target}";
        }
        return new TuneStep
        {
            Kind = TuneStepKind.Dll, Id = slot, Label = label, Target = target ?? "", Skip = skip,
            Detail = skip == null ? $"{f?.Version ?? "?"} → {target}" : "",
        };
    }

    /// <summary>"310.5.3" vs "310.5.3.0" etc.: compared as RHI shows them (trailing ".0" trimmed).</summary>
    public static bool SameVersion(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return DlssStreamlineService.FormatVersion(a).Equals(DlssStreamlineService.FormatVersion(b), StringComparison.OrdinalIgnoreCase);
    }

    private static void AddExtra(TunePlan plan, TunePreset preset, TuneGameFacts g, string id)
    {
        if (preset.Extras == null || !preset.Extras.TryGetValue(id, out var wanted)) return;
        string label = DetailPanelBuilder.TuneExtraLabel(id);
        var (option, error) = g.ExtrasOption.TryGetValue(id, out var o) ? o : (wanted, null);
        string? skip = g.ExtrasInstalled.Contains(id) ? "Already installed"
            : g.ExtrasBlocked.TryGetValue(id, out var why) ? why
            : error;
        string optLabel = DetailPanelBuilder.TuneExtraOptionLabel(id);
        string detail = skip == null && !string.IsNullOrEmpty(option)
            ? (optLabel == "DLL name" ? $"as {option}" : $"{optLabel.ToLowerInvariant()} {option}") : "";
        plan.Steps.Add(new TuneStep
        {
            Kind = TuneStepKind.Install, Id = TuneComponentIds.ExtraPrefix + id, Label = label,
            Detail = detail, Target = option ?? "", Skip = skip,
        });
    }

    private static void Add(TunePlan plan, TuneStepKind kind, string id, string label, string? skip)
        => plan.Steps.Add(new TuneStep { Kind = kind, Id = id, Label = label, Skip = skip });

    /// <summary>What untuning will do, as lines for the confirmation ("Remove RenoDX", "DLSS Super Resolution back to 310.1.0").</summary>
    public static List<string> DescribeUndo(TunedGameState st)
    {
        var lines = new List<string>();
        foreach (var c in Enumerable.Reverse(st.Installed))
            lines.Add(c == TuneComponentIds.NeuralRendering && st.NrMethodInstalled != null
                ? $"Remove Neural Rendering ({DetailPanelBuilder.NrMethodLabel(st.NrMethodInstalled)})"
                : $"Remove {TuneComponentIds.Label(c)}");
        foreach (var (slot, d) in st.Dlls)
            lines.Add(d.HadBackup
                ? $"{TuneDllSlots.Label(slot)} back to {d.PreviousVersion ?? "the version before"}"
                : $"{TuneDllSlots.Label(slot)} back to the game's own file");
        foreach (var (key, before) in st.NvidiaBefore)
            if (TuneItems.Get(key) is { } item) lines.Add($"{NvidiaLabel(item)} back to {item.Format(before)}");
        return lines;
    }
}
