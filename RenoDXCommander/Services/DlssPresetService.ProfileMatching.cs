using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    // ── Private helpers ───────────────────────────────────────────────────────

    private uint GetPreset(string gameName, string installPath, uint settingId)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return 0;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) return 0;

            var setting = profile.Settings.FirstOrDefault(s => s.SettingId == settingId);
            if (setting?.CurrentValue is uint value)
                return value;

            // Fallback: try raw NVAPI for newer settings not visible through NvAPIWrapper
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(profile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawValue = GetSettingRawNvApi(sessionHandle, profileHandle, settingId);
                if (rawValue.HasValue)
                    return rawValue.Value;
            }

            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.GetPreset] Error for '{gameName}' — {ex.Message}");
            return 0;
        }
    }

    private bool SetPreset(string gameName, string installPath, uint settingId, uint preset)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null)
            {
                if (!AutoCreateProfiles)
                {
                    CrashReporter.Log($"[DlssPresetService.SetPreset] No profile found for '{gameName}' (auto-create disabled)");
                    return false;
                }
                // Auto-create a profile for this game
                profile = CreateProfileForGame(gameName, installPath);
                if (profile == null)
                {
                    CrashReporter.Log($"[DlssPresetService.SetPreset] No profile found and could not create one for '{gameName}'");
                    return false;
                }
            }
            else if (AutoCreateProfiles && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                // Ensure the game's exe is registered in the profile's Applications list
                EnsureExeRegistered(profile, gameName, installPath);
            }

            profile.SetSetting(settingId, preset);
            _session.Save();

            CrashReporter.Log($"[DlssPresetService.SetPreset] Set 0x{settingId:X8}={preset} for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            // If SETTING_NOT_FOUND, try direct raw NVAPI call (bypasses NvAPIWrapper entirely)
            if (ex.Message.Contains("SETTING_NOT_FOUND"))
            {
                try
                {
                    var profile = FindProfile(gameName, installPath);
                    if (profile != null && _session != null)
                    {
                        // Get raw IntPtr handles from the NvAPIWrapper structs
                        var sessionHandle = GetHandlePtr(_session.Handle);
                        var profileHandle = GetHandlePtr(profile.Handle);
                        if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
                        {
                            if (SetSettingRawNvApi(sessionHandle, profileHandle, settingId, preset))
                                return true;
                        }
                    }
                }
                catch (Exception ex2)
                {
                    CrashReporter.Log($"[DlssPresetService.SetPreset] Raw NVAPI fallback failed for '{gameName}' — {ex2.Message}");
                }
            }
            CrashReporter.Log($"[DlssPresetService.SetPreset] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a setting from a game's profile so it inherits from the global/base profile.
    /// </summary>
    private bool DeletePreset(string gameName, string installPath, uint settingId)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) return false;

            try { profile.DeleteSetting(settingId); }
            catch (Exception delEx)
            {
                CrashReporter.Log($"[DlssPresetService.DeletePreset] DeleteSetting 0x{settingId:X8} failed for '{gameName}' — {delEx.Message}");
                // Fallback: write the correct default value for this setting
                // (NOT 0 — some settings use 0 as a meaningful non-default value)
                var defaultValue = GetSettingDefaultValue(settingId);
                SetPreset(gameName, installPath, settingId, defaultValue);
            }
            _session.Save();
            CrashReporter.Log($"[DlssPresetService.DeletePreset] Deleted 0x{settingId:X8} for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.DeletePreset] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the correct "cleared/default/no-override" value for a given setting ID.
    /// Used as fallback when profile.DeleteSetting fails.
    /// </summary>
    private static uint GetSettingDefaultValue(uint settingId)
    {
        return settingId switch
        {
            NGX_DLSS_SR_RENDER_SCALE_ID => 0x03,      // App Controlled (Default) — NOT 0x00 which is Performance
            NGX_DLSS_RR_RENDER_SCALE_ID => 0x03,      // App Controlled (Default) — NOT 0x00 which is Performance
            _ => 0x00,                                  // Most settings: 0 = Default/Off/No Override
        };
    }

    /// <summary>
    /// Finds the NVIDIA driver profile for a game by matching title or exe names.
    /// </summary>
    private DriverSettingsProfile? FindProfile(string gameName, string installPath)
    {
        if (_cachedProfiles == null) return null;

        // Check the per-game lookup cache first (avoids repeated expensive scanning)
        if (_profileLookupCache.TryGetValue(gameName, out var cached))
            return cached;

        var result = FindProfileUncached(gameName, installPath);
        _profileLookupCache[gameName] = result;
        return result;
    }

    /// <summary>
    /// The actual profile matching logic — called once per game, result is cached.
    /// </summary>
    private DriverSettingsProfile? FindProfileUncached(string gameName, string installPath)
    {
        if (_cachedProfiles == null) return null;

        // Check manifest profile name override first (handles cases like "Dead Space" → "Dead Space (Remake)")
        if (_profileNameOverrides != null
            && _profileNameOverrides.TryGetValue(gameName, out var overrideName)
            && _cachedProfiles.TryGetValue(overrideName, out var overrideProfile))
        {
            CrashReporter.Log($"[DlssPresetService.FindProfile] Override matched: '{gameName}' → profile '{overrideName}'");
            return overrideProfile;
        }
        else if (_profileNameOverrides != null && _profileNameOverrides.TryGetValue(gameName, out var missedName))
        {
            CrashReporter.Log($"[DlssPresetService.FindProfile] Override '{gameName}' → '{missedName}' NOT FOUND in cached profiles");
        }

        // Try exact title match first
        if (_cachedProfiles.TryGetValue(gameName, out var exactProfile))
            return exactProfile;

        // Try fuzzy title match early (strip special characters like ™, ®, colons)
        var normalizedGameName = NormalizeForMatch(gameName);
        foreach (var kvp in _cachedProfiles)
        {
            if (NormalizeForMatch(kvp.Key) == normalizedGameName)
            {
                return kvp.Value;
            }
        }

        // Try manifest launch exe override — direct match without folder scanning
        if (_launchExeOverrides != null && _launchExeOverrides.TryGetValue(gameName, out var overrideExe) && !string.IsNullOrEmpty(overrideExe))
        {
            var exeName = Path.GetFileName(overrideExe);
            foreach (var profile in _cachedProfiles.Values)
            {
                try
                {
                    foreach (var app in profile.Applications)
                    {
                        if (app.ApplicationName.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                        {
                            CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{profile.Name}' via manifest launchExeOverride '{exeName}'");
                            return profile;
                        }
                    }
                }
                catch { /* Skip profiles that throw on Applications access */ }
            }
        }

        // Try matching by exe names in the install path (recursive to handle subdirectories)
        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
        {
            try
            {
                var exeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var file in Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories))
                        exeNames.Add(Path.GetFileName(file));
                }
                catch (UnauthorizedAccessException)
                {
                    // Fallback to top-level only if recursive fails (WindowsApps etc.)
                    foreach (var file in Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly))
                        exeNames.Add(Path.GetFileName(file)!);
                }

                // Remove generic exe names that cause false matches across games
                exeNames.ExceptWith(_excludedProfileExeNames);

                // Try matching profile applications against exe names
                foreach (var profile in _cachedProfiles.Values)
                {
                    foreach (var app in profile.Applications)
                    {
                        if (exeNames.Contains(app.ApplicationName))
                        {
                            CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{profile.Name}' via app '{app.ApplicationName}'");
                            return profile;
                        }
                    }
                }

                // Try matching profile NAME against exe names (custom profiles named after the exe)
                foreach (var exeName in exeNames)
                {
                    if (_cachedProfiles.TryGetValue(exeName, out var exeProfile))
                    {
                        CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{exeProfile.Name}' via profile name == exe name");
                        return exeProfile;
                    }
                }

                CrashReporter.Log($"[DlssPresetService.FindProfile] No profile matched any exe in '{installPath}'");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.FindProfile] Error scanning '{installPath}' — {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the raw IntPtr from an NvAPIWrapper handle struct via reflection.
    /// </summary>
    private static IntPtr GetHandlePtr<T>(T handle) where T : struct
    {
        var field = typeof(T).GetField("_MemoryAddress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null) return IntPtr.Zero;
        return (IntPtr)field.GetValue(handle)!;
    }

    /// <summary>
    /// Normalizes a string for fuzzy matching by removing special characters and lowercasing.
    /// </summary>
    private static string NormalizeForMatch(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == ' ')
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Creates a new NVIDIA driver profile for a game that doesn't have one.
    /// Uses the largest exe in the install path as the application name.
    /// </summary>
    private DriverSettingsProfile? CreateProfileForGame(string gameName, string installPath)
    {
        if (_session == null || _cachedProfiles == null) return null;
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return null;

        try
        {
            // Find the game exe (largest exe, excluding known non-game names)
            var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };
            string? gameExe = null;
            try
            {
                gameExe = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                    .OrderByDescending(e => new FileInfo(e).Length)
                    .Select(Path.GetFileName)
                    .FirstOrDefault();
            }
            catch { }

            if (string.IsNullOrEmpty(gameExe)) return null;

            // Sanitize profile name — NVIDIA doesn't accept commas in profile names
            var profileName = gameName.Replace(",", "");

            // Create the profile
            var profile = DriverSettingsProfile.CreateProfile(_session, profileName, null);

            // Add the exe as an application
            ProfileApplication.CreateApplication(profile, gameExe, profileName, "", Array.Empty<string>(), false, "");

            _session.Save();

            // Cache the new profile under both the sanitized name and original game name
            _cachedProfiles.TryAdd(profileName, profile);
            _profileLookupCache[gameName] = profile; // Map original game name to the profile

            CrashReporter.Log($"[DlssPresetService.CreateProfileForGame] Created profile '{gameName}' with app '{gameExe}'");
            ProfilesCreatedCount++;
            return profile;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.CreateProfileForGame] Failed for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Public method: returns true if a profile already exists for this game, false if not.
    /// </summary>
    public bool HasProfile(string gameName, string installPath)
    {
        return FindProfile(gameName, installPath) != null;
    }

    /// <summary>
    /// Public method: creates a profile for the game if one doesn't exist. Returns true if created.
    /// </summary>
    public bool EnsureProfileExists(string gameName, string installPath)
    {
        if (FindProfile(gameName, installPath) != null) return false; // already exists
        return CreateProfileForGame(gameName, installPath) != null;
    }

    /// <summary>
    /// Ensures the game's exe is registered in the profile's Applications list.
    /// If no exe from the install path is found in the profile, adds the largest exe.
    /// This is needed because NVIDIA applies settings based on the Applications list,
    /// not the profile name — a profile matched by title without the exe registered won't work.
    /// </summary>
    private void EnsureExeRegistered(DriverSettingsProfile profile, string gameName, string installPath)
    {
        try
        {
            // Get exe names from install path
            var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };
            var exeFiles = new List<string>();
            try
            {
                exeFiles = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                    .Where(e => !Path.GetFileName(e).Contains(" - copy", StringComparison.OrdinalIgnoreCase)
                             && !Path.GetFileName(e).Contains(" copy", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { return; }

            if (exeFiles.Count == 0) return;

            // Check if any exe is already registered
            var registeredApps = profile.Applications.Select(a => a.ApplicationName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var exe in exeFiles)
            {
                if (registeredApps.Contains(Path.GetFileName(exe)))
                    return; // Already registered
            }

            // Not registered — add the largest exe
            var gameExe = exeFiles.OrderByDescending(e => new FileInfo(e).Length).Select(Path.GetFileName).FirstOrDefault();
            if (string.IsNullOrEmpty(gameExe)) return;

            ProfileApplication.CreateApplication(profile, gameExe, gameName, "", Array.Empty<string>(), false, "");
            _session?.Save();
            CrashReporter.Log($"[DlssPresetService.EnsureExeRegistered] Added '{gameExe}' to profile '{profile.Name}' for '{gameName}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.EnsureExeRegistered] Failed for '{gameName}' — {ex.Message}");
        }
    }
}

