// TuneItems.cs -- the NVIDIA profile settings a Tune preset can set: DLSS presets, DLSS render scales and every
// per-game driver setting on a game's page (Driver Settings section). Each is a DisplayModeItem (Choice) so the same
// Read / Write code is used to apply a value and to put the game's old value back when the game is untuned.
// Items shared with Display modes (SR / RR preset and scale, FPS limit) are taken from DisplayModeItems.

using System.Globalization;

namespace RenoDXCommander.Services;

/// <summary>Keys of Tune's NVIDIA items. Never rename: stored in settings.json.</summary>
public static class TuneItemKeys
{
    public const string SrPreset = DisplayModeItemKeys.DlssSrPreset;
    public const string RrPreset = DisplayModeItemKeys.DlssRrPreset;
    public const string FgPreset = "DlssFgPreset";
    public const string SrScale = DisplayModeItemKeys.DlssSrScale;
    public const string RrScale = DisplayModeItemKeys.DlssRrScale;

    public const string VSync = "DrvVSync";
    public const string TearControl = "DrvTearControl";
    public const string LowLatency = "DrvLowLatency";
    public const string SmoothMotion = "DrvSmoothMotion";
    public const string SmoothMotionApis = "DrvSmoothMotionApis";
    public const string FlipPacing = "DrvFlipPacing";
    public const string PowerMode = "DrvPowerMode";
    public const string GSync = "DrvGSync";
    public const string FpsLimit = DisplayModeItemKeys.DriverFpsCap;
    public const string ReBar = "DrvReBar";
    public const string ReBarMode = "DrvReBarMode";
    public const string ReBarSize = "DrvReBarSize";
    public const string LodBias = "DrvLodBias";
    public const string Aniso = "DrvAniso";
    public const string TextureQuality = "DrvTextureQuality";

    /// <summary>DLSS items: only applied when the game has that DLSS component.</summary>
    public static readonly IReadOnlyList<string> DlssItems = new[] { SrPreset, RrPreset, FgPreset, SrScale, RrScale };

    public static bool IsDriverSetting(string key) => !DlssItems.Contains(key, StringComparer.OrdinalIgnoreCase);
}

public static class TuneItems
{
    public const string GroupDlssPresets = "DLSS presets";
    public const string GroupDlssScale = "DLSS render scale";
    public const string GroupDriver = "Driver settings";

    private const string WriteFailed = "Couldn't write the NVIDIA profile";

    private static IReadOnlyList<DisplayModeItem>? _all;

    /// <summary>All items in the order the editor lists them.</summary>
    public static IReadOnlyList<DisplayModeItem> All => _all ??= Build();

    public static DisplayModeItem? Get(string key) => All.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static string GroupOf(string key) => key switch
    {
        TuneItemKeys.SrPreset or TuneItemKeys.RrPreset or TuneItemKeys.FgPreset => GroupDlssPresets,
        TuneItemKeys.SrScale or TuneItemKeys.RrScale => GroupDlssScale,
        _ => GroupDriver,
    };

    private static string S(uint v) => v.ToString(CultureInfo.InvariantCulture);
    private static string? NeedNvidia(DisplayModeContext c) => c.NvidiaReady ? null : "No NVIDIA graphics card / driver found";

    private static IReadOnlyList<(string, string)> Opts((string Name, uint Value)[] list)
        => list.Select(o => (o.Name, S(o.Value))).ToList();

    private static DisplayModeItem Copy(string key, string label, string group)
    {
        var src = DisplayModeItems.Get(key) ?? throw new InvalidOperationException($"Display mode item {key} missing");
        return new DisplayModeItem
        {
            Key = src.Key, Label = label, Group = group, Help = src.Help, Kind = DisplayModeValueKind.Choice,
            Choices = src.Choices, Unavailable = src.Unavailable, Read = src.Read, Write = src.Write,
        };
    }

    /// <summary>A uint driver setting with a fixed option list.</summary>
    private static DisplayModeItem Uint(string key, string label, string help, (string Name, uint Value)[] options,
        Func<DlssPresetService, string, string, uint> get, Func<DlssPresetService, string, string, uint, bool> set) => new()
    {
        Key = key, Label = label, Group = GroupDriver, Help = help, Kind = DisplayModeValueKind.Choice,
        Choices = () => Opts(options),
        Unavailable = NeedNvidia,
        Read = c => S(get(c.Nvidia!, c.GameName, c.InstallPath)),
        Write = (c, v) => uint.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                          && set(c.Nvidia!, c.GameName, c.InstallPath, n) ? null : WriteFailed,
    };

    private static IReadOnlyList<DisplayModeItem> Build()
    {
        var list = new List<DisplayModeItem>
        {
            // ── DLSS presets ──
            Copy(TuneItemKeys.SrPreset, "Super Resolution preset", GroupDlssPresets),
            Copy(TuneItemKeys.RrPreset, "Ray Reconstruction preset", GroupDlssPresets),
            new()
            {
                Key = TuneItemKeys.FgPreset, Label = "Frame Generation preset", Group = GroupDlssPresets, Kind = DisplayModeValueKind.Choice,
                Help = "The driver's DLSS Frame Generation preset override for this game (NVIDIA profile).",
                Choices = () => Opts(DlssPresetService.FgPresets),
                Unavailable = NeedNvidia,
                Read = c => S(c.Nvidia!.GetFgPreset(c.GameName, c.InstallPath)),
                Write = (c, v) => uint.TryParse(v, out var p) && c.Nvidia!.SetFgPreset(c.GameName, c.InstallPath, p) ? null : WriteFailed,
            },
            // ── DLSS render scale ──
            Copy(TuneItemKeys.SrScale, "Super Resolution render scale", GroupDlssScale),
            Copy(TuneItemKeys.RrScale, "Ray Reconstruction render scale", GroupDlssScale),

            // ── Driver settings (Driver Settings section, same order) ──
            Uint(TuneItemKeys.VSync, "VSync", "The driver's vertical sync mode for this game.",
                DlssPresetService.VSyncModeOptions, (n, g, p) => n.GetVSyncMode(g, p), (n, g, p, v) => n.SetVSyncMode(g, p, v)),
            Uint(TuneItemKeys.TearControl, "Tear control", "Standard or Adaptive VSync.",
                DlssPresetService.VSyncTearControlOptions, (n, g, p) => n.GetVSyncTearControl(g, p), (n, g, p, v) => n.SetVSyncTearControl(g, p, v)),
            Uint(TuneItemKeys.LowLatency, "Low latency", "NVIDIA Low Latency Mode (Off, On or Ultra). ReLimiter turns it off when it installs.",
                DlssPresetService.LowLatencyModeOptions, (n, g, p) => n.GetLowLatencyMode(g, p), (n, g, p, v) => n.SetLowLatencyMode(g, p, v)),
            new()
            {
                Key = TuneItemKeys.SmoothMotion, Label = "Smooth Motion", Group = GroupDriver, Kind = DisplayModeValueKind.Choice,
                Help = "NVIDIA Smooth Motion (driver frame generation, RTX 40 series and newer). On also allows it for every API, as the game page does.",
                Choices = () => Opts(DlssPresetService.SmoothMotionEnableOptions),
                Unavailable = NeedNvidia,
                Read = c => S(c.Nvidia!.GetSmoothMotionEnable(c.GameName, c.InstallPath)),
                Write = (c, v) =>
                {
                    if (!uint.TryParse(v, out var n)) return WriteFailed;
                    var nv = c.Nvidia!;
                    bool ok = nv.SetSmoothMotionEnable(c.GameName, c.InstallPath, n);
                    // The game page sets the allowed APIs with the switch (All when on, None when off)
                    if (ok && n != 0 && nv.GetSmoothMotionApis(c.GameName, c.InstallPath) == 0)
                        nv.SetSmoothMotionApis(c.GameName, c.InstallPath, 7u);
                    else if (ok && n == 0)
                        nv.SetSmoothMotionApis(c.GameName, c.InstallPath, 0u);
                    return ok ? null : WriteFailed;
                },
            },
            Uint(TuneItemKeys.SmoothMotionApis, "Smooth Motion APIs", "Which graphics APIs Smooth Motion works in.",
                DlssPresetService.SmoothMotionApisOptions, (n, g, p) => n.GetSmoothMotionApis(g, p), (n, g, p, v) => n.SetSmoothMotionApis(g, p, v)),
            Uint(TuneItemKeys.FlipPacing, "Smooth Motion flip pacing", "Smooth Motion's flip pacing (On favours smoothness, Off latency).",
                DlssPresetService.SmoothMotionFlipPacingFsOptions, (n, g, p) => n.GetSmoothMotionFlipPacingFs(g, p),
                (n, g, p, v) => n.SetSmoothMotionFlipPacingFs(g, p, v) & n.SetSmoothMotionFlipPacingWin(g, p, v == 0xFFFFFFFF ? 1u : v)),
            Uint(TuneItemKeys.PowerMode, "Power mode", "The driver's power management mode for this game.",
                DlssPresetService.PowerManagementOptions, (n, g, p) => n.GetPowerManagementMode(g, p), (n, g, p, v) => n.SetPowerManagementMode(g, p, v)),
            new()
            {
                Key = TuneItemKeys.GSync, Label = "G-SYNC", Group = GroupDriver, Kind = DisplayModeValueKind.Choice,
                Help = "G-SYNC on (the driver's normal setting) or off for this game.",
                Choices = () => new List<(string, string)> { ("Enabled", "1"), ("Disabled", "0") },
                Unavailable = NeedNvidia,
                Read = c => c.Nvidia!.GetPerGameGSyncEnabled(c.GameName, c.InstallPath) ? "1" : "0",
                Write = (c, v) => c.Nvidia!.SetPerGameGSyncEnabled(c.GameName, c.InstallPath, v != "0") ? null : WriteFailed,
            },
            Copy(TuneItemKeys.FpsLimit, "FPS limit", GroupDriver),
            new()
            {
                Key = TuneItemKeys.ReBar, Label = "ReBAR", Group = GroupDriver, Kind = DisplayModeValueKind.Choice,
                Help = "Resizable BAR for this game. Can ask for administrator rights.",
                Choices = () => new List<(string, string)> { ("Auto (Default)", "1"), ("Off", "0"), ("On", "2") },
                Unavailable = NeedNvidia,
                Read = c => S(c.Nvidia!.GetReBarEnableMode(c.GameName, c.InstallPath)),
                Write = (c, v) => uint.TryParse(v, out var n) && c.Nvidia!.SetReBarEnableMode(c.GameName, c.InstallPath, n) ? null : WriteFailed,
            },
            Uint(TuneItemKeys.ReBarMode, "ReBAR mode", "Standard or Optimized (with ReBAR On).",
                DlssPresetService.ReBarModes, (n, g, p) => n.GetReBarMode(g, p), (n, g, p, v) => n.SetReBarMode(g, p, v)),
            new()
            {
                Key = TuneItemKeys.ReBarSize, Label = "ReBAR size limit", Group = GroupDriver, Kind = DisplayModeValueKind.Choice,
                Help = "ReBAR's size limit (with ReBAR On).",
                Choices = () => DlssPresetService.ReBarSizeLimits.Select(o => (o.Name, o.Value.ToString(CultureInfo.InvariantCulture))).ToList(),
                Unavailable = NeedNvidia,
                Read = c => c.Nvidia!.GetReBarSizeLimit(c.GameName, c.InstallPath).ToString(CultureInfo.InvariantCulture),
                Write = (c, v) => ulong.TryParse(v, out var n) && c.Nvidia!.SetReBarSizeLimit(c.GameName, c.InstallPath, n) ? null : WriteFailed,
            },
            new()
            {
                Key = TuneItemKeys.LodBias, Label = "LOD bias", Group = GroupDriver, Kind = DisplayModeValueKind.Choice,
                Help = "Texture LOD bias for this game (DirectX).",
                Choices = () => DlssPresetService.LodBiasOptions
                    .Select(o => (o.Name, o.Eighths?.ToString(CultureInfo.InvariantCulture) ?? "default")).ToList(),
                Unavailable = NeedNvidia,
                Read = c => c.Nvidia!.GetLodBias(c.GameName, c.InstallPath)?.ToString(CultureInfo.InvariantCulture) ?? "default",
                Write = (c, v) =>
                {
                    int? e = v == "default" ? null : int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
                    if (v != "default" && e == null) return WriteFailed;
                    return c.Nvidia!.SetLodBias(c.GameName, c.InstallPath, e) ? null : WriteFailed;
                },
            },
            Uint(TuneItemKeys.Aniso, "Anisotropic filtering", "The driver's anisotropic filtering for this game.",
                DlssPresetService.AnisoOptions.Select(o => (o.Name, o.Level)).ToArray(),
                (n, g, p) => n.GetAnisoLevel(g, p), (n, g, p, v) => n.SetAnisoLevel(g, p, v)),
            new()
            {
                Key = TuneItemKeys.TextureQuality, Label = "Texture filtering quality", Group = GroupDriver, Kind = DisplayModeValueKind.Choice,
                Help = "The driver's texture filtering quality for this game.",
                Choices = () => DlssPresetService.TextureQualityOptions
                    .Select(o => (o.Name, o.Value?.ToString(CultureInfo.InvariantCulture) ?? "default")).ToList(),
                Unavailable = NeedNvidia,
                Read = c => c.Nvidia!.GetTextureQuality(c.GameName, c.InstallPath)?.ToString(CultureInfo.InvariantCulture) ?? "default",
                Write = (c, v) =>
                {
                    uint? q = v == "default" ? null : uint.TryParse(v, out var n) ? n : null;
                    if (v != "default" && q == null) return WriteFailed;
                    return c.Nvidia!.SetTextureQuality(c.GameName, c.InstallPath, q) ? null : WriteFailed;
                },
            },
        };
        return list;
    }
}
