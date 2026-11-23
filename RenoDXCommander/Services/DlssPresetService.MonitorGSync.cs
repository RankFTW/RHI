// DlssPresetService.MonitorGSync.cs — per-monitor "Allow G-SYNC" (the NVIDIA app's System › Displays › G-SYNC switch).
// It is a driver (DRS) profile named after the monitor's EDID, "0x<manufacturer>:0x<product>" (e.g. "0x6936:0x3DB2"),
// with GSYNC_APP_MODE_ID (0x1194F158) = 1 allowed / 0 off. Validated G-SYNC monitors have a predefined profile
// (plus 0x106D5CFF = 1); for other monitors the NVIDIA app creates the profile the first time the switch is used.
// The driver reads it when it starts (it then rebuilds nvlddmkm\State\DisplayDatabase\<monitor>\MonitorDataRegistryKey
// from it), so a change takes effect after a GPU restart or a Windows restart. Writing needs no admin rights.
// Found by dumping every DRS profile with the switch on and off (Nov 2026). See GSyncMonitorService for the rest.

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    /// <summary>
    /// The monitor profile's own "Allow G-SYNC" value, or null when the profile or value doesn't exist
    /// (the monitor never had the switch changed and isn't on NVIDIA's validated list).
    /// </summary>
    public bool? GetMonitorGSyncAllowed(string profileName)
    {
        if (!_isSupported || _session == null) return null;
        if (!_sessionLock.Wait(millisecondsTimeout: 5_000))
        {
            CrashReporter.Log($"[DlssPresetService.GetMonitorGSyncAllowed] Lock timeout for '{profileName}' — NVAPI may be hung.");
            return null;
        }
        try
        {
            var profile = _session.FindProfileByName(profileName);
            if (profile == null) return null;
            var sH = GetHandlePtr(_session.Handle);
            var pH = GetHandlePtr(profile.Handle);
            if (sH == IntPtr.Zero || pH == IntPtr.Zero) return null;
            var v = GetOwnSettingRawNvApi(sH, pH, GSYNC_APP_MODE_ID);
            return v.HasValue ? v.Value != 0 : null;
        }
        catch (Exception ex)
        {
            // NvAPIWrapper throws when the profile doesn't exist
            CrashReporter.Log($"[DlssPresetService.GetMonitorGSyncAllowed] '{profileName}': {ex.Message}");
            return null;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Sets the monitor profile's "Allow G-SYNC" value, creating the profile if needed, and saves.
    /// Takes effect the next time the graphics driver starts.
    /// </summary>
    public bool SetMonitorGSyncAllowed(string profileName, bool allow)
    {
        if (!_isSupported || _session == null) return false;
        if (!_sessionLock.Wait(millisecondsTimeout: 5_000))
        {
            CrashReporter.Log($"[DlssPresetService.SetMonitorGSyncAllowed] Lock timeout for '{profileName}' — NVAPI may be hung.");
            return false;
        }
        try
        {
            NvAPIWrapper.DRS.DriverSettingsProfile? profile = null;
            try { profile = _session.FindProfileByName(profileName); }
            catch (Exception ex) { CrashReporter.Log($"[DlssPresetService.SetMonitorGSyncAllowed] '{profileName}' not found ({ex.Message}), creating it"); }
            if (profile == null)
            {
                profile = NvAPIWrapper.DRS.DriverSettingsProfile.CreateProfile(_session, profileName, null);
                CrashReporter.Log($"[DlssPresetService.SetMonitorGSyncAllowed] Created profile '{profileName}'");
            }
            var sH = GetHandlePtr(_session.Handle);
            var pH = GetHandlePtr(profile.Handle);
            if (sH == IntPtr.Zero || pH == IntPtr.Zero) return false;
            bool ok = SetSettingRawNvApi(sH, pH, GSYNC_APP_MODE_ID, allow ? 1u : 0u);   // also saves
            CrashReporter.Log($"[DlssPresetService.SetMonitorGSyncAllowed] '{profileName}' = {(allow ? 1 : 0)} → {(ok ? "saved" : "failed")}");
            return ok;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetMonitorGSyncAllowed] '{profileName}': {ex.Message}");
            return false;
        }
        finally
        {
            _sessionLock.Release();
        }
    }
}
