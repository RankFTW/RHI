using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// Owns settings persistence (load/save settings file), theme, density,
/// verbose logging, shader pack selection, and related computed UI properties.
/// Extracted from MainViewModel per Requirement 1.1.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    // Settings stored as JSON — ApplicationData.Current throws in unpackaged WinUI 3
    private static readonly string _settingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "settings.json");

    [ObservableProperty] private bool _skipUpdateCheck;
    [ObservableProperty] private bool _betaOptIn;
    [ObservableProperty] private bool _verboseLogging;
    [ObservableProperty] private string _lastSeenVersion = "";
    [ObservableProperty] private List<string> _selectedShaderPacks = new();
    [ObservableProperty] private string _addonWatchFolder = "";
    [ObservableProperty] private bool _useCustomShaders;
    [ObservableProperty] private string _screenshotPath = "";
    [ObservableProperty] private string _overlayHotkey = "36,0,0,0";
    [ObservableProperty] private string _screenshotHotkey = "44,0,0,0";
    [ObservableProperty] private string _ulOsdHotkey = "F12";
    [ObservableProperty] private bool _ulSharedPresets = false;
    [ObservableProperty] private string _osHotkey = "Insert";
    [ObservableProperty] private string _osGpuType = "NVIDIA";
    [ObservableProperty] private bool _osDlssInputs = true;
    [ObservableProperty] private bool _osFirstTimeWarningDismissed;
    [ObservableProperty] private bool _perGameScreenshotFolders;
    [ObservableProperty] private bool _addonWarningDismissed;
    [ObservableProperty] private List<string> _enabledGlobalAddons = new();
    [ObservableProperty] private bool _globalSkipRdxUpdates;
    [ObservableProperty] private bool _globalSkipRsUpdates;
    [ObservableProperty] private bool _globalSkipUlUpdates;
    [ObservableProperty] private bool _globalSkipDcUpdates;
    [ObservableProperty] private bool _globalSkipOsUpdates;
    [ObservableProperty] private bool _globalSkipRefUpdates;
    [ObservableProperty] private bool _cacheAllShaders = true;
    [ObservableProperty] private string _lastUpdateCheckUtc = "";
    [ObservableProperty] private string _dxvkVariant = "Development";
    [ObservableProperty] private string _reShadeChannel = "Stable";

    /// <summary>
    /// Optional callback invoked after any settings-specific property changes,
    /// so that MainViewModel can persist the full settings bundle.
    /// </summary>
    public Action? SettingsChanged { get; set; }

    /// <summary>
    /// Guard flag — true while settings are being loaded so that
    /// property-change handlers don't trigger saves mid-load.
    /// </summary>
    public bool IsLoadingSettings { get; set; }

    // ── Verbose logging ───────────────────────────────────────────────────────────

    partial void OnVerboseLoggingChanged(bool value)
    {
        CrashReporter.VerboseLogging = value;
    }

    // ── Settings file I/O ─────────────────────────────────────────────────────────

    public static Dictionary<string, string> LoadSettingsFile()
    {
        try
        {
            if (!System.IO.File.Exists(_settingsFilePath)) return new(StringComparer.OrdinalIgnoreCase);
            var json = System.IO.File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsViewModel.LoadSettingsFile] Failed to load settings — {ex.Message}"); return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void SaveSettingsFile(Dictionary<string, string> settings)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsFilePath)!);
        var json = JsonSerializer.Serialize(settings);
        FileHelper.WriteAllTextWithRetry(_settingsFilePath, json, "SettingsViewModel.SaveSettingsFile");
    }

    /// <summary>
    /// Loads settings-specific values (SkipUpdateCheck, VerboseLogging,
    /// LastSeenVersion, SelectedShaderPacks) from the given settings dictionary.
    /// Called by MainViewModel during LoadNameMappings.
    /// </summary>
    public void LoadSettingsFromDict(Dictionary<string, string> s)
    {
        if (s.TryGetValue("SkipUpdateCheck", out var sucVal))
            SkipUpdateCheck = sucVal == "true";

        if (s.TryGetValue("BetaOptIn", out var boVal))
            BetaOptIn = boVal == "true";

        if (s.TryGetValue("VerboseLogging", out var vlVal))
            VerboseLogging = vlVal == "true";

        if (s.TryGetValue("LastSeenVersion", out var lsvVal))
            LastSeenVersion = lsvVal ?? "";

        // Migration: retain SelectedShaderPacks only when the persisted mode
        // was "Select".  Any other value (or absent key) means the user was on
        // an old mode — start with an empty selection.
        var wasSelectMode = s.TryGetValue("ShaderDeployMode", out var sdm)
                            && string.Equals(sdm, "Select", StringComparison.OrdinalIgnoreCase);

        if (wasSelectMode && s.TryGetValue("SelectedShaderPacks", out var sspVal))
        {
            try
            {
                SelectedShaderPacks = JsonSerializer.Deserialize<List<string>>(sspVal) ?? new();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsViewModel.LoadSettingsFromDict] Failed to deserialize SelectedShaderPacks — {ex.Message}");
                SelectedShaderPacks = new();
            }
        }
        else
        {
            SelectedShaderPacks = new();
        }

        // Ensure Lilium is included by default on fresh installs (no prior selection),
        // but respect the user's choice if they previously saved a selection without it.
        if (!wasSelectMode && SelectedShaderPacks.Count == 0)
        {
            if (!SelectedShaderPacks.Contains("Lilium", StringComparer.OrdinalIgnoreCase))
                SelectedShaderPacks.Add("Lilium");
        }

        if (s.TryGetValue("UseCustomShaders", out var ucsVal))
            UseCustomShaders = ucsVal == "true";

        if (s.TryGetValue("AddonWatchFolder", out var awfVal))
            AddonWatchFolder = awfVal ?? "";

        if (s.TryGetValue("ScreenshotPath", out var spVal))
            ScreenshotPath = spVal ?? "";

        if (s.TryGetValue("OverlayHotkey", out var ohVal))
            OverlayHotkey = ohVal ?? "36,0,0,0";

        if (s.TryGetValue("ScreenshotHotkey", out var sshVal))
            ScreenshotHotkey = sshVal ?? "44,0,0,0";

        if (s.TryGetValue("UlOsdHotkey", out var ulhVal))
            UlOsdHotkey = ulhVal ?? "F12";

        if (s.TryGetValue("UlSharedPresets", out var ulspVal))
            UlSharedPresets = ulspVal == "true";

        if (s.TryGetValue("OsHotkey", out var oshVal))
            OsHotkey = oshVal ?? "Insert";

        if (s.TryGetValue("OsGpuType", out var ogtVal))
            OsGpuType = ogtVal ?? "NVIDIA";

        if (s.TryGetValue("OsDlssInputs", out var odiVal))
            OsDlssInputs = odiVal == "true";

        if (s.TryGetValue("OsFirstTimeWarningDismissed", out var osftwVal))
            OsFirstTimeWarningDismissed = osftwVal == "true";

        if (s.TryGetValue("PerGameScreenshotFolders", out var pgsfVal))
            PerGameScreenshotFolders = pgsfVal == "true";

        if (s.TryGetValue("AddonWarningDismissed", out var awdVal))
            AddonWarningDismissed = awdVal == "true";

        if (s.TryGetValue("EnabledGlobalAddons", out var egaVal))
        {
            try { EnabledGlobalAddons = JsonSerializer.Deserialize<List<string>>(egaVal) ?? new(); }
            catch { EnabledGlobalAddons = new(); }
        }

        if (s.TryGetValue("GlobalSkipRdxUpdates", out var gsrVal)) GlobalSkipRdxUpdates = gsrVal == "true";
        if (s.TryGetValue("GlobalSkipRsUpdates", out var gssVal)) GlobalSkipRsUpdates = gssVal == "true";
        if (s.TryGetValue("GlobalSkipUlUpdates", out var gsuVal)) GlobalSkipUlUpdates = gsuVal == "true";
        if (s.TryGetValue("GlobalSkipDcUpdates", out var gsdVal)) GlobalSkipDcUpdates = gsdVal == "true";
        if (s.TryGetValue("GlobalSkipOsUpdates", out var gsoVal)) GlobalSkipOsUpdates = gsoVal == "true";
        if (s.TryGetValue("GlobalSkipRefUpdates", out var gsrefVal)) GlobalSkipRefUpdates = gsrefVal == "true";
        if (s.TryGetValue("CacheAllShaders", out var casVal)) CacheAllShaders = casVal != "false"; // default true
        if (s.TryGetValue("LastUpdateCheckUtc", out var luc)) LastUpdateCheckUtc = luc;
        if (s.TryGetValue("DxvkVariant", out var dvVal)) DxvkVariant = dvVal ?? "Development";
        if (s.TryGetValue("ReShadeChannel", out var rscVal)) ReShadeChannel = rscVal ?? "Stable";
    }

    /// <summary>
    /// Writes settings-specific values into the given settings dictionary.
    /// Called by MainViewModel during SaveNameMappings.
    /// </summary>
    public void SaveSettingsToDict(Dictionary<string, string> s)
    {
        s["SkipUpdateCheck"]   = SkipUpdateCheck ? "true" : "false";
        s["BetaOptIn"]         = BetaOptIn ? "true" : "false";
        s["VerboseLogging"]    = VerboseLogging ? "true" : "false";
        s["LastSeenVersion"]   = LastSeenVersion;
        s["ShaderDeployMode"]  = SelectedShaderPacks.Count > 0 ? "Select" : "Off";
        s["SelectedShaderPacks"] = JsonSerializer.Serialize(SelectedShaderPacks);
        s["UseCustomShaders"]  = UseCustomShaders ? "true" : "false";
        if (!string.IsNullOrWhiteSpace(AddonWatchFolder))
            s["AddonWatchFolder"] = AddonWatchFolder;
        s["ScreenshotPath"] = ScreenshotPath;
        s["OverlayHotkey"] = OverlayHotkey;
        s["ScreenshotHotkey"] = ScreenshotHotkey;
        s["UlOsdHotkey"] = UlOsdHotkey;
        s["UlSharedPresets"] = UlSharedPresets ? "true" : "false";
        s["OsHotkey"] = OsHotkey;
        s["OsGpuType"] = OsGpuType;
        s["OsDlssInputs"] = OsDlssInputs ? "true" : "false";
        s["OsFirstTimeWarningDismissed"] = OsFirstTimeWarningDismissed ? "true" : "false";
        s["PerGameScreenshotFolders"] = PerGameScreenshotFolders ? "true" : "false";
        s["AddonWarningDismissed"] = AddonWarningDismissed ? "true" : "false";
        s["EnabledGlobalAddons"] = JsonSerializer.Serialize(EnabledGlobalAddons);
        s["GlobalSkipRdxUpdates"] = GlobalSkipRdxUpdates ? "true" : "false";
        s["GlobalSkipRsUpdates"] = GlobalSkipRsUpdates ? "true" : "false";
        s["GlobalSkipUlUpdates"] = GlobalSkipUlUpdates ? "true" : "false";
        s["GlobalSkipDcUpdates"] = GlobalSkipDcUpdates ? "true" : "false";
        s["GlobalSkipOsUpdates"] = GlobalSkipOsUpdates ? "true" : "false";
        s["GlobalSkipRefUpdates"] = GlobalSkipRefUpdates ? "true" : "false";
        s["CacheAllShaders"] = CacheAllShaders ? "true" : "false";
        s["LastUpdateCheckUtc"] = LastUpdateCheckUtc;
        s["DxvkVariant"] = DxvkVariant;
        s["ReShadeChannel"] = ReShadeChannel;
    }

    public void LoadThemeAndDensity()
    {
        // Theme/density removed — no longer used
    }
}
