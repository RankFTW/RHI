// TunePreset.cs -- Tune presets: a named set of components and NVIDIA settings that the Tune button next to a game's
// name installs / applies in one go, and removes again on a second click (only what Tune itself added or changed).
// Saved in settings.json as "TunePresets" (TuneData). Store: Services/TunePresetStore.cs.

namespace RenoDXCommander.Models;

/// <summary>Values of <see cref="TunePreset.HdrMod"/>. Never rename: stored in settings.json.</summary>
public static class TuneHdrMod
{
    public const string None = "";
    public const string RenoDx = "RenoDX";
    public const string Luma = "Luma";
    /// <summary>RenoDX when the game has a mod, else Luma.</summary>
    public const string RenoDxElseLuma = "RenoDXElseLuma";
}

/// <summary>Values of <see cref="TunePreset.FrameLimiter"/>. Never rename: stored in settings.json.</summary>
public static class TuneLimiter
{
    public const string None = "";
    public const string ReLimiter = "ReLimiter";
    public const string DisplayCommander = "DisplayCommander";
}

/// <summary>Values of <see cref="TunePreset.NrMethod"/> besides the method keys (DetailPanelBuilder.TuneNrMethods).</summary>
public static class TuneNr
{
    public const string None = "";
    /// <summary>The method the Neural Rendering section picks for the game.</summary>
    public const string Auto = "Auto";
}

/// <summary>Values of the DLL fields of <see cref="TunePreset"/>: "" = leave, "Newest", or a version from the list.</summary>
public static class TuneDll
{
    public const string Leave = "";
    public const string Newest = "Newest";
}

/// <summary>One preset. Empty / missing values mean "leave the game as it is".</summary>
public sealed class TunePreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Not offered on game pages.</summary>
    public bool Hidden { get; set; }

    public bool ReShade { get; set; }
    public string HdrMod { get; set; } = TuneHdrMod.None;
    public string FrameLimiter { get; set; } = TuneLimiter.None;

    public string DlssSr { get; set; } = TuneDll.Leave;
    public string DlssRr { get; set; } = TuneDll.Leave;
    public string DlssFg { get; set; } = TuneDll.Leave;
    public string Streamline { get; set; } = TuneDll.Leave;

    /// <summary>Install OptiScaler (Extras), with <see cref="OsDllName"/> ("" = OptiScaler's default name for the game).</summary>
    public bool OptiScaler { get; set; }
    public string OsDllName { get; set; } = "";

    /// <summary>
    /// Other Extras to install: TuneExtraIds id → option ("" = the row's default; else a DLL name, DXVK variant or
    /// dgVoodoo2 version). Missing = leave.
    /// </summary>
    public Dictionary<string, string> Extras { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Neural Rendering method: "" = none, "Auto" = the section's pick, or a method key.</summary>
    public string NrMethod { get; set; } = TuneNr.None;
    /// <summary>DLSS5 Tool / ShortFuse / Uplift add-on version for the method ("" = Latest). Only with a fixed method.</summary>
    public string NrAddonVersion { get; set; } = "";
    /// <summary>nvngx_dlssnr.dll version the method deploys ("" = Latest).</summary>
    public string NrDllVersion { get; set; } = "";

    /// <summary>NVIDIA profile settings (DLSS presets, render scales, driver settings): TuneItems key → value.</summary>
    public Dictionary<string, string> Nvidia { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TunePreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Hidden = Hidden,
        ReShade = ReShade,
        HdrMod = HdrMod ?? "",
        FrameLimiter = FrameLimiter ?? "",
        DlssSr = DlssSr ?? "",
        DlssRr = DlssRr ?? "",
        DlssFg = DlssFg ?? "",
        Streamline = Streamline ?? "",
        OptiScaler = OptiScaler,
        OsDllName = OsDllName ?? "",
        NrMethod = NrMethod ?? "",
        Extras = new Dictionary<string, string>(Extras ?? new(), StringComparer.OrdinalIgnoreCase),
        NrAddonVersion = NrAddonVersion ?? "",
        NrDllVersion = NrDllVersion ?? "",
        Nvidia = new Dictionary<string, string>(Nvidia ?? new(), StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>How many things the preset does (for "RHI Tuned (7 steps)").</summary>
    public int StepCount =>
        (ReShade ? 1 : 0)
        + (string.IsNullOrEmpty(HdrMod) ? 0 : 1)
        + (string.IsNullOrEmpty(FrameLimiter) ? 0 : 1)
        + (string.IsNullOrEmpty(DlssSr) ? 0 : 1)
        + (string.IsNullOrEmpty(DlssRr) ? 0 : 1)
        + (string.IsNullOrEmpty(DlssFg) ? 0 : 1)
        + (string.IsNullOrEmpty(Streamline) ? 0 : 1)
        + (OptiScaler ? 1 : 0)
        + (Extras?.Count ?? 0)
        + (string.IsNullOrEmpty(NrMethod) ? 0 : 1)
        + (Nvidia?.Count ?? 0);
}

/// <summary>A DLL Tune replaced, and what was there before.</summary>
public sealed class TunedDll
{
    public string Path { get; set; } = "";
    /// <summary>The .original backup existed before Tune (the game was already on a swapped version).</summary>
    public bool HadBackup { get; set; }
    /// <summary>Version before Tune (as RHI shows it), used to swap back when <see cref="HadBackup"/>.</summary>
    public string? PreviousVersion { get; set; }
    /// <summary>Version Tune put in.</summary>
    public string? NewVersion { get; set; }
}

/// <summary>What Tune did to one game, so a second click can undo exactly that.</summary>
public sealed class TunedGameState
{
    public string PresetId { get; set; } = "";
    public string PresetName { get; set; } = "";
    public DateTime TunedUtc { get; set; }
    /// <summary>Components Tune installed (TuneComponents ids), in install order.</summary>
    public List<string> Installed { get; set; } = new();
    /// <summary>DLLs Tune swapped: "SR" / "RR" / "FG" / "SL" → details.</summary>
    public Dictionary<string, TunedDll> Dlls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>NVIDIA settings Tune changed: TuneItems key → the value before.</summary>
    public Dictionary<string, string> NvidiaBefore { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per-game RHI settings Tune changed (TuneSettingKeys) → the value before ("" = not set).</summary>
    public Dictionary<string, string> SettingsBefore { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The Neural Rendering method Tune installed, or null.</summary>
    public string? NrMethodInstalled { get; set; }
}

/// <summary>Keys of <see cref="TunedGameState.SettingsBefore"/>. Never rename: stored in settings.json.</summary>
public static class TuneSettingKeys
{
    public const string OsDllName = "OsDllName";
    public const string NrAddonVersion = "NrAddonVersion";
    public const string NrDllVersion = "NrDllVersion";
    public const string NrMethod = "NrMethod";
    /// <summary>"Extra:" + extra id: the extra's per-game setting (DXVK variant, dgVoodoo2 version).</summary>
    public const string ExtraPrefix = "Extra:";
}

/// <summary>Everything saved under "TunePresets" in settings.json.</summary>
public sealed class TuneData
{
    public List<TunePreset> Presets { get; set; } = new();
    /// <summary>Tuned games keyed "GameName|Store".</summary>
    public Dictionary<string, TunedGameState> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
