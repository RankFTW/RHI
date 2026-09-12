namespace RenoDXCommander.Models;

/// <summary>
/// Graphics API support information scraped from a PCGamingWiki page's
/// "Other information → API" section.
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
}
