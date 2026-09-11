using System.Diagnostics;
using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    // ── Get render scale ──────────────────────────────────────────────────────

    /// <summary>Returns the current SR render scale percentage (0 = Off/Default, 33-100 = active).</summary>
    public uint GetSrRenderScale(string gameName, string installPath)
    {
        var mode = GetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID);
        if (mode != RENDER_SCALE_CUSTOM) return 0;
        return GetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID);
    }

    /// <summary>Returns the current RR render scale percentage (0 = Off/Default, 33-100 = active).</summary>
    public uint GetRrRenderScale(string gameName, string installPath)
    {
        var mode = GetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID);
        if (mode != RENDER_SCALE_CUSTOM) return 0;
        return GetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID);
    }

    // ── Set render scale ──────────────────────────────────────────────────────

    /// <summary>Sets the SR render scale. 0 = reset to Default (delete from profile). 33-100 = set custom percentage.</summary>
    public bool SetSrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            DeletePreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID);
            DeletePreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID);
            return true;
        }
        SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

    /// <summary>Sets the RR render scale. 0 = reset to Default (delete from profile). 33-100 = set custom percentage.</summary>
    public bool SetRrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            DeletePreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID);
            DeletePreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID);
            return true;
        }
        SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

    // ── ReBAR (Resizable BAR) ─────────────────────────────────────────────────

    private const uint REBAR_ENABLE_ID    = 0x000BFA21;  // 0=Off, 1=Auto (default), 2=On
    private const uint REBAR_FEATURE_ID   = 0x000F00BA;
    private const uint REBAR_EXPR_MODES_ID = 0x00C09D09;
    private const uint REBAR_SIZE_LIMIT_ID = 0x000F00FF;

    /// <summary>ReBAR mode options. Standard = Mode 0, Optimized = Mode 2.</summary>
    public static readonly (string Name, uint Value)[] ReBarModes =
    [
        ("Standard", 0x00000000),
        ("Optimized", 0x00000002),
    ];

    /// <summary>ReBAR size limit options. Value is the size in bytes as a 64-bit integer.</summary>
    public static readonly (string Name, ulong Value)[] ReBarSizeLimits =
    [
        ("512MB", 0x0000000020000000),
        ("1GB (Default)", 0x0000000040000000),
        ("1.5GB", 0x0000000060000000),
        ("2GB", 0x0000000080000000),
        ("4GB", 0x0000000100000000),
    ];

    /// <summary>Returns true if ReBAR Feature is enabled for this game's NVIDIA profile.</summary>
    public bool GetReBarEnabled(string gameName, string installPath)
        => GetPreset(gameName, installPath, REBAR_FEATURE_ID) != 0;

    /// <summary>Returns the ReBAR Expr Mode value (0 = Standard, 2 = Optimized).</summary>
    public uint GetReBarMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, REBAR_EXPR_MODES_ID);

    /// <summary>
    /// Returns the ReBAR Enable mode for a game profile (0x000BFA21).
    /// 0 = Off, 1 = Auto (driver default), 2 = On. Returns 1 (Auto) if not set in profile.
    /// </summary>
    public uint GetReBarEnableMode(string gameName, string installPath)
    {
        if (!_isSupported || _session == null) return 1;
        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) return 1;
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(profile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var raw = GetSettingRawNvApi(sessionHandle, profileHandle, REBAR_ENABLE_ID);
                // null = not in profile = Auto; 0 = Off, 1 = Auto explicit, 2 = On
                if (raw.HasValue) return raw.Value;
            }
        }
        catch { }
        return 1; // Not set = Auto
    }

    /// <summary>
    /// Sets the ReBAR Enable mode for a game profile (0x000BFA21).
    /// 0 = Off, 1 = Auto (driver default), 2 = On.
    /// Also syncs REBAR_FEATURE_ID for backwards compatibility.
    /// </summary>
    public bool SetReBarEnableMode(string gameName, string installPath, uint mode)
    {
        CrashReporter.Log($"[DlssPresetService.SetReBarEnableMode] gameName='{gameName}', mode={mode}");
        // Write the new Enable ID via raw NVAPI — non-deletable, always write explicit value.
        // Off=0, Auto=1, On=2
        var ok = SetRtxHdrRaw(gameName, installPath, REBAR_ENABLE_ID, mode);
        // Sync legacy REBAR_FEATURE_ID: On=1, Off=0, Auto=delete (let driver decide)
        try
        {
            if (mode == 1) // Auto — remove legacy override entirely
            {
                var profile = FindProfile(gameName, installPath);
                if (profile != null) { try { profile.DeleteSetting(REBAR_FEATURE_ID); profile.DeleteSetting(REBAR_EXPR_MODES_ID); profile.DeleteSetting(REBAR_SIZE_LIMIT_ID); _session?.Save(); } catch { } }
            }
            else if (mode == 0) // Off — delete legacy settings and size limit (no Disabled entry)
            {
                var profile = FindProfile(gameName, installPath);
                if (profile != null) { try { profile.DeleteSetting(REBAR_FEATURE_ID); profile.DeleteSetting(REBAR_EXPR_MODES_ID); profile.DeleteSetting(REBAR_SIZE_LIMIT_ID); _session?.Save(); } catch { } }
            }
            else // On
                SetReBarEnabled(gameName, installPath, true, 2u);
        }
        catch { }
        return ok;
    }

    /// <summary>
    /// Returns the global (base profile) ReBAR Enable mode (0x000BFA21).
    /// 0 = Off, 1 = Auto, 2 = On. Returns 1 if not set.
    /// </summary>
    public uint GetGlobalReBarEnableMode()
    {
        if (!_isSupported || _session == null) return 1;
        try
        {
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(_session.BaseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var raw = GetSettingRawNvApi(sessionHandle, profileHandle, REBAR_ENABLE_ID);
                CrashReporter.Log($"[DlssPresetService.GetGlobalReBarEnableMode] raw={raw?.ToString() ?? "null (not in profile → Auto)"}");
                if (raw.HasValue) return raw.Value;
            }
        }
        catch { }
        return 1; // Not set = Auto
    }

    /// <summary>
    /// Sets the global (base profile) ReBAR Enable mode (0x000BFA21) via raw NVAPI.
    /// Also syncs REBAR_FEATURE_ID on the base profile via PS helper (requires elevation).
    /// </summary>
    public bool SetGlobalReBarEnableMode(uint mode)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            // Write 0x000BFA21 directly via raw NVAPI on the base profile.
            // This ID is non-deletable (returns -160) — always write explicit value.
            // Off=0, Auto=1 (driver default), On=2
            var sessionHandle = GetHandlePtr(_session.Handle);
            var baseHandle   = GetHandlePtr(_session.BaseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && baseHandle != IntPtr.Zero)
                SetSettingRawNvApi(sessionHandle, baseHandle, REBAR_ENABLE_ID, mode);

            // Sync legacy REBAR_FEATURE_ID + REBAR_EXPR_MODES_ID + REBAR_SIZE_LIMIT_ID via PS helper
            // (base profile writes for these require elevation on most systems)
            var nvApiPath  = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_global_rebar_enable_mode.ps1");

            string featureBlock = mode == 2
                ? "$base.SetSetting([uint32]0x000F00BA, [uint32]1)"
                : "try { $base.DeleteSetting([uint32]0x000F00BA) } catch {}";
            string exprBlock = mode == 2
                ? "$base.SetSetting([uint32]0x00C09D09, [uint32]0)"
                : "try { $base.DeleteSetting([uint32]0x00C09D09) } catch {}";
            string sizeLimitBlock = "# size limit written by C# after session reload";

            string scriptBody = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$base = $session.BaseProfile
{featureBlock}
{exprBlock}
{sizeLimitBlock}
$session.Save()
";
            File.WriteAllText(scriptPath, scriptBody);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName  = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            System.Diagnostics.Process.Start(psi);
            // Pause to let the elevated PS complete — base profile writes are fast
            System.Threading.Thread.Sleep(3000);
            try { File.Delete(scriptPath); } catch { }
            // Reload session
            try
            {
                _session = DriverSettingsSession.CreateAndLoad();
                _cachedProfiles = new(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _session.Profiles) _cachedProfiles.TryAdd(p.Name, p);
                InvalidateProfileLookupCache();
            }
            catch { }
            CrashReporter.Log($"[DlssPresetService.SetGlobalReBarEnableMode] Set mode={mode}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetGlobalReBarEnableMode] Error — {ex.Message}");
            return false;
        }
    }

    /// <summary>Returns the ReBAR Size Limit in bytes (0 = not set / use driver default).</summary>
    public ulong GetReBarSizeLimit(string gameName, string installPath)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return 0;

        // Check local cache first (populated when RHI writes the value)
        if (_rebarSizeLimitCache.TryGetValue(gameName, out var cached))
            return cached;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) { CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] No profile for '{gameName}'"); return 0; }

            // Try raw NVAPI first — handles BINARY type correctly on all drivers
            try
            {
                EnsureNativeFunctions();
                if (_nativeGetSettingPtr != null && _session != null)
                {
                    const int STRUCT_SIZE = 12320;
                    var ptr = Marshal.AllocHGlobal(STRUCT_SIZE);
                    try
                    {
                        unsafe { new Span<byte>((void*)ptr, STRUCT_SIZE).Clear(); }
                        Marshal.WriteInt32(ptr, 0, STRUCT_SIZE | (1 << 16));

                        uint extraParam = 0;
                        var sessionH = GetHandlePtr(_session.Handle);
                        var profileH = GetHandlePtr(profile.Handle);

                        if (sessionH != IntPtr.Zero && profileH != IntPtr.Zero)
                        {
                            int result = _nativeGetSettingPtr(sessionH, profileH, REBAR_SIZE_LIMIT_ID, ptr, ref extraParam);
                            if (result == 0)
                            {
                                // Check setting type: offset 4104 (0=DWORD, 1=BINARY)
                                var settingType = Marshal.ReadInt32(ptr, 4104);
                                if (settingType == 1) // BINARY — length at 8216, data at 8220
                                {
                                    var binLen = Marshal.ReadInt32(ptr, 8216);
                                    if (binLen >= 8)
                                    {
                                        var val = (ulong)Marshal.ReadInt64(ptr, 8220);
                                        return val;
                                    }
                                }
                                else if (settingType == 4) // QWORD — 64-bit integer directly at 8220
                                {
                                    var val = (ulong)Marshal.ReadInt64(ptr, 8220);
                                    return val;
                                }
                                else // DWORD
                                {
                                    var dword = (uint)Marshal.ReadInt32(ptr, 8220);
                                    if (dword != 0) return dword;
                                }
                            }
                            // result != 0 means setting not found on this profile
                        }
                    }
                    finally { Marshal.FreeHGlobal(ptr); }
                }
            }
            catch (Exception rawEx)
            {
                CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Raw read failed for '{gameName}' — {rawEx.Message}");
            }

            // Fallback: try NvAPIWrapper (works on older drivers)
            try
            {
                var setting = profile.GetSetting(REBAR_SIZE_LIMIT_ID);
                if (setting != null)
                {
                    if (setting.CurrentValue is byte[] bytes && bytes.Length >= 8)
                        return BitConverter.ToUInt64(bytes, 0);
                    if (setting.CurrentValue is uint dwordVal && dwordVal != 0)
                        return dwordVal;
                }
            }
            catch { /* NvAPIWrapper can't handle BINARY on newer drivers */ }

            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Error for '{gameName}' — {ex.Message}");
            return 0;
        }
    }

    /// <summary>Reads ReBAR Size Limit via a PowerShell helper (fallback for drivers where GetSetting throws).</summary>
    private ulong ReadReBarSizeLimitViaPs(string gameName)
    {
        // If we've previously written a value for this game, use our local cache
        if (_rebarSizeLimitCache.TryGetValue(gameName, out var cached))
        {
            CrashReporter.Log($"[DlssPresetService.ReadReBarSizeLimitViaPs] Using cached value 0x{cached:X16} for '{gameName}'");
            return cached;
        }
        return 0;
    }

    // Local cache for ReBAR size limits written during this session
    private readonly Dictionary<string, ulong> _rebarSizeLimitCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes ReBAR Size Limit via PowerShell using NvAPIWrapper (fallback when raw NVAPI returns error).</summary>
    private bool SetReBarSizeLimitViaPs(string? profileName, ulong sizeBytes, bool useBaseProfile)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_rebar_size_ps.ps1");
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");
            var hexBytes = BitConverter.ToString(BitConverter.GetBytes(sizeBytes)).Replace("-", ",0x");

            string profileBlock;
            if (useBaseProfile)
            {
                profileBlock = "$profile = $session.BaseProfile";
            }
            else
            {
                profileBlock = $@"$profile = $null
foreach ($p in $session.Profiles) {{
    if ($p.Name -eq '{(profileName ?? "").Replace("'", "''")}') {{ $profile = $p; break }}
}}
if ($null -eq $profile) {{ exit 1 }}";
            }

            var doneFile = scriptPath + ".done";
            if (File.Exists(doneFile)) try { File.Delete(doneFile); } catch { }

            var script = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
{profileBlock}
[byte[]]$bytes = @(0x{hexBytes})
try {{
    $profile.SetSetting([uint32]0x000F00FF, $bytes)
    $session.Save()
    [System.IO.File]::WriteAllText('{doneFile.Replace("\\", "\\\\")}', '0')
    exit 0
}} catch {{
    [System.IO.File]::WriteAllText('{doneFile.Replace("\\", "\\\\")}', '2')
    exit 2
}}
";
            File.WriteAllText(scriptPath, script);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            System.Diagnostics.Process.Start(psi);

            // Poll for sentinel file — runas returns a ShellExecute wrapper handle, not the PS process
            // so WaitForExit() returns immediately. The sentinel written by the script tells us when done.
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!File.Exists(doneFile) && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(100);

            int exitCode = 2;
            if (File.Exists(doneFile))
            {
                var content = File.ReadAllText(doneFile).Trim();
                exitCode = content == "0" ? 0 : 2;
                try { File.Delete(doneFile); } catch { }
            }
            try { File.Delete(scriptPath); } catch { }

            // Reload session
            try
            {
                _session = DriverSettingsSession.CreateAndLoad();
                _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _session.Profiles)
                    _cachedProfiles.TryAdd(p.Name, p);
                InvalidateProfileLookupCache();
            }
            catch { }

            if (profileName != null)
                _rebarSizeLimitCache[profileName] = sizeBytes;

            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitViaPs] Set 0x{sizeBytes:X16} via PS helper (profile='{profileName ?? "BaseProfile"}', exitCode={exitCode})");
            return exitCode == 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitViaPs] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Enables or disables ReBAR for a game. When enabling, also sets Mode to the specified value.</summary>
    public bool SetReBarEnabled(string gameName, string installPath, bool enabled, uint mode = 0x00000000)
    {
        CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] gameName='{gameName}', enabled={enabled}, mode=0x{mode:X8}");
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null)
            {
                if (!AutoCreateProfiles) return false;
                profile = CreateProfileForGame(gameName, installPath);
                if (profile == null) return false;
            }
            else if (AutoCreateProfiles && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                EnsureExeRegistered(profile, gameName, installPath);
            }

            uint featureVal = enabled ? 1u : 0u;

            // Set Feature flag, Expr Modes, and Size Limit (default 1GB)
            profile.SetSetting(REBAR_FEATURE_ID, featureVal);
            if (enabled)
            {
                profile.SetSetting(REBAR_EXPR_MODES_ID, mode);
                // Also set Size Limit to 1GB default if not already set
                var existingSize = GetReBarSizeLimit(gameName, installPath);
                if (existingSize == 0)
                {
                    var sH2 = GetHandlePtr(_session.Handle);
                    var pH2 = GetHandlePtr(profile.Handle);
                    if (sH2 != IntPtr.Zero && pH2 != IntPtr.Zero)
                        SetQwordSettingRawNvApi(sH2, pH2, REBAR_SIZE_LIMIT_ID, 0x0000000040000000UL);
                }
            }
            else
            {
                profile.SetSetting(REBAR_EXPR_MODES_ID, 0u);
            }
            _session.Save();

            CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] Set ReBAR Feature=0x{featureVal:X8}, Mode=0x{mode:X8} for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("INVALID_USER_PRIVILEGE"))
            {
                // Requires elevation — use helper process
                CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] Requires elevation, launching elevated helper...");
                return SetReBarElevated(gameName, installPath, enabled, mode);
            }
            CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets ReBAR via an elevated process that re-invokes NVAPI with admin rights.</summary>
    private bool SetReBarElevated(string gameName, string installPath, bool enabled, uint mode)
    {
        try
        {
            // Resolve the actual NVIDIA profile name (may differ from RHI game name)
            var matchedProfile = FindProfile(gameName, installPath);
            var profileName = matchedProfile?.Name ?? gameName;

            // Build command: use PowerShell to load NVAPI and set the settings with admin
            var featureVal = enabled ? 1u : 0u;
            var modeVal = enabled ? mode : 0u;

            // Write a temporary script that sets the NVAPI values
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_rebar_set.ps1");
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");

            var script = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$profile = $null
foreach ($p in $session.Profiles) {{
    if ($p.Name -eq '{profileName.Replace("'", "''")}') {{ $profile = $p; break }}
}}
if ($null -eq $profile) {{
    foreach ($p in $session.Profiles) {{
        foreach ($app in $p.Applications) {{
            # Try to find by install path
        }}
    }}
}}
if ($null -ne $profile) {{
    $profile.SetSetting([uint32]0x000F00BA, [uint32]{featureVal})
    $profile.SetSetting([uint32]0x00C09D09, [uint32]{modeVal})
    $session.Save()
    Write-Host 'OK'
}} else {{
    Write-Host 'PROFILE_NOT_FOUND'
}}
";
            File.WriteAllText(scriptPath, script);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(10000);

            // Clean up
            try { File.Delete(scriptPath); } catch { }

            // Reload session to reflect changes made by the elevated process
            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _session.Profiles)
                _cachedProfiles.TryAdd(p.Name, p);
            InvalidateProfileLookupCache();

            // After session reload, set Size Limit to 1GB default via raw binary if enabling and not already set
            if (enabled)
            {
                var existingSize = GetReBarSizeLimit(gameName, installPath);
                if (existingSize == 0)
                {
                    var freshProfile = FindProfile(gameName, installPath);
                    if (freshProfile != null)
                    {
                        var sH3 = GetHandlePtr(_session.Handle);
                        var pH3 = GetHandlePtr(freshProfile.Handle);
                        if (sH3 != IntPtr.Zero && pH3 != IntPtr.Zero)
                            SetQwordSettingRawNvApi(sH3, pH3, REBAR_SIZE_LIMIT_ID, 0x0000000040000000UL);
                    }
                }
            }

            CrashReporter.Log($"[DlssPresetService.SetReBarElevated] Elevated process completed for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetReBarElevated] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets the ReBAR Expr Mode (0 = Standard, 2 = Optimized). Only meaningful when ReBAR is enabled.</summary>
    public bool SetReBarMode(string gameName, string installPath, uint mode)
    {
        var result = SetPreset(gameName, installPath, REBAR_EXPR_MODES_ID, mode);
        if (!result)
        {
            // Likely privilege error — try elevated
            CrashReporter.Log($"[DlssPresetService.SetReBarMode] Direct set failed, trying elevated for '{gameName}'");
            return SetReBarElevated(gameName, installPath, true, mode);
        }
        return result;
    }

    /// <summary>Sets the ReBAR Size Limit. Pass 0 to clear (revert to driver default). Value is size in bytes.</summary>
    public bool SetReBarSizeLimit(string gameName, string installPath, ulong sizeBytes)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        var profile = FindProfile(gameName, installPath);
        if (profile == null)
        {
            if (!AutoCreateProfiles) return false;
            profile = CreateProfileForGame(gameName, installPath);
            if (profile == null) return false;
        }

        // ReBAR Size Limit is stored as QWORD (type 4 / DRS_DWORD64) by the driver.
        // NvAPIWrapper encodes it as Binary (type 1) which writes garbage — use raw NVAPI QWORD write.
        CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimit] Writing 0x{sizeBytes:X16} for '{gameName}' via QWORD raw NVAPI");
        var sessionH = GetHandlePtr(_session.Handle);
        var profileH = GetHandlePtr(profile.Handle);
        if (sessionH == IntPtr.Zero || profileH == IntPtr.Zero)
        {
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimit] Failed to get handles for '{gameName}'");
            return false;
        }
        var success = SetQwordSettingRawNvApi(sessionH, profileH, REBAR_SIZE_LIMIT_ID, sizeBytes);
        if (success) _rebarSizeLimitCache[gameName] = sizeBytes;
        return success;
    }

}
