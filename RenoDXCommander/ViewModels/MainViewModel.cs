// MainViewModel.cs -- Core scaffolding: constructor, fields, observable properties, forwarding properties, UI callbacks, and shared helpers.

using CommunityToolkit.Mvvm.ComponentModel;
using RenoDXCommander.Collections;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Microsoft.UI.Xaml;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HttpClient        _http;
    public HttpClient HttpClient => _http;
    private readonly IModInstallService _installer;
    public IModInstallService ModInstallServiceInstance => _installer;
    private readonly IAuxInstallService _auxInstaller;
    private readonly IREFrameworkService _refService;
    private readonly ICrashReporter _crashReporter;
    private readonly IWikiService _wikiService;
    private readonly IManifestService _manifestService;
    private readonly IGameLibraryService _gameLibraryService;
    private readonly IGameDetectionService _gameDetectionService;
    private readonly IPeHeaderService _peHeaderService;
    private readonly IUpdateService _updateService;
    private readonly IShaderPackService _shaderPackService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly FilterViewModel _filterViewModel;
    private readonly IUpdateOrchestrationService _updateOrchestrationService;
    private readonly IDllOverrideService _dllOverrideService;
    private readonly IGameNameService _gameNameService;
    private readonly IGameInitializationService _gameInitializationService;
    private readonly IAddonPackService _addonPackService;
    private readonly INexusModsService _nexusModsService;
    private readonly IPcgwService _pcgwService;
    private readonly IUltrawideFixService _uwFixService;
    private readonly IUltraPlusService _ultraPlusService;
    private readonly IOptiScalerService _optiScalerService;
    private readonly IDxvkService _dxvkService;
    private readonly IOptiScalerWikiService _optiScalerWikiService;
    private readonly IHdrDatabaseService _hdrDatabaseService;
    private readonly GitHubETagCache _etagCache;
    /// <summary>
    /// Task that tracks the background shader pack download/extraction.
    /// Awaited before the post-init shader sync so packs are available.
    /// </summary>
    private Task? _shaderPackReadyTask;
    public IUpdateService UpdateServiceInstance => _updateService;
    public IShaderPackService ShaderPackServiceInstance => _shaderPackService;
    public IAddonPackService AddonPackServiceInstance => _addonPackService;
    public IGameDetectionService GameDetectionServiceInstance => _gameDetectionService;
    public SettingsViewModel Settings => _settingsViewModel;
    /// <summary>True when the user has selected the Nightly ReShade build channel.</summary>
    public bool IsReShadeNightly => string.Equals(_settingsViewModel.ReShadeChannel, "Nightly", StringComparison.OrdinalIgnoreCase);
    public FilterViewModel Filter => _filterViewModel;
    public IDllOverrideService DllOverrideServiceInstance => _dllOverrideService;
    public IGameNameService GameNameServiceInstance => _gameNameService;
    public IUpdateOrchestrationService UpdateOrchestrationServiceInstance => _updateOrchestrationService;
    public IGameInitializationService GameInitializationServiceInstance => _gameInitializationService;
    public IPeHeaderService PeHeaderServiceInstance => _peHeaderService;
    public IAuxInstallService AuxInstallServiceInstance => _auxInstaller;
    public IOptiScalerService OptiScalerServiceInstance => _optiScalerService;
    public IDxvkService DxvkServiceInstance => _dxvkService;
    public ReShadeNightlyService ReShadeNightlyServiceInstance => _rsNightlyService;
    public IReShadeUpdateService ReShadeUpdateServiceInstance => _rsUpdateService;
    public IOptiScalerWikiService OptiScalerWikiServiceInstance => _optiScalerWikiService;
    public IHdrDatabaseService HdrDatabaseServiceInstance => _hdrDatabaseService;
    public IREFrameworkService REFrameworkServiceInstance => _refService;
    public RemoteManifest? Manifest => _manifest;

    public bool SkipUpdateCheck
    {
        get => _settingsViewModel.SkipUpdateCheck;
        set => _settingsViewModel.SkipUpdateCheck = value;
    }
    public bool BetaOptIn
    {
        get => _settingsViewModel.BetaOptIn;
        set => _settingsViewModel.BetaOptIn = value;
    }
    public bool VerboseLogging
    {
        get => _settingsViewModel.VerboseLogging;
        set => _settingsViewModel.VerboseLogging = value;
    }
    public string LastSeenVersion
    {
        get => _settingsViewModel.LastSeenVersion;
        set => _settingsViewModel.LastSeenVersion = value;
    }

    public string UpdateButtonTooltip => "Update ReShade, RenoDX, ReLimiter, Display Commander, and RE Framework for all games";

    /// <summary>
    /// The global shader picker button is disabled while custom shaders are active.
    /// </summary>
    public bool IsGlobalShaderButtonEnabled => !Settings.UseCustomShaders;

    [ObservableProperty] private bool _lumaFeatureEnabled = true;

    [ObservableProperty] private AppPage currentPage = AppPage.GameView;
    [ObservableProperty] private GameCardViewModel? selectedGame;
    [ObservableProperty] private bool hasUpdatesAvailable;
    [ObservableProperty] private ViewLayout _currentViewLayout = ViewLayout.Detail;
    [ObservableProperty] private int _compactPageIndex = 0;

    public Visibility HasUpdatesAvailableVisibility =>
        HasUpdatesAvailable ? Visibility.Visible : Visibility.Collapsed;

    partial void OnHasUpdatesAvailableChanged(bool value)
        => OnPropertyChanged(nameof(HasUpdatesAvailableVisibility));

    public Visibility DetailPanelVisibility =>
        CurrentViewLayout == ViewLayout.Detail ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CardGridVisibility =>
        CurrentViewLayout == ViewLayout.Grid ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CompactViewVisibility =>
        CurrentViewLayout == ViewLayout.Compact ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Returns Visible when in Detail OR Compact mode (both use the DetailScrollViewer).
    /// </summary>
    public Visibility DetailOrCompactVisibility =>
        CurrentViewLayout == ViewLayout.Grid ? Visibility.Collapsed : Visibility.Visible;

    public string LayoutToggleLabel => CurrentViewLayout switch
    {
        ViewLayout.Detail => "Detail View",
        ViewLayout.Grid => "Grid View",
        ViewLayout.Compact => "Compact View",
        _ => "Detail View",
    };

    // Backward-compatible property for code that still checks grid mode
    public bool IsGridLayout => CurrentViewLayout == ViewLayout.Grid;

    partial void OnCurrentViewLayoutChanged(ViewLayout value)
    {
        OnPropertyChanged(nameof(DetailPanelVisibility));
        OnPropertyChanged(nameof(CardGridVisibility));
        OnPropertyChanged(nameof(CompactViewVisibility));
        OnPropertyChanged(nameof(DetailOrCompactVisibility));
        OnPropertyChanged(nameof(LayoutToggleLabel));
        OnPropertyChanged(nameof(IsGridLayout));
    }

    public ViewLayout NextViewLayout() => CurrentViewLayout switch
    {
        ViewLayout.Detail => ViewLayout.Grid,
        ViewLayout.Grid => ViewLayout.Compact,
        ViewLayout.Compact => ViewLayout.Detail,
        _ => ViewLayout.Detail,
    };

    public void NavigateCompactPage(int delta)
    {
        CompactPageIndex = ((CompactPageIndex + delta) % 3 + 3) % 3;
    }

    /// <summary>
    /// Raised when an install would overwrite a dxgi.dll that RDXC cannot identify
    /// as ReShade or Display Commander. The UI should show a confirmation dialog
    /// <summary>
    /// Async callback set by the UI layer. Called when a foreign dxgi.dll is detected.
    /// Returns true if the user confirms overwrite, false to cancel.
    /// </summary>
    public Func<GameCardViewModel, string, Task<bool>>? ConfirmForeignDxgiOverwrite { get; set; }

    /// <summary>
    /// Async callback set by the UI layer. Called when a Vulkan install is requested
    /// but RDXC is not running as admin. Shows an info dialog explaining elevation is required.
    /// </summary>
    public Func<Task>? ShowVulkanAdminRequiredDialog { get; set; }

    /// <summary>
    /// Async callback set by the UI layer. Called before the first Vulkan layer install
    /// in a session to warn the user that the layer is global (affects all Vulkan apps).
    /// Returns true if the user chooses to proceed, false to cancel.
    /// </summary>
    public Func<Task<bool>>? ShowVulkanLayerWarningDialog { get; set; }

    // ── Testable seams for Vulkan layer operations in InstallReShadeVulkanAsync ──
    /// <summary>
    /// Delegate used by <see cref="InstallReShadeVulkanAsync"/> to check Vulkan layer status.
    /// Defaults to <see cref="VulkanLayerService.IsLayerInstalled()"/>.
    /// Tests can replace this with a custom func to control the result.
    /// </summary>
    internal Func<bool> IsVulkanLayerInstalledFunc { get; set; } = VulkanLayerService.IsLayerInstalled;

    /// <summary>
    /// Delegate used by <see cref="InstallReShadeVulkanAsync"/> to install the Vulkan layer.
    /// Defaults to <see cref="VulkanLayerService.InstallLayer()"/>.
    /// Tests can replace this with a spy/mock to track invocations.
    /// </summary>
    internal Action InstallLayerAction { get; set; } = VulkanLayerService.InstallLayer;

    /// <summary>
    /// Delegate used by <see cref="InstallReShadeVulkanAsync"/> to check admin status.
    /// Defaults to <see cref="VulkanLayerService.IsRunningAsAdmin()"/>.
    /// Tests can replace this to avoid real admin checks.
    /// </summary>
    internal Func<bool> IsRunningAsAdminFunc { get; set; } = VulkanLayerService.IsRunningAsAdmin;

    /// <summary>
    /// Delegate used by <see cref="InstallReShadeVulkanAsync"/> to dispatch UI updates.
    /// Defaults to using <see cref="DispatcherQueue"/>. Tests can replace this to run
    /// the action synchronously (e.g. <c>action => action()</c>).
    /// </summary>
    internal Action<Action>? DispatchUiAction { get; set; }

    /// <summary>
    /// Async callback set by the UI layer. Shows the global shader selection picker.
    /// Takes the current selection, returns the confirmed selection or null on cancel.
    /// </summary>
    public Func<List<string>?, Task<List<string>?>>? ShowShaderSelectionPicker { get; set; }

    /// <summary>
    /// Async callback set by the UI layer. Shows the per-game shader selection picker.
    /// Takes the game name and current selection, returns the confirmed selection or null on cancel.
    /// </summary>
    public Func<string, List<string>?, Task<List<string>?>>? ShowPerGameShaderSelectionPicker { get; set; }

    /// <summary>Guard flag — true while LoadNameMappings is running so that
    /// property-change handlers don't call SaveNameMappings before all fields
    /// have been loaded.</summary>
    private bool _isLoadingSettings
    {
        get => _settingsViewModel.IsLoadingSettings;
        set => _settingsViewModel.IsLoadingSettings = value;
    }

    /// <summary>
    /// Deploys shaders to all installed game locations.
    /// Mirrors the logic in RefreshAsync but can be triggered on demand via the ⚙ button.
    /// </summary>
    public void DeployAllShaders()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Collect all unique pack IDs needed across all games
                var allNeededPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var card in _allCards)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) continue;
                    bool rsInstalled = card.RequiresVulkanInstall
                        ? VulkanFootprintService.Exists(card.InstallPath)
                        : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;
                    if (!rsInstalled) continue;
                    var sel = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);
                    if (sel != null) allNeededPacks.UnionWith(sel);
                }

                // Ensure needed packs are downloaded (no-op if already cached)
                if (allNeededPacks.Count > 0)
                    await _shaderPackService.EnsurePacksAsync(allNeededPacks);

                foreach (var card in _allCards)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) continue;

                    bool rsInstalled = card.RequiresVulkanInstall
                        ? VulkanFootprintService.Exists(card.InstallPath)
                        : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;

                    var effectiveSelection = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);

                    if (rsInstalled)
                    {
                        _shaderPackService.SyncGameFolder(card.InstallPath, effectiveSelection);
                    }
                }
            }
            catch (Exception ex)
            { _crashReporter.Log($"[MainViewModel.DeployAllShaders] Failed — {ex.Message}"); }
        });
    }

    /// <summary>
    /// Deploys shaders for a single game card (by name).
    /// Called when saving a per-game shader mode override so changes take effect immediately.
    /// </summary>
    public void DeployShadersForCard(string gameName)
    {
        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                bool rsInstalled = card.RequiresVulkanInstall
                    ? VulkanFootprintService.Exists(card.InstallPath)
                    : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;

                var effectiveSelection = ResolveShaderSelection(gameName, card.ShaderModeOverride);

                // Ensure needed packs are downloaded before deploying
                if (effectiveSelection != null)
                    await _shaderPackService.EnsurePacksAsync(effectiveSelection);

                if (rsInstalled)
                {
                    _shaderPackService.SyncGameFolder(card.InstallPath, effectiveSelection);
                }
            }
            catch (Exception ex)
            { _crashReporter.Log($"[MainViewModel.DeployShadersForCard] Failed for '{gameName}' — {ex.Message}"); }
        });
    }

    /// <summary>
    /// Resolves the effective shader pack selection for a game.
    /// Priority chain: per-game Custom → per-game Select → global custom → global packs.
    /// </summary>
    internal IEnumerable<string>? ResolveShaderSelection(string gameName, string? shaderModeOverride)
    {
        // 1. Per-game "Custom" mode → custom shader sentinel
        if (string.Equals(shaderModeOverride, "Custom", StringComparison.OrdinalIgnoreCase))
            return new[] { ShaderPackService.CustomShaderSentinel };

        // 2. Per-game "Select" mode → per-game pack selection
        if (string.Equals(shaderModeOverride, "Select", StringComparison.OrdinalIgnoreCase)
            && _gameNameService.PerGameShaderSelection.TryGetValue(gameName, out var perGameSel))
            return perGameSel;

        // 3. Global UseCustomShaders enabled → custom shader sentinel
        if (_settingsViewModel.UseCustomShaders)
            return new[] { ShaderPackService.CustomShaderSentinel };

        // 4. Fallback → global pack selection
        return _settingsViewModel.SelectedShaderPacks;
    }

    /// <summary>
    /// Builds the effective screenshot save path for a game based on current settings.
    /// Returns null if no screenshot path is configured.
    /// </summary>
    internal string? BuildScreenshotSavePath(string gameName)
    {
        var basePath = _settingsViewModel.ScreenshotPath;
        if (string.IsNullOrEmpty(basePath)) return null;
        if (!_settingsViewModel.PerGameScreenshotFolders) return basePath;
        var sanitized = AuxInstallService.SanitizeDirectoryName(gameName);
        if (string.IsNullOrEmpty(sanitized)) return basePath;
        return basePath + @"\" + sanitized;
    }

    private List<GameMod> _allMods = new();
    private Dictionary<string, string> _genericNotes = new(StringComparer.OrdinalIgnoreCase);
    private List<GameCardViewModel> _allCards = new();
    public IReadOnlyList<GameCardViewModel> AllCards => _allCards;
    private List<DetectedGame> _manualGames = new();
    private RemoteManifest? _manifest;
    private HashSet<string> _manifestNativeHdrGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _manifestBlacklist = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _manifest32BitGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _manifest64BitGames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _manifestEngineOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ManifestDllNames> _manifestDllNameOverrides = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hiddenGames => _gameNameService.HiddenGames;
    private HashSet<string> _favouriteGames => _gameNameService.FavouriteGames;
    private Dictionary<string, string> _engineTypeCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _resolvedPathCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _addonFileCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, MachineType> _bitnessCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Game names that have DXVK enabled (loaded from saved library).</summary>
    private HashSet<string> _dxvkEnabledGames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Game names excluded from DXVK Update All (loaded from saved library).</summary>
    private HashSet<string> _excludeFromUpdateAllDxvk = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Maps current (renamed) game name → original store-detected name.
    /// Populated during ApplyGameRenames so the Overrides dialog can reset to the original.</summary>
    private Dictionary<string, string> _originalDetectedNames => _gameNameService.OriginalDetectedNames;

    // Settings file I/O delegated to SettingsViewModel

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private string _subStatusText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isBackgroundScanning;
    [ObservableProperty] private string _backgroundScanStatusText = "";
    private bool _hasInitialized;
    public bool HasInitialized => _hasInitialized;
    public void MarkInitialized() => _hasInitialized = true;

    /// <summary>
    /// The game name that was selected when the app last closed.
    /// Set from the saved library on startup; consumed by TryRestoreSelection.
    /// </summary>
    internal string? LastSelectedGameName { get; set; }

    // ── Forwarding properties — delegate to FilterViewModel, preserve UI bindings ──
    public string SearchQuery
    {
        get => _filterViewModel.SearchQuery;
        set => _filterViewModel.SearchQuery = value;
    }
    public string FilterMode
    {
        get => _filterViewModel.FilterMode;
        set => _filterViewModel.FilterMode = value;
    }
    public IReadOnlySet<string> ActiveFilters => _filterViewModel.ActiveFilters;
    public bool ShowHidden
    {
        get => _filterViewModel.ShowHidden;
        set => _filterViewModel.ShowHidden = value;
    }
    public int TotalGames
    {
        get => _filterViewModel.TotalGames;
        set => _filterViewModel.TotalGames = value;
    }
    public int InstalledCount
    {
        get => _filterViewModel.InstalledCount;
        set => _filterViewModel.InstalledCount = value;
    }
    public int HiddenCount
    {
        get => _filterViewModel.HiddenCount;
        set => _filterViewModel.HiddenCount = value;
    }
    public int FavouriteCount
    {
        get => _filterViewModel.FavouriteCount;
        set => _filterViewModel.FavouriteCount = value;
    }

    public BatchObservableCollection<GameCardViewModel> DisplayedGames { get; } = new();

    // UE common warnings shown at bottom of every generic UE info dialog
    private const string UnrealWarnings =
        "\n\n⚠ COMMON UNREAL ENGINE MOD WARNINGS\n\n" +
        "🖥 Black Screen on Launch\n" +
        "Upgrade `R10G10B10A2_UNORM` → `output size`\n" +
        "Unlock upgrade sliders: Settings Mode → Advanced, then restart game.\n\n" +
        "🖥 DLSS FG Flickering\n" +
        "Replace DLSSG DLL with older 3.8.x (locks FG x2) or use DLSS FIX (beta) from Discord.";

    public MainViewModel(
        HttpClient http,
        IModInstallService installer,
        IAuxInstallService auxInstaller,
        ICrashReporter crashReporter,
        IWikiService wikiService,
        IManifestService manifestService,
        IGameLibraryService gameLibraryService,
        IGameDetectionService gameDetectionService,
        IPeHeaderService peHeaderService,
        IUpdateService updateService,
        IShaderPackService shaderPackService,
        ILumaService lumaService,
        IReShadeUpdateService rsUpdateService,
        INormalReShadeUpdateService normalRsUpdateService,
        ReShadeNightlyService rsNightlyService,
        SettingsViewModel settingsViewModel,
        FilterViewModel filterViewModel,
        IUpdateOrchestrationService updateOrchestrationService,
        IDllOverrideService dllOverrideService,
        IGameNameService gameNameService,
        IGameInitializationService gameInitializationService,
        IREFrameworkService refService,
        INexusModsService nexusModsService,
        IPcgwService pcgwService,
        IUltrawideFixService uwFixService,
        IUltraPlusService ultraPlusService,
        IOptiScalerService optiScalerService,
        IDxvkService dxvkService,
        IOptiScalerWikiService optiScalerWikiService,
        IHdrDatabaseService hdrDatabaseService,
        GitHubETagCache etagCache)
    {
        _http = http;
        _installer = installer;
        _auxInstaller = auxInstaller;
        _refService = refService;
        _crashReporter = crashReporter;
        _wikiService = wikiService;
        _manifestService = manifestService;
        _gameLibraryService = gameLibraryService;
        _gameDetectionService = gameDetectionService;
        _peHeaderService = peHeaderService;
        _updateService = updateService;
        _shaderPackService = shaderPackService;
        _lumaService = lumaService;
        _rsUpdateService = rsUpdateService;
        _normalRsUpdateService = normalRsUpdateService;
        _rsNightlyService = rsNightlyService;
        _settingsViewModel = settingsViewModel;
        _filterViewModel = filterViewModel;
        _updateOrchestrationService = updateOrchestrationService;
        _dllOverrideService = dllOverrideService;
        _gameNameService = gameNameService;
        _gameInitializationService = gameInitializationService;
        _addonPackService = new AddonPackService(http);
        _nexusModsService = nexusModsService;
        _pcgwService = pcgwService;
        _uwFixService = uwFixService;
        _ultraPlusService = ultraPlusService;
        _optiScalerService = optiScalerService;
        _dxvkService = dxvkService;
        _optiScalerWikiService = optiScalerWikiService;
        _hdrDatabaseService = hdrDatabaseService;
        _etagCache = etagCache;
        // Wire up SettingsChanged so property changes trigger a full save
        _settingsViewModel.SettingsChanged = () => SaveNameMappings();
        // Wire up DllOverrideService changes to trigger save
        _dllOverrideService.OverridesChanged = () => SaveNameMappings();
        // Wire up FilterViewModel to persist filter mode on change
        _filterViewModel.FilterModeChanged = () => SaveNameMappings();
        // Wire up FilterViewModel to persist custom filters on change
        _filterViewModel.CustomFiltersChanged = () => SaveNameMappings();
        // Initialize FilterViewModel with the DisplayedGames collection
        _filterViewModel.Initialize(DisplayedGames);
        // Forward FilterViewModel property changes so UI bindings on MainViewModel still work
        _filterViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(InstalledCount) or nameof(TotalGames)
                or nameof(HiddenCount) or nameof(FavouriteCount)
                or nameof(FilterMode) or nameof(SearchQuery) or nameof(ShowHidden))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };
        // Raise IsGlobalShaderButtonEnabled when the custom-shaders toggle changes
        // and re-deploy all shaders so every installed game reflects the new setting
        _settingsViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.UseCustomShaders))
            {
                OnPropertyChanged(nameof(IsGlobalShaderButtonEnabled));
                if (_hasInitialized && !_isLoadingSettings)
                    DeployAllShaders();
            }
        };
        // Subscribe to installer events — on install we'll perform a full refresh
        LoadNameMappings();
        LoadThemeAndDensity();
    }

    // --- persisted settings: delegated to GameNameService ---
    private Dictionary<string, string> _nameMappings => _gameNameService.NameMappings;
    /// <summary>Persisted install-path → user-chosen name.  Applied after every detection scan so renames survive Refresh.</summary>
    private Dictionary<string, string> _gameRenames => _gameNameService.GameRenames;

    private readonly ILumaService _lumaService;
    private readonly IReShadeUpdateService _rsUpdateService;
    private readonly INormalReShadeUpdateService _normalRsUpdateService;
    private readonly ReShadeNightlyService _rsNightlyService;
    private List<LumaMod> _lumaMods = new();
    private HashSet<string> _lumaEnabledGames => _gameNameService.LumaEnabledGames;
    /// <summary>
    /// Games the user has explicitly disabled Luma for — prevents manifest lumaDefaultGames
    /// from re-enabling Luma on every refresh.
    /// </summary>
    private HashSet<string> _lumaDisabledGames => _gameNameService.LumaDisabledGames;
    /// <summary>Games configured to use normal (non-addon) ReShade.</summary>
    private HashSet<string> _normalReShadeGames => _gameNameService.NormalReShadeGames;
    /// <summary>
    /// Games in this set are excluded from all wiki matching.
    /// Their cards show a Discord link instead of an install button.
    /// </summary>
    private HashSet<string> _wikiExclusions => _gameNameService.WikiExclusions;
    /// <summary>
    /// Manifest-driven unlinks: games in this set ignore their fuzzy wiki match
    /// and fall through to the generic engine addon instead.
    /// </summary>
    private HashSet<string> _manifestWikiUnlinks = new(StringComparer.OrdinalIgnoreCase);

    // VerboseLogging change handling delegated to SettingsViewModel

    partial void OnLumaFeatureEnabledChanged(bool value)
    {
        foreach (var c in _allCards) c.LumaFeatureEnabled = value;
    }

    partial void OnSelectedGameChanged(GameCardViewModel? oldValue, GameCardViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null)
        {
            newValue.IsSelected = true;
            // Only persist the selection after initial load is complete,
            // so the saved LastSelectedGameName isn't overwritten by auto-select.
            if (HasInitialized)
                LastSelectedGameName = newValue.GameName;
        }
    }

    /// <summary>Games for which the user has toggled UE-Extended ON.</summary>
    private HashSet<string> _ueExtendedGames => _gameNameService.UeExtendedGames;
    private HashSet<string> _updateAllExcludedReShade => _gameNameService.UpdateAllExcludedReShade;
    private HashSet<string> _updateAllExcludedRenoDx => _gameNameService.UpdateAllExcludedRenoDx;
    private HashSet<string> _updateAllExcludedUl => _gameNameService.UpdateAllExcludedUl;
    private HashSet<string> _updateAllExcludedDc => _gameNameService.UpdateAllExcludedDc;
    private HashSet<string> _updateAllExcludedOs => _gameNameService.UpdateAllExcludedOs;
    private HashSet<string> _updateAllExcludedRef => _gameNameService.UpdateAllExcludedRef;
    private Dictionary<string, string> _perGameShaderMode => _gameNameService.PerGameShaderMode;
    /// <summary>Per-game Vulkan rendering path preferences. Key = game name, Value = "DirectX" or "Vulkan".</summary>
    private Dictionary<string, string> _vulkanRenderingPaths => _gameNameService.VulkanRenderingPaths;
    /// <summary>Per-game bitness overrides. Key = game name, Value = "32" or "64". Absent = auto-detect.</summary>
    private Dictionary<string, string> _bitnessOverrides => _gameNameService.BitnessOverrides;
    /// <summary>Per-game API overrides. Key = game name, Value = list of GraphicsApiType names that are ON. Absent = auto-detect.</summary>
    private Dictionary<string, List<string>> _apiOverrides => _gameNameService.ApiOverrides;
    /// <summary>Session-scoped flag — true after the global Vulkan layer warning has been shown once this session.</summary>
    private bool _vulkanLayerWarningShownThisSession = false;

    /// <summary>When true, the next CheckForUpdatesAsync call bypasses the cooldown timer (e.g. Full Refresh).</summary>
    private bool _forceUpdateCheck;

    // Dispatcher reference for cross-thread UI updates
    private Microsoft.UI.Dispatching.DispatcherQueue? DispatcherQueue { get; set; }
    public void SetDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dq) => DispatcherQueue = dq;

    /// <summary>Store the background shader-pack download task so InitializeAsync can await it.</summary>
    public void SetShaderPackReadyTask(Task task) => _shaderPackReadyTask = task;

}
