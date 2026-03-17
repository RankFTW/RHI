namespace RenoDXCommander.Models;

public class AuxInstalledRecord
{
    public string  GameName       { get; set; } = "";
    public string  InstallPath    { get; set; } = "";
    /// <summary>"DisplayCommander" or "ReShade"</summary>
    public string  AddonType      { get; set; } = "";
    /// <summary>Filename used on disk (e.g. dxgi.dll or zzz_display_commander.addon64)</summary>
    public string  InstalledAs    { get; set; } = "";
    public string? SourceUrl      { get; set; }
    public long?   RemoteFileSize { get; set; }
    public DateTime InstalledAt   { get; set; }
}
