using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Owns name mappings, game renames, wiki exclusions, and settings persistence.
/// Extracted from MainViewModel per Requirement 1.5.
/// </summary>
public class GameNameService : IGameNameService
{
    private readonly IGameDetectionService _gameDetectionService;
    private readonly IModInstallService _installer;
    private readonly IAuxInstallService _auxInstaller;
    private readonly ILumaService _lumaService;

    // ── Persisted data ────────────────────────────────────────────────────────
    private Dictionary<string, string> _nameMappings = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tracks which name mappings were injected by the remote manifest (not user-set).</summary>
    private readonly HashSet<string> _manifestNameMappingKeys = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _gameRenames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _wikiExclusions = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hiddenGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _favouriteGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _ueExtendedGames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Games where the user has explicitly opted OUT of UE-Extended (wants standard generic UE addon).</summary>
    private HashSet<string> _ueExtendedOptOutGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedReShade = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedRenoDx = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedUl = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedDc = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedOs = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedRef = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _perGameShaderMode = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _perGameShaderSelection = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _perGameAddonMode = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _perGameAddonSelection = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lumaEnabledGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lumaDisabledGames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Games where Luma TAA Engine.ini settings are enabled (r.DefaultFeature.AntiAliasing=2, r.PostProcessAAQuality=4).</summary>
    private HashSet<string> _lumaTaaEnabled = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _normalReShadeGames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Games where RHI should NOT automatically re-merge reshade.ini. Composite-keyed "GameName|Store".</summary>
    private HashSet<string> _rsIniLockedGames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _folderOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _vulkanRenderingPaths = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _bitnessOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _apiOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _reShadeChannelOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _dxvkVariantOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _liliumPresetOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game OptiScaler variant override. Key = "GameName|Store", Value = "Stable" or "Nightly".</summary>
    private Dictionary<string, string> _osVariantOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game Neural Rendering method override. Key = "GameName|Store", Value = "DLSS5Tool", "DLSS5ToolBridge", "ShortFuse", or "Feeder". Absent = auto-detect.</summary>
    private Dictionary<string, string> _nrMethodOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game HDR auto-toggle overrides. Key = game name, Value = "On" or "Off". Absent = use global default.</summary>
    private Dictionary<string, string> _hdrToggleOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game Resolution auto-toggle overrides. Key = game name, Value = "On" or "Off". Absent = use global default.</summary>
    private Dictionary<string, string> _resToggleOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game launch executable overrides. Key = game name, Value = absolute exe path.</summary>
    private Dictionary<string, string> _launchExeOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game launch arguments. Key = game name, Value = arguments string.</summary>
    private Dictionary<string, string> _launchArgsOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game engine version overrides. Key = game name, Value = hint string (e.g. "Unreal Engine 4", "Unreal Engine 5", "Unreal Engine 5.7"). Only applied when auto-detection produces a versionless result.</summary>
    private Dictionary<string, string> _engineVersionOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game custom ReShade DLL selection. Key = game name, Value = DLL filename (not full path). The DLL resides in Custom\ReShade\ folder.</summary>
    private Dictionary<string, string> _customReShadeSelection = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games with RTX HDR enabled via NVIDIA driver profile.</summary>
    private HashSet<string> _rtxHdrGames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games where Streamline should be deployed to the OptiScaler subfolder. Composite-keyed "GameName|Store".</summary>
    private HashSet<string> _osDeployStreamline = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games where DLSS Enabler should be deployed to the OptiScaler subfolder. Composite-keyed "GameName|Store".</summary>
    private HashSet<string> _osDeployDlssEnabler = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games where Dilated Motion Vectors should be disabled (Off) in Engine.ini. Composite-keyed "GameName|Store".</summary>
    private HashSet<string> _osDilatedMotionVectorsOff = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>FSR crash fix level per game. Key = "GameName|Store", Value = "FSR2", "FSR3", or "FSR3.1". Absent = None.</summary>
    private Dictionary<string, string> _osFsrCrashFix = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game FG Input override. Key = "GameName|Store", Value = INI string. Absent = "auto".</summary>
    private Dictionary<string, string> _osFgInput = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game FG Output override. Key = "GameName|Store", Value = INI string. Absent = "auto".</summary>
    private Dictionary<string, string> _osFgOutput = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game FG Nvngx Replacement. Key = "GameName|Store", Value = INI string. Absent = "None".</summary>
    private Dictionary<string, string> _osFgNvngxReplacement = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games where FSR-FG swapchain override is enabled in Engine.ini. Composite-keyed "GameName|Store".</summary>
    private HashSet<string> _osFsrFgSwapchain = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games where upscaler plugin is enabled via Engine.ini. Composite-keyed "GameName|Store".</summary>
    private HashSet<string> _osUpscalerPlugin = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game Streamline version override. Key = "GameName|Store", Value = version string. Absent = use default.</summary>
    private Dictionary<string, string> _osStreamlineVersion = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-game Ultimate ASI Loader installed DLL name. Key = "GameName|Store", Value = dll filename. Absent = not installed.</summary>
    private Dictionary<string, string> _ualInstalledAs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Games where ShortFuse auto-config is DISABLED. Composite-keyed "GameName|Store". Absent = enabled (default).</summary>
    private HashSet<string> _sfAutoConfigDisabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps current (renamed) game name → original store-detected name.</summary>
    private Dictionary<string, string> _originalDetectedNames = new(StringComparer.OrdinalIgnoreCase);

    // ── Public accessors for MainViewModel ────────────────────────────────────
    public Dictionary<string, string> NameMappings => _nameMappings;
    public Dictionary<string, string> GameRenames => _gameRenames;
    public HashSet<string> WikiExclusions => _wikiExclusions;
    public HashSet<string> HiddenGames => _hiddenGames;
    public HashSet<string> FavouriteGames => _favouriteGames;
    public HashSet<string> UeExtendedGames => _ueExtendedGames;
    /// <summary>Games where the user has explicitly opted out of UE-Extended.</summary>
    public HashSet<string> UeExtendedOptOutGames => _ueExtendedOptOutGames;
    public HashSet<string> UpdateAllExcludedReShade => _updateAllExcludedReShade;
    public HashSet<string> UpdateAllExcludedRenoDx => _updateAllExcludedRenoDx;
    public HashSet<string> UpdateAllExcludedUl => _updateAllExcludedUl;
    public HashSet<string> UpdateAllExcludedDc => _updateAllExcludedDc;
    public HashSet<string> UpdateAllExcludedOs => _updateAllExcludedOs;
    public HashSet<string> UpdateAllExcludedRef => _updateAllExcludedRef;
    public Dictionary<string, string> PerGameShaderMode => _perGameShaderMode;
    public Dictionary<string, List<string>> PerGameShaderSelection => _perGameShaderSelection;
    public Dictionary<string, string> PerGameAddonMode => _perGameAddonMode;
    public Dictionary<string, List<string>> PerGameAddonSelection => _perGameAddonSelection;
    public HashSet<string> LumaEnabledGames => _lumaEnabledGames;
    public HashSet<string> LumaDisabledGames => _lumaDisabledGames;
    /// <summary>Games where Luma TAA Engine.ini settings are deployed.</summary>
    public HashSet<string> LumaTaaEnabled => _lumaTaaEnabled;
    public HashSet<string> NormalReShadeGames => _normalReShadeGames;
    /// <summary>Games where reshade.ini auto-update is locked (Keep ReShade.ini Updated = No).</summary>
    public HashSet<string> RsIniLockedGames => _rsIniLockedGames;
    public Dictionary<string, string> FolderOverrides => _folderOverrides;
    /// <summary>Per-game Vulkan rendering path preferences. Key = game name, Value = "DirectX" or "Vulkan".</summary>
    public Dictionary<string, string> VulkanRenderingPaths => _vulkanRenderingPaths;
    /// <summary>Per-game bitness overrides. Key = game name, Value = "32" or "64". Absent = auto-detect.</summary>
    public Dictionary<string, string> BitnessOverrides => _bitnessOverrides;
    /// <summary>Per-game API overrides. Key = game name, Value = list of GraphicsApiType names that are ON. Absent = auto-detect.</summary>
    public Dictionary<string, List<string>> ApiOverrides => _apiOverrides;
    /// <summary>Per-game ReShade channel overrides. Key = game name, Value = "Stable" or "Nightly". Absent = use global default.</summary>
    public Dictionary<string, string> ReShadeChannelOverrides => _reShadeChannelOverrides;
    /// <summary>Per-game DXVK variant overrides. Key = game name, Value = "Development", "Stable", or "LiliumHdr". Absent = use global default.</summary>
    public Dictionary<string, string> DxvkVariantOverrides => _dxvkVariantOverrides;
    /// <summary>Per-game Lilium HDR DXVK preset index. 0=Safest (default), 5=Experimental. Absent = 0.</summary>
    public Dictionary<string, int> LiliumPresetOverrides => _liliumPresetOverrides;
    /// <summary>Per-game OptiScaler variant override. Key = "GameName|Store", Value = "Stable" or "Nightly".</summary>
    public Dictionary<string, string> OsVariantOverrides => _osVariantOverrides;
    /// <summary>Per-game Neural Rendering method override. Key = "GameName|Store", Value = "DLSS5Tool", "DLSS5ToolBridge", "ShortFuse", or "Feeder". Absent = auto-detect.</summary>
    public Dictionary<string, string> NrMethodOverrides => _nrMethodOverrides;
    /// <summary>Per-game HDR auto-toggle overrides. "On" or "Off". Absent = use global.</summary>
    public Dictionary<string, string> HdrToggleOverrides => _hdrToggleOverrides;
    /// <summary>Per-game Resolution auto-toggle overrides. "On" or "Off". Absent = use global.</summary>
    public Dictionary<string, string> ResToggleOverrides => _resToggleOverrides;
    /// <summary>Per-game launch executable overrides. Key = game name, Value = absolute exe path.</summary>
    public Dictionary<string, string> LaunchExeOverrides => _launchExeOverrides;
    /// <summary>Per-game launch arguments. Key = game name, Value = arguments string.</summary>
    public Dictionary<string, string> LaunchArgsOverrides => _launchArgsOverrides;
    /// <summary>Per-game engine version overrides. Only applied when auto-detection fails to determine version.</summary>
    public Dictionary<string, string> EngineVersionOverrides => _engineVersionOverrides;
    /// <summary>Per-game custom ReShade DLL selection. Key = game name, Value = DLL filename.</summary>
    public Dictionary<string, string> CustomReShadeSelection => _customReShadeSelection;
    /// <summary>Games with RTX HDR enabled via NVIDIA driver profile.</summary>
    public HashSet<string> RtxHdrGames => _rtxHdrGames;
    public Dictionary<string, string> OriginalDetectedNames => _originalDetectedNames;

    /// <summary>Games where Streamline should be deployed. Composite-keyed "GameName|Store".</summary>
    public HashSet<string> OsDeployStreamline => _osDeployStreamline;

    /// <summary>Games where DLSS Enabler should be deployed. Composite-keyed "GameName|Store".</summary>
    public HashSet<string> OsDeployDlssEnabler => _osDeployDlssEnabler;

    /// <summary>Games where Dilated Motion Vectors are set to Off in Engine.ini. Composite-keyed "GameName|Store".</summary>
    public HashSet<string> OsDilatedMotionVectorsOff => _osDilatedMotionVectorsOff;

    /// <summary>FSR crash fix level per game. Value = "FSR2", "FSR3", or "FSR3.1". Absent = None.</summary>
    public Dictionary<string, string> OsFsrCrashFix => _osFsrCrashFix;

    /// <summary>Per-game FG Input override. Key = "GameName|Store", Value = INI string. Absent = "auto".</summary>
    public Dictionary<string, string> OsFgInput => _osFgInput;

    /// <summary>Per-game FG Output override. Key = "GameName|Store", Value = INI string. Absent = "auto".</summary>
    public Dictionary<string, string> OsFgOutput => _osFgOutput;

    /// <summary>Per-game FG Nvngx Replacement. Key = "GameName|Store", Value = INI string. Absent = "None".</summary>
    public Dictionary<string, string> OsFgNvngxReplacement => _osFgNvngxReplacement;

    /// <summary>Games where FSR-FG swapchain override is enabled. Composite-keyed "GameName|Store".</summary>
    public HashSet<string> OsFsrFgSwapchain => _osFsrFgSwapchain;

    /// <summary>Games where upscaler plugin is enabled via Engine.ini. Composite-keyed "GameName|Store".</summary>
    public HashSet<string> OsUpscalerPlugin => _osUpscalerPlugin;

    /// <summary>Per-game Streamline version override. Key = "GameName|Store", Value = version string. Absent = use default.</summary>
    public Dictionary<string, string> OsStreamlineVersion => _osStreamlineVersion;
    /// <summary>Per-game Ultimate ASI Loader installed DLL name. Composite-keyed "GameName|Store".</summary>
    public Dictionary<string, string> UalInstalledAs => _ualInstalledAs;
    /// <summary>Games where ShortFuse auto-config is disabled. Composite-keyed "GameName|Store". Absent = enabled.</summary>
    public HashSet<string> SfAutoConfigDisabled => _sfAutoConfigDisabled;

    public GameNameService(
        IGameDetectionService gameDetectionService,
        IModInstallService installer,
        IAuxInstallService auxInstaller,
        ILumaService lumaService)
    {
        _gameDetectionService = gameDetectionService;
        _installer = installer;
        _auxInstaller = auxInstaller;
        _lumaService = lumaService;
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all name mappings and settings from the persisted settings file.
    /// Returns the loaded settings dictionary for further processing by callers.
    /// </summary>
    public Dictionary<string, string> LoadNameMappings(
        IDllOverrideService dllOverrideService,
        SettingsViewModel settingsViewModel,
        Action<ViewLayout> setViewLayout,
        Action<string> setFilterMode,
        Action<List<CustomFilter>> setCustomFilters)
    {
        _nameMappings              = new(StringComparer.OrdinalIgnoreCase);
        _wikiExclusions            = new(StringComparer.OrdinalIgnoreCase);
        _ueExtendedGames           = new(StringComparer.OrdinalIgnoreCase);
        _ueExtendedOptOutGames     = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedReShade  = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedRenoDx   = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedUl       = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedDc       = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedOs       = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedRef      = new(StringComparer.OrdinalIgnoreCase);
        _perGameShaderMode         = new(StringComparer.OrdinalIgnoreCase);
        _perGameShaderSelection    = new(StringComparer.OrdinalIgnoreCase);
        _perGameAddonMode          = new(StringComparer.OrdinalIgnoreCase);
        _perGameAddonSelection     = new(StringComparer.OrdinalIgnoreCase);
        _gameRenames            = new(StringComparer.OrdinalIgnoreCase);
        _folderOverrides        = new(StringComparer.OrdinalIgnoreCase);
        _vulkanRenderingPaths   = new(StringComparer.OrdinalIgnoreCase);
        _bitnessOverrides       = new(StringComparer.OrdinalIgnoreCase);
        _apiOverrides           = new(StringComparer.OrdinalIgnoreCase);
        _reShadeChannelOverrides = new(StringComparer.OrdinalIgnoreCase);
        _dxvkVariantOverrides = new(StringComparer.OrdinalIgnoreCase);
        _nrMethodOverrides = new(StringComparer.OrdinalIgnoreCase);
        _lumaEnabledGames       = new(StringComparer.OrdinalIgnoreCase);
        _lumaDisabledGames      = new(StringComparer.OrdinalIgnoreCase);
        _normalReShadeGames     = new(StringComparer.OrdinalIgnoreCase);
        _rsIniLockedGames       = new(StringComparer.OrdinalIgnoreCase);
        _hiddenGames            ??= new(StringComparer.OrdinalIgnoreCase);
        _favouriteGames         ??= new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> s;
        try { s = SettingsViewModel.LoadSettingsFile(); }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GameNameService.LoadNameMappings] Settings file unreadable — {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }

        T Load<T>(string key, T fallback)
        {
            try
            {
                if (s.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                    return JsonSerializer.Deserialize<T>(v) ?? fallback;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[GameNameService.LoadNameMappings] Key '{key}' failed — {ex.Message}");
            }
            return fallback;
        }

        _nameMappings = new(Load<Dictionary<string, string>>("NameMappings",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        _wikiExclusions = new HashSet<string>(
            Load<List<string>>("WikiExclusions", new()), StringComparer.OrdinalIgnoreCase);

        _ueExtendedGames = new HashSet<string>(
            Load<List<string>>("UeExtendedGames", new()), StringComparer.OrdinalIgnoreCase);

        _ueExtendedOptOutGames = new HashSet<string>(
            Load<List<string>>("UeExtendedOptOutGames", new()), StringComparer.OrdinalIgnoreCase);

        _updateAllExcludedReShade = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedReShade", new()),
            StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedRenoDx = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedRenoDx", new()),
            StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedUl = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedUl", new()),
            StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedDc = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedDc", new()),
            StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedOs = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedOs", new()),
            StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedRef = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedRef", new()),
            StringComparer.OrdinalIgnoreCase);

        // Legacy migration: if old key exists and new sets are empty, copy legacy entries
        var legacy = Load<List<string>>("UpdateAllExcluded", new());
        if (legacy.Count > 0 && _updateAllExcludedReShade.Count == 0
            && _updateAllExcludedRenoDx.Count == 0)
        {
            foreach (var name in legacy)
            {
                _updateAllExcludedReShade.Add(name);
                _updateAllExcludedRenoDx.Add(name);
            }
        }

        var pgsmDict = Load<Dictionary<string, string>?>("PerGameShaderMode", null);
        _perGameShaderMode = new(StringComparer.OrdinalIgnoreCase);
        if (pgsmDict != null)
        {
            foreach (var kv in pgsmDict)
                _perGameShaderMode[kv.Key] = kv.Value;
        }

        var pgssDict = Load<Dictionary<string, List<string>>?>("PerGameShaderSelection", null);
        if (pgssDict != null)
        {
            _perGameShaderSelection = new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in pgssDict)
            {
                if (_perGameShaderMode.ContainsKey(kv.Key))
                    _perGameShaderSelection[kv.Key] = kv.Value;
            }
        }

        var pgamDict = Load<Dictionary<string, string>?>("PerGameAddonMode", null);
        _perGameAddonMode = new(StringComparer.OrdinalIgnoreCase);
        if (pgamDict != null)
        {
            foreach (var kv in pgamDict)
                _perGameAddonMode[kv.Key] = kv.Value;
        }

        var pgasDict = Load<Dictionary<string, List<string>>?>("PerGameAddonSelection", null);
        if (pgasDict != null)
        {
            // Migration: rename "RenoDX DLSS5" to "DLSS5 Tool" in all per-game selections
            foreach (var key in pgasDict.Keys.ToList())
            {
                var list = pgasDict[key];
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Equals("RenoDX DLSS5", StringComparison.OrdinalIgnoreCase))
                        list[i] = "DLSS5 Tool";
            }
            _perGameAddonSelection = new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in pgasDict)
            {
                // Load all selections — don't require a matching mode entry.
                // A selection without a mode entry is fine; the mode just defaults to Global.
                _perGameAddonSelection[kv.Key] = kv.Value;
            }
        }

        settingsViewModel.LoadSettingsFromDict(s);

        _lumaEnabledGames = new HashSet<string>(
            Load<List<string>>("LumaEnabledGames", new()),
            StringComparer.OrdinalIgnoreCase);

        _lumaDisabledGames = new HashSet<string>(
            Load<List<string>>("LumaDisabledGames", new()),
            StringComparer.OrdinalIgnoreCase);

        _normalReShadeGames = new HashSet<string>(
            Load<List<string>>("NormalReShadeGames", new()),
            StringComparer.OrdinalIgnoreCase);

        _rsIniLockedGames = new HashSet<string>(
            Load<List<string>>("RsIniLockedGames", new()),
            StringComparer.OrdinalIgnoreCase);

        _gameRenames = new(Load<Dictionary<string, string>>("GameRenames",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        var dllOverrides = new Dictionary<string, DllOverrideConfig>(Load<Dictionary<string, DllOverrideConfig>>("DllOverrides",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var manifestOptOuts = new HashSet<string>(
            Load<List<string>>("ManifestDllOptOuts", new()), StringComparer.OrdinalIgnoreCase);
        dllOverrideService.SetOverridesFromSettings(dllOverrides, manifestOptOuts);

        var folderOvDict = Load<Dictionary<string, string>>("FolderOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _folderOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in folderOvDict)
            _folderOverrides[kv.Key] = kv.Value;

        var vulkanPathsDict = Load<Dictionary<string, string>>("VulkanRenderingPaths",
            new(StringComparer.OrdinalIgnoreCase));
        _vulkanRenderingPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in vulkanPathsDict)
            _vulkanRenderingPaths[kv.Key] = kv.Value;

        var bitnessOvDict = Load<Dictionary<string, string>>("BitnessOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _bitnessOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in bitnessOvDict)
            _bitnessOverrides[kv.Key] = kv.Value;

        var apiOvDict = Load<Dictionary<string, List<string>>?>("ApiOverrides", null);
        _apiOverrides = new(StringComparer.OrdinalIgnoreCase);
        if (apiOvDict != null)
        {
            foreach (var kv in apiOvDict)
                _apiOverrides[kv.Key] = kv.Value;
        }

        var rsChannelOvDict = Load<Dictionary<string, string>>("ReShadeChannelOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _reShadeChannelOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in rsChannelOvDict)
            _reShadeChannelOverrides[kv.Key] = kv.Value;

        // ── Migration: global Nightly → per-game Nightly ──────────────────────────
        // The global ReShade channel setting has been removed. Users who had Nightly
        // globally now get per-game "Nightly" overrides for all games without an
        // existing per-game channel override.
        if (string.Equals(settingsViewModel.ReShadeChannel, "Nightly", StringComparison.OrdinalIgnoreCase)
            && !_reShadeChannelOverrides.ContainsKey("__nightly_migration_done"))
        {
            // We can't enumerate all game names here (not loaded yet), so we set a
            // sentinel that MainViewModel will check after cards are built.
            // For now just flag it — the actual migration runs in InitializeAsync.
            _reShadeChannelOverrides["__nightly_migration_pending"] = "true";
            settingsViewModel.ReShadeChannel = "Stable";
            CrashReporter.Log("[GameNameService.LoadNameMappings] Nightly migration flagged — global channel reset to Stable");
        }

        var dxvkVariantOvDict = Load<Dictionary<string, string>>("DxvkVariantOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _dxvkVariantOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in dxvkVariantOvDict)
            _dxvkVariantOverrides[kv.Key] = kv.Value;

        var osVariantOvDict = Load<Dictionary<string, string>>("OsVariantOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _osVariantOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in osVariantOvDict) _osVariantOverrides[kv.Key] = kv.Value;

        var nrMethodOvDict = Load<Dictionary<string, string>>("NrMethodOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _nrMethodOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in nrMethodOvDict) _nrMethodOverrides[kv.Key] = kv.Value;

        var liliumPresetOvDict = Load<Dictionary<string, int>>("LiliumPresetOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _liliumPresetOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in liliumPresetOvDict)
            _liliumPresetOverrides[kv.Key] = kv.Value;

        var hdrToggleOvDict = Load<Dictionary<string, string>>("HdrToggleOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _hdrToggleOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in hdrToggleOvDict)
            _hdrToggleOverrides[kv.Key] = kv.Value;

        var resToggleOvDict = Load<Dictionary<string, string>>("ResToggleOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _resToggleOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in resToggleOvDict)
            _resToggleOverrides[kv.Key] = kv.Value;

        _lumaTaaEnabled = new(StringComparer.OrdinalIgnoreCase);
        foreach (var g in Load<List<string>>("LumaTaaEnabled", new()))
            _lumaTaaEnabled.Add(g);

        var launchExeOvDict = Load<Dictionary<string, string>>("LaunchExeOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _launchExeOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in launchExeOvDict)
            _launchExeOverrides[kv.Key] = kv.Value;

        var launchArgsOvDict = Load<Dictionary<string, string>>("LaunchArgsOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _launchArgsOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in launchArgsOvDict)
            _launchArgsOverrides[kv.Key] = kv.Value;

        var engineVersionOvDict = Load<Dictionary<string, string>>("EngineVersionOverrides",
            new(StringComparer.OrdinalIgnoreCase));
        _engineVersionOverrides = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in engineVersionOvDict)
            _engineVersionOverrides[kv.Key] = kv.Value;

        var customReShadeSelDict = Load<Dictionary<string, string>>("CustomReShadeSelection",
            new(StringComparer.OrdinalIgnoreCase));
        _customReShadeSelection = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in customReShadeSelDict)
            _customReShadeSelection[kv.Key] = kv.Value;

        _hiddenGames = new HashSet<string>(
            Load<List<string>>("HiddenGames", _hiddenGames?.ToList() ?? new()),
            StringComparer.OrdinalIgnoreCase);

        _favouriteGames = new HashSet<string>(
            Load<List<string>>("FavouriteGames", _favouriteGames?.ToList() ?? new()),
            StringComparer.OrdinalIgnoreCase);

        _rtxHdrGames = new HashSet<string>(
            Load<List<string>>("RtxHdrGames", _rtxHdrGames?.ToList() ?? new()), StringComparer.OrdinalIgnoreCase);

        _osDeployStreamline = new HashSet<string>(
            Load<List<string>>("OsDeployStreamline", new()), StringComparer.OrdinalIgnoreCase);

        _osDeployDlssEnabler = new HashSet<string>(
            Load<List<string>>("OsDeployDlssEnabler", new()), StringComparer.OrdinalIgnoreCase);

        _osDilatedMotionVectorsOff = new HashSet<string>(
            Load<List<string>>("OsDilatedMotionVectorsOff", new()), StringComparer.OrdinalIgnoreCase);

        var osFsrCrashFixDict = Load<Dictionary<string, string>>("OsFsrCrashFix", new(StringComparer.OrdinalIgnoreCase));
        _osFsrCrashFix = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in osFsrCrashFixDict) _osFsrCrashFix[kv.Key] = kv.Value;

        var osFgInputDict = Load<Dictionary<string, string>>("OsFgInput", new(StringComparer.OrdinalIgnoreCase));
        _osFgInput = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in osFgInputDict) _osFgInput[kv.Key] = kv.Value;

        var osFgOutputDict = Load<Dictionary<string, string>>("OsFgOutput", new(StringComparer.OrdinalIgnoreCase));
        _osFgOutput = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in osFgOutputDict) _osFgOutput[kv.Key] = kv.Value;

        var osFgNvngxDict = Load<Dictionary<string, string>>("OsFgNvngxReplacement", new(StringComparer.OrdinalIgnoreCase));
        _osFgNvngxReplacement = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in osFgNvngxDict) _osFgNvngxReplacement[kv.Key] = kv.Value;

        _osFsrFgSwapchain = new HashSet<string>(
            Load<List<string>>("OsFsrFgSwapchain", new()), StringComparer.OrdinalIgnoreCase);

        _osUpscalerPlugin = new HashSet<string>(
            Load<List<string>>("OsUpscalerPlugin", new()), StringComparer.OrdinalIgnoreCase);

        var osStreamlineVersionDict = Load<Dictionary<string, string>>("OsStreamlineVersion", new(StringComparer.OrdinalIgnoreCase));
        _osStreamlineVersion = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in osStreamlineVersionDict) _osStreamlineVersion[kv.Key] = kv.Value;

        var ualInstalledAsDict = Load<Dictionary<string, string>>("UalInstalledAs", new(StringComparer.OrdinalIgnoreCase));
        _ualInstalledAs = new(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ualInstalledAsDict) _ualInstalledAs[kv.Key] = kv.Value;

        _sfAutoConfigDisabled = new HashSet<string>(
            Load<List<string>>("SfAutoConfigDisabled", new()), StringComparer.OrdinalIgnoreCase);

        if (s.TryGetValue("ViewLayout", out var vlVal) && int.TryParse(vlVal, out var vlInt) && Enum.IsDefined(typeof(ViewLayout), vlInt))
            setViewLayout((ViewLayout)vlInt);
        else if (s.TryGetValue("GridLayout", out var glVal))  // backward compat
            setViewLayout(ViewLayout.Detail);

        if (s.TryGetValue("FilterMode", out var fmVal) && !string.IsNullOrWhiteSpace(fmVal))
            setFilterMode(fmVal);

        var customFilters = Load<List<CustomFilter>>("CustomFilters", new());
        setCustomFilters(customFilters);

        CrashReporter.Log($"[GameNameService.LoadNameMappings] Loaded {_gameRenames.Count} renames, {dllOverrides.Count} DLL overrides, {_folderOverrides.Count} folder overrides");

        return s;
    }

    /// <summary>Persists all settings to disk.</summary>
    public void SaveNameMappings(
        IDllOverrideService dllOverrideService,
        SettingsViewModel settingsViewModel,
        ViewLayout currentViewLayout,
        bool isLoadingSettings,
        string filterMode,
        List<CustomFilter> customFilters)
    {
        if (isLoadingSettings) return;

        // Retry with short delays to handle file contention from concurrent background tasks
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var s = SettingsViewModel.LoadSettingsFile();
                s["NameMappings"]    = JsonSerializer.Serialize(
                    _nameMappings
                        .Where(kv => !_manifestNameMappingKeys.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value));
                s["WikiExclusions"]  = JsonSerializer.Serialize(_wikiExclusions.ToList());
                s["UeExtendedGames"] = JsonSerializer.Serialize(_ueExtendedGames.ToList());
                s["UeExtendedOptOutGames"] = JsonSerializer.Serialize(_ueExtendedOptOutGames.ToList());
                s.Remove("DcModeLevel");
                s.Remove("DcModeEnabled");
                s.Remove("DcDllFileName");
                s.Remove("PerGameDcModeOverride");
                s.Remove("DcCustomDllFileNames");
                s.Remove("DcLegacyMode");
                s["UpdateAllExcludedReShade"] = JsonSerializer.Serialize(_updateAllExcludedReShade.ToList());
                s["UpdateAllExcludedRenoDx"]  = JsonSerializer.Serialize(_updateAllExcludedRenoDx.ToList());
                s["UpdateAllExcludedUl"]      = JsonSerializer.Serialize(_updateAllExcludedUl.ToList());
                s["UpdateAllExcludedDc"]      = JsonSerializer.Serialize(_updateAllExcludedDc.ToList());
                s["UpdateAllExcludedOs"]      = JsonSerializer.Serialize(_updateAllExcludedOs.ToList());
                s["UpdateAllExcludedRef"]     = JsonSerializer.Serialize(_updateAllExcludedRef.ToList());
                s.Remove("UpdateAllExcluded");
                s["PerGameShaderMode"]    = JsonSerializer.Serialize(_perGameShaderMode);
                s["PerGameShaderSelection"] = JsonSerializer.Serialize(_perGameShaderSelection);
                s["PerGameAddonMode"]     = JsonSerializer.Serialize(_perGameAddonMode);
                s["PerGameAddonSelection"] = JsonSerializer.Serialize(_perGameAddonSelection);
                settingsViewModel.SaveSettingsToDict(s);
                s["LumaEnabledGames"]   = JsonSerializer.Serialize(_lumaEnabledGames.ToList());
                s["LumaDisabledGames"]  = JsonSerializer.Serialize(_lumaDisabledGames.ToList());
                s["NormalReShadeGames"] = JsonSerializer.Serialize(_normalReShadeGames.ToList());
                s["RsIniLockedGames"]   = JsonSerializer.Serialize(_rsIniLockedGames.ToList());
                s["GameRenames"]         = JsonSerializer.Serialize(_gameRenames);
                s["DllOverrides"]        = JsonSerializer.Serialize(dllOverrideService.GetUserOverridesForSave());
                s["ManifestDllOptOuts"]  = JsonSerializer.Serialize(dllOverrideService.ManifestDllOverrideOptOuts.ToList());
                s["FolderOverrides"]     = JsonSerializer.Serialize(_folderOverrides);
                s["VulkanRenderingPaths"] = JsonSerializer.Serialize(_vulkanRenderingPaths);
                s["BitnessOverrides"]    = JsonSerializer.Serialize(_bitnessOverrides);
                s["ApiOverrides"]        = JsonSerializer.Serialize(_apiOverrides);
                s["ReShadeChannelOverrides"] = JsonSerializer.Serialize(_reShadeChannelOverrides);
                s["DxvkVariantOverrides"] = JsonSerializer.Serialize(_dxvkVariantOverrides);
                s["LiliumPresetOverrides"] = JsonSerializer.Serialize(_liliumPresetOverrides);
                s["OsVariantOverrides"] = JsonSerializer.Serialize(_osVariantOverrides);
                s["NrMethodOverrides"] = JsonSerializer.Serialize(_nrMethodOverrides);
                s["HdrToggleOverrides"] = JsonSerializer.Serialize(_hdrToggleOverrides);
                s["ResToggleOverrides"] = JsonSerializer.Serialize(_resToggleOverrides);
                s["LaunchExeOverrides"] = JsonSerializer.Serialize(_launchExeOverrides);
                s["LaunchArgsOverrides"] = JsonSerializer.Serialize(_launchArgsOverrides);
                s["EngineVersionOverrides"] = JsonSerializer.Serialize(_engineVersionOverrides);
                s["LumaTaaEnabled"] = JsonSerializer.Serialize(_lumaTaaEnabled.ToList());
                s["CustomReShadeSelection"] = JsonSerializer.Serialize(_customReShadeSelection);
                s["HiddenGames"]         = JsonSerializer.Serialize(_hiddenGames?.ToList() ?? new List<string>());
                s["FavouriteGames"]      = JsonSerializer.Serialize(_favouriteGames?.ToList() ?? new List<string>());
                s["RtxHdrGames"]         = JsonSerializer.Serialize(_rtxHdrGames?.ToList() ?? new List<string>());
                s["OsDeployStreamline"]  = JsonSerializer.Serialize(_osDeployStreamline.ToList());
                s["OsDeployDlssEnabler"] = JsonSerializer.Serialize(_osDeployDlssEnabler.ToList());
                s["OsDilatedMotionVectorsOff"] = JsonSerializer.Serialize(_osDilatedMotionVectorsOff.ToList());
                s["OsFsrCrashFix"] = JsonSerializer.Serialize(_osFsrCrashFix);
                s["OsFgInput"] = JsonSerializer.Serialize(_osFgInput);
                s["OsFgOutput"] = JsonSerializer.Serialize(_osFgOutput);
                s["OsFgNvngxReplacement"] = JsonSerializer.Serialize(_osFgNvngxReplacement);
                s["OsFsrFgSwapchain"] = JsonSerializer.Serialize(_osFsrFgSwapchain.ToList());
                s["OsUpscalerPlugin"] = JsonSerializer.Serialize(_osUpscalerPlugin.ToList());
                if (_osStreamlineVersion.Count > 0) s["OsStreamlineVersion"] = JsonSerializer.Serialize(_osStreamlineVersion);
                if (_ualInstalledAs.Count > 0) s["UalInstalledAs"] = JsonSerializer.Serialize(_ualInstalledAs);
                if (_sfAutoConfigDisabled.Count > 0) s["SfAutoConfigDisabled"] = JsonSerializer.Serialize(_sfAutoConfigDisabled.ToList());
                else s.Remove("SfAutoConfigDisabled");
                s["ViewLayout"]          = ((int)currentViewLayout).ToString();
                s["FilterMode"]          = filterMode;
                s["CustomFilters"]       = JsonSerializer.Serialize(customFilters);
                SettingsViewModel.SaveSettingsFile(s);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50 * (attempt + 1)); // 50ms, 100ms
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[GameNameService.SaveNameMappings] Failed to save settings — {ex.Message}");
                return;
            }
        }
    }

    // ── Name mapping CRUD ─────────────────────────────────────────────────────

    public void AddNameMapping(string detectedName, string wikiKey)
    {
        if (string.IsNullOrWhiteSpace(detectedName) || string.IsNullOrWhiteSpace(wikiKey)) return;
        _nameMappings[detectedName] = wikiKey;
        // User explicitly set this — remove manifest-origin mark so it persists to settings
        _manifestNameMappingKeys.Remove(detectedName);
    }

    public string? GetNameMapping(string detectedName)
    {
        if (string.IsNullOrWhiteSpace(detectedName)) return null;
        if (_nameMappings.TryGetValue(detectedName, out var v)) return v;
        var norm = _gameDetectionService.NormalizeName(detectedName);
        foreach (var kv in _nameMappings)
            if (_gameDetectionService.NormalizeName(kv.Key) == norm) return kv.Value;
        return null;
    }

    /// <summary>Returns the user-set name mapping only (excludes manifest-injected mappings).</summary>
    public string? GetUserNameMapping(string detectedName)
    {
        if (string.IsNullOrWhiteSpace(detectedName)) return null;
        if (_manifestNameMappingKeys.Contains(detectedName)) return null;
        if (_nameMappings.TryGetValue(detectedName, out var v)) return v;
        var norm = _gameDetectionService.NormalizeName(detectedName);
        foreach (var kv in _nameMappings)
        {
            if (_gameDetectionService.NormalizeName(kv.Key) == norm)
                return _manifestNameMappingKeys.Contains(kv.Key) ? null : kv.Value;
        }
        return null;
    }

    /// <summary>Marks a name mapping key as manifest-origin (not user-set).</summary>
    public void MarkManifestNameMapping(string key) => _manifestNameMappingKeys.Add(key);

    public void RemoveNameMapping(string detectedName)
    {
        if (string.IsNullOrWhiteSpace(detectedName)) return;
        // Don't remove manifest-origin mappings — they're managed remotely
        if (_manifestNameMappingKeys.Contains(detectedName)) return;
        _nameMappings.Remove(detectedName);
        var norm = _gameDetectionService.NormalizeName(detectedName);
        var toRemove = _nameMappings.Keys
            .Where(k => !_manifestNameMappingKeys.Contains(k) && _gameDetectionService.NormalizeName(k) == norm).ToList();
        foreach (var k in toRemove) _nameMappings.Remove(k);
    }

    // ── Game renames ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renames a game everywhere: card, detected game, all settings HashSets/Dicts,
    /// persisted install records, and library file.
    /// </summary>
    public void RenameGame(string oldName, string newName,
        List<GameCardViewModel> allCards,
        List<DetectedGame> manualGames,
        IDllOverrideService dllOverrideService)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)) return;

        var card = allCards.FirstOrDefault(c =>
            c.GameName.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.GameName = newName;
            if (card.DetectedGame != null)
                card.DetectedGame.Name = newName;

            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                var key = card.InstallPath.TrimEnd(Path.DirectorySeparatorChar);
                _gameRenames[key] = newName;
            }

            if (_folderOverrides.TryGetValue(oldName, out var ovStored))
            {
                var parts = ovStored.Split('|');
                var origPath = parts.Length > 1 ? parts[1] : "";
                if (!string.IsNullOrEmpty(origPath))
                {
                    var origKey = origPath.TrimEnd(Path.DirectorySeparatorChar);
                    _gameRenames[origKey] = newName;
                }
            }
        }

        // Migrate composite-keyed HashSets (independent per store)
        MigrateCompositeHashSet(_hiddenGames, oldName, newName);
        MigrateCompositeHashSet(_favouriteGames, oldName, newName);
        MigrateCompositeHashSet(_updateAllExcludedReShade, oldName, newName);
        MigrateCompositeHashSet(_updateAllExcludedRenoDx, oldName, newName);
        MigrateCompositeHashSet(_updateAllExcludedUl, oldName, newName);
        MigrateCompositeHashSet(_updateAllExcludedDc, oldName, newName);
        MigrateCompositeHashSet(_updateAllExcludedOs, oldName, newName);
        MigrateCompositeHashSet(_updateAllExcludedRef, oldName, newName);
        MigrateCompositeHashSet(_lumaEnabledGames, oldName, newName);
        MigrateCompositeHashSet(_lumaDisabledGames, oldName, newName);
        MigrateCompositeHashSet(_normalReShadeGames, oldName, newName);
        MigrateCompositeHashSet(_rsIniLockedGames, oldName, newName);
        MigrateCompositeHashSet(_osDeployStreamline, oldName, newName);
        MigrateCompositeHashSet(_osDeployDlssEnabler, oldName, newName);
        MigrateCompositeHashSet(_osDilatedMotionVectorsOff, oldName, newName);
        MigrateCompositeDict(_osFsrCrashFix, oldName, newName);
        MigrateCompositeDict(_osFgInput, oldName, newName);
        MigrateCompositeDict(_osFgOutput, oldName, newName);
        MigrateCompositeDict(_osFgNvngxReplacement, oldName, newName);
        MigrateCompositeHashSet(_osFsrFgSwapchain, oldName, newName);
        MigrateCompositeHashSet(_osUpscalerPlugin, oldName, newName);
        MigrateCompositeDict(_osStreamlineVersion, oldName, newName);
        MigrateCompositeDict(_ualInstalledAs, oldName, newName);
        MigrateCompositeHashSet(_sfAutoConfigDisabled, oldName, newName);

        // Migrate name-only HashSets (shared across stores)
        MigrateHashSet(_wikiExclusions, oldName, newName);
        MigrateHashSet(_ueExtendedGames, oldName, newName);
        MigrateHashSet(_ueExtendedOptOutGames, oldName, newName);
        MigrateHashSet(_rtxHdrGames, oldName, newName);

        // Migrate composite-keyed Dictionaries (independent per store)
        MigrateCompositeDict(_perGameShaderMode, oldName, newName);
        MigrateCompositeDict(_perGameShaderSelection, oldName, newName);
        MigrateCompositeDict(_perGameAddonMode, oldName, newName);
        MigrateCompositeDict(_perGameAddonSelection, oldName, newName);
        MigrateCompositeDict(_folderOverrides, oldName, newName);
        MigrateCompositeDict(_vulkanRenderingPaths, oldName, newName);
        MigrateCompositeDict(_bitnessOverrides, oldName, newName);
        MigrateCompositeDict(_apiOverrides, oldName, newName);
        MigrateCompositeDict(_reShadeChannelOverrides, oldName, newName);
        MigrateCompositeDict(_dxvkVariantOverrides, oldName, newName);
        MigrateCompositeDict(_liliumPresetOverrides, oldName, newName);
        MigrateCompositeDict(_customReShadeSelection, oldName, newName);
        MigrateCompositeDict(_osVariantOverrides, oldName, newName);
        MigrateCompositeDict(_nrMethodOverrides, oldName, newName);
        // These four are name-only (not per-store) — use name-only migration
        MigrateDict(_hdrToggleOverrides, oldName, newName);
        MigrateDict(_resToggleOverrides, oldName, newName);
        MigrateDict(_launchExeOverrides, oldName, newName);
        MigrateDict(_launchArgsOverrides, oldName, newName);
        MigrateDict(_engineVersionOverrides, oldName, newName);

        // Migrate name-only Dictionaries (shared across stores)
        MigrateDict(_nameMappings, oldName, newName);

        // Migrate DLL override config
        dllOverrideService.MigrateOverride(oldName, newName);

        // Migrate manual games list
        var manualGame = manualGames.FirstOrDefault(g =>
            g.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (manualGame != null)
            manualGame.Name = newName;

        // Update persisted install records (RenoDX mod)
        if (card?.InstalledRecord != null)
        {
            _installer.RemoveRecord(card.InstalledRecord);
            card.InstalledRecord.GameName = newName;
            _installer.SaveRecordPublic(card.InstalledRecord);
        }

        // Update persisted aux records (ReShade)
        if (card?.RsRecord != null)
        {
            _auxInstaller.RemoveRecord(card.RsRecord);
            card.RsRecord.GameName = newName;
            _auxInstaller.SaveAuxRecord(card.RsRecord);
        }

        // Update persisted Luma record
        if (card?.LumaRecord != null)
        {
            _lumaService.RemoveLumaRecord(card.LumaRecord.GameName, card.LumaRecord.InstallPath);
            card.LumaRecord.GameName = newName;
            _lumaService.SaveLumaRecord(card.LumaRecord);
        }

        card?.NotifyAll();
    }

    public string? GetOriginalStoreName(string currentName)
    {
        if (_originalDetectedNames.TryGetValue(currentName, out var orig))
            return orig;
        return null;
    }

    public void RemoveGameRename(string gameName, List<GameCardViewModel> allCards)
    {
        var card = allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null) return;

        var keysToRemove = _gameRenames
            .Where(kv => kv.Value.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();
        foreach (var k in keysToRemove)
            _gameRenames.Remove(k);
    }

    // ── Apply methods ─────────────────────────────────────────────────────────

    public void ApplyGameRenames(List<DetectedGame> games)
    {
        if (_gameRenames.Count == 0) return;
        foreach (var g in games)
        {
            var key = g.InstallPath.TrimEnd(Path.DirectorySeparatorChar);
            if (_gameRenames.TryGetValue(key, out var newName))
            {
                if (!g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                    _originalDetectedNames[newName] = g.Name;
                g.Name = newName;
            }
        }
    }

    public void ApplyFolderOverrides(List<DetectedGame> games)
    {
        if (_folderOverrides.Count == 0) return;
        foreach (var g in games)
        {
            // Try composite key first (name|store), fall back to name-only for legacy entries
            var compositeKey = Models.GameKey.From(g.Name, g.Source ?? "").ToKey();
            if (!_folderOverrides.TryGetValue(compositeKey, out var stored))
                _folderOverrides.TryGetValue(g.Name, out stored);

            if (stored != null)
            {
                var overridePath = stored.Split('|')[0];
                if (!string.IsNullOrEmpty(overridePath))
                    g.InstallPath = overridePath;
            }
        }
    }

    public void ClearOriginalDetectedNames() => _originalDetectedNames.Clear();

    // ── Static helpers ────────────────────────────────────────────────────────

    public static void MigrateHashSet(HashSet<string> set, string oldName, string newName)
    {
        if (set.Remove(oldName))
            set.Add(newName);
    }

    public static void MigrateDict<TValue>(Dictionary<string, TValue> dict, string oldName, string newName)
    {
        if (dict.Remove(oldName, out var value))
            dict[newName] = value;
    }

    // ── Composite key migration helpers (for multi-store support) ─────────────

    /// <summary>
    /// Migrates all entries in a composite-keyed HashSet that match the old name (any store).
    /// </summary>
    public static void MigrateCompositeHashSet(HashSet<string> set, string oldName, string newName)
    {
        var toMigrate = set.Where(k => GameKey.Parse(k).MatchesName(oldName)).ToList();
        foreach (var old in toMigrate)
        {
            set.Remove(old);
            var parsed = GameKey.Parse(old);
            set.Add(new GameKey(newName, parsed.Store).ToKey());
        }
    }

    /// <summary>
    /// Migrates all entries in a composite-keyed Dictionary that match the old name (any store).
    /// </summary>
    public static void MigrateCompositeDict<TValue>(Dictionary<string, TValue> dict, string oldName, string newName)
    {
        var toMigrate = dict.Where(kv => GameKey.Parse(kv.Key).MatchesName(oldName)).ToList();
        foreach (var kv in toMigrate)
        {
            dict.Remove(kv.Key);
            var parsed = GameKey.Parse(kv.Key);
            dict[new GameKey(newName, parsed.Store).ToKey()] = kv.Value;
        }
    }
}
