namespace RenoDXCommander.Models;

public class DxvkInstalledRecord
{
    /// <summary>Game name as detected by RHI.</summary>
    public string GameName { get; set; } = "";

    /// <summary>Resolved install path for the game.</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>DXVK version tag (e.g. "v2.7.1").</summary>
    public string DxvkVersion { get; set; } = "";

    /// <summary>DXVK DLLs deployed to the Game_Directory root.</summary>
    public List<string> InstalledDlls { get; set; } = new();

    /// <summary>DXVK DLLs deployed to OptiScaler/plugins/ folder.</summary>
    public List<string> PluginFolderDlls { get; set; } = new();

    /// <summary>Original files backed up with .original extension.</summary>
    public List<string> BackedUpFiles { get; set; } = new();

    /// <summary>Whether RHI deployed dxvk.conf to the game directory.</summary>
    public bool DeployedConf { get; set; }

    /// <summary>Whether any DXVK DLL is in the OptiScaler plugins folder.</summary>
    public bool InOptiScalerPlugins { get; set; }

    /// <summary>
    /// Legacy field — no longer set on new installs. Kept for backwards compatibility
    /// with existing records from before the direct-mode migration.
    /// When true on an old record, the uninstall path handles migration cleanup.
    /// </summary>
    public bool IsProxyMode { get; set; }

    /// <summary>
    /// True when Lilium HDR DXVK is installed as d3d9.dll directly (NOT proxy mode).
    /// Uses Vulkan layer ReShade instead of local d3d9.dll hook.
    /// On uninstall, must restore ReShade as d3d9.dll and remove Vulkan footprint.
    /// </summary>
    public bool IsLiliumHdrMode { get; set; }

    /// <summary>Timestamp of installation.</summary>
    public DateTime InstalledAt { get; set; }
}
