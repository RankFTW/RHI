using System.Runtime.InteropServices;
using System.Text.Json;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    // ── Profile Export/Import ──────────────────────────────────────────────────

    /// <summary>All RHI-managed setting IDs for export/import.</summary>
    private static readonly uint[] ManagedSettingIds =
    [
        VSYNC_MODE_ID, VSYNC_TEAR_CONTROL_ID,
        POWER_MANAGEMENT_MODE_ID, CPU_EXPR_MODES_ID,
        LATENCY_ULL_ENABLE_ID, LATENCY_ULL_MODE_ID, LATENCY_MAX_PRE_RENDERED_FRAMES_ID,
        SMOOTH_MOTION_ENABLE_ID, SMOOTH_MOTION_APIS_ID, SMOOTH_MOTION_FLIP_PACING_FS_ID, SMOOTH_MOTION_FLIP_PACING_WIN_ID,
        REBAR_FEATURE_ID, REBAR_EXPR_MODES_ID,
        NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID,
        DLSS_SR_PRESET_OVERRIDE_ID,
        NGX_DLSS_SR_RENDER_SCALE_ID, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, NGX_DLSS_RR_RENDER_SCALE_ID, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID,
        MFG_MODE_OVERRIDE_ID, MFG_GENERATION_FACTOR_ID, MFG_DYNAMIC_MAX_COUNT_ID, MFG_DYNAMIC_TARGET_FPS_ID,
    ];

    /// <summary>Exports all RHI-managed NVIDIA profile settings to a dictionary for serialization.</summary>
    public Dictionary<string, object> ExportProfiles(IEnumerable<(string GameName, string InstallPath)> games)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!_isSupported || _session == null || _cachedProfiles == null) return result;

        var managedSet = new HashSet<uint>(ManagedSettingIds);
        var sessionHandle = GetHandlePtr(_session.Handle);

        foreach (var (gameName, installPath) in games)
        {
            try
            {
                var profile = FindProfile(gameName, installPath);
                if (profile == null) continue;

                // Skip if already exported under a different game name (same profile)
                if (result.ContainsKey(profile.Name)) continue;

                var profileData = new Dictionary<string, object>();

                // Get applications
                try
                {
                    var apps = profile.Applications.Select(a => a.ApplicationName).ToList();
                    if (apps.Count > 0) profileData["applications"] = apps;
                }
                catch { /* Some profiles throw on Applications access */ }

                // Get managed settings — first pass via NvAPIWrapper (fast)
                var settings = new Dictionary<string, uint>();
                try
                {
                    foreach (var setting in profile.Settings)
                    {
                        if (managedSet.Contains(setting.SettingId) && setting.CurrentValue is uint v)
                            settings[$"0x{setting.SettingId:X8}"] = v;
                    }
                }
                catch { /* Some profiles throw on Settings access */ }

                // Second pass: raw API for ALL managed settings not yet found
                // (needed for newer settings written via raw API that NvAPIWrapper can't enumerate)
                if (sessionHandle != IntPtr.Zero)
                {
                    var profileHandle = GetHandlePtr(profile.Handle);
                    if (profileHandle != IntPtr.Zero)
                    {
                        foreach (var settingId in ManagedSettingIds)
                        {
                            if (settings.ContainsKey($"0x{settingId:X8}")) continue;
                            try
                            {
                                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, settingId);
                                if (rawVal.HasValue)
                                    settings[$"0x{settingId:X8}"] = rawVal.Value;
                            }
                            catch { /* Skip unreadable settings */ }
                        }
                    }
                }

                if (settings.Count > 0)
                {
                    profileData["settings"] = settings;
                    result[profile.Name] = profileData;
                    CrashReporter.Log($"[DlssPresetService.ExportProfiles] '{gameName}' → profile '{profile.Name}': {settings.Count} settings");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.ExportProfiles] Skipping '{gameName}': {ex.Message}");
            }
        }

        // Export global/base profile settings (Shader Cache, G-Sync, ReBAR, etc.)
        try
        {
            var globalSettings = new Dictionary<string, object>();
            var globalDword = new Dictionary<string, uint>();

            // Read global DWORD settings
            uint[] globalSettingIds = { SHADER_CACHE_SIZE_ID, SHADER_PRECOMPILE_ID, GSYNC_APP_MODE_ID, GSYNC_GLOBAL_MODE_ID, PREFERRED_REFRESH_RATE_ID, REBAR_FEATURE_ID, REBAR_EXPR_MODES_ID };
            var baseProfile = _session.BaseProfile;
            var baseHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && baseHandle != IntPtr.Zero)
            {
                foreach (var id in globalSettingIds)
                {
                    try
                    {
                        var rawVal = GetSettingRawNvApi(sessionHandle, baseHandle, id);
                        if (rawVal.HasValue)
                            globalDword[$"0x{id:X8}"] = rawVal.Value;
                    }
                    catch { }
                }
            }

            // Read global ReBAR Size Limit (binary)
            var rebarSize = GetGlobalReBarSizeLimit();
            if (rebarSize != 0)
                globalSettings["rebarSizeLimit"] = rebarSize;

            if (globalDword.Count > 0)
                globalSettings["settings"] = globalDword;

            if (globalSettings.Count > 0)
            {
                result["__global__"] = globalSettings;
                CrashReporter.Log($"[DlssPresetService.ExportProfiles] Exported global profile: {globalDword.Count} DWORD settings");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.ExportProfiles] Global profile export failed: {ex.Message}");
        }

        CrashReporter.Log($"[DlssPresetService.ExportProfiles] Exported {result.Count} profiles with custom settings");
        return result;
    }

    /// <summary>Imports NVIDIA profile settings from a previously exported dictionary.</summary>
    public int ImportProfiles(Dictionary<string, object> data)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null) return 0;
        int importedCount = 0;

        foreach (var kvp in data)
        {
            try
            {
                var profileName = kvp.Key;
                if (kvp.Value is not System.Text.Json.JsonElement elem) continue;

                // Handle global profile separately
                if (profileName == "__global__")
                {
                    try
                    {
                        var baseProfile = _session.BaseProfile;
                        var sessionHandle = GetHandlePtr(_session.Handle);
                        var baseHandle = GetHandlePtr(baseProfile.Handle);
                        // ReBAR Feature/Mode require elevation — use PS helper for those
                        bool hasReBarFeature = false;
                        uint rebarFeatureVal = 0;
                        uint rebarModeVal = 0;

                        if (elem.TryGetProperty("settings", out var globalSettingsElem) && globalSettingsElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            foreach (var prop in globalSettingsElem.EnumerateObject())
                            {
                                if (uint.TryParse(prop.Name.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var settingId))
                                {
                                    var value = prop.Value.GetUInt32();
                                    if (settingId == REBAR_FEATURE_ID) { hasReBarFeature = true; rebarFeatureVal = value; continue; }
                                    if (settingId == REBAR_EXPR_MODES_ID) { rebarModeVal = value; continue; }
                                    // Always use raw API for base profile — NvAPIWrapper's SetSetting silently fails for many settings
                                    bool written = false;
                                    if (sessionHandle != IntPtr.Zero && baseHandle != IntPtr.Zero)
                                        written = SetSettingRawNvApi(sessionHandle, baseHandle, settingId, value);
                                    if (!written)
                                    {
                                        try { baseProfile.SetSetting(settingId, value); } catch { }
                                    }
                                }
                            }
                        }

                        // Write ReBAR Feature/Mode via PS helper (requires elevation)
                        if (hasReBarFeature && rebarFeatureVal != 0)
                            SetGlobalReBarEnabled(true);
                        else if (hasReBarFeature)
                            SetGlobalReBarEnabled(false);

                        if (elem.TryGetProperty("rebarSizeLimit", out var rebarSizeElem))
                        {
                            var sizeVal = rebarSizeElem.GetUInt64();
                            if (sizeVal != 0)
                                SetGlobalReBarSizeLimit(sizeVal);
                        }

                        _session.Save();
                        importedCount++;
                        CrashReporter.Log("[DlssPresetService.ImportProfiles] Restored global profile settings");
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DlssPresetService.ImportProfiles] Global profile import failed — {ex.Message}");
                    }
                    continue;
                }

                // Find or create profile
                DriverSettingsProfile? profile = null;
                if (!_cachedProfiles.TryGetValue(profileName, out profile))
                {
                    // Create profile
                    profile = DriverSettingsProfile.CreateProfile(_session, profileName, null);
                    _cachedProfiles.TryAdd(profileName, profile);
                    CrashReporter.Log($"[DlssPresetService.ImportProfiles] Created profile '{profileName}'");
                }

                // Register applications
                if (elem.TryGetProperty("applications", out var appsElem) && appsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var existingApps = profile.Applications.Select(a => a.ApplicationName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var appElem in appsElem.EnumerateArray())
                    {
                        var appName = appElem.GetString();
                        if (!string.IsNullOrEmpty(appName) && !existingApps.Contains(appName))
                        {
                            try
                            {
                                ProfileApplication.CreateApplication(profile, appName, profileName, "", Array.Empty<string>(), false, "");
                                CrashReporter.Log($"[DlssPresetService.ImportProfiles] Registered '{appName}' on '{profileName}'");
                            }
                            catch { }
                        }
                    }
                }

                // Apply settings
                if (elem.TryGetProperty("settings", out var settingsElem) && settingsElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in settingsElem.EnumerateObject())
                    {
                        if (uint.TryParse(prop.Name.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var settingId))
                        {
                            var value = prop.Value.GetUInt32();
                            try
                            {
                                profile.SetSetting(settingId, value);
                            }
                            catch
                            {
                                // Fallback to raw API for settings NvAPIWrapper doesn't know
                                var sH = GetHandlePtr(_session.Handle);
                                var pH = GetHandlePtr(profile.Handle);
                                if (sH != IntPtr.Zero && pH != IntPtr.Zero)
                                    SetSettingRawNvApi(sH, pH, settingId, value);
                            }
                        }
                    }
                }

                importedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.ImportProfiles] Error importing '{kvp.Key}' — {ex.Message}");
            }
        }

        if (importedCount > 0)
            _session.Save();

        CrashReporter.Log($"[DlssPresetService.ImportProfiles] Imported {importedCount} profiles");
        return importedCount;
    }

}
