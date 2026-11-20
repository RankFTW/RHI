// BackupModels.cs -- what a .rhibackup file says about the PC it came from (backup.json inside the zip) and the
// restore plan written to %LocalAppData%\RHI\restore\plan.json. Plan: docs/backup-restore-plan.md.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>Component ids used in backups. Never rename: they're stored in backup files.</summary>
public static class BackupComponents
{
    public const string ReShade = "ReShade";
    public const string Dxvk = "DXVK";
    public const string ReFramework = "REFramework";
    public const string RenoDx = "RenoDX";
    public const string Luma = "Luma";
    public const string ReLimiter = "ReLimiter";
    public const string DisplayCommander = "DisplayCommander";
    public const string DofFix = "DOFFix";
    public const string OptiScaler = "OptiScaler";

    /// <summary>The order restores install in: ReShade first, DXVK before OptiScaler (it moves DXVK aside), OptiScaler last.</summary>
    public static readonly string[] InstallOrder =
        { ReShade, Dxvk, ReFramework, RenoDx, Luma, ReLimiter, DisplayCommander, DofFix, OptiScaler };

    public static string Label(string id) => id switch
    {
        ReShade => "ReShade",
        Dxvk => "DXVK",
        ReFramework => "RE Framework",
        RenoDx => "RenoDX",
        Luma => "Luma",
        ReLimiter => "ReLimiter",
        DisplayCommander => "Display Commander",
        DofFix => "DOF Fix",
        OptiScaler => "OptiScaler",
        _ => id,
    };

    public static int OrderOf(string id)
    {
        int i = Array.IndexOf(InstallOrder, id);
        return i < 0 ? InstallOrder.Length : i;
    }
}

public sealed class BackupManifest
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public string RhiVersion { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public bool HasNvidia { get; set; }
    public List<BackupGame> Games { get; set; } = new();
}

public sealed class BackupGame
{
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public bool Is32Bit { get; set; }
    public List<BackupComponent> Components { get; set; } = new();
    /// <summary>Config files kept for this game: paths relative to the install folder.</summary>
    public List<string> ConfigFiles { get; set; } = new();
    /// <summary>Folder inside the zip that holds this game's config files ("games/3").</summary>
    public string ConfigFolder { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => GameKey.From(Name, Store).ToKey();
}

public sealed class BackupComponent
{
    public string Id { get; set; } = "";
    /// <summary>What was installed, for the review list only (version, mod file name).</summary>
    public string? Detail { get; set; }
}

/// <summary>A restore waiting for the first scan (restore\plan.json).</summary>
public sealed class RestorePlan
{
    public DateTime BackupCreatedUtc { get; set; }
    public string RhiVersion { get; set; } = "";
    public bool HasNvidia { get; set; }
    public List<BackupGame> Games { get; set; } = new();
}
