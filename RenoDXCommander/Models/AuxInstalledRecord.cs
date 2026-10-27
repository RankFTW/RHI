namespace RenoDXCommander.Models;

public class AuxInstalledRecord
{
    public string  GameName       { get; set; } = "";
    public string  InstallPath    { get; set; } = "";
    /// <summary>Store/platform where this game is installed (Steam, Xbox, Epic, etc.).</summary>
    public string  Store          { get; set; } = "";
    /// <summary>"DisplayCommander" or "ReShade"</summary>
    public string  AddonType      { get; set; } = "";
    /// <summary>Filename used on disk (e.g. dxgi.dll or zzz_display_commander.addon64)</summary>
    public string  InstalledAs    { get; set; } = "";
    public string? SourceUrl      { get; set; }
    public long?   RemoteFileSize { get; set; }
    public DateTime InstalledAt   { get; set; }
    /// <summary>ReShade build channel used at install time ("Stable" or "Nightly"). Null = legacy/unknown (treated as global default).</summary>
    public string? Channel        { get; set; }
    /// <summary>OptiScaler variant used at install time ("Stable" or "Nightly"). Null = legacy/Stable.</summary>
    public string? OsVariant      { get; set; }
    /// <summary>OptiScaler nightly build date installed (e.g. "20261004"). Null = latest at time of install.</summary>
    public string? OsNightlyBuild { get; set; }
}
