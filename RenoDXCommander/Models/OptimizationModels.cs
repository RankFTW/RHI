using System.Security.Cryptography;
using System.Text;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Models;

/// <summary>How closely a game matches the user's configured RHI defaults.</summary>
public enum OptimizationState
{
    /// <summary>Nothing (that applies to this game) matches the defaults yet.</summary>
    NotOptimized,
    /// <summary>Some applicable items match, some do not (e.g. the defaults changed since the last optimize).</summary>
    PartiallyOptimized,
    /// <summary>Every applicable item matches the current defaults.</summary>
    Optimized,
    /// <summary>A blocking problem (game running, missing folder, unsupported route…) prevents optimizing.</summary>
    NeedsAttention,
}

/// <summary>
/// The user's currently configured DLSS/Streamline/Neural Rendering defaults (the same settings Quick Apply uses).
/// Nothing here is hardware specific: it is whatever the user configured in "DLSS &amp; Streamline Defaults".
/// </summary>
public sealed class OptimizationDefaults
{
    public string SrVersion { get; init; } = "";
    public string RrVersion { get; init; } = "";
    public string FgVersion { get; init; } = "";
    public string StreamlineVersion { get; init; } = "";
    public string NrVersion { get; init; } = "";
    public bool SrOverride { get; init; }
    public bool RrOverride { get; init; }
    public bool FgOverride { get; init; }
    public uint SrPreset { get; init; }
    public uint RrPreset { get; init; }
    public uint FgPreset { get; init; }
    public uint NrPreset { get; init; }
    public uint SrRenderScale { get; init; }
    public uint RrRenderScale { get; init; }

    public static OptimizationDefaults From(SettingsViewModel s, bool nrFeatureEnabled) => new()
    {
        SrVersion = s.DefaultDlssVersion,
        RrVersion = s.DefaultDlssdVersion,
        FgVersion = s.DefaultDlssgVersion,
        StreamlineVersion = s.DefaultStreamlineVersion,
        NrVersion = nrFeatureEnabled ? s.DefaultDlssnrVersion : "",
        SrOverride = s.DefaultSrDriverOverride,
        RrOverride = s.DefaultRrDriverOverride,
        FgOverride = s.DefaultFgDriverOverride,
        SrPreset = s.DefaultSrPreset,
        RrPreset = s.DefaultRrPreset,
        FgPreset = s.DefaultFgPreset,
        NrPreset = nrFeatureEnabled ? s.DefaultNrPreset : 0,
        SrRenderScale = s.DefaultSrRenderScale,
        RrRenderScale = s.DefaultRrRenderScale,
    };

    /// <summary>True when at least one default is configured (otherwise there is nothing to optimize with).</summary>
    public bool HasAny =>
        SrVersion.Length > 0 || RrVersion.Length > 0 || FgVersion.Length > 0 || StreamlineVersion.Length > 0 || NrVersion.Length > 0
        || SrOverride || RrOverride || FgOverride
        || SrPreset != 0 || RrPreset != 0 || FgPreset != 0 || NrPreset != 0 || SrRenderScale != 0 || RrRenderScale != 0;

    /// <summary>Short stable fingerprint; changes whenever any default changes ("Defaults changed" detection).</summary>
    public string Fingerprint()
    {
        var text = string.Join("|", SrVersion, RrVersion, FgVersion, StreamlineVersion, NrVersion,
            SrOverride, RrOverride, FgOverride, SrPreset, RrPreset, FgPreset, NrPreset, SrRenderScale, RrRenderScale);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }
}

/// <summary>What is currently true for one game (read from the card, the NVIDIA profile and the install manifest).</summary>
public sealed class GameOptimizationSnapshot
{
    public bool HasDlss { get; init; }
    public bool HasDlssd { get; init; }
    public bool HasDlssg { get; init; }
    public bool HasStreamline { get; init; }
    public bool HasDlssnr { get; init; }

    public string? DlssVersion { get; init; }
    public string? DlssdVersion { get; init; }
    public string? DlssgVersion { get; init; }
    public string? StreamlineVersion { get; init; }
    public string? DlssnrVersion { get; init; }

    public uint SrPreset { get; init; }
    public uint RrPreset { get; init; }
    public uint FgPreset { get; init; }
    public uint NrPreset { get; init; }
    public uint SrRenderScale { get; init; }
    public uint RrRenderScale { get; init; }
    public bool SrOverride { get; init; }
    public bool RrOverride { get; init; }
    public bool FgOverride { get; init; }

    /// <summary>The Neural Rendering method RHI would use for this game (stored choice, else auto-selected).</summary>
    public string? NrRoute { get; init; }
    /// <summary>True when that route is applicable to this game (bitness / API / DLSS presence).</summary>
    public bool NrRouteSupported { get; init; }
    /// <summary>True when a Neural Rendering method is already installed for the game.</summary>
    public bool NrInstalled { get; init; }
    /// <summary>True when ReShade is installed in the game. Every automatic Neural Rendering route needs it.</summary>
    public bool ReShadeInstalled { get; init; }
    /// <summary>
    /// Whether the game's NR DLL is byte-identical to the cached build of the configured default. Null = could not be
    /// compared (nothing cached / nothing configured); the version string is used instead. Needed because build labels
    /// such as "310.8.SF-v2" are not what the DLL's own file version reports ("310.8.2").
    /// </summary>
    public bool? NrDllMatchesDefault { get; init; }
}

/// <summary>One line of the status report ("SR preset", "Neural Rendering", …).</summary>
public sealed record OptimizationItem(string Name, bool Satisfied, string Detail);

public sealed class OptimizationReport
{
    public OptimizationState State { get; init; }
    public IReadOnlyList<OptimizationItem> Items { get; init; } = Array.Empty<OptimizationItem>();
    /// <summary>The global defaults changed since this game was last optimized and it no longer fully matches.</summary>
    public bool DefaultsChanged { get; init; }
    public string Summary { get; init; } = "";
}

public enum PreflightSeverity { Warning, Blocking }
public sealed record PreflightIssue(PreflightSeverity Severity, string Message);

/// <summary>Everything the preflight needs to know, gathered by the caller (so the rules stay pure and testable).</summary>
public sealed class PreflightInput
{
    public bool InstallPathExists { get; init; }
    public string? GameExecutable { get; init; }
    public bool ExecutableIsValidPe { get; init; }
    public bool GameIsRunning { get; init; }
    public bool ApiDetected { get; init; }
    public bool BitnessKnown { get; init; }
    public string? NrRoute { get; init; }
    public bool NrRouteSupported { get; init; }
    public bool DefaultsConfigured { get; init; }
    public bool NvidiaProfileAccessNeeded { get; init; }
    public bool NvidiaProfileAccessAvailable { get; init; }
    public bool Elevated { get; init; }
    public IReadOnlyList<string> ForeignComponents { get; init; } = Array.Empty<string>();
}

/// <summary>Per-game record kept between "Optimize" and "Undo Optimization".</summary>
public sealed class DllStateRecord
{
    public string Component { get; set; } = "";   // Dlss | Dlssd | Dlssg | Dlssnr | Streamline
    public string Path { get; set; } = "";        // dll path (Streamline: the folder)
    public string? VersionBefore { get; set; }    // display-format version, null if unknown
    public bool HadBackupBefore { get; set; }     // an RHI ".original" backup already existed
}

public sealed class OptimizationRestorePoint
{
    public string GameKey { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string DefaultsFingerprint { get; set; } = "";

    // NVIDIA per-game profile values relevant to Quick Apply (0/false == not set, which is how RHI stores "default")
    public uint SrPreset { get; set; }
    public uint RrPreset { get; set; }
    public uint FgPreset { get; set; }
    public uint NrPreset { get; set; }
    public uint SrRenderScale { get; set; }
    public uint RrRenderScale { get; set; }
    public bool SrOverride { get; set; }
    public bool RrOverride { get; set; }
    public bool FgOverride { get; set; }
    public bool ProfileCaptured { get; set; }

    public List<DllStateRecord> Dlls { get; set; } = new();

    // Neural Rendering
    public string? NrStoredMethodBefore { get; set; }
    public bool NrWasInstalledBefore { get; set; }
    public string? NrMethodInstalledByOptimize { get; set; }
    public bool ReShadeInstalledByOptimize { get; set; }

    public List<string> Notes { get; set; } = new();
}

/// <summary>What an Undo has to do for each DLL component.</summary>
public enum DllUndoAction { None, RestoreOriginal, SwapToPreviousVersion, CannotRestore }
public sealed record DllUndoStep(string Component, string Path, DllUndoAction Action, string? Version, string Reason);
