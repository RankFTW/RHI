namespace RenoDXCommander.Models;

public class LumaMod
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public string Status { get; set; } = "✅";
    public string? SpecialNotes { get; set; }
    public string? FeatureNotes { get; set; }
    /// <summary>True when this is the generic Unreal Engine Luma mod, not a named game-specific mod.</summary>
    public bool IsGenericLuma { get; set; }
    /// <summary>Nexus Mods page URL, if the mod is hosted on Nexus (may be the only download or alongside GitHub).</summary>
    public string? NexusUrl { get; set; }

    /// <summary>
    /// True when the Luma wiki's Special Notes column mentions dgVoodoo2 — indicates this
    /// is a DX9 game that needs D3D9.dll + dgVoodoo.conf deployed alongside Luma.
    /// </summary>
    public bool RequiresDgVoodoo { get; set; }

    /// <summary>
    /// Specific dgVoodoo2 version recommended by the Luma wiki for this mod (e.g. "2.87.3").
    /// When set and present in the manifest dgVoodooVersions dict, this version is preferred
    /// over the default (latest). Null = use the default.
    /// </summary>
    public string? DgVoodooVersion { get; set; }
}

/// <summary>
/// Per-game data scraped from the Luma Framework generic Unreal Engine wiki table.
/// </summary>
public class LumaGenericGameEntry
{
    public string Name { get; set; } = "";
    /// <summary>Notes text (e.g. "In-game: AA FXAA High", "-DX11 launch argument").</summary>
    public string? Notes { get; set; }
    /// <summary>Engine.ini keys to write on install. Each entry is (Section, Key, Value).</summary>
    public List<(string Section, string Key, string Value)> EngineIniKeys { get; set; } = new();
    /// <summary>Full launch argument string scraped from notes (e.g. "-dx11", "-nod3d9ex", "-oss=Steam -dx11"). Null if none required.</summary>
    public string? LaunchArgs { get; set; }
    /// <summary>True when the HDR column is checked.</summary>
    public bool HdrSupported { get; set; }
    /// <summary>True when the DLSS/FSR column is checked (✅).</summary>
    public bool DlssFsrSupported { get; set; }
    /// <summary>True when the DLSS/FSR column shows ⛔ (explicitly blocked/incompatible).</summary>
    public bool DlssFsrBlocked { get; set; }
    /// <summary>UE version string (e.g. "4.27.2", "5.4.0").</summary>
    public string? UeVersion { get; set; }
}
