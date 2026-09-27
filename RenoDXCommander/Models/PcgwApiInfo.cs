namespace RenoDXCommander.Models;

/// <summary>
/// Graphics API support information scraped from a PCGamingWiki page's
/// "Other information → API" section, plus config file location from the
/// "Game data → Configuration file(s)" section.
/// </summary>
public class PcgwApiInfo
{
    public bool HasDirectX9  { get; set; }
    public bool HasDirectX10 { get; set; }
    public bool HasDirectX11 { get; set; }
    public bool HasDirectX12 { get; set; }
    public bool HasVulkan    { get; set; }
    public bool HasOpenGL    { get; set; }
    public bool HasMetal     { get; set; }

    /// <summary>
    /// The Windows config file directory scraped from the PCGW "Game data → Configuration file(s)"
    /// section. Uses standard Windows environment variable syntax (e.g. %LOCALAPPDATA%\GameName\Saved\Config\Windows).
    /// Null if not found or not a Windows path. Passed directly to ResolveEngineIniDir as projectNameOverride.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// The Microsoft Store / Xbox Game Pass config path, scraped from the "Microsoft Store" row
    /// in the PCGW config table. Usually ends in \WinGDK instead of \Windows.
    /// Falls back to ConfigPath when null.
    /// </summary>
    public string? ConfigPathXbox { get; set; }

    /// <summary>
    /// Engine name from the PCGW infobox (e.g. "Unreal Engine 5", "Unity", "NW.js").
    /// First engine listed when the page has multiple. Null when absent.
    /// Used as fallback EngineHint when PE detection returns empty.
    /// </summary>
    public string? Engine { get; set; }
}
