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

    // ── State ─────────────────────────────────────────────────────────────────
    private DriverSettingsSession? _session;
    private Dictionary<string, DriverSettingsProfile>? _cachedProfiles;
    private bool _isSupported;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    public bool IsSupported => _isSupported;

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
                CrashReporter.Log($"[DlssPresetService.SetPreset] No profile found for '{gameName}'");
                return false;
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
}
