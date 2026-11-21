using System.Globalization;

namespace RenoDXCommander.Services;

/// <summary>
/// Per-game texture filtering settings: texture LOD bias, anisotropic filtering and
/// texture filtering quality. IDs and values match NVIDIA Profile Inspector.
/// </summary>
public partial class DlssPresetService
{
    // LOD bias (DX). Value = bias x 8 as a signed int (-1.0 = 0xFFFFFFF8).
    private const uint LOD_BIAS_DX_ID = 0x00738E8F;
    // Transparency supersampling (AA_MODE_REPLAY). DX10+ games ignore the LOD bias unless this is set;
    // 0x08 = replay mode "all" (Profile Inspector: "Full Transparency AA (DLSS Mods)").
    private const uint TRANSPARENCY_SS_ID = 0x10D48A85;
    private const uint TRANSPARENCY_SS_LOD_VALUE = 0x00000008;
    // Negative LOD bias: Allow = 0, Clamp = 1
    private const uint LOD_BIAS_NEGATIVE_ID = 0x0019BB68;
    // Driver-controlled LOD bias (auto, for SGSSAA): Off = 0
    private const uint LOD_BIAS_AUTO_ID = 0x00638E8F;

    // Anisotropic filtering: mode App-controlled = 0 / Override = 1, level 2/4/8/16
    private const uint ANISO_MODE_ID = 0x10D2BB16;
    private const uint ANISO_LEVEL_ID = 0x101E61A9;

    // Texture filtering quality: High Quality = 0xFFFFFFF6, Quality = 0, Performance = 0x0A, High Performance = 0x14
    private const uint TEXTURE_QUALITY_ID = 0x00CE2691;
    // Trilinear optimization: On = 0, Off = 1 (the control panel turns it off with High Quality)
    private const uint TRILINEAR_OPT_ID = 0x002ECAF2;
    private const uint TEXTURE_QUALITY_HIGH = 0xFFFFFFF6;

    /// <summary>LOD bias choices in eighths (-24 = -3.000 … +8 = +1.000). null = Default (not set).</summary>
    public static readonly (string Name, int? Eighths)[] LodBiasOptions = BuildLodBiasOptions();

    private static (string Name, int? Eighths)[] BuildLodBiasOptions()
    {
        var list = new List<(string, int?)> { ("Default", null) };
        for (int e = -24; e <= 8; e++)
        {
            var name = FormatLodBias(e);
            // Rough starting points: log2(render width / output width)
            name += e switch
            {
                -5  => "  (DLSS Quality)",
                -6  => "  (DLSS Balanced)",
                -8  => "  (DLSS Performance)",
                -13 => "  (DLSS Ultra Performance)",
                _   => "",
            };
            list.Add((name, e));
        }
        return list.ToArray();
    }

    /// <summary>"-0.625", "+0.000", "+1.000".</summary>
    public static string FormatLodBias(int eighths)
        => (eighths >= 0 ? "+" : "") + (eighths / 8.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>Anisotropic filtering choices. 0 = App controlled (not set).</summary>
    public static readonly (string Name, uint Level)[] AnisoOptions =
    [
        ("App Controlled", 0),
        ("2x", 2),
        ("4x", 4),
        ("8x", 8),
        ("16x", 16),
    ];

    /// <summary>Texture filtering quality choices. null = Default (not set, the driver uses Quality).</summary>
    public static readonly (string Name, uint? Value)[] TextureQualityOptions =
    [
        ("Default", null),
        ("High Quality", TEXTURE_QUALITY_HIGH),
        ("Quality", 0x00000000),
        ("Performance", 0x0000000A),
        ("High Performance", 0x00000014),
    ];

    // ── LOD bias ─────────────────────────────────────────────────────────────

    /// <summary>The game's own LOD bias in eighths, or null when it has none (Default).</summary>
    public int? GetLodBias(string gameName, string installPath)
        => GetOwnPreset(gameName, installPath, LOD_BIAS_DX_ID) is uint v ? unchecked((int)v) : null;

    /// <summary>
    /// Sets the texture LOD bias (in eighths), together with the settings it needs to take effect:
    /// transparency supersampling "all" (DX10+ games ignore the bias without it), negative bias allowed
    /// and the automatic bias off. null removes all of them. A transparency supersampling value the user
    /// set themselves (anything other than ours) is left alone.
    /// </summary>
    public bool SetLodBias(string gameName, string installPath, int? eighths)
    {
        try
        {
            var ownReplay = GetOwnPreset(gameName, installPath, TRANSPARENCY_SS_ID);
            if (eighths is not int e)
            {
                bool ok = DeleteOwn(gameName, installPath, LOD_BIAS_DX_ID);
                if (ownReplay == TRANSPARENCY_SS_LOD_VALUE)
                    ok &= DeleteOwn(gameName, installPath, TRANSPARENCY_SS_ID);
                ok &= DeleteOwn(gameName, installPath, LOD_BIAS_NEGATIVE_ID);
                ok &= DeleteOwn(gameName, installPath, LOD_BIAS_AUTO_ID);
                CrashReporter.Log($"[DlssPresetService.SetLodBias] '{gameName}' → Default ({(ok ? "ok" : "failed")})");
                return ok;
            }

            e = Math.Clamp(e, -24, 24);
            bool set = SetPreset(gameName, installPath, LOD_BIAS_DX_ID, unchecked((uint)e));
            if (ownReplay is null or 0)
                set &= SetPreset(gameName, installPath, TRANSPARENCY_SS_ID, TRANSPARENCY_SS_LOD_VALUE);
            set &= SetPreset(gameName, installPath, LOD_BIAS_NEGATIVE_ID, 0);
            set &= SetPreset(gameName, installPath, LOD_BIAS_AUTO_ID, 0);
            CrashReporter.Log($"[DlssPresetService.SetLodBias] '{gameName}' → {FormatLodBias(e)} ({(set ? "ok" : "failed")})");
            return set;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetLodBias] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    // ── Anisotropic filtering ────────────────────────────────────────────────

    /// <summary>The forced anisotropic level (2/4/8/16), or 0 when the game controls it.</summary>
    public uint GetAnisoLevel(string gameName, string installPath)
    {
        if (GetOwnPreset(gameName, installPath, ANISO_MODE_ID) != 1) return 0;
        return GetOwnPreset(gameName, installPath, ANISO_LEVEL_ID) ?? 0;
    }

    /// <summary>Forces anisotropic filtering to <paramref name="level"/>; 0 hands it back to the game.</summary>
    public bool SetAnisoLevel(string gameName, string installPath, uint level)
    {
        bool ok;
        if (level == 0)
        {
            ok = DeleteOwn(gameName, installPath, ANISO_MODE_ID);
            ok &= DeleteOwn(gameName, installPath, ANISO_LEVEL_ID);
        }
        else
        {
            ok = SetPreset(gameName, installPath, ANISO_MODE_ID, 1);
            ok &= SetPreset(gameName, installPath, ANISO_LEVEL_ID, level);
        }
        CrashReporter.Log($"[DlssPresetService.SetAnisoLevel] '{gameName}' → {(level == 0 ? "App Controlled" : $"{level}x")} ({(ok ? "ok" : "failed")})");
        return ok;
    }

    // ── Texture filtering quality ────────────────────────────────────────────

    /// <summary>The game's own texture filtering quality, or null when it has none (Default).</summary>
    public uint? GetTextureQuality(string gameName, string installPath)
        => GetOwnPreset(gameName, installPath, TEXTURE_QUALITY_ID);

    /// <summary>
    /// Sets texture filtering quality. High Quality also turns trilinear optimization off, as the NVIDIA
    /// Control Panel does; other choices remove that. null removes both.
    /// </summary>
    public bool SetTextureQuality(string gameName, string installPath, uint? value)
    {
        bool ok = value is uint v
            ? SetPreset(gameName, installPath, TEXTURE_QUALITY_ID, v)
            : DeleteOwn(gameName, installPath, TEXTURE_QUALITY_ID);
        ok &= value == TEXTURE_QUALITY_HIGH
            ? SetPreset(gameName, installPath, TRILINEAR_OPT_ID, 1)
            : DeleteOwn(gameName, installPath, TRILINEAR_OPT_ID);
        CrashReporter.Log($"[DlssPresetService.SetTextureQuality] '{gameName}' → {(value is uint q ? $"0x{q:X8}" : "Default")} ({(ok ? "ok" : "failed")})");
        return ok;
    }

    /// <summary>
    /// Removes a setting only when the game has its own value. DeletePreset writes 0 when the delete
    /// fails, and a missing setting fails, so calling it on an unset setting would create a 0 value.
    /// </summary>
    private bool DeleteOwn(string gameName, string installPath, uint settingId)
        => GetOwnPreset(gameName, installPath, settingId) == null || DeletePreset(gameName, installPath, settingId);
}
