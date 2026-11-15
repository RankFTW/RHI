// DlssEnablerConfig.cs -- DLSS Enabler's settings file (dlss-enabler.ini, beside its DLL), read and edited one value
// at a time so its comments stay. DLSS Enabler writes the file itself ("auto-generated, manual edits may be
// overwritten"), so RHI only changes single values, with the game closed.
// DefaultTemplate is the file DLSS Enabler 4.10.0.23 generated (SILENT HILL: Townfall, Nov 2026) with every value at
// its default; configVersion tells newer DLSS Enabler versions which schema it is, so they migrate it.

using System.Text;

namespace RenoDXCommander.Services;

public static class DlssEnablerConfig
{
    public const string FileName = "dlss-enabler.ini";

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    /// <summary>Reads <c>[section] key</c> (trimmed), or null when the file, section or key is missing.</summary>
    public static string? Get(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        return GetFromText(File.ReadAllText(path), section, key);
    }

    /// <summary>Sets <c>[section] key=value</c>, keeping every other line. Adds the key (or section) when missing.</summary>
    public static void Set(string path, string section, string key, string value)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        WriteAtomic(path, SetInText(text, section, key, value));
    }

    /// <summary>Writes the default settings file when there is none. Returns true when it created one.</summary>
    public static bool CreateIfMissing(string folder)
    {
        var path = PathIn(folder);
        if (File.Exists(path)) return false;
        WriteAtomic(path, DefaultTemplate.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        return true;
    }

    /// <summary>UTF-8 without BOM (as DLSS Enabler writes it), via a temp file. Throws on failure so callers can show it.</summary>
    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".rhi-tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    // ── Text editing (pure, unit tested) ───────────────────────────────────────

    internal static string? GetFromText(string text, string section, string key)
    {
        string? current = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1].Trim(); continue; }
            if (current == null || !current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(eq + 1)..].Trim();
        }
        return null;
    }

    internal static string SetInText(string text, string section, string key, string value)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : (text.Length == 0 ? "\r\n" : "\n");
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        bool trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);

        int sectionStart = -1, lastKeyLine = -1;
        string? current = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (sectionStart >= 0) break; // left our section
                current = line[1..^1].Trim();
                if (current.Equals(section, StringComparison.OrdinalIgnoreCase)) { sectionStart = i; lastKeyLine = i; }
                continue;
            }
            if (sectionStart < 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            lastKeyLine = i;
            if (line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                return Join(lines, nl, trailingNewline);
            }
        }

        if (sectionStart >= 0) lines.Insert(lastKeyLine + 1, $"{key}={value}");
        else
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        return Join(lines, nl, true);
    }

    private static string Join(List<string> lines, string nl, bool trailingNewline)
    {
        var sb = new StringBuilder(string.Join(nl, lines));
        if (trailingNewline) sb.Append(nl);
        return sb.ToString();
    }

    // ── Default file (DLSS Enabler 4.10.0.23) ──────────────────────────────────

    internal const string DefaultTemplate = """
; =============================================================================
; DLSS Enabler Settings
; This file is auto-generated. Manual edits may be overwritten.
; =============================================================================

[Meta]
; Schema version, taken from the DLL that wrote this file. Delete this line to force a one-time settings migration on next launch.
configVersion=4.10.0.23

[NGX]
; Enable screen-space ray tracing (SSRTGI master switch). Needs D3D12 + active DLSS upscaling to take effect.
ScreenSpaceRayTracing=false
; Enable the Analytical Rendering layer ([NearField], [Grade]) on top of SSRTGI. Off restores plain SSRTGI.
NeuralRendering=false

; Enable ray-traced global illumination.
GlobalIllumination=true
; Enable ray-traced ambient occlusion.
AmbientOcclusion=true
; SSRTGI quality: 0 = ULTRA, 1 = HIGH, 2 = MEDIUM, 3 = LOW. Ignored while Overdrive is active.
RayTracingQuality=1
; SSRTGI ray length / effective range, 1-100.
RayTracingRange=50
; Global illumination intensity, 1-100 % (50 = default).
GiIntensity=50
; Ambient occlusion intensity, 1-100 % (50 = default).
AoIntensity=50
; HUD detection: 0 = GAME, 1 = OFF, 2 = ON.
HudDetectionMode=0

[NearField]
; Analytical Rendering on objects close to the camera. Only active while NGX.NeuralRendering is true.
; [0 .. 100] Contrast of ambient occlusion on near objects, % (50 = default)
AoContrast=50
; [0 .. 100] Shadows a surface casts on itself (creases, under a brim), % (50 = default)
SelfShadowIntensity=50
; [0 .. 100] How far a surface looks for what shades it, % (50 = default)
SelfShadowReach=50
; [0 .. 100] Higher keeps only deep creases, lower spreads wider, % (50 = default)
SelfShadowFocus=50
; [0 .. 100] Deepening of light-to-dark gradients across forms, % (50 = default)
FormDepth=50
; [0 .. 100] Size of the forms it works on, % (50 = default)
FormScale=50
; [0 .. 100] Fine-detail sharpening, flat areas only, % (50 = default)
Sharpening=50
; [0 .. 100] Light-direction shading of fine surface texture, % (50 = default)
Relief=50

[Grade]
; Colour grade, applied last to the whole frame. Only active while NGX.NeuralRendering is true.
; Master switch for the colour grade.
Enable=false
; [-100 .. 100] Negative cools, positive warms, % (0 = neutral)
Temperature=1
; [-100 .. 100] Negative green, positive magenta, % (0 = neutral)
Tint=0
; [-100 .. 100] Overall S-curve contrast, % (0 = neutral)
Contrast=-52
; [0 .. 100] How much saturation yellows lose, %
YellowReduction=52

[Reflex]
; Override the game's V-Sync setting with the Vsync value below.
VsyncOverride=false
; V-Sync state applied when VsyncOverride is enabled.
Vsync=false
; Enable the Reflex frame-rate limiter (uses DesiredFpsLimit).
FpsLimit=false
; Override the game's Reflex Boost setting with the Boost value below.
BoostOverride=false
; Reflex Boost state applied when BoostOverride is enabled.
Boost=false
; Target FPS cap used when FpsLimit is enabled; range 30-360.
DesiredFpsLimit=60

[UI]
; Show the performance monitoring overlay.
Monitoring=true
; Show the settings sidebar panel.
SideBar=true
; Menu toggle key. Any Win32 virtual-key code works.
; Accepts a named key (Tilde, F1-F12, VK_INSERT, Numpad0-9, VK_OEM_1..7, VK_TAB...), a single A-Z / 0-9 char, hex 0xNN, or decimal 1-255. Default: Tilde.
; See: https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes
ToggleKey=Tilde
; UI scale override; 0 = auto. Multiplier of base UI size, e.g. 1.5 = +50% larger, 2.0 = double, 1.0 = no change.
ScaleOverride=0

[DeepDVC]
; DeepDVC effect intensity (0.0-1.0).
Intensity=0.5
; DeepDVC saturation boost (0.0-1.0).
SaturationBoost=0.75

[Performance]
; Overdrive preset: 0 = OFF, 1 = PERF, 2 = QUALITY, 3 = LATENCY.
OverdriveMode=0
; Forced MFG multiplier: 0 = OFF (game controls), 1 = x1 (FG off), 2 = x2, 3 = x3, 4 = x4, 5 = x5, 6 = x6. x5/x6 require sl.dlss_g >= 2.11.
MFGOverrideMode=0
; Enable hotkeys for switching MFG modes at runtime.
MFGHotkeys=false
; Enable dynamic MFG (auto-select the multiplier from base FPS).
DynamicMFG=false
; Dynamic MFG thresholds (used only in CUSTOM mode, DFGMode=1). Base FPS BELOW a threshold selects that
; multiplier; lower thresholds win (x6 is checked first). Range 10-200 FPS, min 5 FPS gap between them,
; order must hold: Threshold6 <= Threshold5 <= Threshold4 <= Threshold3 <= Threshold2.
; In AUTO mode (DFGMode=0) these are recomputed as ceil(DFGTargetFps / N) and ignored on load.
; Base FPS below this -> 2X (default 90).
DynamicMFGThreshold2=90
; Base FPS below this -> 3X (default 60).
DynamicMFGThreshold3=60
; Base FPS below this -> 4X (default 30).
DynamicMFGThreshold4=30
; Base FPS below this -> 5X (default 24).
DynamicMFGThreshold5=15
; Base FPS below this -> 6X (default 20).
DynamicMFGThreshold6=5
; Dynamic FG threshold source: 0 = AUTO (thresholds from DFGTargetFps), 1 = CUSTOM (manual thresholds above).
DFGMode=0
; Target output FPS for DFG AUTO mode; range 30-300.
DFGTargetFps=120
; DFG Instinct: raise the multiplier during action to reduce latency.
DFGInstinct=false
; DFG Instinct base-FPS floor; range 30-240. Won't raise the multiplier if output FPS would drop below this.
DFGMinFps=30
; Force-load the DLSSG module even when the game does not request it.
ForceLoadDLSSG=false

[Debug]
; Raw MFG debug/feature flags, stored as a decimal bitmask. Anti-ghosting bits are stripped and kept in [GhostBuster].
; Feature bits:  0x02000000 DLSSG.UI as UI mask (DyingLight 2 only) | 0x04000000 temporal HUD pin | 0x08000000 HUD optical-flow interp |
;                0x10000000 ignore DLSSG.UI texture | 0x40000000 pin DLSSG.Backbuffer to subframe-1.
; Debug viz:     0x00010000 frame-index line | 0x00020000 HUD-detection | 0x00040000 disocclusion tint |
;                0x00080000 artifacts detection | 0x00800000 camera-MV fallback tint | 0x01000000 trapezoid-zone viz.
Flags=0
; Initialize OptiScaler early during startup.
EarlyInit=false
; Force FSR-only frame generation, bypassing DLSSG.
UseFsrOnly=false
; Force hybrid MFG on; false = AUTO.
HybridMfgForced=false
; Disable the in-game overlay UI (always written as false by the app).
DisableUI=false

[GhostBuster]
; Enable GhostBuster anti-ghosting correction (maps to the ANTIGHOSTING_ENABLE flag bit).
Enabled=true
; GhostBuster debug visualization mode; 0 = off.
DebugMode=0
""";
}
