namespace RenoDXCommander.Models;

/// <summary>
/// Holds pre-fetched global NVAPI driver settings for the Settings page.
/// All values are nullable — null means the fetch failed or timed out.
/// </summary>
public record NvApiSettingsSnapshot
{
    // Shader cache
    public uint? ShaderCacheSize { get; init; }
    public uint? ShaderPrecompile { get; init; }

    // G-Sync
    public uint? GSyncMode { get; init; }
    public uint? GSyncEnabled { get; init; }
    public bool? GSyncIndicator { get; init; }

    // FPS & Refresh
    public uint? FpsLimit { get; init; }
    public uint? PreferredRefreshRate { get; init; }

    // DMFG
    public uint? DmfgFrameCount { get; init; }
    public uint? DmfgTargetFps { get; init; }

    // ReBAR
    public uint? ReBarEnableMode { get; init; }
    public ulong? ReBarSizeLimit { get; init; }

    // VSync & Power
    public uint? VSyncMode { get; init; }
    public uint? PowerMode { get; init; }

    /// <summary>
    /// Creates a snapshot with safe default values (used when fetch times out or fails).
    /// </summary>
    public static NvApiSettingsSnapshot Default => new()
    {
        ShaderCacheSize = 0,
        ShaderPrecompile = 0,
        GSyncMode = 0x10244799, // Fullscreen only (default)
        GSyncEnabled = 0,
        GSyncIndicator = false,
        FpsLimit = 0,
        PreferredRefreshRate = 0,
        DmfgFrameCount = 0,
        DmfgTargetFps = 0,
        ReBarEnableMode = 1, // Auto
        ReBarSizeLimit = 0x40000000, // 1GB
        VSyncMode = null,
        PowerMode = null
    };
}
