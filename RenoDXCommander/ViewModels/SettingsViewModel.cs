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
    [ObservableProperty] private bool _globalShadersOff;
    [ObservableProperty] private string _screenshotPath = "";
    [ObservableProperty] private string _overlayHotkey = "36,0,0,0";
    [ObservableProperty] private string _screenshotHotkey = "44,0,0,0";
    [ObservableProperty] private string _ulOsdHotkey = "F12";
    [ObservableProperty] private bool _ulSharedPresets = false;
    [ObservableProperty] private bool _ulDlssHooks = true;
    [ObservableProperty] private int _ulTargetFps; // 0 = off/disabled
    [ObservableProperty] private string _osHotkey = "Insert";
    [ObservableProperty] private string _osGpuType = "NVIDIA";
    [ObservableProperty] private bool _osDlssInputs = true;
    [ObservableProperty] private bool _osFirstTimeWarningDismissed;
    [ObservableProperty] private bool _ueExtendedWarningDismissed;
    [ObservableProperty] private bool _perGameScreenshotFolders;
    [ObservableProperty] private bool _rsVariableListUseTabs = true; // Group effect files with tabs instead of a tree
    [ObservableProperty] private bool _addonWarningDismissed;
    [ObservableProperty] private bool _dxvkWarningDismissed;
    [ObservableProperty] private bool _mfgWarningDismissed;
    [ObservableProperty] private bool _engineBadgeWarningDismissed;
    [ObservableProperty] private bool _lumaRenodxCombinedWarningDismissed;
    [ObservableProperty] private List<string> _enabledGlobalAddons = new();
    [ObservableProperty] private bool _firstLaunchSetupDone;
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
    [ObservableProperty] private int _peakNits;
    [ObservableProperty] private bool _peakNitsEnabled = true;
    [ObservableProperty] private HashSet<int> _peakNitsPresets = new() { 1, 2, 3 };

    // ── Component Auto-Update ────────────────────────────────────────────────
    /// <summary>When true, silently installs component updates in the background after an update check.</summary>
    [ObservableProperty] private bool _autoUpdateComponents;

    // ── DLSS/Streamline Auto-Update ───────────────────────────────────────────
    [ObservableProperty] private bool _autoUpdateDlss;
    [ObservableProperty] private bool _autoUpdateStreamline;
    [ObservableProperty] private string _lastKnownNewestDlss = "";
    [ObservableProperty] private string _lastKnownNewestStreamline = "";
    [ObservableProperty] private bool _hdrAutoToggle;
    [ObservableProperty] private List<uint> _hdrTargetDisplays = new();
    // ── Resolution Auto-Toggle (dev-only) ─────────────────────────────────────
    [ObservableProperty] private bool _resolutionAutoToggle;
    /// <summary>Target resolution key in "WxH@Hz" format. Empty = no override.</summary>
    [ObservableProperty] private string _resolutionTarget = "";
    [ObservableProperty] private List<uint> _resTargetDisplays = new();
    [ObservableProperty] private bool _dropHelperEnabled = true;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _recentGamesMenu;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private List<string> _recentLaunches = new();

    // ── Nexus Mods integration (dev-unlocked only) ────────────────────────────
    [ObservableProperty] private string _nexusApiKey = "";
    [ObservableProperty] private bool _nexusIsPremium;
    [ObservableProperty] private string _nexusUsername = "";

    // ── Digital Vibrance ──────────────────────────────────────────────────────
    /// <summary>Per-display DVC values. Key = display index (string), Value = 0-100.</summary>
    public Dictionary<string, int> DigitalVibranceSettings { get; set; } = new();

    // ── Detail panel section collapse state ───────────────────────────────────
    /// <summary>
    /// Section keys that are currently collapsed in the detail panel.
    /// Keys: "Components", "GameOverrides", "NeuralRendering", "NvidiaProfile", "Management"
    /// Absent = expanded (default).
    /// </summary>
    public HashSet<string> CollapsedDetailSections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Detail panel section order ────────────────────────────────────────────
    /// <summary>
    /// Ordered list of section keys for the detail panel.
    /// Default order: Components, GameOverrides, NeuralRendering, NvidiaProfile, Management.
    /// Absent or incomplete = use default order.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSectionOrder = new[]
        { "Components", "GameOverrides", "NeuralRendering", "NvidiaProfile", "Management", "Extras" };

    public List<string> DetailSectionOrder { get; set; } = new(DefaultSectionOrder);

    // ── DLSS/Streamline Defaults ──────────────────────────────────────────────
    [ObservableProperty] private string _defaultDlssVersion = "";
    [ObservableProperty] private string _defaultDlssdVersion = "";
    [ObservableProperty] private string _defaultDlssgVersion = "";
    [ObservableProperty] private string _defaultDlssnrVersion = "";
    [ObservableProperty] private string _defaultStreamlineVersion = "";
    [ObservableProperty] private uint _defaultSrPreset = 0;
    [ObservableProperty] private uint _defaultRrPreset = 0;
    [ObservableProperty] private uint _defaultFgPreset = 0;
    [ObservableProperty] private uint _defaultNrPreset = 0;
    [ObservableProperty] private uint _defaultSrRenderScale = 0;
    [ObservableProperty] private uint _defaultRrRenderScale = 0;

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

        if (s.TryGetValue("GlobalShadersOff", out var gsoffVal))
            GlobalShadersOff = gsoffVal == "true";

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

        if (s.TryGetValue("UlDlssHooks", out var uldhVal))
            UlDlssHooks = uldhVal == "true";

        if (s.TryGetValue("UlTargetFps", out var ultfVal) && int.TryParse(ultfVal, out var ultfInt))
            UlTargetFps = ultfInt;

        if (s.TryGetValue("OsHotkey", out var oshVal))
            OsHotkey = oshVal ?? "Insert";

        if (s.TryGetValue("OsGpuType", out var ogtVal))
            OsGpuType = ogtVal ?? "NVIDIA";

        if (s.TryGetValue("OsDlssInputs", out var odiVal))
            OsDlssInputs = odiVal == "true";

        if (s.TryGetValue("OsFirstTimeWarningDismissed", out var osftwVal))
            OsFirstTimeWarningDismissed = osftwVal == "true";

        if (s.TryGetValue("UeExtendedWarningDismissed", out var uewdVal))
            UeExtendedWarningDismissed = uewdVal == "true";

        if (s.TryGetValue("PerGameScreenshotFolders", out var pgsfVal))
            PerGameScreenshotFolders = pgsfVal == "true";

        if (s.TryGetValue("RsVariableListUseTabs", out var rsTabsVal))
            RsVariableListUseTabs = rsTabsVal != "false"; // default true

        if (s.TryGetValue("AddonWarningDismissed", out var awdVal))
            AddonWarningDismissed = awdVal == "true";

        if (s.TryGetValue("DxvkWarningDismissed", out var dwdVal))
            DxvkWarningDismissed = dwdVal == "true";

        if (s.TryGetValue("MfgWarningDismissed", out var mwdVal))
            MfgWarningDismissed = mwdVal == "true";

        if (s.TryGetValue("EngineBadgeWarningDismissed", out var ebwdVal))
            EngineBadgeWarningDismissed = ebwdVal == "true";

        if (s.TryGetValue("EnabledGlobalAddons", out var egaVal))
        {
            try
            {
                var addons = JsonSerializer.Deserialize<List<string>>(egaVal) ?? new();
                // Migration: remove old "RenoDX DLSS5" name — renamed to "DLSS5 Tool"
                addons.RemoveAll(a => a.Equals("RenoDX DLSS5", StringComparison.OrdinalIgnoreCase));
                EnabledGlobalAddons = addons;
            }
            catch { EnabledGlobalAddons = new(); }
        }

        if (s.TryGetValue("FirstLaunchSetupDone", out var flsdVal))
            FirstLaunchSetupDone = flsdVal == "true";

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
        if (s.TryGetValue("PeakNits", out var pnVal) && int.TryParse(pnVal, out var pnInt)) PeakNits = pnInt;
        if (s.TryGetValue("PeakNitsEnabled", out var pneVal)) PeakNitsEnabled = pneVal != "false"; // default true
        if (s.TryGetValue("PeakNitsPresets", out var pnpVal))
        {
            try { PeakNitsPresets = System.Text.Json.JsonSerializer.Deserialize<HashSet<int>>(pnpVal) ?? new() { 1, 2, 3 }; }
            catch { PeakNitsPresets = new() { 1, 2, 3 }; }
        }
        if (s.TryGetValue("AutoUpdateComponents", out var aucVal)) AutoUpdateComponents = aucVal == "true";
        if (s.TryGetValue("AutoUpdateDlss", out var audVal)) AutoUpdateDlss = audVal == "true";
        if (s.TryGetValue("AutoUpdateStreamline", out var ausVal)) AutoUpdateStreamline = ausVal == "true";
        if (s.TryGetValue("LastKnownNewestDlss", out var lkndVal)) LastKnownNewestDlss = lkndVal ?? "";
        if (s.TryGetValue("LastKnownNewestStreamline", out var lknsVal)) LastKnownNewestStreamline = lknsVal ?? "";
        if (s.TryGetValue("HdrAutoToggle", out var hatVal)) HdrAutoToggle = hatVal == "true";
        if (s.TryGetValue("HdrTargetDisplays", out var htdVal))
        {
            try { HdrTargetDisplays = System.Text.Json.JsonSerializer.Deserialize<List<uint>>(htdVal) ?? new(); }
            catch { HdrTargetDisplays = new(); }
        }
        if (s.TryGetValue("ResolutionAutoToggle", out var ratVal)) ResolutionAutoToggle = ratVal == "true";
        if (s.TryGetValue("ResolutionTarget", out var rtVal)) ResolutionTarget = rtVal ?? "";
        if (s.TryGetValue("ResTargetDisplays", out var rtdVal))
        {
            try { ResTargetDisplays = System.Text.Json.JsonSerializer.Deserialize<List<uint>>(rtdVal) ?? new(); }
            catch { ResTargetDisplays = new(); }
        }
        if (s.TryGetValue("DropHelperEnabled", out var dheVal)) DropHelperEnabled = dheVal != "false"; // default true
        if (s.TryGetValue("CloseToTray", out var cttVal)) CloseToTray = cttVal == "true";
        if (s.TryGetValue("RecentGamesMenu", out var rgmVal)) RecentGamesMenu = rgmVal == "true";
        if (s.TryGetValue("StartWithWindows", out var swwVal)) StartWithWindows = swwVal == "true";
        if (s.TryGetValue("RecentLaunches", out var rlVal))
        {
            try { RecentLaunches = System.Text.Json.JsonSerializer.Deserialize<List<string>>(rlVal) ?? new(); }
            catch { RecentLaunches = new(); }
        }
        // Nexus Mods (dev-unlocked only — stored but never logged)
        if (s.TryGetValue("NexusApiKey",    out var nakVal)) NexusApiKey    = nakVal ?? "";
        if (s.TryGetValue("NexusIsPremium", out var nipVal)) NexusIsPremium = nipVal == "true";
        if (s.TryGetValue("NexusUsername",  out var nunVal)) NexusUsername  = nunVal ?? "";

        // DLSS/Streamline defaults
        if (s.TryGetValue("DefaultDlssVersion", out var ddv)) DefaultDlssVersion = ddv ?? "";
        if (s.TryGetValue("DefaultDlssdVersion", out var ddrv)) DefaultDlssdVersion = ddrv ?? "";
        if (s.TryGetValue("DefaultDlssgVersion", out var ddgv)) DefaultDlssgVersion = ddgv ?? "";
        if (s.TryGetValue("DefaultDlssnrVersion", out var ddnrv)) DefaultDlssnrVersion = ddnrv ?? "";
        if (s.TryGetValue("DefaultStreamlineVersion", out var dsv)) DefaultStreamlineVersion = dsv ?? "";
        if (s.TryGetValue("DefaultSrPreset", out var dsp) && uint.TryParse(dsp, out var dspVal)) DefaultSrPreset = dspVal;
        if (s.TryGetValue("DefaultRrPreset", out var drp) && uint.TryParse(drp, out var drpVal)) DefaultRrPreset = drpVal;
        if (s.TryGetValue("DefaultFgPreset", out var dfp) && uint.TryParse(dfp, out var dfpVal)) DefaultFgPreset = dfpVal;
        if (s.TryGetValue("DefaultNrPreset", out var dnrp) && uint.TryParse(dnrp, out var dnrpVal)) DefaultNrPreset = dnrpVal;
        if (s.TryGetValue("DefaultSrRenderScale", out var dsr) && uint.TryParse(dsr, out var dsrVal)) DefaultSrRenderScale = dsrVal;
        if (s.TryGetValue("DefaultRrRenderScale", out var drr) && uint.TryParse(drr, out var drrVal)) DefaultRrRenderScale = drrVal;

        // Digital Vibrance per-display settings
        if (s.TryGetValue("DigitalVibrance", out var dvcVal))
        {
            try { DigitalVibranceSettings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(dvcVal) ?? new(); }
            catch { DigitalVibranceSettings = new(); }
        }

        // Detail panel section collapse state
        if (s.TryGetValue("CollapsedDetailSections", out var cdsVal))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(cdsVal);
                CollapsedDetailSections = new HashSet<string>(list ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch { CollapsedDetailSections = new(StringComparer.OrdinalIgnoreCase); }
        }

        // Detail panel section order
        if (s.TryGetValue("DetailSectionOrder", out var dsoVal))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dsoVal);
                // Merge: keep loaded order for known keys, append any missing ones at end
                if (loaded != null)
                {
                    var known = new HashSet<string>(DefaultSectionOrder, StringComparer.OrdinalIgnoreCase);
                    var merged = loaded.Where(k => known.Contains(k)).ToList();
                    foreach (var def in DefaultSectionOrder)
                        if (!merged.Contains(def, StringComparer.OrdinalIgnoreCase)) merged.Add(def);
                    DetailSectionOrder = merged;
                }
            }
            catch { DetailSectionOrder = new(DefaultSectionOrder); }
        }
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
        s["GlobalShadersOff"]  = GlobalShadersOff ? "true" : "false";
        if (!string.IsNullOrWhiteSpace(AddonWatchFolder))
            s["AddonWatchFolder"] = AddonWatchFolder;
        s["ScreenshotPath"] = ScreenshotPath;
        s["OverlayHotkey"] = OverlayHotkey;
        s["ScreenshotHotkey"] = ScreenshotHotkey;
        s["UlOsdHotkey"] = UlOsdHotkey;
        s["UlSharedPresets"] = UlSharedPresets ? "true" : "false";
        s["UlDlssHooks"] = UlDlssHooks ? "true" : "false";
        if (UlTargetFps > 0) s["UlTargetFps"] = UlTargetFps.ToString();
        else s.Remove("UlTargetFps"); // 0 = off — remove stale value
        s["OsHotkey"] = OsHotkey;
        s["OsGpuType"] = OsGpuType;
        s["OsDlssInputs"] = OsDlssInputs ? "true" : "false";
        s["OsFirstTimeWarningDismissed"] = OsFirstTimeWarningDismissed ? "true" : "false";
        s["UeExtendedWarningDismissed"] = UeExtendedWarningDismissed ? "true" : "false";
        s["PerGameScreenshotFolders"] = PerGameScreenshotFolders ? "true" : "false";
        s["RsVariableListUseTabs"]    = RsVariableListUseTabs ? "true" : "false";
        s["AddonWarningDismissed"] = AddonWarningDismissed ? "true" : "false";
        s["DxvkWarningDismissed"] = DxvkWarningDismissed ? "true" : "false";
        s["MfgWarningDismissed"] = MfgWarningDismissed ? "true" : "false";
        s["EngineBadgeWarningDismissed"] = EngineBadgeWarningDismissed ? "true" : "false";
        s["EnabledGlobalAddons"] = JsonSerializer.Serialize(EnabledGlobalAddons);
        s["FirstLaunchSetupDone"] = FirstLaunchSetupDone ? "true" : "false";
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
        if (PeakNits > 0) s["PeakNits"] = PeakNits.ToString();
        s["PeakNitsEnabled"] = PeakNitsEnabled ? "true" : "false";
        if (PeakNitsPresets.Count < 3)
            s["PeakNitsPresets"] = System.Text.Json.JsonSerializer.Serialize(PeakNitsPresets);
        else
            s.Remove("PeakNitsPresets"); // All 3 checked = default — remove stale non-default value
        if (AutoUpdateComponents) s["AutoUpdateComponents"] = "true"; else s.Remove("AutoUpdateComponents");
        if (AutoUpdateDlss) s["AutoUpdateDlss"] = "true"; else s.Remove("AutoUpdateDlss");
        if (AutoUpdateStreamline) s["AutoUpdateStreamline"] = "true"; else s.Remove("AutoUpdateStreamline");
        if (!string.IsNullOrEmpty(LastKnownNewestDlss)) s["LastKnownNewestDlss"] = LastKnownNewestDlss;
        if (!string.IsNullOrEmpty(LastKnownNewestStreamline)) s["LastKnownNewestStreamline"] = LastKnownNewestStreamline;
        s["HdrAutoToggle"] = HdrAutoToggle ? "true" : "false";
        if (HdrTargetDisplays.Count > 0) s["HdrTargetDisplays"] = System.Text.Json.JsonSerializer.Serialize(HdrTargetDisplays);
        s["ResolutionAutoToggle"] = ResolutionAutoToggle ? "true" : "false";
        if (!string.IsNullOrEmpty(ResolutionTarget)) s["ResolutionTarget"] = ResolutionTarget;
        if (ResTargetDisplays.Count > 0) s["ResTargetDisplays"] = System.Text.Json.JsonSerializer.Serialize(ResTargetDisplays);
        if (!DropHelperEnabled) s["DropHelperEnabled"] = "false";
        else s["DropHelperEnabled"] = "true";
        s["CloseToTray"] = CloseToTray ? "true" : "false";
        s["RecentGamesMenu"] = RecentGamesMenu ? "true" : "false";
        s["StartWithWindows"] = StartWithWindows ? "true" : "false";
        if (RecentLaunches.Count > 0) s["RecentLaunches"] = System.Text.Json.JsonSerializer.Serialize(RecentLaunches);
        // Nexus Mods — key stored as-is (local settings.json, not transmitted anywhere)
        if (!string.IsNullOrEmpty(NexusApiKey))    s["NexusApiKey"]    = NexusApiKey;
        if (NexusIsPremium)                        s["NexusIsPremium"] = "true";
        if (!string.IsNullOrEmpty(NexusUsername))  s["NexusUsername"]  = NexusUsername;

        // DLSS/Streamline defaults
        if (!string.IsNullOrEmpty(DefaultDlssVersion)) s["DefaultDlssVersion"] = DefaultDlssVersion;
        if (!string.IsNullOrEmpty(DefaultDlssdVersion)) s["DefaultDlssdVersion"] = DefaultDlssdVersion;
        if (!string.IsNullOrEmpty(DefaultDlssgVersion)) s["DefaultDlssgVersion"] = DefaultDlssgVersion;
        if (!string.IsNullOrEmpty(DefaultDlssnrVersion)) s["DefaultDlssnrVersion"] = DefaultDlssnrVersion;
        if (!string.IsNullOrEmpty(DefaultStreamlineVersion)) s["DefaultStreamlineVersion"] = DefaultStreamlineVersion;
        if (DefaultSrPreset != 0) s["DefaultSrPreset"] = DefaultSrPreset.ToString();
        if (DefaultRrPreset != 0) s["DefaultRrPreset"] = DefaultRrPreset.ToString();
        if (DefaultFgPreset != 0) s["DefaultFgPreset"] = DefaultFgPreset.ToString();
        if (DefaultNrPreset != 0) s["DefaultNrPreset"] = DefaultNrPreset.ToString();
        if (DefaultSrRenderScale != 0) s["DefaultSrRenderScale"] = DefaultSrRenderScale.ToString();
        else s.Remove("DefaultSrRenderScale");
        if (DefaultRrRenderScale != 0) s["DefaultRrRenderScale"] = DefaultRrRenderScale.ToString();
        else s.Remove("DefaultRrRenderScale");

        // Digital Vibrance per-display settings
        if (DigitalVibranceSettings.Count > 0)
            s["DigitalVibrance"] = System.Text.Json.JsonSerializer.Serialize(DigitalVibranceSettings);

        // Detail panel section collapse state — only save when any section is collapsed
        if (CollapsedDetailSections.Count > 0)
            s["CollapsedDetailSections"] = System.Text.Json.JsonSerializer.Serialize(CollapsedDetailSections.ToList());
        else
            s.Remove("CollapsedDetailSections");

        // Detail panel section order — only save when different from default
        if (!DetailSectionOrder.SequenceEqual(DefaultSectionOrder, StringComparer.OrdinalIgnoreCase))
            s["DetailSectionOrder"] = System.Text.Json.JsonSerializer.Serialize(DetailSectionOrder);
        else
            s.Remove("DetailSectionOrder");
    }

    public void LoadThemeAndDensity()
    {
        // Theme/density removed — no longer used
    }
}
