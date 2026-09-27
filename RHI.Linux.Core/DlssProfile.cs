using System.Globalization;
using System.Text.Json;

namespace RHI.Linux.Core;

// Windows RHI writes these values into the game's NVIDIA driver profile. Under Proton the
// driver profile is provided by dxvk-nvapi, which accepts the same setting IDs through
// DXVK_NVAPI_DRS_SETTINGS, so the per-game values are applied through Steam launch options.
public static class DlssProfile
{
    public const uint SrPresetId = 0x10E41DF3, RrPresetId = 0x10E41DF7, FgPresetId = 0x10E41DF1, NrPresetId = 0x10E41DF8;
    public const uint SrPresetOverrideId = 0x00634291;
    public const uint SrScaleModeId = 0x10AFB768, SrScaleId = 0x10E41DF5, RrScaleModeId = 0x10BD9423, RrScaleId = 0x10C7D4A2;
    public const uint SrLatestId = 0x10E41E01, RrLatestId = 0x10E41E02, FgLatestId = 0x10E41E03, NrLatestId = 0x10E41E04;
    public const uint MfgModeId = 0x10308298, MfgFactorId = 0x104D6667, MfgDynamicMaxId = 0x10562D0F, MfgTargetFpsId = 0x10CF4125;
    public const uint ScaleCustom = 0x06, MfgOff = 0, MfgFixed = 2, MfgDynamic = 4, TargetFpsMaxRefresh = 0x01000000;

    public static readonly uint[] Managed =
    [
        SrPresetId, RrPresetId, FgPresetId, NrPresetId, SrPresetOverrideId, SrScaleModeId, SrScaleId, RrScaleModeId, RrScaleId,
        SrLatestId, RrLatestId, FgLatestId, NrLatestId, MfgModeId, MfgFactorId, MfgDynamicMaxId, MfgTargetFpsId,
    ];

    public static (string Name, uint Value)[] SrPresets =
        [("Default", 0), ("J - TF1", 0x0A), ("K - TF1", 0x0B), ("L - TF2", 0x0C), ("M - TF2", 0x0D), ("NVIDIA Recommended", 0x00FFFFFF)];
    public static (string Name, uint Value)[] RrPresets = [("Default", 0), ("D - TF1", 0x04), ("E - TF1", 0x05), ("NVIDIA Recommended", 0x00FFFFFF)];
    public static (string Name, uint Value)[] FgPresets = [("Default", 0), ("A", 0x01), ("B", 0x02), ("NVIDIA Recommended", 0x00FFFFFE)];
    public static (string Name, uint Value)[] NrPresets = [("Default", 0), ("NVIDIA Recommended", 0x00FFFFFF)];
    public static readonly (string Name, uint Value)[] RenderScaleOptions =
    [
        ("Off", 0), ("100% DLAA", 100), ("99% DLAA Alt", 99), ("88% DLAA Lite", 88), ("77% Ultra Quality", 77), ("75% Quality+", 75),
        ("67% Quality", 67), ("58% Balanced", 58), ("50% Performance", 50), ("45% Performance-", 45), ("33% Ultra Perf", 33), ("Custom", 0xFFFFFFFF),
    ];
    public static readonly (uint Fps, string Label)[] TargetFpsOptions =
    [
        (59, "59 FPS (60Hz VRR Cap)"), (73, "73 FPS (75Hz VRR Cap)"), (97, "97 FPS (100Hz VRR Cap)"), (116, "116 FPS (120Hz VRR Cap)"),
        (138, "138 FPS (144Hz VRR Cap)"), (157, "157 FPS (165Hz VRR Cap)"), (171, "171 FPS (180Hz VRR Cap)"), (189, "189 FPS (200Hz VRR Cap)"),
        (224, "224 FPS (240Hz VRR Cap)"), (258, "258 FPS (280Hz VRR Cap)"), (275, "275 FPS (300Hz VRR Cap)"), (324, "324 FPS (360Hz VRR Cap)"),
        (416, "416 FPS (480Hz VRR Cap)"), (431, "431 FPS (500Hz VRR Cap)"),
    ];

    // Presets added by the RHI manifest ("dlssPresets") without a client update, like Windows.
    public static void ApplyManifestPresets(JsonElement? presets)
    {
        if (presets is not { ValueKind: JsonValueKind.Object } root) return;
        (string, uint)[] Merge((string Name, uint Value)[] existing, string key)
        {
            if (!root.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array) return existing;
            var merged = existing.ToList();
            foreach (var item in list.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || !item.TryGetProperty("value", out var v) || !v.TryGetUInt32(out var value)) continue;
                if (item.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
                { if (name != "Default") merged.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); continue; }
                if (merged.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                var index = merged.FindIndex(1, p => p.Name == "NVIDIA Recommended" || string.Compare(name, p.Name, StringComparison.OrdinalIgnoreCase) < 0);
                merged.Insert(index < 0 ? merged.Count : index, (name, value));
            }
            return merged.ToArray();
        }
        SrPresets = Merge(SrPresets, "sr"); RrPresets = Merge(RrPresets, "rr"); FgPresets = Merge(FgPresets, "fg"); NrPresets = Merge(NrPresets, "nr");
    }

    private static string Key(uint id) => "0x" + id.ToString("X8");
    public static uint Get(GamePreferences prefs, uint id) => prefs.DriverSettings.TryGetValue(Key(id), out var value) ? value : 0;
    public static bool Has(GamePreferences prefs, uint id) => prefs.DriverSettings.ContainsKey(Key(id));
    public static void Set(GamePreferences prefs, uint id, uint? value)
    {
        if (value == null) prefs.DriverSettings.Remove(Key(id)); else prefs.DriverSettings[Key(id)] = value.Value;
    }

    public static uint Preset(GamePreferences prefs, DlssKind kind) => Get(prefs, PresetId(kind));
    private static uint PresetId(DlssKind kind) => kind switch { DlssKind.SR => SrPresetId, DlssKind.RR => RrPresetId, DlssKind.FG => FgPresetId, _ => NrPresetId };
    public static (string Name, uint Value)[] Presets(DlssKind kind) => kind switch { DlssKind.SR => SrPresets, DlssKind.RR => RrPresets, DlssKind.FG => FgPresets, _ => NrPresets };
    public static void SetPreset(GamePreferences prefs, DlssKind kind, uint preset)
    {
        Set(prefs, PresetId(kind), preset == 0 ? null : preset);
        // SR also needs the companion "preset override" mode: 1 = NVIDIA Recommended, 2 = custom preset.
        if (kind == DlssKind.SR) Set(prefs, SrPresetOverrideId, preset == 0 ? null : preset == 0x00FFFFFF ? 1u : 2u);
    }

    public static uint RenderScale(GamePreferences prefs, DlssKind kind) =>
        Get(prefs, kind == DlssKind.SR ? SrScaleModeId : RrScaleModeId) == ScaleCustom ? Get(prefs, kind == DlssKind.SR ? SrScaleId : RrScaleId) : 0;
    public static void SetRenderScale(GamePreferences prefs, DlssKind kind, uint percent)
    {
        if (percent != 0 && percent is < 33 or > 100) throw new ArgumentOutOfRangeException(nameof(percent), "Render scale must be 33-100%.");
        Set(prefs, kind == DlssKind.SR ? SrScaleModeId : RrScaleModeId, percent == 0 ? null : ScaleCustom);
        Set(prefs, kind == DlssKind.SR ? SrScaleId : RrScaleId, percent == 0 ? null : percent);
    }

    private static uint LatestId(DlssKind kind) => kind switch { DlssKind.SR => SrLatestId, DlssKind.RR => RrLatestId, DlssKind.FG => FgLatestId, _ => NrLatestId };
    public static bool DriverOverride(GamePreferences prefs, DlssKind kind) => Get(prefs, LatestId(kind)) == 1;
    public static void SetDriverOverride(GamePreferences prefs, DlssKind kind, bool enabled) => Set(prefs, LatestId(kind), enabled ? 1u : null);
    public static bool AnyDriverOverride(GamePreferences prefs) => DlssFiles.Dlls.Any(k => DriverOverride(prefs, k));

    public static void SetMfg(GamePreferences prefs, uint mode, uint count = 0, uint targetFps = 0)
    {
        Set(prefs, MfgModeId, mode == MfgOff ? null : mode);
        Set(prefs, MfgFactorId, mode == MfgFixed && count > 0 ? count : null);
        Set(prefs, MfgDynamicMaxId, mode == MfgDynamic && count > 0 ? count : null);
        Set(prefs, MfgTargetFpsId, mode == MfgDynamic && targetFps > 0 ? targetFps : null);
    }

    public static void Reset(GamePreferences prefs) { foreach (var id in Managed) Set(prefs, id, null); }
    public static bool IsDefault(GamePreferences prefs) => !Managed.Any(id => Has(prefs, id));

    // Proton only exposes NVAPI/NGX to the game when these are enabled; RHI owns the NGX updater
    // variable (used for the "NVIDIA Override" latest-DLL option) and adds NVAPI when DLSS is managed.
    public static LaunchExtras Extras(GamePreferences prefs, bool neuralRendering, IEnumerable<string>? dlls = null)
    {
        var environment = new Dictionary<string, string?> { ["PROTON_ENABLE_NGX_UPDATER"] = AnyDriverOverride(prefs) ? "1" : null };
        if (neuralRendering || !IsDefault(prefs)) environment["PROTON_ENABLE_NVAPI"] = "1";
        return new(dlls?.ToList() ?? [], environment, Managed.ToDictionary(id => id, id => Has(prefs, id) ? (uint?)Get(prefs, id) : null));
    }

    public static string Describe(uint id, uint value) => $"0x{id:X8}=0x{value.ToString("X", CultureInfo.InvariantCulture)}";
}
