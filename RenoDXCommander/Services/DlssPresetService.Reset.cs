using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    // ── Restore Profile Defaults ──────────────────────────────────────────────

    /// <summary>
    /// Restores a game's NVIDIA driver profile to factory defaults.
    /// All user-modified settings are removed. If the profile was user-created (not predefined by NVIDIA),
    /// the profile is deleted entirely. Reloads the session afterward to update cached profiles.
    /// </summary>
    /// <returns>True if restore succeeded, false on failure or if no profile found.</returns>
    public bool RestoreProfileDefaults(string gameName, string installPath)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null)
            {
                CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] No profile found for '{gameName}'");
                return false;
            }

            EnsureNativeFunctions();

            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(profile.Handle);

            if (sessionHandle == IntPtr.Zero || profileHandle == IntPtr.Zero)
            {
                CrashReporter.Log("[DlssPresetService.RestoreProfileDefaults] Could not resolve handles");
                return false;
            }

            // Try raw NvAPI_DRS_RestoreProfileDefault first
            if (_nativeRestoreProfileDefault != null)
            {
                int result = _nativeRestoreProfileDefault(sessionHandle, profileHandle);
                CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] NvAPI_DRS_RestoreProfileDefault returned {result} for '{gameName}'");

                if (result == 0 || result == -183) // 0 = success, -183 = PROFILE_REMOVED (user-created)
                {
                    if (_nativeSaveSettings != null)
                        _nativeSaveSettings(sessionHandle);

                    ReloadSession();
                    CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] Restored defaults for '{gameName}'");
                    return true;
                }
            }
            else
            {
                CrashReporter.Log("[DlssPresetService.RestoreProfileDefaults] _nativeRestoreProfileDefault is null, trying DeleteProfile...");
            }

            // Fallback: delete all user-modified settings on the profile individually
            // This preserves the profile and its applications but removes user overrides
            try
            {
                var settingsToDelete = new List<uint>();
                try
                {
                    foreach (var setting in profile.Settings)
                    {
                        settingsToDelete.Add(setting.SettingId);
                    }
                }
                catch (Exception enumEx)
                {
                    CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] Settings enumeration failed (binary settings?) — {enumEx.Message}");
                    // If enumeration fails, delete all our managed setting IDs as a best effort
                    settingsToDelete.AddRange(ManagedSettingIds);
                    settingsToDelete.AddRange(new uint[] { MFG_MODE_OVERRIDE_ID, MFG_GENERATION_FACTOR_ID, MFG_DYNAMIC_MAX_COUNT_ID, MFG_DYNAMIC_TARGET_FPS_ID });
                }

                int deleted = 0;
                foreach (var settingId in settingsToDelete)
                {
                    try { profile.DeleteSetting(settingId); deleted++; } catch { }
                }

                // Also explicitly reset settings that are invisible to NvAPIWrapper's enumeration
                // (newer settings written via raw NVAPI — Smooth Motion, ULL, MFG)
                // These can't be deleted via profile.DeleteSetting — use SetSettingRawNvApi to set them to 0
                var invisibleSettings = new uint[]
                {
                    SMOOTH_MOTION_ENABLE_ID, SMOOTH_MOTION_APIS_ID,
                    SMOOTH_MOTION_FLIP_PACING_FS_ID, SMOOTH_MOTION_FLIP_PACING_WIN_ID,
                    LATENCY_ULL_ENABLE_ID, LATENCY_ULL_MODE_ID,
                    MFG_MODE_OVERRIDE_ID, MFG_GENERATION_FACTOR_ID,
                    MFG_DYNAMIC_MAX_COUNT_ID, MFG_DYNAMIC_TARGET_FPS_ID,
                };
                var sessionH = GetHandlePtr(_session.Handle);
                // Re-find the profile after potential reload — handles may have changed
                var freshProfile = FindProfile(gameName, installPath);
                if (freshProfile != null && sessionH != IntPtr.Zero)
                {
                    var profileH = GetHandlePtr(freshProfile.Handle);
                    if (profileH != IntPtr.Zero)
                    {
                        foreach (var settingId in invisibleSettings)
                        {
                            try { SetSettingRawNvApi(sessionH, profileH, settingId, 0); } catch { }
                        }
                        if (_nativeSaveSettings != null)
                            _nativeSaveSettings(sessionH);
                    }
                }

                _session.Save();
                ReloadSession();
                CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] Deleted {deleted} settings for '{gameName}'");
                return true;
            }
            catch (Exception delEx)
            {
                CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] Setting deletion failed — {delEx.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.RestoreProfileDefaults] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Resets ALL per-game NVIDIA profiles to factory defaults AND clears global base profile overrides.
    /// Iterates all games in RHI and calls RestoreProfileDefaults on each, then resets the base profile.
    /// Returns the count of profiles successfully reset.
    /// </summary>
    public int ResetAllProfiles(IEnumerable<(string GameName, string InstallPath)> games)
    {
        if (!_isSupported || _session == null) return 0;
        int count = 0;
        foreach (var (gameName, installPath) in games)
        {
            try
            {
                if (RestoreProfileDefaults(gameName, installPath))
                    count++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.ResetAllProfiles] Failed for '{gameName}' — {ex.Message}");
            }
        }

        ResetGlobalProfile();
        return count;
    }

    /// <summary>
    /// Resets the global/base NVIDIA profile settings to factory defaults.
    /// Clears Shader Cache, G-Sync, Refresh Rate, ReBAR, DLSS overrides from the base profile.
    /// </summary>
    public void ResetGlobalProfile()
    {
        if (!_isSupported || _session == null) return;
        try
        {
            var baseProfile = _session.BaseProfile;
            // Delete all known managed global settings from the base profile
            var globalSettingIds = new uint[]
            {
                // DLSS Latest DLL + presets (global overrides from NVIDIA App)
                DLSS_SR_LATEST_DLL_ID,     // 0x10E41E01
                DLSS_RR_LATEST_DLL_ID,     // 0x10E41E02
                DLSS_FG_LATEST_DLL_ID,     // 0x10E41E03
                NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, // 0x10E41DF3
                NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, // 0x10E41DF7
                NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID, // 0x10E41DF1
                NGX_DLSS_SR_RENDER_SCALE_ID,    // 0x10AFB768
                NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, // 0x10E41DF5
                NGX_DLSS_RR_RENDER_SCALE_ID,    // 0x10BD9423
                NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID, // 0x10C7D4A2
                MFG_MODE_OVERRIDE_ID,           // 0x10308298
                MFG_GENERATION_FACTOR_ID,       // 0x104D6667
                MFG_DYNAMIC_MAX_COUNT_ID,       // 0x10562D0F
                MFG_DYNAMIC_TARGET_FPS_ID,      // 0x10CF4125
                // Global settings (Shader Cache, G-Sync, Refresh Rate, ReBAR)
                SHADER_CACHE_SIZE_ID,           // 0x00AC8497
                GSYNC_APP_MODE_ID,              // 0x1194F158
                GSYNC_GLOBAL_MODE_ID,           // 0x1094F1F7
                PREFERRED_REFRESH_RATE_ID,      // 0x0064B541
                REBAR_FEATURE_ID,               // 0x000F00BA
                REBAR_EXPR_MODES_ID,            // 0x00C09D09
                REBAR_SIZE_LIMIT_ID,            // 0x000F00FF
            };
            int globalDeleted = 0;
            foreach (var id in globalSettingIds)
            {
                try { baseProfile.DeleteSetting(id); globalDeleted++; } catch { }
            }

            // Shader Pre-Compile needs raw API (DeleteSetting can't reach it on the base profile)
            // Write the default value (0x01 = Low) explicitly
            var sessionHandle = GetHandlePtr(_session.Handle);
            var baseHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && baseHandle != IntPtr.Zero)
            {
                SetSettingRawNvApi(sessionHandle, baseHandle, SHADER_PRECOMPILE_ID, 0x00000001); // Low (default)
            }

            _session.Save();
            ReloadSession();
            CrashReporter.Log($"[DlssPresetService.ResetGlobalProfile] Cleared {globalDeleted} global base profile settings + reset Shader Pre-Compile");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.ResetGlobalProfile] Global profile reset failed — {ex.Message}");
        }
    }

    private void ReloadSession()
    {
        try
        {
            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _session.Profiles)
                _cachedProfiles.TryAdd(p.Name, p);
            InvalidateProfileLookupCache();
        }
        catch (Exception reloadEx)
        {
            CrashReporter.Log($"[DlssPresetService.ReloadSession] Session reload failed — {reloadEx.Message}");
        }
    }

}
