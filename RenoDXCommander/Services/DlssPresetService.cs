using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DLSS preset overrides via the NVIDIA Driver Settings (NvDRS) API.
/// Presets are per-game settings stored in the NVIDIA driver profile.
/// Silently no-ops on AMD/Intel systems where NVAPI is unavailable.
/// </summary>
public class DlssPresetService
{
    // ── Setting IDs ───────────────────────────────────────────────────────────
    private const uint NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF3;
    private const uint NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF7;
    private const uint NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF1;
    private const uint NGX_DLSS_SR_RENDER_SCALE_ID = 0x10AFB768; // SR Render Scale mode (Default=3, Custom=6)
    private const uint NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID = 0x10E41DF5; // SR Render Scale custom value (percentage as uint)
    private const uint NGX_DLSS_RR_RENDER_SCALE_ID = 0x10BD9423; // RR Render Scale mode
    private const uint NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID = 0x10C7D4A2; // RR Render Scale custom value

    private const uint RENDER_SCALE_DEFAULT = 0x03;
    private const uint RENDER_SCALE_CUSTOM = 0x06;

    // ── Preset values ─────────────────────────────────────────────────────────
    public static readonly (string Name, uint Value)[] SrPresets =
    [
        ("Default", 0x00000000),
        ("J", 0x0000000A),
        ("K", 0x0000000B),
        ("L", 0x0000000C),
        ("M", 0x0000000D),
    ];

    public static readonly (string Name, uint Value)[] RrPresets =
    [
        ("Default", 0x00000000),
        ("D", 0x00000004),
        ("E", 0x00000005),
    ];

    public static readonly (string Name, uint Value)[] FgPresets =
    [
        ("Default", 0x00000000),
        ("A", 0x00000001),
        ("B", 0x00000002),
    ];

    /// <summary>Named render scale options for SR and RR. "Custom" is handled separately via a TextBox.</summary>
    public static readonly (string Name, uint Value)[] RenderScaleOptions =
    [
        ("Off", 0),
        ("100% — DLAA", 100),
        ("99% — DLAA [If 100% Breaks]", 99),
        ("88% — DLAA Lite", 88),
        ("83% — Ultra Quality+", 83),
        ("77% — Ultra Quality", 77),
        ("72% — High Quality", 72),
        ("67% — Quality", 67),
        ("62% — Balanced Quality", 62),
        ("58% — Balanced", 58),
        ("54% — Balanced Performance", 54),
        ("50% — Performance", 50),
        ("49% — 007 Fix", 49),
        ("45% — Extra Performance", 45),
        ("41% — High Performance", 41),
        ("37% — Extreme Performance", 37),
        ("33% — Ultra Performance", 33),
        ("Custom", 0xFFFFFFFF), // Sentinel — actual value comes from TextBox
    ];

    // ── State ─────────────────────────────────────────────────────────────────
    private DriverSettingsSession? _session;
    private Dictionary<string, DriverSettingsProfile>? _cachedProfiles;
    private bool _isSupported;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    public bool IsSupported => _isSupported;

    /// <summary>When true, SetPreset will auto-create NVIDIA profiles for games that don't have one.</summary>
    public bool AutoCreateProfiles { get; set; } = true;

    /// <summary>Counter for profiles created during the current batch operation. Reset before use.</summary>
    public int ProfilesCreatedCount { get; set; }

    /// <summary>
    /// Initializes NVAPI. Call once during app startup.
    /// No-op if NVIDIA drivers are not installed.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var handle = LoadLibrary("nvapi64.dll");
            if (handle == IntPtr.Zero)
            {
                CrashReporter.Log("[DlssPresetService.Initialize] nvapi64.dll not found — NVIDIA drivers not installed, presets disabled");
                return;
            }
            FreeLibrary(handle);

            NVIDIA.Initialize();
            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in _session.Profiles)
            {
                _cachedProfiles.TryAdd(profile.Name, profile);
            }
            _isSupported = true;

            CrashReporter.Log($"[DlssPresetService.Initialize] NVAPI initialized, {_cachedProfiles.Count} profiles cached");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.Initialize] Failed — {ex.Message}");
            _isSupported = false;
        }
    }

    // ── Get presets ───────────────────────────────────────────────────────────

    public uint GetSrPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    public uint GetRrPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    public uint GetFgPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    // ── Set presets ───────────────────────────────────────────────────────────

    public bool SetSrPreset(string gameName, string installPath, uint preset)
        => SetPreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);

    public bool SetRrPreset(string gameName, string installPath, uint preset)
        => SetPreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);

    public bool SetFgPreset(string gameName, string installPath, uint preset)
        => SetPreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);

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

    /// <summary>Sets the SR render scale. 0 = reset to Default. 33-100 = set custom percentage.</summary>
    public bool SetSrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID, RENDER_SCALE_DEFAULT);
            return SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, 0);
        }
        SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

    /// <summary>Sets the RR render scale. 0 = reset to Default. 33-100 = set custom percentage.</summary>
    public bool SetRrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID, RENDER_SCALE_DEFAULT);
            return SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID, 0);
        }
        SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

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
            CrashReporter.Log($"[DlssPresetService.SetPreset] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds the NVIDIA driver profile for a game by matching title or exe names.
    /// </summary>
    private DriverSettingsProfile? FindProfile(string gameName, string installPath)
    {
        if (_cachedProfiles == null) return null;

        // Try exact title match first
        if (_cachedProfiles.TryGetValue(gameName, out var exactProfile))
            return exactProfile;

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

                CrashReporter.Log($"[DlssPresetService.FindProfile] '{gameName}' — found {exeNames.Count} exe(s) in '{installPath}': {string.Join(", ", exeNames.Take(5))}");

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

                // Try fuzzy title match (strip special characters like colons, compare case-insensitive)
                var normalizedGameName = NormalizeForMatch(gameName);
                foreach (var kvp in _cachedProfiles)
                {
                    if (NormalizeForMatch(kvp.Key) == normalizedGameName)
                    {
                        CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{kvp.Key}' via fuzzy title match for '{gameName}'");
                        return kvp.Value;
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

            // Create the profile
            var profile = DriverSettingsProfile.CreateProfile(_session, gameName, null);

            // Add the exe as an application
            ProfileApplication.CreateApplication(profile, gameExe, gameName, "", Array.Empty<string>(), false, "");

            _session.Save();

            // Cache the new profile
            _cachedProfiles.TryAdd(gameName, profile);

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
