// UlV4Rules.cs -- pure rules for ReLimiter v4 (unit tested in UlV4RulesTests).
// v4 needs an NVIDIA card, is 64-bit only, doesn't work in Vulkan / DXVK / OpenGL games and is built for DirectX 12.
// DX12 games move to v4 automatically; DX9/10/11 games can pick it in the ReLimiter cog; everything else stays on
// the last v3 release. Crossing between v3 and v4 gives the game a fresh relimiter.ini with only RHI's lines.

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
        if (is32Bit) return Blocked("ReLimiter v4 is 64-bit only. This game stays on v3.");
        if (vulkan) return Blocked("ReLimiter v4 doesn't work in Vulkan games. This game stays on v3.");
        if (dxvk) return Blocked("ReLimiter v4 doesn't work with DXVK, which runs the game through Vulkan. This game stays on v3.");
        if (openGl) return Blocked("ReLimiter v4 doesn't work in OpenGL games. This game stays on v3.");
        if (gpu == UlGpuState.NotNvidia) return Blocked("ReLimiter v4 needs an NVIDIA graphics card. This game stays on v3.");

        var notes = new List<string>();
        if (gpu == UlGpuState.Unknown)
            notes.Add("RHI couldn't detect your graphics card, so this game isn't moved to v4 automatically. v4 needs an NVIDIA card.");
        if (dx12 == UlDx12State.No)
            notes.Add("ReLimiter v4 is built for DirectX 12 and may not work as intended in this game.");
        else if (dx12 == UlDx12State.Unknown)
            notes.Add("RHI can't tell yet whether this game runs DirectX 12. It moves to v4 automatically once the game has run " +
                      "in DirectX 12 with ReShade, or set the Rendering API in Game Overrides.");

        bool auto = gpu == UlGpuState.Nvidia && dx12 == UlDx12State.Yes;
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

    /// <summary>
    /// A fresh relimiter.ini with only the lines RHI writes (OSD key, shared presets, DLSS info hooks, frame rate cap).
    /// ReLimiter fills in everything else with its own defaults on first start. Pure.
    /// </summary>
    internal static string BuildFreshIni(string? osdKey, bool sharedPresets, bool dlssHooks, int targetFps)
    {
        var sb = new StringBuilder();
        sb.Append("[FrameLimiter]\r\n");
        if (!string.IsNullOrWhiteSpace(osdKey)) sb.Append("osd_toggle_key=").Append(osdKey.Trim()).Append("\r\n");
        sb.Append("shared_presets=").Append(sharedPresets ? "true" : "false").Append("\r\n");
        sb.Append("dlss_info_hooks=").Append(dlssHooks ? "true" : "false").Append("\r\n");
        if (targetFps > 0) sb.Append("target_fps=").Append(targetFps).Append("\r\n");
        return sb.ToString();
    }
}
