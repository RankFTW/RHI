namespace RenoDXCommander.Models;

/// <summary>
/// Holds captured OptiScaler cog settings for one user-configurable preset slot.
/// Null fields mean "not captured" — they are not applied when loading the preset.
/// </summary>
public class OsPreset
{
    public string? Name                   { get; set; }  // user-editable label
    public string? FgInput                { get; set; }  // INI value e.g. "upscaler", "auto"
    public string? FgOutput               { get; set; }  // INI value e.g. "dlssg", "auto"
    public string? FgNvngxReplacement     { get; set; }  // INI value e.g. "Arturs", "None"
    public bool?   DeployStreamline       { get; set; }
    public bool?   DeployDlssEnabler      { get; set; }
    public string? SrPreset               { get; set; }  // raw INI value e.g. "13", "auto"
    public string? RrPreset               { get; set; }  // raw INI value e.g. "4", "auto"
    public string? RenderScale            { get; set; }  // display name e.g. "Off", "67% Quality"
    public bool?   DisableFlipMetering    { get; set; }
    public string? HudFix                 { get; set; }  // "true", "false", "auto"
    public float?  FramerateLimit         { get; set; }  // fps value, 0 = Off
    // ── DLSS NR preset fields ─────────────────────────────────────────────────
    public string? NrRuntime              { get; set; }  // e.g. "310.8.2", "310.8.SF-v2"
    public string? NrEnabled              { get; set; }  // "true", "false", "auto"
    public string? NrRunBeforeSr          { get; set; }  // "true", "false", "auto"
    public string? NrPasses               { get; set; }  // "1", "2", "3", "auto"
    public string? NrWorkingScale         { get; set; }  // "0.5", "0.75", "1.0", "1.5", "auto"
    public string? NrFinishedPicture      { get; set; }  // "true", "false", "auto"
}
