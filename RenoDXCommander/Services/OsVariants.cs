// OsVariants.cs -- the OptiScaler versions RHI can install: the four built-ins, recommended forks from the
// manifest ("Fork:<id>", OsForkService, updated from GitHub) and the user's custom builds ("Custom:<slug>",
// OsCustomBuildService, never updated). Every dropdown, label, validation list and "what kind of build is
// this" question goes through here, so a custom build behaves like a built-in one everywhere.
// Plan: docs/custom-optiscaler-builds-plan.md.

namespace RenoDXCommander.Services;

internal static class OsVariants
{
    public const string Stable = "Stable";
    public const string Nightly = "Nightly";
    public const string DlssNr = "DlssNr";
    public const string F5 = "F5";

    /// <summary>Internal value prefix for custom builds: "Custom:&lt;slug&gt;".</summary>
    public const string CustomPrefix = "Custom:";

    /// <summary>The built-in versions, in dropdown order: internal value and label.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> BuiltIns = new[]
    {
        (Stable, "Stable"),
        (Nightly, "Nightly"),
        (DlssNr, "DLSS NR"),
        (F5, "F5 DLSSNR"),
    };

    public static bool IsCustom(string? variant) =>
        variant != null && variant.StartsWith(CustomPrefix, StringComparison.Ordinal);

    public static bool IsFork(string? variant) => OsForkService.IsFork(variant);

    /// <summary>Built-ins, then recommended forks, then custom builds (each by name).</summary>
    public static List<(string Value, string Label)> All()
    {
        var list = new List<(string, string)>(BuiltIns);
        foreach (var f in OsForkService.All) list.Add((f.Variant, f.Label));
        foreach (var b in OsCustomBuildService.All) list.Add((b.Id, b.Label));
        return list;
    }

    /// <summary>True for a built-in or a custom build that still exists.</summary>
    public static bool IsKnown(string? variant) =>
        variant != null && (BuiltIns.Any(b => b.Value == variant) || OsCustomBuildService.Get(variant) != null
                            || OsForkService.Get(variant) != null);

    /// <summary>Display label ("F5 DLSSNR", "Custom: Venom 1107"); unknown customs read "Custom build (removed)".</summary>
    public static string Label(string? variant)
    {
        if (string.IsNullOrEmpty(variant)) return "Stable";
        foreach (var b in BuiltIns) if (b.Value == variant) return b.Label;
        if (IsCustom(variant)) return OsCustomBuildService.Get(variant)?.Label ?? "Custom build (removed)";
        if (IsFork(variant)) return OsForkService.Get(variant)?.Label ?? "OptiScaler fork (removed)";
        return variant;
    }

    /// <summary>Internal value for a dropdown label (also accepts internal values and old preset labels).</summary>
    public static string FromLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return Stable;
        foreach (var (value, l) in All())
            if (string.Equals(l, label, StringComparison.Ordinal) || string.Equals(value, label, StringComparison.Ordinal))
                return value;
        return label;
    }

    /// <summary>
    /// An OptiScaler 10 build (ships an OptiScaler\ subfolder): Nightly, DLSS NR, F5, or a custom build
    /// with that layout. These get the ⚙'s advanced sections, plugin hosting and the Streamline step.
    /// </summary>
    public static bool IsOs10(string? variant) =>
        variant is Nightly or DlssNr or F5
        || (IsCustom(variant) && OsCustomBuildService.Get(variant)?.IsOs10 == true)
        || (IsFork(variant) && OsForkService.Get(variant)?.IsOs10 == true);

    /// <summary>Builds that need NVIDIA's nvngx_dlssnr.dll beside them: DLSS NR, F5, or a custom build marked so.</summary>
    public static bool NeedsNrDll(string? variant) =>
        variant is DlssNr or F5
        || (IsCustom(variant) && OsCustomBuildService.Get(variant)?.NeedsNrDll == true)
        || (IsFork(variant) && OsForkService.Get(variant)?.NeedsNrDll == true);
}
