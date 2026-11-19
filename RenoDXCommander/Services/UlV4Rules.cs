// UlV4Rules.cs -- pure rules for ReLimiter v4 (unit tested in UlV4RulesTests).
// v4 needs an NVIDIA card, is 64-bit only, doesn't work in Vulkan / DXVK / OpenGL games and is built for DirectX 12.
// DX12 games move to v4 automatically; DX9/10/11 games can pick it in the ReLimiter cog; everything else stays on
// the last v3 release. Crossing between v3 and v4 gives the game a fresh relimiter.ini with only RHI's lines.

using System.Text;

namespace RenoDXCommander.Services;

/// <summary>What RHI knows about the user's graphics card for ReLimiter v4.</summary>
internal enum UlGpuState { Nvidia, NotNvidia, Unknown }

/// <summary>Whether a game runs DirectX 12, as far as RHI can tell.</summary>
internal enum UlDx12State { Yes, No, Unknown }

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

    /// <summary>
    /// Reads which API the game last started on from ReShade.log text. Any D3D12 device = DX12 (DX12 games often
    /// open a D3D11 or D3D9 device on the side for video or UI). Otherwise a D3D11 / D3D10 / D3D9 / OpenGL device
    /// = not DX12. Null when the log shows no device (the game hasn't run with ReShade, or it's a Vulkan layer log). Pure.
    /// </summary>
    internal static UlDx12State? ApiFromReShadeLog(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Contains("Redirecting D3D12CreateDevice", StringComparison.Ordinal)) return UlDx12State.Yes;
        if (text.Contains("Redirecting D3D11CreateDevice", StringComparison.Ordinal)
            || text.Contains("Redirecting D3D10CreateDevice", StringComparison.Ordinal)
            || text.Contains("Redirecting Direct3DCreate9", StringComparison.Ordinal)
            || text.Contains("Redirecting wglCreateContext", StringComparison.Ordinal))
            return UlDx12State.No;
        return null;
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
