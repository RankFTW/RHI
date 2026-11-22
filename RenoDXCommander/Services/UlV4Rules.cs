// UlV4Rules.cs -- pure rules for ReLimiter v4 (unit tested in UlV4RulesTests).
// v4 needs an NVIDIA card and is tested only in 64-bit DirectX 12. 64-bit DX12 games move to v4 automatically;
// every other game (DX9/10/11, Vulkan, DXVK, OpenGL, 32-bit) can pick it in the ReLimiter cog, where v4's untested
// fallback pacing applies, and otherwise stays on the last v3 release. Crossing between v3 and v4 gives the game a fresh relimiter.ini with only RHI's lines.

using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>What RHI knows about the user's graphics card for ReLimiter v4.</summary>
internal enum UlGpuState { Nvidia, NotNvidia, Unknown }

/// <summary>Whether a game runs DirectX 12, as far as RHI can tell.</summary>
internal enum UlDx12State { Yes, No, Unknown }

/// <summary>The API a game last rendered with, read from its ReShade.log.</summary>
internal enum UlLogApi { Dx12, Older, Vulkan }

/// <summary>
/// The v4 verdict for one game. <see cref="Allowed"/> = the user may pick v4; <see cref="Auto"/> = the game moves to
/// v4 by itself. <see cref="Reason"/> says why v4 is blocked; <see cref="Note"/> is shown when it's allowed with a caveat.
/// </summary>
internal sealed record UlV4Verdict(bool Allowed, bool Auto, string? Reason, string? Note);

internal static class UlV4Rules
{
    /// <summary>The first ReLimiter major version with the new rules.</summary>
    internal const int V4Major = 4;

    internal const string IniName = "relimiter.ini";

    /// <summary>Decides whether a game can use ReLimiter v4. Pure.</summary>
    internal static UlV4Verdict Check(bool is32Bit, bool vulkan, bool dxvk, bool openGl, UlGpuState gpu, UlDx12State dx12)
    {
        if (gpu == UlGpuState.NotNvidia) return Blocked("ReLimiter v4 needs an NVIDIA graphics card. This game stays on v3.");

        // Untested in v4 (it falls back to a plain cap or its untested routes): allowed, never automatic
        var notes = new List<string>();
        bool untested = is32Bit || vulkan || dxvk || openGl;
        if (is32Bit)
            notes.Add("ReLimiter v4 is untested in 32-bit games; it gives them a plain frame rate cap.");
        if (vulkan)
            notes.Add("ReLimiter v4 is untested in Vulkan games.");
        else if (dxvk)
            notes.Add("ReLimiter v4 is untested with DXVK, which runs the game through Vulkan.");
        if (openGl)
            notes.Add("ReLimiter v4 is untested in OpenGL games.");
        if (untested)
            notes.Add("You can try it here, but this game isn't moved to v4 automatically.");
        if (gpu == UlGpuState.Unknown)
            notes.Add("RHI couldn't detect your graphics card, so this game isn't moved to v4 automatically. v4 needs an NVIDIA card.");
        if (!untested && dx12 == UlDx12State.No)
            notes.Add("ReLimiter v4 is built for DirectX 12 and may not work as intended in this game.");
        else if (!untested && dx12 == UlDx12State.Unknown)
            notes.Add("RHI can't tell yet whether this game runs DirectX 12. It moves to v4 automatically once the game has run " +
                      "in DirectX 12 with ReShade, or set the Rendering API in Game Overrides.");

        bool auto = !untested && gpu == UlGpuState.Nvidia && dx12 == UlDx12State.Yes;
        return new UlV4Verdict(true, auto, null, notes.Count == 0 ? null : string.Join(" ", notes));

        static UlV4Verdict Blocked(string reason) => new(false, false, reason, null);
    }

    /// <summary>
    /// Whether the game should be on v4: "4" = the user picked v4 (only if allowed), "3" = the user picked v3,
    /// anything else = automatic (v4 when the verdict says so). Pure.
    /// </summary>
    internal static bool WantsV4(string? choice, UlV4Verdict verdict) => choice switch
    {
        "4" => verdict.Allowed,
        "3" => false,
        _   => verdict.Auto,
    };

    private static readonly Regex RlVersionRx = new(@"Registered add-on ""ReLimiter"" v(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex RlApiRx = new(@"\[ReLimiter\] on_present: captured presenting swapchain [^\r\n]*?\bapi=(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex DeviceRx = new(
        @"Redirecting (D3D12CreateDevice|D3D11On12CreateDevice|D3D11CreateDevice|D3D10CreateDevice|Direct3DCreate9|wglCreateContext|vkCreateDevice|vkCreateInstance)|(Running on )",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the API the game last rendered with from ReShade.log text. Pure. In order:
    /// (1) ReLimiter's own "on_present: captured presenting swapchain ... api=N" line: the API of the swap chain the
    /// game presents with (logged for DX11 / DX12 only). Its numbers depend on the ReLimiter version, which the log
    /// names ("Registered add-on "ReLimiter" vX"): before v3.3.1 None/DX11/DX12/Vulkan/OpenGL = 0-4, from v3.3.1
    /// None/DX9/DX11/DX12/Vulkan/OpenGL = 0-5 (ActiveAPI in ReLimiter's swapchain_manager.h). Any DX12 = DX12.
    /// (2) Only Vulkan devices / instances and no D3D or OpenGL device = Vulkan (e.g. an emulator).
    /// (3) The device each "Running on" line (ReShade starting to render) follows: DX12 when any of them follows a
    /// D3D12 device, else the API of the last one. "Any D3D12 device" is wrong: some DX11 games create a D3D12 device
    /// as a test first (ZERO PARADES), and add-ons such as DLSS 5 Bridge and GITC Uplift create one inside DX11 games
    /// after ReShade starts. Some DX12 games start with a DX11 splash window (Hogwarts Legacy: D3D11 RUN D3D12 RUN).
    /// D3D11On12CreateDevice is D3D12 (D3D11 on top of a D3D12 device), and so are the D3D11 lines that follow it
    /// (Nice Day for Fishing, BLACKWOOD). Known miss without ReLimiter's line: HSF makes its D3D12 device just
    /// before a DX11 game's swap chain (FINAL FANTASY XV).
    /// Without any "Running on" line: the last device. Null when the log shows nothing usable.
    /// </summary>
    internal static UlLogApi? ApiFromReShadeLog(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var ver = RlVersionRx.Match(text);
        if (ver.Success)
        {
            var v = new Version(int.Parse(ver.Groups[1].Value), int.Parse(ver.Groups[2].Value), int.Parse(ver.Groups[3].Value));
            bool oldEnum = v < new Version(3, 3, 1);
            int dx11 = oldEnum ? 1 : 2, dx12 = oldEnum ? 2 : 3;
            var apis = RlApiRx.Matches(text).Select(m => int.Parse(m.Groups[1].Value)).ToList();
            if (apis.Contains(dx12)) return UlLogApi.Dx12;
            if (apis.Contains(dx11)) return UlLogApi.Older;
        }

        UlLogApi? current = null, lastRun = null;
        bool anyDevice = false, onlyVulkan = true, in11On12 = false;
        foreach (Match m in DeviceRx.Matches(text))
        {
            if (m.Groups[2].Success) // "Running on": ReShade started rendering on the current device
            {
                if (current == UlLogApi.Dx12) return UlLogApi.Dx12;
                lastRun = current ?? lastRun;
                continue;
            }
            var fn = m.Groups[1].Value;
            anyDevice = true;
            bool vk = fn.StartsWith("vk", StringComparison.Ordinal);
            onlyVulkan &= vk;
            if (fn == "D3D11On12CreateDevice") { current = UlLogApi.Dx12; in11On12 = true; continue; }
            if (in11On12 && fn == "D3D11CreateDevice") continue; // part of the D3D11-on-12 device
            in11On12 = false;
            current = fn == "D3D12CreateDevice" ? UlLogApi.Dx12 : vk ? UlLogApi.Vulkan : UlLogApi.Older;
        }
        if (!anyDevice) return null;
        if (onlyVulkan) return UlLogApi.Vulkan;
        return lastRun ?? current;
    }

    /// <summary>Major version of a tag or file version ("v3.3.5", "4.0.0.0"), or null. Pure.</summary>
    internal static int? MajorOf(string? version) => ParseVersion(version)?.Major;

    /// <summary>"v3.3.5" / "3.3.5.0" → Version, or null. Pure.</summary>
    internal static Version? ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var s = version.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) s = s[..cut];
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>True when <paramref name="have"/> is older than <paramref name="target"/> (missing parts count as 0). Pure.</summary>
    internal static bool IsOlder(Version have, Version target) => Normal(have) < Normal(target);

    /// <summary>True when two versions are the same (missing parts count as 0: "v4.0.0" = 4.0.0.0). Pure.</summary>
    internal static bool SameVersion(Version a, Version b) => Normal(a) == Normal(b);

    private static Version Normal(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>A file version as shown on the card: "4.0.0.0" → "4.0.0", "3.3.5.1" → "3.3.5.1". Pure.</summary>
    internal static string DisplayVersion(Version v) =>
        v.Revision > 0 ? v.ToString(4) : v.Build >= 0 ? v.ToString(3) : v.ToString(2);

    /// <summary>The newest release tag with the given major version (by version number, not date), or null. Pure.</summary>
    internal static string? NewestTagWithMajor(IEnumerable<string> tags, int major) =>
        tags.Select(t => (Tag: t, Ver: ParseVersion(t)))
            .Where(x => x.Ver != null && x.Ver.Major == major)
            .OrderByDescending(x => x.Ver)
            .Select(x => x.Tag)
            .FirstOrDefault();

    /// <summary>Name of the backup made when a game's relimiter.ini is replaced on a major version change. Pure.</summary>
    internal static string BackupName(int oldMajor) => $"{IniName}.v{oldMajor}-backup";

    // ── Seeding a game's relimiter.ini from the user's template ──────────────────
    // Keys only one major version reads (from ReLimiter's config.cpp, v3.3.5 vs v4.0.1, Nov 2026). Everything else is
    // shared, or is a v3 pacing key v4 already ignores and drops itself (its kRemovedKeys). Matched in every section
    // (the OSD preset sections carry the show_* keys).

    /// <summary>Keys v3 reads and v4 doesn't: left out when a v4 game is seeded from the template.</summary>
    internal static readonly HashSet<string> V3OnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "adaptive_smoothing", "osd_show_adaptive_smoothing", "smoothing_percentile", "smoothing_window", "smoothing_bias_us",
        "dmfg_output_cap", "osd_show_gpu_power", "show_gpu_power", "osd_show_pqi_breakdown", "show_pqi_breakdown",
    };

    /// <summary>Keys v4 reads and v3 doesn't: left out when a v3 game is seeded from the template.</summary>
    internal static readonly HashSet<string> V4OnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "advanced_settings", "config_version", "control_file", "developer_settings", "pacing_preference", "pacing_test_mode",
        "native_pcl_enabled", "hang_report", "hang_report_dump", "hang_report_test_after_s", "hook_installer_thread",
        "flip_metering_off", "lead_queue_correction",
        "fg_flip_gate_give_back", "fg_present_ahead", "fg_present_order", "fg_present_order_trailing",
        "fg_render_break_every", "fg_render_break_window", "fg_separate_presenter_hold",
        "gpu_present_gate_probes", "gpu_queue_probe", "gpu_queue_probe_source", "gpu_wait_ab_interleave_s",
        "gpu_wait_late_guard", "gpu_wait_present_first", "gpu_wait_start_limit",
        "unified_hold_share", "unified_queue_allow_us", "unified_queue_law", "unified_queue_margin_end",
        "unified_queue_need_frames", "unified_queue_rank", "unified_queue_upstream",
        "osd_show_jitter", "show_jitter", "osd_show_native_pcl", "show_native_pcl", "osd_show_native_pcl_breakdown",
        "show_native_pcl_breakdown", "osd_show_pacing_quality", "show_pacing_quality",
    };

    /// <summary>The template's lines without the keys the other major version uses (comments and sections kept). Pure.</summary>
    internal static List<string> StripOtherVersionKeys(IEnumerable<string> lines, int major)
    {
        var drop = major >= V4Major ? V3OnlyKeys : V4OnlyKeys;
        var result = new List<string>();
        foreach (var line in lines)
        {
            var t = line.TrimStart();
            int eq = t.IndexOf('=');
            if (eq > 0 && !t.StartsWith(';') && !t.StartsWith('#') && drop.Contains(t[..eq].Trim())) continue;
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// A game's relimiter.ini from the user's template (Settings' relimiter.ini in RHI's inis folder): the template
    /// without the other major version's keys, plus RHI's settings: shared presets / DLSS info / frame rate cap when
    /// they're on, the OSD key when the template has none, and for v4 native PC Latency. No template (or an empty
    /// one) = <see cref="BuildFreshIni"/>. Pure.
    /// </summary>
    internal static string BuildSeededIni(string? template, int major, string? osdKey, bool sharedPresets, bool dlssHooks,
        int targetFps, bool nativePcl = false)
    {
        bool v4 = major >= V4Major;
        var lines = (template ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        if (!lines.Any(l => { var t = l.Trim(); return t.Contains('=') && !t.StartsWith(';') && !t.StartsWith('#'); }))
            return BuildFreshIni(osdKey, sharedPresets, dlssHooks, targetFps, nativePcl && v4);

        lines = StripOtherVersionKeys(lines, major);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        const string S = "FrameLimiter";
        if (!string.IsNullOrWhiteSpace(osdKey) && !HasKey(lines, S, "osd_toggle_key"))
            lines = ReLimiterPresetKeys.SetKey(lines, S, "osd_toggle_key", osdKey.Trim());
        if (sharedPresets) lines = ReLimiterPresetKeys.SetKey(lines, S, "shared_presets", "true");
        if (dlssHooks) lines = ReLimiterPresetKeys.SetKey(lines, S, "dlss_info_hooks", "true");
        if (targetFps > 0) lines = ReLimiterPresetKeys.SetKey(lines, S, "target_fps", targetFps.ToString());
        if (v4 && nativePcl) lines = ReLimiterPresetKeys.SetKey(lines, S, "native_pcl_enabled", "true");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static bool HasKey(List<string> lines, string section, string key)
    {
        string? current = null;
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { current = t[1..^1].Trim(); continue; }
            int eq = t.IndexOf('=');
            if (eq > 0 && string.Equals(current, section, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// A fresh relimiter.ini with only the lines RHI writes (OSD key, shared presets, DLSS info hooks, frame rate cap,
    /// and for v4 native PC Latency on). ReLimiter fills in everything else with its own defaults on first start. Pure.
    /// </summary>
    internal static string BuildFreshIni(string? osdKey, bool sharedPresets, bool dlssHooks, int targetFps, bool nativePcl = false)
    {
        var sb = new StringBuilder();
        sb.Append("[FrameLimiter]\r\n");
        if (!string.IsNullOrWhiteSpace(osdKey)) sb.Append("osd_toggle_key=").Append(osdKey.Trim()).Append("\r\n");
        sb.Append("shared_presets=").Append(sharedPresets ? "true" : "false").Append("\r\n");
        sb.Append("dlss_info_hooks=").Append(dlssHooks ? "true" : "false").Append("\r\n");
        if (targetFps > 0) sb.Append("target_fps=").Append(targetFps).Append("\r\n");
        // v4 only reads PC Latency with this on (and the account in Performance Log Users, Settings › Components)
        if (nativePcl) sb.Append("native_pcl_enabled=true\r\n");
        return sb.ToString();
    }
}
