using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RenoDXCommander.Collections;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HttpClient        _http;
    public HttpClient HttpClient => _http;
    private readonly IModInstallService _installer;
    private readonly IAuxInstallService _auxInstaller;
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
    /// <summary>
    /// Task that tracks the background shader pack download/extraction.
    /// Awaited before the post-init shader sync so packs are available.
    /// </summary>
    private Task? _shaderPackReadyTask;
    public IUpdateService UpdateServiceInstance => _updateService;
    public IShaderPackService ShaderPackServiceInstance => _shaderPackService;
    public IGameDetectionService GameDetectionServiceInstance => _gameDetectionService;
    public SettingsViewModel Settings => _settingsViewModel;
    public FilterViewModel Filter => _filterViewModel;
    public IDllOverrideService DllOverrideServiceInstance => _dllOverrideService;
    public IGameNameService GameNameServiceInstance => _gameNameService;
    public IUpdateOrchestrationService UpdateOrchestrationServiceInstance => _updateOrchestrationService;
    public IGameInitializationService GameInitializationServiceInstance => _gameInitializationService;

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

    [ObservableProperty] private bool _dcModeEnabled;
    [ObservableProperty] private string _dcDllFileName = "dxgi.dll";

    public Visibility DcDllPickerVisibility =>
        DcModeEnabled ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private bool _lumaFeatureEnabled = true;

    [ObservableProperty] private AppPage currentPage = AppPage.GameView;
    [ObservableProperty] private GameCardViewModel? selectedGame;
    [ObservableProperty] private bool hasUpdatesAvailable;
    [ObservableProperty] private bool _isGridLayout = false;

    public Visibility HasUpdatesAvailableVisibility =>
        HasUpdatesAvailable ? Visibility.Visible : Visibility.Collapsed;

    partial void OnHasUpdatesAvailableChanged(bool value)
        => OnPropertyChanged(nameof(HasUpdatesAvailableVisibility));

    public Visibility DetailPanelVisibility =>
        IsGridLayout ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CardGridVisibility =>
        IsGridLayout ? Visibility.Visible : Visibility.Collapsed;

    public string LayoutToggleLabel =>
        IsGridLayout ? "Detail View" : "Grid View";

    partial void OnIsGridLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(DetailPanelVisibility));
        OnPropertyChanged(nameof(CardGridVisibility));
        OnPropertyChanged(nameof(LayoutToggleLabel));
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
    /// Async callback set by the UI layer. Called when a foreign winmm.dll is detected.
    /// Returns true if the user confirms overwrite, false to cancel.
    /// </summary>
    public Func<GameCardViewModel, string, Task<bool>>? ConfirmForeignWinmmOverwrite { get; set; }

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
    /// property-change handlers (DcModeEnabled, DcDllFileName, etc.) don't
    /// call SaveNameMappings before all fields have been loaded.</summary>
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
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var card in _allCards)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) continue;

                    bool rsInstalled = card.RequiresVulkanInstall
                        ? VulkanFootprintService.Exists(card.InstallPath)
                        : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;
                    bool dcInstalled = card.DcStatus == GameStatus.Installed || card.DcStatus == GameStatus.UpdateAvailable;

                    // Resolve effective selection: per-game override wins, otherwise global
                    var effectiveSelection = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);

                    if (rsInstalled || dcInstalled)
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

        _ = Task.Run(() =>
        {
            try
            {
                bool rsInstalled = card.RequiresVulkanInstall
                    ? VulkanFootprintService.Exists(card.InstallPath)
                    : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;
                bool dcInstalled = card.DcStatus == GameStatus.Installed || card.DcStatus == GameStatus.UpdateAvailable;

                // Resolve effective selection: per-game override wins, otherwise global
                var effectiveSelection = ResolveShaderSelection(gameName, card.ShaderModeOverride);

                if (rsInstalled || dcInstalled)
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
    /// Per-game selection wins; otherwise falls back to the global selection.
    /// </summary>
    private IEnumerable<string>? ResolveShaderSelection(string gameName, string? shaderModeOverride)
    {
        // Per-game selection takes precedence when the game has a "Select" override
        if (shaderModeOverride != null
            && _gameNameService.PerGameShaderSelection.TryGetValue(gameName, out var perGameSel))
            return perGameSel;

        return _settingsViewModel.SelectedShaderPacks;
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
    /// <summary>Maps current (renamed) game name → original store-detected name.
    /// Populated during ApplyGameRenames so the Overrides dialog can reset to the original.</summary>
    private Dictionary<string, string> _originalDetectedNames => _gameNameService.OriginalDetectedNames;

    // Settings file I/O delegated to SettingsViewModel

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private string _subStatusText = "";
    [ObservableProperty] private bool _isLoading = true;
    private bool _hasInitialized;
    public bool HasInitialized => _hasInitialized;
    public void MarkInitialized() => _hasInitialized = true;

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
        SettingsViewModel settingsViewModel,
        FilterViewModel filterViewModel,
        IUpdateOrchestrationService updateOrchestrationService,
        IDllOverrideService dllOverrideService,
        IGameNameService gameNameService,
        IGameInitializationService gameInitializationService)
    {
        _http = http;
        _installer = installer;
        _auxInstaller = auxInstaller;
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
        _settingsViewModel = settingsViewModel;
        _filterViewModel = filterViewModel;
        _updateOrchestrationService = updateOrchestrationService;
        _dllOverrideService = dllOverrideService;
        _gameNameService = gameNameService;
        _gameInitializationService = gameInitializationService;
        // Wire up SettingsChanged so property changes trigger a full save
        _settingsViewModel.SettingsChanged = () => SaveNameMappings();
        // Wire up DllOverrideService changes to trigger save
        _dllOverrideService.OverridesChanged = () => SaveNameMappings();
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
        // Subscribe to installer events — on install we'll perform a full refresh
        LoadNameMappings();
        LoadThemeAndDensity();
    }

    // --- persisted settings: delegated to GameNameService ---
    private Dictionary<string, string> _nameMappings => _gameNameService.NameMappings;
    /// <summary>Persisted install-path → user-chosen name.  Applied after every detection scan so renames survive Refresh.</summary>
    private Dictionary<string, string> _gameRenames => _gameNameService.GameRenames;

    // ── Luma Framework ────────────────────────────────────────────────────────────
    private readonly ILumaService _lumaService;
    private readonly IReShadeUpdateService _rsUpdateService;
    private List<LumaMod> _lumaMods = new();
    private HashSet<string> _lumaEnabledGames => _gameNameService.LumaEnabledGames;
    /// <summary>
    /// Games the user has explicitly disabled Luma for — prevents manifest lumaDefaultGames
    /// from re-enabling Luma on every refresh.
    /// </summary>
    private HashSet<string> _lumaDisabledGames => _gameNameService.LumaDisabledGames;
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

    partial void OnDcModeEnabledChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(DcDllPickerVisibility));
        if (_isLoadingSettings) return;
        SaveNameMappings();
        ApplyDcModeSwitch((wasEnabled: oldValue, wasDllFileName: DcDllFileName));
    }

    partial void OnDcDllFileNameChanged(string oldValue, string newValue)
    {
        if (_isLoadingSettings) return;
        SaveNameMappings();
        ApplyDcModeSwitch((wasEnabled: DcModeEnabled, wasDllFileName: oldValue));
    }

    /// <summary>
    /// Renames installed ReShade and Display Commander files to match any manifest DLL name
    /// overrides. Called after every BuildCards so that adding a manifest override takes
    /// effect on next Refresh without the user needing to reinstall.
    ///
    /// Only runs for games that do NOT already have a user-set DLL override, since user
    /// overrides take priority and their filenames are already correct.
    /// </summary>
    private void ApplyManifestDllRenames()
    {
        if (_manifestDllNameOverrides.Count == 0) return;

        foreach (var card in _allCards)
        {
            if (card.DllOverrideEnabled) continue;             // user override takes priority
            if (_manifestDllOverrideOptOuts.Contains(card.GameName)) continue; // user opted out
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            var manifestNames = GetManifestDllNames(card.GameName);
            if (manifestNames == null) continue;

            // Determine effective filenames — fall back to current installed name when manifest field is empty
            var effectiveRs = !string.IsNullOrEmpty(manifestNames.ReShade)
                ? manifestNames.ReShade
                : (card.RsRecord?.InstalledAs ?? (card.Is32Bit ? AuxInstallService.RsDcModeName32 : AuxInstallService.RsDcModeName));
            var effectiveDc = !string.IsNullOrEmpty(manifestNames.Dc)
                ? manifestNames.Dc
                : (card.DcRecord?.InstalledAs ?? (card.Is32Bit ? AuxInstallService.DcNormalName32 : AuxInstallService.DcNormalName));

            // ── Inject into _dllOverrides so the UI toggle turns on and filenames appear ──
            SetDllOverride(card.GameName, effectiveRs, effectiveDc);
            _manifestDllOverrideGames.Add(card.GameName);
            card.DllOverrideEnabled = true;

            // ── ReShade rename (only if file exists under the old name) ────────────
            if (card.RsRecord != null
                && !card.RsRecord.InstalledAs.Equals(effectiveRs, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = Path.Combine(card.InstallPath, card.RsRecord.InstalledAs);
                var newPath = Path.Combine(card.InstallPath, effectiveRs);
                try
                {
                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath)) File.Delete(newPath);
                        File.Move(oldPath, newPath);
                        card.RsRecord.InstalledAs = effectiveRs;
                        _auxInstaller.SaveAuxRecord(card.RsRecord);
                        card.RsInstalledFile = effectiveRs;
                        _crashReporter.Log($"[MainViewModel.ApplyManifestDllRenames] RS {card.GameName}: {Path.GetFileName(oldPath)} → {effectiveRs}");
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.ApplyManifestDllRenames] RS rename failed for '{card.GameName}' — {ex.Message}");
                }
            }

            // ── Display Commander rename ─────────────────────────────────────────
            if (card.DcRecord != null
                && !card.DcRecord.InstalledAs.Equals(effectiveDc, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = Path.Combine(card.InstallPath, card.DcRecord.InstalledAs);
                var newPath = Path.Combine(card.InstallPath, effectiveDc);
                try
                {
                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath)) File.Delete(newPath);
                        File.Move(oldPath, newPath);
                        card.DcRecord.InstalledAs = effectiveDc;
                        _auxInstaller.SaveAuxRecord(card.DcRecord);
                        card.DcInstalledFile = effectiveDc;
                        _crashReporter.Log($"[MainViewModel.ApplyManifestDllRenames] DC {card.GameName}: {Path.GetFileName(oldPath)} → {effectiveDc}");
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.ApplyManifestDllRenames] DC rename failed for '{card.GameName}' — {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Renames installed DC files across all games to match the current effective DC mode.
    /// Called after toggling DC mode or changing the global DLL filename so that a subsequent
    /// Refresh picks up the new naming.
    ///
    /// Shader transitions:
    ///   Off → On  → deploy shaders
    ///   On  → Off → remove shaders and restore originals
    ///   On  → On (DLL change only) → DLL rename only, no shader action (Requirement 10.3)
    /// </summary>
    public void ApplyDcModeSwitch((bool wasEnabled, string wasDllFileName) previous)
    {
        foreach (var card in _allCards)
        {
            if (card.DllOverrideEnabled) continue;
            if (card.IsLumaMode) continue;
            if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath)) continue;

            // Resolve the new effective DC mode for this game
            var (newEnabled, newDllFileName) = ResolveEffectiveDcMode(card.GameName, card);

            // Update RS blocked flag
            card.RsBlockedByDcMode = newEnabled;

            // ── Uninstall RDXC-installed ReShade DLL when DC mode is active ──
            // DC Mode reads ReShade from the DC AppData folder instead of game folders.
            // Use UninstallDllOnly to avoid touching shader folders (Requirement 5.1, 5.3).
            if (newEnabled && card.RsRecord != null)
            {
                try
                {
                    _auxInstaller.UninstallDllOnly(card.RsRecord);
                    _crashReporter.Log($"[MainViewModel.ApplyDcModeSwitch] Uninstalled ReShade from {card.GameName}");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.ApplyDcModeSwitch] RS uninstall failed for '{card.GameName}' — {ex.Message}");
                }
                card.RsRecord           = null;
                card.RsInstalledFile    = null;
                card.RsInstalledVersion = null;
                card.RsStatus = GameStatus.NotInstalled;
            }

            // ── DC file rename ──
            string? dcOldName = card.DcRecord?.InstalledAs;
            string? dcNewName = card.DcRecord != null ? newDllFileName : null;

            bool dcNeedsRename = dcOldName != null && dcNewName != null
                && !dcOldName.Equals(dcNewName, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(card.InstallPath, dcOldName));

            if (dcNeedsRename)
                RenameFile(card, isRs: false, dcOldName!, dcNewName!);

            // ── Shader transitions based on effective enabled change ──
            // Determine previous effective enabled state for this card.
            // Per-game overrides are unchanged by a global switch, so we only need to
            // check whether the card was previously enabled vs now.
            // Requirements: 3.1, 3.2, 3.3, 10.1, 10.2, 10.3
            bool wasEnabled = previous.wasEnabled;
            // Cards with per-game overrides are unaffected by global changes —
            // recompute their previous state from the override itself.
            var perGameOverride = GetPerGameDcModeOverride(card.GameName);
            if (perGameOverride == "Off")
                wasEnabled = false;
            else if (perGameOverride == "Custom")
                wasEnabled = true;
            else if (card.RequiresVulkanInstall && perGameOverride == null)
                wasEnabled = false;

            try
            {
                if (!wasEnabled && newEnabled && card.DcRecord != null)
                {
                    // Off → On with DC installed: deploy shaders
                    _shaderPackService.SyncGameFolder(card.InstallPath,
                        ResolveShaderSelection(card.GameName, card.ShaderModeOverride));
                }
                else if (wasEnabled && !newEnabled)
                {
                    // On → Off: remove shaders and restore originals
                    _shaderPackService.RemoveFromGameFolder(card.InstallPath);
                    _shaderPackService.RestoreOriginalIfPresent(card.InstallPath);
                }
                // Both On (DLL change only) or both Off → no shader action (Requirement 10.3)
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.ApplyDcModeSwitch] Shader transition failed for '{card.GameName}' — {ex.Message}");
            }

            card.NotifyAll();
        }

        // Sync ReShade DLLs to DC folder whenever DC mode is deployed
        AuxInstallService.SyncReShadeToDisplayCommander();
    }

    /// <summary>
    /// Applies DC Mode file renames for a single game card (by name).
    /// Called when the user saves a per-game DC Mode override so changes take
    /// effect immediately without requiring a full Refresh.
    /// </summary>
    public void ApplyDcModeSwitchForCard(string gameName, string? previousPerGameDcMode)
    {
        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null) return;
        if (card.DllOverrideEnabled) return;
        if (card.IsLumaMode) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath)) return;

        // Resolve the new effective DC mode for this game
        var (newEnabled, newDllFileName) = ResolveEffectiveDcMode(card.GameName, card);

        // Update RS blocked flag
        card.RsBlockedByDcMode = newEnabled;

        // Uninstall RDXC-installed ReShade DLL when DC mode is active for this game.
        // Use UninstallDllOnly to avoid touching shader folders (Requirement 5.2, 5.4).
        if (newEnabled && card.RsRecord != null)
        {
            try
            {
                _auxInstaller.UninstallDllOnly(card.RsRecord);
                _crashReporter.Log($"[MainViewModel.ApplyDcModeSwitchForCard] Uninstalled ReShade from {card.GameName}");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.ApplyDcModeSwitchForCard] RS uninstall failed for '{card.GameName}' — {ex.Message}");
            }
            card.RsRecord           = null;
            card.RsInstalledFile    = null;
            card.RsInstalledVersion = null;
            card.RsStatus = GameStatus.NotInstalled;
        }

        // DC file rename
        string? dcOldName = card.DcRecord?.InstalledAs;
        string? dcNewName = card.DcRecord != null ? newDllFileName : null;

        bool dcNeedsRename = dcOldName != null && dcNewName != null
            && !dcOldName.Equals(dcNewName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(card.InstallPath, dcOldName));

        if (dcNeedsRename)
            RenameFile(card, isRs: false, dcOldName!, dcNewName!);

        // ── Shader transitions based on effective enabled change ──
        // Determine previous effective enabled state from the previous per-game override.
        // Requirements: 3.4, 3.5, 3.6, 10.1, 10.2, 10.3
        bool wasEnabled;
        if (previousPerGameDcMode == "Off")
            wasEnabled = false;
        else if (previousPerGameDcMode == "Custom")
            wasEnabled = true;
        else // "Global" or null — was following global setting
        {
            if (card.RequiresVulkanInstall && previousPerGameDcMode == null)
                wasEnabled = false;
            else
                wasEnabled = DcModeEnabled;
        }

        try
        {
            if (!wasEnabled && newEnabled && card.DcRecord != null)
            {
                // Off → On with DC installed: deploy shaders
                _shaderPackService.SyncGameFolder(card.InstallPath,
                    ResolveShaderSelection(card.GameName, card.ShaderModeOverride));
            }
            else if (wasEnabled && !newEnabled)
            {
                // On → Off: remove shaders and restore originals
                _shaderPackService.RemoveFromGameFolder(card.InstallPath);
                _shaderPackService.RestoreOriginalIfPresent(card.InstallPath);
            }
            // Both On (DLL change only) or both Off → no shader action (Requirement 10.3)
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.ApplyDcModeSwitchForCard] Shader transition failed for '{card.GameName}' — {ex.Message}");
        }

        card.NotifyAll();
    }

    /// <summary>Renames a single DC or ReShade file for a game card during a DC Mode switch.
    /// Foreign DLLs at the destination are backed up to <c>.original</c>; foreign
    /// DLLs that were previously displaced are restored when the slot is vacated.</summary>
    private void RenameFile(GameCardViewModel card, bool isRs, string oldName, string newName)
    {
        var oldPath = Path.Combine(card.InstallPath, oldName);
        var newPath = Path.Combine(card.InstallPath, newName);
        var label = isRs ? "RS" : "DC";

        try
        {
            // Back up any foreign DLL sitting in the destination slot
            // (e.g. a third-party dxgi.dll or winmm.dll) instead of deleting it.
            if (File.Exists(newPath))
            {
                if (!AuxInstallService.BackupForeignDll(newPath))
                    File.Delete(newPath);   // known RDXC file — safe to remove
            }

            File.Move(oldPath, newPath);

            // Restore any previously-backed-up foreign DLL now that we've
            // vacated the old slot (e.g. switching DC from dxgi.dll → winmm.dll
            // frees dxgi.dll for the original owner).
            AuxInstallService.RestoreForeignDll(oldPath);

            if (isRs)
            {
                card.RsRecord!.InstalledAs = newName;
                card.RsInstalledFile = newName;
                _auxInstaller.SaveAuxRecord(card.RsRecord);
            }
            else
            {
                card.DcRecord!.InstalledAs = newName;
                card.DcInstalledFile = newName;
                _auxInstaller.SaveAuxRecord(card.DcRecord);
            }
            _crashReporter.Log($"[MainViewModel.RenameFile] {label} {card.GameName}: {oldName} → {newName}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.RenameFile] {label} rename failed for '{card.GameName}' — {ex.Message}");
        }
    }



    // VerboseLogging change handling delegated to SettingsViewModel

    partial void OnLumaFeatureEnabledChanged(bool value)
    {
        foreach (var c in _allCards) c.LumaFeatureEnabled = value;
    }

    partial void OnSelectedGameChanged(GameCardViewModel? oldValue, GameCardViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    public string? GetPerGameDcModeOverride(string gameName)
        => _perGameDcModeOverride.TryGetValue(gameName, out var v) ? v : null;

    public void SetPerGameDcModeOverride(string gameName, string? mode)
    {
        if (mode != null)
            _perGameDcModeOverride[gameName] = mode;
        else
            _perGameDcModeOverride.Remove(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) { card.PerGameDcMode = mode; card.NotifyAll(); }
    }

    /// <summary>Returns the persisted DC Mode Custom DLL filename for a game, or null if none set.</summary>
    public string? GetDcCustomDllFileName(string gameName)
        => _dcCustomDllFileNames.TryGetValue(gameName, out var v) ? v : null;

    /// <summary>Sets or removes the DC Mode Custom DLL filename for a game.</summary>
    public void SetDcCustomDllFileName(string gameName, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
            _dcCustomDllFileNames[gameName] = fileName;
        else
            _dcCustomDllFileNames.Remove(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) { card.DcCustomDllFileName = fileName; card.NotifyAll(); }
    }

    /// <summary>
    /// Resolves the effective DC mode (enabled flag + DLL filename) for a game,
    /// walking the resolution chain: DLL override / Luma → per-game Custom → per-game Off →
    /// Global/absent → Vulkan check → global enabled check → manifest override → global DLL filename.
    /// </summary>
    public (bool enabled, string dllFileName) ResolveEffectiveDcMode(string gameName, GameCardViewModel card)
    {
        // DLL override takes full precedence
        if (card.DllOverrideEnabled) return (false, "");
        if (card.IsLumaMode) return (false, "");

        var perGameOverride = _gameNameService.PerGameDcModeOverride.TryGetValue(gameName, out var v) ? v : null;

        if (perGameOverride == "Off")
            return (false, card.Is32Bit ? AuxInstallService.DcNormalName32 : AuxInstallService.DcNormalName);

        if (perGameOverride == "Custom")
        {
            var customDll = GetDcCustomDllFileName(gameName);
            return (true, !string.IsNullOrWhiteSpace(customDll) ? customDll : "dxgi.dll");
        }

        // "Global" or absent
        if (card.RequiresVulkanInstall && perGameOverride == null)
            return (false, card.Is32Bit ? AuxInstallService.DcNormalName32 : AuxInstallService.DcNormalName);

        if (!DcModeEnabled)
            return (false, card.Is32Bit ? AuxInstallService.DcNormalName32 : AuxInstallService.DcNormalName);

        // Global DC mode is on — check manifest override
        var manifestDll = GetManifestDllNames(gameName)?.Dc;
        if (!string.IsNullOrWhiteSpace(manifestDll) && perGameOverride != "Custom")
            return (true, manifestDll);

        return (true, DcDllFileName);
    }

    /// <summary>Games for which the user has toggled UE-Extended ON.</summary>
    private HashSet<string> _ueExtendedGames => _gameNameService.UeExtendedGames;
    /// <summary>Per-game DC Mode overrides. Key = game name, Value = "Global", "Off", or "Custom". Absent = follow global.</summary>
    private Dictionary<string, string> _perGameDcModeOverride => _gameNameService.PerGameDcModeOverride;
    /// <summary>Per-game DC Mode Custom DLL filenames. Key = game name, Value = custom DLL filename.</summary>
    private Dictionary<string, string> _dcCustomDllFileNames => _gameNameService.DcCustomDllFileNames;
    private HashSet<string> _updateAllExcludedReShade => _gameNameService.UpdateAllExcludedReShade;
    private HashSet<string> _updateAllExcludedDc => _gameNameService.UpdateAllExcludedDc;
    private HashSet<string> _updateAllExcludedRenoDx => _gameNameService.UpdateAllExcludedRenoDx;
    private Dictionary<string, string> _perGameShaderMode => _gameNameService.PerGameShaderMode;
    /// <summary>Per-game Vulkan rendering path preferences. Key = game name, Value = "DirectX" or "Vulkan".</summary>
    private Dictionary<string, string> _vulkanRenderingPaths => _gameNameService.VulkanRenderingPaths;
    /// <summary>Session-scoped flag — true after the global Vulkan layer warning has been shown once this session.</summary>
    private bool _vulkanLayerWarningShownThisSession = false;

    /// <summary>Returns the persisted Vulkan rendering path for a game, or "DirectX" if none set.</summary>
    public string GetVulkanRenderingPath(string gameName)
        => _vulkanRenderingPaths.TryGetValue(gameName, out var path) ? path : "DirectX";

    /// <summary>Sets the per-game Vulkan rendering path preference. "DirectX" removes the override (default).</summary>
    public void SetVulkanRenderingPath(string gameName, string renderingPath)
    {
        if (renderingPath == "DirectX")
            _vulkanRenderingPaths.Remove(gameName);
        else
            _vulkanRenderingPaths[gameName] = renderingPath;
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.VulkanRenderingPath = renderingPath;
            card.NotifyAll();
        }
    }

    // ── DLL Naming Override ───────────────────────────────────────────────────────

    /// <summary>Per-game DLL naming overrides — delegated to DllOverrideService.</summary>
    private Dictionary<string, DllOverrideConfig> _dllOverrides => _dllOverrideService.GetAllOverrides();

    /// <summary>
    /// Tracks games whose DLL override was injected from the remote manifest rather than set by the user.
    /// These entries are shown in the UI like user overrides but are NOT persisted to settings.json —
    /// they are re-applied from the manifest on every launch/refresh.
    /// </summary>
    private HashSet<string> _manifestDllOverrideGames => _dllOverrideService.ManifestDllOverrideGames;

    /// <summary>
    /// Games where the user has explicitly disabled a manifest-driven DLL override.
    /// These are persisted to settings.json so the opt-out survives refreshes.
    /// </summary>
    private HashSet<string> _manifestDllOverrideOptOuts => _dllOverrideService.ManifestDllOverrideOptOuts;

    // ── Folder Override ──────────────────────────────────────────────────────────

    /// <summary>Per-game install folder overrides. Key = game name, Value = "overridePath|originalPath".</summary>
    private Dictionary<string, string> _folderOverrides => _gameNameService.FolderOverrides;

    public void SetFolderOverride(string gameName, string folderPath)
    {
        // Preserve the original path if this is the first override
        string original = "";
        if (_folderOverrides.TryGetValue(gameName, out var existing))
        {
            var parts = existing.Split('|');
            original = parts.Length > 1 ? parts[1] : parts[0];
        }
        else
        {
            // First time — find the current card's path as original
            var card = _allCards.FirstOrDefault(c =>
                c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            original = card?.DetectedGame?.InstallPath ?? card?.InstallPath ?? "";
        }
        _folderOverrides[gameName] = $"{folderPath}|{original}";
        SaveNameMappings();
        SaveLibrary();
    }

    /// <summary>
    /// Resets the folder for an auto-detected game back to its original detected path.
    /// For manual games, removes the game entirely.
    /// </summary>
    public void ResetFolderOverride(GameCardViewModel card)
    {
        if (card.IsManuallyAdded)
        {
            RemoveManualGameCommand.Execute(card);
            return;
        }

        // Retrieve original path
        var originalPath = "";
        if (_folderOverrides.TryGetValue(card.GameName, out var stored))
        {
            var parts = stored.Split('|');
            originalPath = parts.Length > 1 ? parts[1] : "";
        }

        _folderOverrides.Remove(card.GameName);

        if (!string.IsNullOrEmpty(originalPath))
        {
            card.InstallPath = originalPath;
            if (card.DetectedGame != null)
                card.DetectedGame.InstallPath = originalPath;
        }

        SaveNameMappings();
        SaveLibrary();
        card.NotifyAll();
    }

    public string? GetFolderOverride(string gameName)
    {
        if (_folderOverrides.TryGetValue(gameName, out var stored))
        {
            var parts = stored.Split('|');
            return parts[0]; // Return just the override path
        }
        return null;
    }

    public bool HasDllOverride(string gameName) => _dllOverrideService.HasDllOverride(gameName);

    public DllOverrideConfig? GetDllOverride(string gameName)
        => _dllOverrideService.GetDllOverride(gameName);

    public void SetDllOverride(string gameName, string reshadeFileName, string dcFileName)
        => _dllOverrideService.SetDllOverride(gameName, reshadeFileName, dcFileName);

    public void RemoveDllOverride(string gameName)
        => _dllOverrideService.RemoveDllOverride(gameName);

    /// <summary>
    /// Called when DLL override is toggled ON — renames existing ReShade and DC
    /// files in the game folder to the custom filenames so they stay installed.
    /// </summary>
    public void EnableDllOverride(GameCardViewModel card, string reshadeFileName, string dcFileName)
        => _dllOverrideService.EnableDllOverride(card, reshadeFileName, dcFileName);

    /// <summary>
    /// Called when DLL override is already ON and the filenames are updated —
    /// renames existing files on disk to the new custom names.
    /// </summary>
    public void UpdateDllOverrideNames(GameCardViewModel card, string newRsName, string newDcName)
        => _dllOverrideService.UpdateDllOverrideNames(card, newRsName, newDcName);

    /// <summary>
    /// Called when DLL override is toggled OFF — removes the custom-named DLL files from the game folder.
    /// </summary>
    public void DisableDllOverride(GameCardViewModel card)
        => _dllOverrideService.DisableDllOverride(card);

    /// <summary>
    /// Games that default to UE-Extended and show "Extended UE Native HDR" instead of "Generic UE".
    /// These games are auto-set to UE-Extended on first build — no toggle needed.
    /// </summary>
    private static readonly HashSet<string> NativeHdrGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Avowed",
        "Lies of P",
        "Lost Soul Aside",
        "Hell is Us",
        "Mafia: The Old Country",
        "Returnal",
        "Marvel's Midnight Suns",
        "Mortal Kombat 1",
        "Alone in the Dark",
        "Still Wakes the Deep",
    };

    /// <summary>
    /// Checks if a game name matches any entry in the NativeHdrGames whitelist
    /// or the remote manifest's native HDR list.
    /// Strips ™, ®, © symbols before comparison to handle store names like "Lost Soul Aside™".
    /// </summary>
    private bool IsNativeHdrGameMatch(string gameName)
        => MatchesGameSet(gameName, NativeHdrGames)
           || (_manifestNativeHdrGames.Count > 0 && MatchesGameSet(gameName, _manifestNativeHdrGames));

    /// <summary>
    /// Checks if a game name matches any entry in the user's _ueExtendedGames set.
    /// Strips ™, ®, © symbols before comparison.
    /// </summary>
    private bool IsUeExtendedGameMatch(string gameName)
        => MatchesGameSet(gameName, _ueExtendedGames);

    /// <summary>
    /// Returns the effective Is32Bit flag for a game, giving manifest overrides priority over PE header auto-detection.
    /// Manifest thirtyTwoBitGames forces 32-bit; sixtyFourBitGames forces 64-bit (overrides incorrect detection).
    /// </summary>
    private bool ResolveIs32Bit(string gameName, MachineType detectedMachine)
    {
        if (_manifest32BitGames.Count > 0 && MatchesGameSet(gameName, _manifest32BitGames))
            return true;
        if (_manifest64BitGames.Count > 0 && MatchesGameSet(gameName, _manifest64BitGames))
            return false;
        return detectedMachine == MachineType.I386;
    }

    /// <summary>
    /// Detects the graphics API for a game install path.
    /// Checks manifest overrides first, then Unity boot.config, PE imports,
    /// engine DLLs, subdirectory exes, D3D12 Agility SDK folders, and finally
    /// the engine type as a last-resort heuristic.
    /// </summary>
    private GraphicsApiType DetectGraphicsApi(string installPath, EngineType engine = EngineType.Unknown, string? gameName = null)
    {
        // Manifest override takes top priority (for games where auto-detection fails).
        // Supports comma-separated values (e.g. "DX12, VLK") — returns the first
        // non-Vulkan API for the primary badge (prefers DX for display), falling
        // back to the first entry. The full set is handled by _DetectAllApisForCard.
        if (gameName != null && _manifest?.GraphicsApiOverrides != null
            && _manifest.GraphicsApiOverrides.TryGetValue(gameName, out var apiStr))
        {
            var overrideApis = GraphicsApiDetector.ParseApiStrings(apiStr);
            if (overrideApis.Count > 0)
            {
                // Prefer a DX API for the primary badge when multiple are listed
                var primary = overrideApis.FirstOrDefault(a => a != GraphicsApiType.Vulkan && a != GraphicsApiType.OpenGL);
                return primary != GraphicsApiType.Unknown ? primary : overrideApis.First();
            }
        }

        // Unity: boot.config is the most reliable source (PE imports are misleading)
        var unityResult = GraphicsApiDetector.DetectUnityFromBootConfig(installPath);
        if (unityResult != GraphicsApiType.Unknown)
            return unityResult;

        // Track best detected API across all file-based checks.
        // We don't return OpenGL immediately because Unity and Unreal statically
        // link opengl32.dll as a fallback but actually render with DirectX.
        var bestDetected = GraphicsApiType.Unknown;

        var exePath = _peHeaderService.FindGameExe(installPath);
        if (exePath != null)
        {
            var result = GraphicsApiDetector.Detect(exePath);
            if (result != GraphicsApiType.Unknown && result != GraphicsApiType.OpenGL)
                return result;
            if (result != GraphicsApiType.Unknown)
                bestDetected = result;
        }

        // Fallback: scan DLLs in the install directory for graphics imports.
        // Many custom engines (Silk, Chrome, etc.) put all graphics calls in an
        // engine DLL rather than the exe. We scan any DLL that isn't a known
        // system/runtime library.
        try
        {
            foreach (var dllPath in Directory.GetFiles(installPath, "*.dll"))
            {
                var dllName = Path.GetFileName(dllPath);
                if (IsSystemOrRuntimeDll(dllName))
                    continue;
                var result = GraphicsApiDetector.Detect(dllPath);
                if (result != GraphicsApiType.Unknown && result != GraphicsApiType.OpenGL)
                    return result;
                if (result != GraphicsApiType.Unknown && bestDetected == GraphicsApiType.Unknown)
                    bestDetected = result;
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.DetectGraphicsApi] DLL scan failed for '{installPath}' — {ex.Message}"); }

        // Fallback: scan exe files in common subdirectories (some games put the
        // real exe in Bin64/, x64/, Win64/, etc. while the root has a launcher)
        string[] subDirs = ["Bin64", "Bin", "x64", "Win64", "Binaries", "Binaries\\Win64", "Binaries\\WinGDK"];
        foreach (var sub in subDirs)
        {
            var subPath = Path.Combine(installPath, sub);
            if (!Directory.Exists(subPath)) continue;
            var subExe = _peHeaderService.FindGameExe(subPath);
            if (subExe != null)
            {
                var result = GraphicsApiDetector.Detect(subExe);
                if (result != GraphicsApiType.Unknown && result != GraphicsApiType.OpenGL)
                    return result;
                if (result != GraphicsApiType.Unknown && bestDetected == GraphicsApiType.Unknown)
                    bestDetected = result;
            }
        }

        // Fallback: check for D3D12Core.dll in subdirectories (e.g. d3d12/ folder).
        // Some games (especially Game Pass/WindowsApps) ship a D3D12 Agility SDK
        // folder next to the exe, which is a strong DX12 signal.
        try
        {
            foreach (var dir in Directory.GetDirectories(installPath))
            {
                if (File.Exists(Path.Combine(dir, "D3D12Core.dll")))
                    return GraphicsApiType.DirectX12;
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.DetectGraphicsApi] D3D12Core scan failed for '{installPath}' — {ex.Message}"); }

        // If the only graphics API found was OpenGL and this is a Unity or Unreal
        // game, override to DX11. Both engines statically link opengl32.dll as a
        // fallback renderer but default to DirectX on Windows.
        if (bestDetected == GraphicsApiType.OpenGL
            && engine is EngineType.Unreal or EngineType.Unity)
            return GraphicsApiType.DirectX11;

        if (bestDetected != GraphicsApiType.Unknown)
            return bestDetected;

        // Last resort: infer from engine type (covers access-denied scenarios
        // like WindowsApps/Xbox Game Pass installs)
        return engine switch
        {
            EngineType.Unreal       => GraphicsApiType.DirectX11,
            EngineType.UnrealLegacy => GraphicsApiType.DirectX9,
            EngineType.Unity        => GraphicsApiType.DirectX11,
            _                       => GraphicsApiType.Unknown,
        };
    }

    /// <summary>
    /// Scans ALL executables in the install directory (and common subdirectories)
    /// and returns the union of all detected graphics APIs. This handles games
    /// like Baldur's Gate 3 that ship separate DX and Vulkan executables.
    /// </summary>
    private HashSet<GraphicsApiType> _DetectAllApisForCard(string installPath, string? gameName = null)
    {
        var result = new HashSet<GraphicsApiType>();

        // Manifest override — merge multi-API tags (e.g. "DX12, VLK")
        if (gameName != null && _manifest?.GraphicsApiOverrides != null
            && _manifest.GraphicsApiOverrides.TryGetValue(gameName, out var apiStr))
        {
            result.UnionWith(GraphicsApiDetector.ParseApiStrings(apiStr));
        }

        // Scan all exes in the install directory
        ScanAllExesInDir(installPath, result);

        // Also scan common subdirectories (mirrors DetectGraphicsApi fallback logic)
        string[] subDirs = ["Bin64", "Bin", "x64", "Win64", "Binaries", "Binaries\\Win64", "Binaries\\WinGDK"];
        foreach (var sub in subDirs)
        {
            var subPath = Path.Combine(installPath, sub);
            if (Directory.Exists(subPath))
                ScanAllExesInDir(subPath, result);
        }

        return result;
    }

    /// <summary>
    /// Scans all .exe files in a directory and adds their detected APIs to the result set.
    /// </summary>
    private static void ScanAllExesInDir(string dirPath, HashSet<GraphicsApiType> result)
    {
        try
        {
            foreach (var exeFile in Directory.GetFiles(dirPath, "*.exe"))
            {
                var apis = GraphicsApiDetector.DetectAllApis(exeFile);
                result.UnionWith(apis);
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanAllExesInDir] Exe scan failed for '{dirPath}' — {ex.Message}"); }
    }

    /// <summary>
    /// Returns true if the DLL filename is a known system, runtime, or middleware
    /// library that should be skipped when scanning for graphics API imports.
    /// </summary>
    private static bool IsSystemOrRuntimeDll(string fileName)
    {
        var name = fileName.ToLowerInvariant();

        // Known graphics DLLs — we want GraphicsApiDetector to scan these, not skip them
        // (they're handled by the DllMap inside Detect), but they won't contain
        // *other* graphics imports so scanning them is pointless.
        if (name is "d3d8.dll" or "d3d9.dll" or "d3d10.dll" or "d3d10_1.dll"
            or "d3d11.dll" or "d3d12.dll" or "dxgi.dll" or "vulkan-1.dll" or "opengl32.dll")
            return true;

        // System / CRT / VC runtime
        if (name.StartsWith("api-ms-win-") || name.StartsWith("vcruntime")
            || name.StartsWith("msvcp") || name.StartsWith("ucrtbase"))
            return true;

        // Common system DLLs
        if (name is "kernel32.dll" or "user32.dll" or "gdi32.dll" or "advapi32.dll"
            or "shell32.dll" or "ole32.dll" or "oleaut32.dll" or "shlwapi.dll"
            or "winmm.dll" or "ws2_32.dll" or "crypt32.dll" or "dbghelp.dll"
            or "imm32.dll" or "setupapi.dll" or "winhttp.dll" or "wininet.dll"
            or "normaliz.dll" or "propsys.dll" or "dwmapi.dll" or "hid.dll"
            or "dnsapi.dll" or "iphlpapi.dll" or "wldap32.dll" or "psapi.dll"
            or "version.dll" or "comctl32.dll" or "comdlg32.dll" or "wintrust.dll"
            or "secur32.dll" or "netapi32.dll" or "userenv.dll" or "bcrypt.dll")
            return true;

        // Common middleware / non-graphics game DLLs
        if (name.StartsWith("steam_api") || name.StartsWith("steamworks")
            || name.StartsWith("xinput") || name.StartsWith("dinput")
            || name.StartsWith("x3daudio") || name.StartsWith("xaudio")
            || name.StartsWith("nvngx") || name.StartsWith("sl.")
            || name.StartsWith("dstorage") || name.StartsWith("mfplat")
            || name.StartsWith("mfreadwrite") || name.StartsWith("ffx_")
            || name.StartsWith("scripthook") || name.StartsWith("bink")
            || name.StartsWith("oo2core") || name.StartsWith("amd_ags")
            || name.StartsWith("nvlowlatency"))
            return true;

        return false;
    }

    /// <summary>
    /// Returns the manifest engine override for a game, if one exists.
    /// The out parameter <paramref name="overrideEngine"/> is the EngineType to use for
    /// mod/fallback selection ("Unreal", "Unreal (Legacy)", "Unity" map to their EngineType;
    /// all other values stay Unknown so the game falls into Other).
    /// The return value is the display label to use in EngineHint (may differ from the
    /// EngineType — e.g. "Silk" stays "Silk" but overrideEngine is Unknown).
    /// Returns null if no override is defined for this game.
    /// </summary>
    private string? ResolveEngineOverride(string gameName, out EngineType overrideEngine)
    {
        overrideEngine = EngineType.Unknown;
        if (_manifestEngineOverrides.Count == 0) return null;

        string? label = null;
        if (_manifestEngineOverrides.TryGetValue(gameName, out var raw))
            label = raw;
        else
        {
            // Try stripped name (™®© removed)
            var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
            if (stripped != gameName && _manifestEngineOverrides.TryGetValue(stripped, out raw))
                label = raw;
        }

        if (label == null) return null;

        // Map known engine strings to EngineType for mod/fallback logic
        overrideEngine = label.Equals("Unreal", StringComparison.OrdinalIgnoreCase)            ? EngineType.Unreal
                       : label.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase)     ? EngineType.Unreal
                       : label.StartsWith("Unreal (Legacy)", StringComparison.OrdinalIgnoreCase) ? EngineType.UnrealLegacy
                       : label.Equals("Unity", StringComparison.OrdinalIgnoreCase)              ? EngineType.Unity
                       : EngineType.Unknown;

        return label;
    }

    /// <summary>
    /// Returns the manifest-defined DLL filenames for a game, if any.
    /// User-set per-game DLL overrides always take priority over the manifest.
    /// Returns null if no manifest override is defined.
    /// </summary>
    private ManifestDllNames? GetManifestDllNames(string gameName)
    {
        if (_manifestDllNameOverrides.Count == 0) return null;
        if (_manifestDllNameOverrides.TryGetValue(gameName, out var names)) return names;
        // Try stripped name (™®© removed)
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName && _manifestDllNameOverrides.TryGetValue(stripped, out names)) return names;
        // Normalized comparison as last resort
        var norm = NormalizeForLookup(gameName);
        foreach (var (key, value) in _manifestDllNameOverrides)
        {
            if (NormalizeForLookup(key) == norm) return value;
        }
        return null;
    }

    /// <summary>
    /// Checks if <paramref name="gameName"/> matches any entry in <paramref name="gameSet"/>.
    /// Tries exact match first, then stripped (™®© removed), then fully normalised.
    /// </summary>
    private static bool MatchesGameSet(string gameName, IEnumerable<string> gameSet)
    {
        // Fast path: exact match (works for HashSet and static lists)
        if (gameSet is ICollection<string> col && col.Contains(gameName)) return true;
        if (gameSet is not ICollection<string> && gameSet.Contains(gameName)) return true;

        // Strip trademark symbols and retry
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName)
        {
            if (gameSet is ICollection<string> col2 && col2.Contains(stripped)) return true;
            if (gameSet is not ICollection<string> && gameSet.Contains(stripped)) return true;
        }

        // Normalised comparison as last resort
        var norm = NormalizeForLookup(gameName);
        foreach (var entry in gameSet)
        {
            if (NormalizeForLookup(entry) == norm) return true;
        }
        return false;
    }



    /// <summary>Returns the per-game shader mode override, or "Global" if no override set.</summary>
    public string GetPerGameShaderMode(string gameName)
        => _perGameShaderMode.TryGetValue(gameName, out var mode) ? mode : "Global";

    /// <summary>Sets the per-game shader mode override. "Global" removes the override.</summary>
    public void SetPerGameShaderMode(string gameName, string mode)
    {
        if (mode == "Global")
        {
            _perGameShaderMode.Remove(gameName);
            // Discard per-game shader selection when reverting to global
            _gameNameService.PerGameShaderSelection.Remove(gameName);
        }
        else
            _perGameShaderMode[gameName] = mode;
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.ShaderModeOverride = mode == "Global" ? null : mode;
        }
    }

    public bool AnyUpdateAvailable =>
        _allCards.Any(c => c.Status    == GameStatus.UpdateAvailable ||
                           c.DcStatus  == GameStatus.UpdateAvailable ||
                           c.RsStatus  == GameStatus.UpdateAvailable);

    // Button colours — purple when updates available, dim when idle
    public string UpdateAllBtnBackground => AnyUpdateAvailable ? "#201838" : "#1E242C";
    public string UpdateAllBtnForeground  => AnyUpdateAvailable ? "#B898E8" : "#6B7A8E";
    public string UpdateAllBtnBorder      => AnyUpdateAvailable ? "#3A2860" : "#283240";


    public bool IsUpdateAllExcludedReShade(string gameName) => _updateAllExcludedReShade.Contains(gameName);
    public bool IsUpdateAllExcludedDc(string gameName) => _updateAllExcludedDc.Contains(gameName);
    public bool IsUpdateAllExcludedRenoDx(string gameName) => _updateAllExcludedRenoDx.Contains(gameName);

    public void ToggleUpdateAllExclusionReShade(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedReShade;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllReShade = set.Contains(gameName);
    }

    public void ToggleUpdateAllExclusionDc(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedDc;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllDc = set.Contains(gameName);
    }

    public void ToggleUpdateAllExclusionRenoDx(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedRenoDx;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllRenoDx = set.Contains(gameName);
    }
    private void LoadNameMappings()
    {
        _isLoadingSettings = true;
        try
        {
            _gameNameService.LoadNameMappings(
                _dllOverrideService,
                _settingsViewModel,
                val => DcModeEnabled = val,
                val => DcDllFileName = val,
                grid => IsGridLayout = grid);
            _crashReporter.Log("[MainViewModel.LoadNameMappings] Delegated to GameNameService");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>
    /// Renames a game everywhere: card, detected game, all settings HashSets/Dicts,
    /// persisted install records (RenoDX, DC, ReShade, Luma), and library file.
    /// Call from the UI thread. Triggers a non-destructive rescan so wiki matching
    /// picks up the corrected name.
    /// </summary>
    public void RenameGame(string oldName, string newName)
    {
        _gameNameService.RenameGame(oldName, newName, _allCards, _manualGames, _dllOverrideService);
        SaveNameMappings();
        SaveLibrary();
        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(newName, StringComparison.OrdinalIgnoreCase));
        card?.NotifyAll();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    /// <summary>
    /// Returns the original store-detected name for a game, before any user rename.
    /// If the game was never renamed, returns null.
    /// </summary>
    public string? GetOriginalStoreName(string currentName)
        => _gameNameService.GetOriginalStoreName(currentName);

    /// <summary>
    /// Removes any persisted rename for the given game, restoring it to its
    /// store-detected name on the next refresh.
    /// </summary>
    public void RemoveGameRename(string gameName)
    {
        _gameNameService.RemoveGameRename(gameName, _allCards);
        SaveNameMappings();
    }

    private static void MigrateHashSet(HashSet<string> set, string oldName, string newName)
        => GameNameService.MigrateHashSet(set, oldName, newName);

    private static void MigrateDict<TValue>(Dictionary<string, TValue> dict, string oldName, string newName)
        => GameNameService.MigrateDict(dict, oldName, newName);

    private void ApplyGameRenames(List<DetectedGame> games)
        => _gameNameService.ApplyGameRenames(games);

    private void ApplyFolderOverrides(List<DetectedGame> games)
        => _gameNameService.ApplyFolderOverrides(games);

    public void AddNameMapping(string detectedName, string wikiKey)
    {
        _gameNameService.AddNameMapping(detectedName, wikiKey);
        SaveNameMappings();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    public string? GetNameMapping(string detectedName)
        => _gameNameService.GetNameMapping(detectedName);

    public void RemoveNameMapping(string detectedName)
    {
        _gameNameService.RemoveNameMapping(detectedName);
        SaveNameMappings();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    public bool IsWikiExcluded(string gameName) =>
        _wikiExclusions.Contains(gameName);

    /// <summary>
    /// Toggles wiki exclusion for a game and updates its card in-place — no full rescan.
    /// Excluded games show a Discord link instead of the install button.
    /// </summary>
    /// <summary>
    /// Toggles wiki exclusion for a game and updates its card synchronously in-place.
    /// This is always called from the UI thread (via dialog ContinueWith on the
    /// synchronisation context), so we update card properties directly — no
    /// DispatcherQueue.TryEnqueue needed, and the UI reflects the change immediately
    /// when the dialog closes without requiring a manual refresh.
    /// </summary>
    public void ToggleWikiExclusion(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return;

        bool nowExcluded;
        if (_wikiExclusions.Contains(gameName))
        {
            _wikiExclusions.Remove(gameName);
            nowExcluded = false;
        }
        else
        {
            _wikiExclusions.Add(gameName);
            nowExcluded = true;
        }
        SaveNameMappings();

        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null)
        {
            DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
            return;
        }

        if (nowExcluded)
        {
            // Exclude: strip wiki mod and show Discord button
            card.Mod           = null;
            card.IsExternalOnly = true;
            card.ExternalUrl   = "https://discord.gg/gF4GRJWZ2A";
            card.ExternalLabel = "Download from Discord";
            card.DiscordUrl    = "https://discord.gg/gF4GRJWZ2A";
            card.WikiStatus    = "💬";
            card.Notes         = "";
            card.IsGenericMod  = false;
            if (card.Status != GameStatus.Installed)
                card.Status = GameStatus.Available;
        }
        else
        {
            // Un-exclude: re-run wiki match in-place and restore the card
            var game = card.DetectedGame;
            if (game == null)
            {
                DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
                return;
            }
            var (_, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
            // Apply manifest engine override (takes priority over auto-detection)
            var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);
            if (engineOverrideLabel != null) engine = engineOverride;
            var mod         = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
            // Wiki unlink: discard false fuzzy match so the game uses its generic engine addon
            if (mod != null && _manifestWikiUnlinks.Contains(game.Name)) mod = null;
            var fallback    = mod == null ? (engine == EngineType.Unreal ? MakeGenericUnreal()
                                            : engine == EngineType.Unity  ? MakeGenericUnity()
                                            : null) : null;

            // Wiki mod matched but has no download URL — inject generic engine addon URL
            if (mod != null && mod.SnapshotUrl == null && mod.NexusUrl == null && mod.DiscordUrl == null)
            {
                var engineFallback = engine == EngineType.Unreal ? MakeGenericUnreal()
                                   : engine == EngineType.Unity  ? MakeGenericUnity() : null;
                if (engineFallback != null)
                {
                    mod = new GameMod
                    {
                        Name            = mod.Name,
                        Maintainer      = engineFallback.Maintainer,
                        SnapshotUrl     = engineFallback.SnapshotUrl,
                        SnapshotUrl32   = engineFallback.SnapshotUrl32,
                        Status          = mod.Status,
                        Notes           = mod.Notes,
                        NameUrl         = mod.NameUrl,
                        IsGenericUnreal = engineFallback.IsGenericUnreal,
                        IsGenericUnity  = engineFallback.IsGenericUnity,
                    };
                    fallback = engineFallback;
                }
            }

            var effectiveMod = mod ?? fallback;

            // Apply manifest snapshot override
            if (_manifest?.SnapshotOverrides != null
                && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOvUrl)
                && !string.IsNullOrEmpty(snapshotOvUrl))
            {
                if (effectiveMod != null)
                    effectiveMod.SnapshotUrl = snapshotOvUrl;
                else
                    effectiveMod = new GameMod { Name = game.Name, SnapshotUrl = snapshotOvUrl, Status = "✅" };
            }

            card.Mod            = effectiveMod;
            card.IsExternalOnly = effectiveMod?.SnapshotUrl == null &&
                                  (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null);
            card.ExternalUrl    = effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "";
            card.ExternalLabel  = effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord";
            card.NexusUrl       = effectiveMod?.NexusUrl;
            card.DiscordUrl     = effectiveMod?.DiscordUrl;
            card.WikiStatus     = (mod == null && fallback != null && !card.UseUeExtended && !card.IsNativeHdrGame)
                                  ? "?"
                                  : effectiveMod?.Status ?? "—";
            card.Notes          = effectiveMod != null
                                  ? BuildNotes(game.Name, effectiveMod, fallback, _genericNotes, card.IsNativeHdrGame)
                                  : "";
            card.IsGenericMod   = card.UseUeExtended || (fallback != null && mod == null);
            if (card.Status != GameStatus.Installed)
                card.Status = effectiveMod != null ? GameStatus.Available : GameStatus.Available;
        }

        card.NotifyAll();
    }

    public const string UeExtendedUrl    = "https://marat569.github.io/renodx/renodx-ue-extended.addon64";
    public const string UeExtendedFile   = "renodx-ue-extended.addon64";
    public const string GenericUnrealFile = "renodx-unrealengine.addon64";

    /// <summary>
    /// Toggles the UE-Extended mode for a Generic UE card.
    /// When ON: Mod.SnapshotUrl → marat569 URL; if the standard generic file is on disk it is deleted.
    /// When OFF: Mod.SnapshotUrl → standard WikiService.GenericUnrealUrl; the extended file is deleted.
    /// Card updates synchronously — no refresh needed.
    /// </summary>
    public void ToggleUeExtended(GameCardViewModel card)
    {
        if (card == null) return;
        // Allow toggle for any UE card that shows the button:
        // IsGenericMod covers most cases, but also allow cards where Mod is null or IsGenericUnreal
        bool isEligible = card.IsGenericMod
                          || card.Mod == null
                          || (card.Mod?.IsGenericUnreal == true);
        if (!isEligible) return;

        bool nowExtended = !card.UseUeExtended;

        if (nowExtended)
            _ueExtendedGames.Add(card.GameName);
        else
            _ueExtendedGames.Remove(card.GameName);
        SaveNameMappings();

        // Swap the SnapshotUrl on the card's Mod in-place
        if (card.Mod != null)
            card.Mod.SnapshotUrl = nowExtended ? UeExtendedUrl : WikiService.GenericUnrealUrl;

        // Delete the opposing addon file from disk (if present)
        if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
        {
            try
            {
                var deleteFile = nowExtended ? GenericUnrealFile : UeExtendedFile;
                var deletePath = Path.Combine(card.InstallPath, deleteFile);
                if (File.Exists(deletePath))
                {
                    File.Delete(deletePath);
                    _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Deleted {deleteFile} from {card.InstallPath}");
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.ToggleUeExtended] Failed to delete file — {ex.Message}");
            }
        }

        // The toggle has swapped the target addon file. The old file was deleted above,
        // so the card is no longer "installed" — reset to Available and clear the record.
        // Leaving a stale InstalledRecord with the old RemoteFileSize would cause
        // CheckForUpdateAsync to compare the new URL's size against the old addon's size
        // and fire a false "update available" on the next refresh.
        if (card.InstalledRecord != null)
        {
            _installer.RemoveRecord(card.InstalledRecord);
            card.InstalledRecord        = null;
            card.InstalledAddonFileName = null;
            card.Status                 = GameStatus.Available;
        }

        card.UseUeExtended = nowExtended;
        card.NotifyAll();
    }

    /// <summary>
    /// Applies hardcoded per-game card overrides after BuildCards completes.
    /// Use this for games that need custom notes, forced Discord routing, or
    /// other card-level adjustments that can't be expressed in WikiService alone.
    /// </summary>
    private static void ApplyCardOverrides(List<GameCardViewModel> cards)
        => GameInitializationService.ApplyCardOverrides(cards);

    // ── Remote Manifest ───────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the working sets (_nameMappings, _ueExtendedGames, etc.)
    /// with values from the remote manifest. Local user overrides always take priority:
    /// manifest values are only applied where no local setting already exists.
    /// Must be called BEFORE BuildCards.
    /// </summary>
    private void ApplyManifest(RemoteManifest? manifest)
    {
        _gameInitializationService.ApplyManifest(
            manifest,
            _gameNameService,
            _dllOverrideService,
            _manifestNativeHdrGames,
            _manifestBlacklist,
            _manifest32BitGames,
            _manifest64BitGames,
            _manifestEngineOverrides,
            _manifestDllNameOverrides,
            _manifestWikiUnlinks,
            _installPathOverrides,
            NormalizeForLookup);
    }

    /// <summary>
    /// Applies wiki status overrides from the remote manifest to the fetched mod list.
    /// Called after _allMods is populated so that manifest-driven statuses win over
    /// the hardcoded WikiService.ApplyStatusOverrides pass.
    /// </summary>
    private void ApplyManifestStatusOverrides()
        => _gameInitializationService.ApplyManifestStatusOverrides(_manifest, _allMods);

    /// <summary>
    /// Applies manifest-driven card overrides AFTER the hardcoded ApplyCardOverrides pass.
    /// Handles GameNotes (appended/set if no hardcoded notes exist) and ForceExternalOnly.
    /// </summary>
    private static void ApplyManifestCardOverrides(RemoteManifest? manifest, List<GameCardViewModel> cards)
        => GameInitializationService.ApplyManifestCardOverrides(manifest, cards);

    /// <summary>Public entry point to persist all settings to disk.</summary>
    public void SaveSettingsPublic() => SaveNameMappings();

    /// <summary>Returns true if the current app version differs from the last seen version.</summary>
    public bool IsNewVersion()
    {
        var current = _updateService.CurrentVersion;
        var currentStr = $"{current.Major}.{current.Minor}.{current.Build}";
        return LastSeenVersion != currentStr;
    }

    /// <summary>Marks the current version as seen and saves settings.</summary>
    public void MarkVersionSeen()
    {
        var current = _updateService.CurrentVersion;
        LastSeenVersion = $"{current.Major}.{current.Minor}.{current.Build}";
        SaveSettingsPublic();
    }

    /// <summary>
    /// Reads the bundled RDXC_PatchNotes.md and extracts the last N version sections.
    /// Each section starts with "## vX.Y.Z".
    /// </summary>
    public static string GetRecentPatchNotes(int count = 3)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "RDXC_PatchNotes.md");
            if (!File.Exists(path)) return "Patch notes file not found.";

            var lines = File.ReadAllLines(path);
            var sections = new List<string>();
            var currentSection = new List<string>();
            bool inSection = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("## v"))
                {
                    if (inSection && currentSection.Count > 0)
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                    }
                    inSection = true;
                    currentSection.Add(line);
                }
                else if (inSection)
                {
                    // Stop at the "---" separator between versions (but don't include it)
                    if (line.Trim() == "---")
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                        inSection = false;
                    }
                    else
                    {
                        currentSection.Add(line);
                    }
                }
            }

            // Capture final section if still in progress
            if (inSection && currentSection.Count > 0 && sections.Count < count)
                sections.Add(string.Join("\n", currentSection));

            return sections.Count > 0
                ? string.Join("\n\n---\n\n", sections)
                : "No patch notes available.";
        }
        catch (Exception ex)
        {
            return $"Error reading patch notes: {ex.Message}";
        }
    }

    private void SaveNameMappings()
    {
        _gameNameService.SaveNameMappings(
            _dllOverrideService,
            _settingsViewModel,
            DcModeEnabled,
            DcDllFileName,
            IsGridLayout,
            _isLoadingSettings);
    }

    private void LoadThemeAndDensity()
    {
        _settingsViewModel.LoadThemeAndDensity();
    }

    // Normalize titles for tolerant lookup: remove punctuation, trademarks, parenthetical text, diacritics
    private static string NormalizeForLookup(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Remove common trademark symbols
        s = s.Replace("™", "").Replace("®", "").Replace("©", "");
        // Remove parenthetical content
        s = Regex.Replace(s, "\\([^)]*\\)", "");
        s = Regex.Replace(s, "\\[[^]]*\\]", "");
        // Normalize unicode and remove diacritics
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        var noDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);
        // Remove punctuation, keep letters/numbers and spaces
        var cleaned = Regex.Replace(noDiacritics, "[^0-9A-Za-z ]+", " ");
        // Collapse whitespace and trim
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        // Remove common edition suffixes
        cleaned = Regex.Replace(cleaned, "\\b(enhanced edition|remastered|edition|ultimate|definitive)\\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        return cleaned.ToLowerInvariant();
    }

    private string? GetGenericNote(string gameName, Dictionary<string, string> genericNotes)
    {
        if (string.IsNullOrEmpty(gameName) || genericNotes == null || genericNotes.Count == 0) return null;
        // Check user name mappings from JSON settings file
        try
        {
            var s = SettingsViewModel.LoadSettingsFile();
            if (s.TryGetValue("NameMappings", out var json) && !string.IsNullOrEmpty(json))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string,string>>(json);
                if (map != null)
                {
                    if (map.TryGetValue(gameName, out var mapped) && !string.IsNullOrEmpty(mapped))
                    {
                        if (genericNotes.TryGetValue(mapped, out var mv) && !string.IsNullOrEmpty(mv)) return mv;
                    }
                    var n = NormalizeForLookup(gameName);
                    foreach (var kv in map)
                    {
                        if (NormalizeForLookup(kv.Key).Equals(n, StringComparison.OrdinalIgnoreCase))
                        {
                            if (genericNotes.TryGetValue(kv.Value, out var mv2) && !string.IsNullOrEmpty(mv2)) return mv2;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.LookupGenericNotes] Name mapping lookup failed for '{gameName}' — {ex.Message}"); }
        // direct
        if (genericNotes.TryGetValue(gameName, out var v) && !string.IsNullOrEmpty(v)) return v;
        // detection-normalized
        try { var k = _gameDetectionService.NormalizeName(gameName); if (!string.IsNullOrEmpty(k) && genericNotes.TryGetValue(k, out var v2) && !string.IsNullOrEmpty(v2)) return v2; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.LookupGenericNotes] NormalizeName failed for '{gameName}' — {ex.Message}"); }
        // normalized-equality scan
        var tgt = NormalizeForLookup(gameName);
        foreach (var kv in genericNotes)
        {
            if (NormalizeForLookup(kv.Key).Equals(tgt, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }
        return null;
    }

    // InstallCompleted event handler removed — card state is updated in-place
    // by InstallModAsync, so no full rescan is needed after install.

    // ── Commands ──────────────────────────────────────────────────────────────────

    [RelayCommand] public void SetFilter(string filter) => _filterViewModel.SetFilter(filter);

    [RelayCommand]
    public void NavigateToSettings() => CurrentPage = AppPage.Settings;

    [RelayCommand]
    public void NavigateToGameView() => CurrentPage = AppPage.GameView;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        ApplyDcModeSwitch((wasEnabled: DcModeEnabled, wasDllFileName: DcDllFileName));
        await InitializeAsync(forceRescan: true);
    }

    [RelayCommand]
    public async Task FullRefreshAsync()
    {
        // Clear all caches so every game is re-scanned from disk.
        _engineTypeCache.Clear();
        _resolvedPathCache.Clear();
        _addonFileCache.Clear();
        _bitnessCache.Clear();
        ApplyDcModeSwitch((wasEnabled: DcModeEnabled, wasDllFileName: DcDllFileName));
        await InitializeAsync(forceRescan: true);
    }

    [RelayCommand]
    public void ToggleShowHidden()
    {
        _filterViewModel.ShowHidden = !_filterViewModel.ShowHidden;
        _filterViewModel.ApplyFilter();
    }

    [RelayCommand]
    public void ToggleHideGame(GameCardViewModel? card)
    {
        if (card == null) return;
        var key = card.GameName;
        _crashReporter.Log($"[MainViewModel.ToggleHide] {key} (currently hidden={card.IsHidden})");
        if (_hiddenGames.Contains(key))
            _hiddenGames.Remove(key);
        else
            _hiddenGames.Add(key);

        card.IsHidden = _hiddenGames.Contains(key);
        SaveLibrary();
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public void ToggleFavourite(GameCardViewModel? card)
    {
        if (card == null) return;
        var key = card.GameName;
        if (_favouriteGames.Contains(key))
            _favouriteGames.Remove(key);
        else
            _favouriteGames.Add(key);

        card.IsFavourite = _favouriteGames.Contains(key);
        SaveLibrary();
        // Only re-filter if on the Favourites tab (unfavouriting removes the card from view)
        if (_filterViewModel.ActiveFilters.Contains("Favourites"))
            _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public void RemoveManualGame(GameCardViewModel? card)
    {
        if (card == null) return;
        if (!card.IsManuallyAdded)
            return;

        // Remove manual entries and the corresponding card
        _manualGames.RemoveAll(g => g.Name.Equals(card.GameName, StringComparison.OrdinalIgnoreCase));
        _allCards.RemoveAll(c => c.IsManuallyAdded && c.GameName.Equals(card.GameName, StringComparison.OrdinalIgnoreCase));
        SaveLibrary();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public void AddManualGame(DetectedGame game)
    {
        if (_manualGames.Any(g => g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase))) return;
        _manualGames.Add(game);

        // Build card for this game immediately
        var (installPath, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
        // Apply manifest engine override (takes priority over auto-detection)
        var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);
        if (engineOverrideLabel != null) engine = engineOverride;

        // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
        if (_installPathOverrides.TryGetValue(game.Name, out var manualSubPath))
        {
            var overridePath = Path.Combine(game.InstallPath, manualSubPath);
            if (Directory.Exists(overridePath))
                installPath = overridePath;
        }

        var mod = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
        // Wiki unlink: discard false fuzzy match so the game uses its generic engine addon
        if (mod != null && _manifestWikiUnlinks.Contains(game.Name)) mod = null;
        var genericUnreal = MakeGenericUnreal();
        var genericUnity  = MakeGenericUnity();
        var fallback = mod == null ? (engine == EngineType.Unreal      ? genericUnreal
                                   : engine == EngineType.Unity       ? genericUnity : null) : null;

        // Wiki mod matched but has no download URL — inject generic engine addon URL
        if (mod != null && mod.SnapshotUrl == null && mod.NexusUrl == null && mod.DiscordUrl == null)
        {
            var engineFallback = engine == EngineType.Unreal ? genericUnreal
                               : engine == EngineType.Unity  ? genericUnity : null;
            if (engineFallback != null)
            {
                mod = new GameMod
                {
                    Name            = mod.Name,
                    Maintainer      = engineFallback.Maintainer,
                    SnapshotUrl     = engineFallback.SnapshotUrl,
                    SnapshotUrl32   = engineFallback.SnapshotUrl32,
                    Status          = mod.Status,
                    Notes           = mod.Notes,
                    NameUrl         = mod.NameUrl,
                    IsGenericUnreal = engineFallback.IsGenericUnreal,
                    IsGenericUnity  = engineFallback.IsGenericUnity,
                };
                fallback = engineFallback;
            }
        }

        var effectiveMod = mod ?? fallback; // null for unknown-engine / legacy games not on wiki

        var records = _installer.LoadAll();
        var record  = records.FirstOrDefault(r => r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

        // Fallback: match by InstallPath for records saved with mod name instead of game name
        var scanPath = installPath.Length > 0 ? installPath : game.InstallPath;
        if (record == null)
        {
            record = records.FirstOrDefault(r =>
                r.InstallPath.Equals(scanPath, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                record.GameName = game.Name;
                _installer.SaveRecordPublic(record);
            }
        }

        // Scan disk for any renodx-* addon file already installed
        var addonOnDisk = ScanForInstalledAddon(scanPath, effectiveMod);
        if (addonOnDisk != null && record == null)
        {
            record = new InstalledModRecord
            {
                GameName      = game.Name,
                InstallPath   = scanPath,
                AddonFileName = addonOnDisk,
                InstalledAt   = File.GetLastWriteTimeUtc(Path.Combine(scanPath, addonOnDisk)),
                SnapshotUrl   = ResolveAddonUrl(addonOnDisk),
            };
            _installer.SaveRecordPublic(record);
        }

        // Patch effectiveMod SnapshotUrl if installed addon has an override URL
        if (addonOnDisk != null && effectiveMod?.SnapshotUrl != null
            && _addonFileUrlOverrides.TryGetValue(addonOnDisk, out var addonOverrideUrlM))
        {
            effectiveMod = new GameMod
            {
                Name        = effectiveMod.Name,
                Maintainer  = effectiveMod.Maintainer,
                SnapshotUrl = addonOverrideUrlM,
                Status      = effectiveMod.Status,
                Notes       = effectiveMod.Notes,
                NexusUrl    = effectiveMod.NexusUrl,
                DiscordUrl  = effectiveMod.DiscordUrl,
                NameUrl     = effectiveMod.NameUrl,
                IsGenericUnreal = effectiveMod.IsGenericUnreal,
                IsGenericUnity  = effectiveMod.IsGenericUnity,
            };
        }

        // Named addon found on disk but no wiki entry → show Discord link
        if (addonOnDisk != null && effectiveMod == null)
        {
            effectiveMod = new GameMod
            {
                Name       = game.Name,
                Status     = "💬",
                DiscordUrl = "https://discord.gg/gF4GRJWZ2A",
            };
        }

        // ── Manifest snapshot override (same logic as BuildCards) ─────────────
        if (_manifest?.SnapshotOverrides != null
            && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOverrideUrlM)
            && !string.IsNullOrEmpty(snapshotOverrideUrlM))
        {
            if (effectiveMod != null)
            {
                effectiveMod.SnapshotUrl = snapshotOverrideUrlM;
            }
            else
            {
                effectiveMod = new GameMod
                {
                    Name        = game.Name,
                    SnapshotUrl = snapshotOverrideUrlM,
                    Status      = "✅",
                };
            }
        }

        // ── Apply NativeHdr / UE-Extended whitelist (same logic as BuildCards) ────
        bool isNativeHdr = IsNativeHdrGameMatch(game.Name);
        bool useUeExt = (addonOnDisk == UeExtendedFile)
                        || IsUeExtendedGameMatch(game.Name)
                        || (isNativeHdr && (effectiveMod?.IsGenericUnreal == true || engine == EngineType.Unreal));
        if (useUeExt && effectiveMod != null)
        {
            effectiveMod = new GameMod
            {
                Name            = effectiveMod?.Name ?? "Generic Unreal Engine",
                Maintainer      = effectiveMod?.Maintainer ?? "ShortFuse",
                SnapshotUrl     = UeExtendedUrl,
                Status          = effectiveMod?.Status ?? "✅",
                Notes           = effectiveMod?.Notes,
                IsGenericUnreal = true,
            };
            if (addonOnDisk == UeExtendedFile || isNativeHdr)
                _ueExtendedGames.Add(game.Name);
        }
        else if (useUeExt && effectiveMod == null)
        {
            effectiveMod = new GameMod
            {
                Name            = "Generic Unreal Engine",
                Maintainer      = "ShortFuse",
                SnapshotUrl     = UeExtendedUrl,
                Status          = "✅",
                IsGenericUnreal = true,
            };
            fallback = effectiveMod;
            if (isNativeHdr)
                _ueExtendedGames.Add(game.Name);
        }

        // UE-Extended whitelist supersedes Nexus/Discord external links
        if (useUeExt && effectiveMod != null)
        {
            effectiveMod.NexusUrl   = null;
            effectiveMod.DiscordUrl = null;
        }

        var auxRecordsManual = _auxInstaller.LoadAll();
        var dcRecManual = auxRecordsManual.FirstOrDefault(r =>
            r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType == AuxInstallService.TypeDc);
        var rsRecManual = auxRecordsManual.FirstOrDefault(r =>
            r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType == AuxInstallService.TypeReShade);

        // Drop stale records whose files no longer exist on disk
        if (rsRecManual != null && !File.Exists(Path.Combine(rsRecManual.InstallPath, rsRecManual.InstalledAs)))
        {
            _auxInstaller.RemoveRecord(rsRecManual);
            rsRecManual = null;
        }
        if (dcRecManual != null && !File.Exists(Path.Combine(dcRecManual.InstallPath, dcRecManual.InstalledAs)))
        {
            _auxInstaller.RemoveRecord(dcRecManual);
            dcRecManual = null;
        }

        // Detect bitness for the manually added game
        var manualMachine = _peHeaderService.DetectGameArchitecture(scanPath);
        _bitnessCache[scanPath.ToLowerInvariant()] = manualMachine;

        var card = new GameCardViewModel
        {
            GameName       = game.Name,
            Mod            = effectiveMod,
            DetectedGame   = game,
            InstallPath    = scanPath,
            Source         = "Manual",
            InstalledRecord = record,
            Status         = record != null ? GameStatus.Installed : GameStatus.Available,
            WikiStatus     = (_wikiExclusions.Contains(game.Name)
                               || (effectiveMod?.SnapshotUrl == null && effectiveMod?.DiscordUrl != null && effectiveMod?.NexusUrl == null))
                              ? "💬"
                              : (mod == null && fallback != null && !useUeExt && !isNativeHdr)
                                ? "?"
                                : effectiveMod?.Status ?? "—",
            Maintainer     = effectiveMod?.Maintainer ?? "",
            IsGenericMod   = useUeExt || (fallback != null && mod == null),
            EngineHint     = engineOverrideLabel != null
                           ? (useUeExt && engine == EngineType.Unknown ? "Unreal Engine" : engineOverrideLabel)
                           : (useUeExt && engine == EngineType.Unknown) ? "Unreal Engine"
                           : engine == EngineType.Unreal       ? "Unreal Engine"
                           : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                           : engine == EngineType.Unity        ? "Unity" : "",
            Notes          = effectiveMod != null ? BuildNotes(game.Name, effectiveMod, fallback, _genericNotes, isNativeHdr) : "",
            InstalledAddonFileName = record?.AddonFileName,
            IsExternalOnly  = _wikiExclusions.Contains(game.Name)
                              ? true
                              : effectiveMod?.SnapshotUrl == null &&
                                (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null),
            ExternalUrl     = _wikiExclusions.Contains(game.Name)
                              ? "https://discord.gg/gF4GRJWZ2A"
                              : effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "",
            ExternalLabel   = _wikiExclusions.Contains(game.Name)
                              ? "Download from Discord"
                              : effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord",
            NexusUrl        = effectiveMod?.NexusUrl,
            DiscordUrl      = _wikiExclusions.Contains(game.Name)
                              ? "https://discord.gg/gF4GRJWZ2A"
                              : effectiveMod?.DiscordUrl,
            NameUrl         = effectiveMod?.NameUrl,
            IsManuallyAdded = true,
            IsFavourite            = _favouriteGames.Contains(game.Name),
            UseUeExtended          = useUeExt,
            IsNativeHdrGame        = isNativeHdr,
            IsManifestUeExtended   = useUeExt && !isNativeHdr,
            PerGameDcMode          = _perGameDcModeOverride.TryGetValue(game.Name, out var pgdmM) ? pgdmM : null,
            DcCustomDllFileName    = _dcCustomDllFileNames.TryGetValue(game.Name, out var dcDllM) ? dcDllM : null,
            ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(game.Name),
            ExcludeFromUpdateAllDc      = _gameNameService.UpdateAllExcludedDc.Contains(game.Name),
            ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(game.Name),
            ShaderModeOverride     = _perGameShaderMode.TryGetValue(game.Name, out var smO) ? smO : null,
            Is32Bit                = ResolveIs32Bit(game.Name, manualMachine),
            GraphicsApi            = DetectGraphicsApi(scanPath, engine, game.Name),
            DetectedApis           = _DetectAllApisForCard(scanPath, game.Name),
            VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(game.Name, out var vrpManual) ? vrpManual : "DirectX",
            LumaFeatureEnabled     = LumaFeatureEnabled,
            DcRecord        = dcRecManual,
            DcStatus        = dcRecManual != null ? GameStatus.Installed : GameStatus.NotInstalled,
            DcInstalledFile = dcRecManual?.InstalledAs,
            DcInstalledVersion = dcRecManual != null ? AuxInstallService.ReadInstalledVersion(dcRecManual.InstallPath, dcRecManual.InstalledAs) : null,
            RsRecord        = rsRecManual,
            RsStatus        = rsRecManual != null ? GameStatus.Installed : GameStatus.NotInstalled,
            RsInstalledFile = rsRecManual?.InstalledAs,
            RsInstalledVersion = rsRecManual != null ? AuxInstallService.ReadInstalledVersion(rsRecManual.InstallPath, rsRecManual.InstalledAs) : null,
            RsBlockedByDcMode = !_dllOverrides.ContainsKey(game.Name)
                                 && (_perGameDcModeOverride.ContainsKey(game.Name) ? pgdmM != "Off" : DcModeEnabled),
        };

        card.IsDualApiGame = GraphicsApiDetector.IsDualApi(card.DetectedApis);

        // For Vulkan games, RS is installed when reshade.ini exists in the game folder.
        if (card.RequiresVulkanInstall)
        {
            bool rsIniExists = File.Exists(Path.Combine(card.InstallPath, "reshade.ini"));
            card.RsStatus = rsIniExists ? GameStatus.Installed : GameStatus.NotInstalled;
            card.RsInstalledVersion = rsIniExists
                ? AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName)
                : null;

            // Vulkan games default to DC mode off unless the user or manifest
            // explicitly set a per-game DC mode override.
            if (card.PerGameDcMode == null)
                card.RsBlockedByDcMode = false;
        }

        _allCards.Add(card);
        _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        SaveLibrary();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
    }

    [RelayCommand]
    public async Task InstallModAsync(GameCardViewModel? card)
    {
        // Install invoked
        if (card?.Mod?.SnapshotUrl == null) return;

        // 32-bit toggle: swap URL before install, restore after
        string? originalSnapshotUrl = card.Mod.SnapshotUrl;
        bool swappedTo32 = card.Is32Bit && card.Mod.SnapshotUrl32 != null;
        if (swappedTo32)
            card.Mod.SnapshotUrl = card.Mod.SnapshotUrl32;
        if (string.IsNullOrEmpty(card.InstallPath))
        {
            card.ActionMessage = "No install path — use 📁 to pick the game folder.";
            return;
        }
        card.IsInstalling = true;
        card.ActionMessage = "Starting download...";
        _crashReporter.Log($"[MainViewModel.InstallModAsync] Install started: {card.GameName} → {card.InstallPath}");
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.ActionMessage   = p.msg;
                card.InstallProgress = p.pct;
            });
            var record = await _installer.InstallAsync(card.Mod, card.InstallPath, progress, card.GameName);

            // Update only this card's observable properties in-place.
            // The card is already in DisplayedGames — WinUI bindings update the
            // card visually the moment each property changes. No collection
            // manipulation (Clear/Add) is needed, so the rest of the UI is untouched.
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.InstalledRecord        = record;
                card.InstalledAddonFileName = record.AddonFileName;
                card.Status                 = GameStatus.Installed;
                card.ActionMessage          = "✅ Installed! Press Home in-game to open ReShade.";
                _crashReporter.Log($"[MainViewModel.InstallModAsync] Install complete: {card.GameName} — {record.AddonFileName}");
                // Update the addon file cache so the next Refresh finds the installed file
                // instead of using the stale "no addon" entry from before the install.
                if (!string.IsNullOrEmpty(card.InstallPath))
                    _addonFileCache[card.InstallPath.ToLowerInvariant()] = record.AddonFileName;
                card.NotifyAll();
                SaveLibrary();
                // Recalculate counts only — do NOT call ApplyFilter() which
                // would Clear() + re-add every card and flash the whole UI.
                _filterViewModel.UpdateCounts();
            });
        }
        catch (Exception ex)
        {
            card.ActionMessage = $"❌ Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallModAsync", ex, note: $"Game: {card.GameName}, Path: {card.InstallPath}");
        }
        finally
        {
            card.IsInstalling = false;
            // Restore original URL if we swapped to 32-bit for the install
            if (swappedTo32 && card.Mod != null && originalSnapshotUrl != null)
                card.Mod.SnapshotUrl = originalSnapshotUrl;
        }
    }

    [RelayCommand]
    public async Task InstallMod32Async(GameCardViewModel? card)
    {
        if (card?.Mod?.SnapshotUrl32 == null) return;
        var orig = card.Mod.SnapshotUrl;
        card.Mod.SnapshotUrl = card.Mod.SnapshotUrl32;
        await InstallModAsync(card);
        card.Mod.SnapshotUrl = orig;
    }

    [RelayCommand]
    public void UninstallMod(GameCardViewModel? card)
    {
        if (card?.InstalledRecord == null) return;
        _crashReporter.Log($"[MainViewModel.UninstallMod] Uninstalling: {card.GameName}");
        _installer.Uninstall(card.InstalledRecord);
        card.InstalledRecord        = null;
        card.InstalledAddonFileName = null;
        card.Status                 = GameStatus.Available;
        card.ActionMessage          = "Mod removed.";
        // Clear the addon file cache so the next Refresh doesn't think a file is still there.
        if (!string.IsNullOrEmpty(card.InstallPath))
            _addonFileCache[card.InstallPath.ToLowerInvariant()] = "";
        SaveLibrary();
        _filterViewModel.UpdateCounts();
    }

    // ── Display Commander commands ────────────────────────────────────────────────

    [RelayCommand]
    public async Task InstallDcAsync(GameCardViewModel? card)
    {
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            card.DcActionMessage = "No install path — use 📁 to pick the game folder.";
            return;
        }

        // Resolve effective DC mode using the canonical resolution method
        var (effectiveDcOn, resolvedDllFileName) = ResolveEffectiveDcMode(card.GameName, card);
        if (effectiveDcOn)
        {
            var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
            if (File.Exists(dxgiPath))
            {
                var fileType = AuxInstallService.IdentifyDxgiFile(dxgiPath);
                if (fileType == AuxInstallService.DxgiFileType.Unknown)
                {
                    // Ask the UI for confirmation via async callback
                    if (ConfirmForeignDxgiOverwrite != null)
                    {
                        var confirmed = await ConfirmForeignDxgiOverwrite(card, dxgiPath);
                        if (!confirmed)
                        {
                            card.DcActionMessage = "⚠ Skipped — unknown dxgi.dll found. Use Overrides to proceed.";
                            return;
                        }
                    }
                    else
                    {
                        card.DcActionMessage = "⚠ Skipped — unknown dxgi.dll found.";
                        return;
                    }
                }
            }
        }

        // Check for foreign winmm.dll before overwriting (when target DLL is winmm.dll)
        var targetDll = effectiveDcOn ? resolvedDllFileName : "";
        if (effectiveDcOn && targetDll.Equals("winmm.dll", StringComparison.OrdinalIgnoreCase))
        {
            var winmmPath = Path.Combine(card.InstallPath, "winmm.dll");
            if (File.Exists(winmmPath))
            {
                var fileType = AuxInstallService.IdentifyWinmmFile(winmmPath);
                if (fileType == AuxInstallService.WinmmFileType.Unknown)
                {
                    if (ConfirmForeignWinmmOverwrite != null)
                    {
                        var confirmed = await ConfirmForeignWinmmOverwrite(card, winmmPath);
                        if (!confirmed)
                        {
                            card.DcActionMessage = "⚠ Skipped — unknown winmm.dll found. Use Overrides to proceed.";
                            return;
                        }
                    }
                    else
                    {
                        card.DcActionMessage = "⚠ Skipped — unknown winmm.dll found.";
                        return;
                    }
                }
            }
        }

        card.DcIsInstalling  = true;
        card.DcActionMessage = "Starting DC download...";
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.DcActionMessage = p.msg;
                card.DcProgress      = p.pct;
            });
            var record = await _auxInstaller.InstallDcAsync(card.GameName, card.InstallPath, effectiveDcOn ? resolvedDllFileName : null,
                existingDcRecord: card.DcRecord,
                existingRsRecord: card.RsRecord,
                shaderModeOverride: card.ShaderModeOverride,
                use32Bit:         card.Is32Bit,
                filenameOverride: card.DllOverrideEnabled
                    ? (GetDllOverride(card.GameName)?.DcFileName)
                    : null,
                selectedPackIds:  ResolveShaderSelection(card.GameName, card.ShaderModeOverride),
                progress:         progress);

            // DC manages shaders globally — remove the per-game Vulkan footprint so
            // RDXC no longer treats this folder as a standalone Vulkan ReShade install.
            VulkanFootprintService.Delete(card.InstallPath);

            DispatcherQueue?.TryEnqueue(() =>
            {
                card.DcRecord           = record;
                card.DcInstalledFile    = record.InstalledAs;
                card.DcInstalledVersion = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);
                card.DcStatus           = GameStatus.Installed;
                card.DcActionMessage    = "✅ Display Commander installed!";
                card.NotifyAll();
            });
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ DC Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallDcAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.DcIsInstalling = false; }
    }

    [RelayCommand]
    public void UninstallDc(GameCardViewModel? card)
    {
        if (card?.DcRecord == null) return;
        try
        {
            // Compute effective DC mode to decide whether to remove shaders
            var effectiveDcOn = !card.DllOverrideEnabled && !card.IsLumaMode
                && !(card.RequiresVulkanInstall && card.PerGameDcMode == null)
                && (card.PerGameDcMode == "Custom" || (card.PerGameDcMode is null or "Global" && DcModeEnabled));

            if (effectiveDcOn && !string.IsNullOrEmpty(card.InstallPath))
            {
                try
                {
                    _shaderPackService.RemoveFromGameFolder(card.InstallPath);
                }
                catch (Exception shaderEx)
                {
                    _crashReporter.Log($"[UninstallDc] Shader removal failed for '{card.GameName}': {shaderEx.Message}");
                }
            }

            _auxInstaller.Uninstall(card.DcRecord);
            card.DcRecord           = null;
            card.DcInstalledFile    = null;
            card.DcInstalledVersion = null;
            card.DcStatus           = GameStatus.NotInstalled;
            card.DcActionMessage    = "Display Commander removed.";

            // Restore Vulkan footprint so shader deployment resumes for Vulkan games
            if (card.RequiresVulkanInstall && !string.IsNullOrEmpty(card.InstallPath)
                && File.Exists(Path.Combine(card.InstallPath, "reshade.ini")))
            {
                VulkanFootprintService.Create(card.InstallPath);
            }

            card.NotifyAll();
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallDc", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── ReShade commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task InstallReShadeAsync(GameCardViewModel? card)
    {
        if (card == null) return;

        // Block ReShade install when DC mode is active for this game
        var effectiveDcOn = !card.DllOverrideEnabled
            && !(card.RequiresVulkanInstall && card.PerGameDcMode == null)
            && (card.PerGameDcMode == "Custom" || (card.PerGameDcMode is null or "Global" && DcModeEnabled));
        if (effectiveDcOn)
        {
            card.RsActionMessage = "🚫 ReShade cannot be installed while DC Mode is active.";
            return;
        }

        if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            card.RsActionMessage = "No install path — use 📁 to pick the game folder.";
            return;
        }

        // ── Vulkan ReShade install flow ───────────────────────────────────────────
        if (card.RequiresVulkanInstall)
        {
            await InstallReShadeVulkanAsync(card);
            return;
        }

        // Check for foreign dxgi.dll before overwriting (when DC mode is OFF, ReShade installs as dxgi.dll)
        var effectiveDcModeRs = !card.DllOverrideEnabled
            && (card.PerGameDcMode == "Custom" || (card.PerGameDcMode is null or "Global" && DcModeEnabled));
        if (!effectiveDcModeRs)
        {
            var dxgiPath = Path.Combine(card.InstallPath, "dxgi.dll");
            if (File.Exists(dxgiPath))
            {
                var fileType = AuxInstallService.IdentifyDxgiFile(dxgiPath);
                if (fileType == AuxInstallService.DxgiFileType.Unknown)
                {
                    if (ConfirmForeignDxgiOverwrite != null)
                    {
                        var confirmed = await ConfirmForeignDxgiOverwrite(card, dxgiPath);
                        if (!confirmed)
                        {
                            card.RsActionMessage = "⚠ Skipped — unknown dxgi.dll found. Use Overrides to proceed.";
                            return;
                        }
                    }
                    else
                    {
                        card.RsActionMessage = "⚠ Skipped — unknown dxgi.dll found.";
                        return;
                    }
                }
            }
        }

        card.RsIsInstalling  = true;
        card.RsActionMessage = "Starting ReShade download...";
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.RsActionMessage = p.msg;
                card.RsProgress      = p.pct;
            });
            var record = await _auxInstaller.InstallReShadeAsync(card.GameName, card.InstallPath, effectiveDcModeRs,
                dcIsInstalled:  card.DcStatus == GameStatus.Installed,
                shaderModeOverride: card.ShaderModeOverride,
                use32Bit:       card.Is32Bit,
                filenameOverride: card.DllOverrideEnabled
                    ? (GetDllOverride(card.GameName)?.ReShadeFileName)
                    : (GetManifestDllNames(card.GameName)?.ReShade is { Length: > 0 } mRs ? mRs : null),
                selectedPackIds: ResolveShaderSelection(card.GameName, card.ShaderModeOverride),
                progress:       progress);
            DispatcherQueue?.TryEnqueue(() =>
            {
                card.RsRecord           = record;
                card.RsInstalledFile    = record.InstalledAs;
                card.RsInstalledVersion = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);
                card.RsStatus           = GameStatus.Installed;
                card.RsActionMessage    = "✅ ReShade installed!";
                card.NotifyAll();
            });
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ ReShade Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallReShadeAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.RsIsInstalling = false; }
    }

    /// <summary>
    /// Vulkan-specific ReShade install flow. Installs the global Vulkan implicit layer,
    /// deploys reshade.ini and ReShadePreset.ini to the game directory, and updates card status.
    /// </summary>
    internal async Task InstallReShadeVulkanAsync(GameCardViewModel card)
    {
        // ── Lightweight deploy path — layer already present, no admin needed ──
        if (IsVulkanLayerInstalledFunc())
        {
            card.RsIsInstalling  = true;
            card.RsActionMessage = "Installing Vulkan ReShade...";
            try
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath);
                AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
                VulkanFootprintService.Create(card.InstallPath);
                _shaderPackService.SyncGameFolder(card.InstallPath,
                    ResolveShaderSelection(card.GameName, card.ShaderModeOverride));

                var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                    VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                Action updateCard = () =>
                {
                    card.RsInstalledVersion = vulkanVersion;
                    card.RsStatus        = GameStatus.Installed;
                    card.RsActionMessage = "✅ Vulkan ReShade installed!";
                    card.NotifyAll();
                };
                if (DispatchUiAction != null) DispatchUiAction(updateCard);
                else DispatcherQueue?.TryEnqueue(() => updateCard());
            }
            catch (Exception ex)
            {
                card.RsActionMessage = $"❌ Vulkan ReShade Failed: {ex.Message}";
                _crashReporter.WriteCrashReport("InstallReShadeVulkanAsync", ex, note: $"Game: {card.GameName}");
            }
            finally { card.RsIsInstalling = false; }
            return;
        }

        // ── Full install path — layer absent, requires admin + InstallLayer() ──

        // 1. Check admin privileges
        if (!IsRunningAsAdminFunc())
        {
            if (ShowVulkanAdminRequiredDialog != null)
                await ShowVulkanAdminRequiredDialog();
            else
                card.RsActionMessage = "⚠ Administrator privileges are required for Vulkan layer installation. Restart RDXC as admin.";
            return;
        }

        // 2. If warning not yet shown this session, show global warning
        if (!_vulkanLayerWarningShownThisSession)
        {
            if (ShowVulkanLayerWarningDialog != null)
            {
                var proceed = await ShowVulkanLayerWarningDialog();
                if (!proceed)
                {
                    card.RsActionMessage = "Vulkan layer install cancelled.";
                    return;
                }
            }
        }

        card.RsIsInstalling  = true;
        card.RsActionMessage = "Installing Vulkan ReShade layer...";
        try
        {
            // 3. Install the global Vulkan layer (copies DLL, writes manifest, registers in registry)
            await Task.Run(() => InstallLayerAction());

            // 4. Deploy reshade.vulkan.ini (as reshade.ini) to game directory
            AuxInstallService.MergeRsVulkanIni(card.InstallPath);

            // 5. Deploy ReShadePreset.ini if present
            AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);

            // 5b. Create Vulkan footprint file so RDXC can detect this game later
            VulkanFootprintService.Create(card.InstallPath);

            // 5c. Deploy shaders locally to the game folder
            _shaderPackService.SyncGameFolder(card.InstallPath,
                ResolveShaderSelection(card.GameName, card.ShaderModeOverride));

            // 6. Mark warning as shown for this session
            _vulkanLayerWarningShownThisSession = true;

            // 7. Update card RS status
            var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
            Action updateCard = () =>
            {
                card.RsInstalledVersion = vulkanVersion;
                card.RsStatus        = GameStatus.Installed;
                card.RsActionMessage = "✅ ReShade installed (Vulkan Layer)!";
                card.NotifyAll();
            };
            if (DispatchUiAction != null) DispatchUiAction(updateCard);
            else DispatcherQueue?.TryEnqueue(() => updateCard());
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ Vulkan ReShade Failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallReShadeVulkanAsync", ex, note: $"Game: {card.GameName}");
        }
        finally { card.RsIsInstalling = false; }
    }

    [RelayCommand]
    public void UninstallReShade(GameCardViewModel? card)
    {
        if (card?.RsRecord == null) return;

        try
        {
            // Remove the RDXC-managed reshade-shaders folder BEFORE calling Uninstall.
            if (!string.IsNullOrEmpty(card.InstallPath))
                _shaderPackService.RemoveFromGameFolder(card.InstallPath);

            _auxInstaller.Uninstall(card.RsRecord);
            card.RsRecord           = null;
            card.RsInstalledFile    = null;
            card.RsInstalledVersion = null;
            card.RsStatus           = GameStatus.NotInstalled;
            card.RsActionMessage    = "ReShade removed.";
            card.NotifyAll();
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallReShade", ex, note: $"Game: {card.GameName}");
        }
    }

    [RelayCommand]
    public void UninstallVulkanReShade(GameCardViewModel? card)
    {
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        try
        {
            // 1. Delete reshade.ini from the game folder
            var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
            if (File.Exists(iniPath))
                File.Delete(iniPath);

            // 2. Delete the Vulkan footprint file
            VulkanFootprintService.Delete(card.InstallPath);

            // 3. Remove RDXC-managed reshade-shaders folder
            _shaderPackService.RemoveFromGameFolder(card.InstallPath);

            // 4. Restore reshade-shaders-original if it exists
            _shaderPackService.RestoreOriginalIfPresent(card.InstallPath);

            // 5. Update card status — do NOT touch the global Vulkan layer
            card.RsStatus        = GameStatus.NotInstalled;
            card.RsActionMessage = "Vulkan ReShade removed.";
            card.NotifyAll();
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallVulkanReShade", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── Luma Framework commands ───────────────────────────────────────────────────

    /// <summary>Fuzzy-matches a game name against the Luma completed mods list.
    /// Also honours _nameMappings so the wiki name override box works for Luma games.</summary>
    private LumaMod? MatchLumaGame(string gameName)
    {
        // 0. User-defined name mappings take priority (same logic as RenoDX wiki matching).
        if (_nameMappings.Count > 0)
        {
            string? mapped = null;
            if (_nameMappings.TryGetValue(gameName, out var m))
                mapped = m;
            else
            {
                var gameNorm = _gameDetectionService.NormalizeName(gameName);
                foreach (var kv in _nameMappings)
                {
                    if (_gameDetectionService.NormalizeName(kv.Key) == gameNorm && !string.IsNullOrEmpty(kv.Value))
                    { mapped = kv.Value; break; }
                }
            }
            if (!string.IsNullOrEmpty(mapped))
            {
                var mappedNorm = _gameDetectionService.NormalizeName(mapped);
                foreach (var lm in _lumaMods)
                    if (_gameDetectionService.NormalizeName(lm.Name) == mappedNorm) return lm;
                var mappedLookup = NormalizeForLookup(mapped);
                foreach (var lm in _lumaMods)
                    if (NormalizeForLookup(lm.Name) == mappedLookup) return lm;
            }
        }

        var norm = _gameDetectionService.NormalizeName(gameName);
        foreach (var lm in _lumaMods)
        {
            if (_gameDetectionService.NormalizeName(lm.Name) == norm)
                return lm;
        }
        // Also try the tolerant NormalizeForLookup which strips edition suffixes,
        // parenthetical text, etc. — but still requires a full match, not a
        // substring check, to avoid false positives like "Nioh 3" matching "Nioh".
        var normLookup = NormalizeForLookup(gameName);
        foreach (var lm in _lumaMods)
        {
            if (NormalizeForLookup(lm.Name) == normLookup)
                return lm;
        }
        return null;
    }

    public bool IsLumaEnabled(string gameName) => _lumaEnabledGames.Contains(gameName);

    /// <summary>
    /// Toggles Luma mode for a game. When enabling: uninstalls RenoDX, ReShade, and
    /// DC (if installed as dxgi.dll). When disabling: uninstalls Luma files.
    /// </summary>
    public void ToggleLumaMode(GameCardViewModel card)
    {
        if (card.LumaMod == null) return;

        card.IsLumaMode = !card.IsLumaMode;

        if (card.IsLumaMode)
        {
            _lumaEnabledGames.Add(card.GameName);
            _lumaDisabledGames.Remove(card.GameName);

            // Remove RenoDX mod if installed
            if (card.InstalledRecord != null)
            {
                try
                {
                    _installer.Uninstall(card.InstalledRecord);
                    card.InstalledRecord = null;
                    card.InstalledAddonFileName = null;
                    card.Status = GameStatus.Available;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] RenoDX uninstall failed — {ex.Message}"); }
            }

            // Remove ReShade if installed
            if (card.RsRecord != null)
            {
                try
                {
                    _auxInstaller.Uninstall(card.RsRecord);
                    card.RsRecord           = null;
                    card.RsInstalledFile    = null;
                    card.RsInstalledVersion = null;
                    card.RsStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] ReShade uninstall failed — {ex.Message}"); }
            }

            // Remove DC if installed (Luma mode hides DC entirely)
            if (card.DcRecord != null)
            {
                try
                {
                    _auxInstaller.Uninstall(card.DcRecord);
                    card.DcRecord           = null;
                    card.DcInstalledFile    = null;
                    card.DcInstalledVersion = null;
                    card.DcStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] DC uninstall failed — {ex.Message}"); }
            }
        }
        else
        {
            _lumaEnabledGames.Remove(card.GameName);
            _lumaDisabledGames.Add(card.GameName);

            // Uninstall Luma files if installed
            if (card.LumaRecord != null)
            {
                try
                {
                    _lumaService.Uninstall(card.LumaRecord);
                    card.LumaRecord = null;
                    card.LumaStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] Luma uninstall failed — {ex.Message}"); }
            }
            else
            {
                // Fallback: even without a record, try to clean up known Luma artifacts
                // (handles cases where record was lost or never saved)
                try
                {
                    var rsDir = Path.Combine(card.InstallPath, "reshade-shaders");
                    if (Directory.Exists(rsDir))
                    {
                        _shaderPackService.RemoveFromGameFolder(card.InstallPath);
                        if (Directory.Exists(rsDir))
                            Directory.Delete(rsDir, true);
                    }
                    var rsIni = Path.Combine(card.InstallPath, "reshade.ini");
                    if (File.Exists(rsIni)) File.Delete(rsIni);

                    // Try to find and remove Luma dll files (common names)
                    foreach (var pattern in new[] { "dxgi.dll", "d3d11.dll", "Luma*.dll", "Luma*.addon*" })
                    {
                        foreach (var f in Directory.GetFiles(card.InstallPath, pattern))
                        {
                            // Only remove if it looks like a Luma file (not ReShade/DC)
                            var fn = Path.GetFileName(f);
                            if (fn.StartsWith("Luma", StringComparison.OrdinalIgnoreCase)
                                || fn.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                                || fn.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(f); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] Failed to delete '{f}' — {ex.Message}"); }
                            }
                        }
                    }
                    card.LumaStatus = GameStatus.NotInstalled;
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.ToggleLumaMode] Fallback cleanup failed — {ex.Message}"); }
            }

            // Always clear the persisted record if it exists on disk
            LumaService.RemoveRecordByPath(card.InstallPath);
        }

        SaveNameMappings();
        card.NotifyAll();
    }

    [RelayCommand]
    public async Task InstallLumaAsync(GameCardViewModel? card)
    {
        if (card?.LumaMod == null || string.IsNullOrEmpty(card.InstallPath)) return;

        card.IsLumaInstalling = true;
        card.LumaActionMessage = "Installing Luma...";
        try
        {
            var record = await _lumaService.InstallAsync(
                card.LumaMod,
                card.InstallPath,
                new Progress<(string msg, double pct)>(p =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        card.LumaActionMessage = p.msg;
                        card.LumaProgress = p.pct;
                    });
                }));

            card.LumaRecord = record;
            card.LumaStatus = GameStatus.Installed;
            card.LumaActionMessage = "Luma installed!";
        }
        catch (Exception ex)
        {
            card.LumaActionMessage = $"❌ Install failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallLuma", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.IsLumaInstalling = false;
            card.NotifyAll();
        }
    }

    [RelayCommand]
    public void UninstallLuma(GameCardViewModel? card)
    {
        if (card?.LumaRecord == null) return;
        try
        {
            _lumaService.Uninstall(card.LumaRecord);
            card.LumaRecord = null;
            card.LumaStatus = GameStatus.NotInstalled;
            card.LumaActionMessage = "Luma removed.";
            card.NotifyAll();
        }
        catch (Exception ex)
        {
            card.LumaActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallLuma", ex, note: $"Game: {card.GameName}");
        }
    }

    // ── Update All commands ──────────────────────────────────────────────────────

    /// <summary>
    /// Eligibility: card must not be hidden, not excluded from Update All.
    /// </summary>
    private IEnumerable<GameCardViewModel> UpdateAllEligible() =>
        _updateOrchestrationService.UpdateAllEligible(_allCards);

    public async Task UpdateAllRenoDxAsync()
    {
        await _updateOrchestrationService.UpdateAllRenoDxAsync(
            _allCards, _dllOverrideService, DispatcherQueue,
            () => SaveLibrary(),
            () =>
            {
                _filterViewModel.UpdateCounts();
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    public async Task UpdateAllReShadeAsync()
    {
        await _updateOrchestrationService.UpdateAllReShadeAsync(
            _allCards, _dllOverrideService, DcModeEnabled, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            shaderResolver: ResolveShaderSelection);
    }

    public async Task UpdateAllDcAsync()
    {
        await _updateOrchestrationService.UpdateAllDcAsync(
            _allCards, _dllOverrideService, ResolveEffectiveDcMode, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            shaderResolver: ResolveShaderSelection);
    }

    // ── Init ──

    public async Task InitializeAsync(bool forceRescan = false)
    {
        IsLoading = true;
        if (!_hasInitialized) DisplayedGames.Clear();
        _allCards.Clear();
        _originalDetectedNames.Clear();

        _crashReporter.Log($"[MainViewModel.InitializeAsync] Started (forceRescan={forceRescan})");
        try
        {
            // One-time migration: rename legacy DC AppData shader folders to .old
            // so Display Commander does not load stale global shaders. (Req 2.1, 2.2)
            ShaderPackService.MigrateLegacyDcShaders();

            var savedLib = _gameLibraryService.Load();
            List<DetectedGame> detectedGames;
            Dictionary<string, bool> addonCache;
            bool wikiFetchFailed = false;
            Task rsTask = Task.CompletedTask; // hoisted so we can defer the await until after cards display

            // Merge hidden/favourite from library file with any already loaded from settings.json
            if (savedLib?.HiddenGames != null)
                foreach (var g in savedLib.HiddenGames) _hiddenGames.Add(g);
            if (savedLib?.FavouriteGames != null)
                foreach (var g in savedLib.FavouriteGames) _favouriteGames.Add(g);
            _manualGames = savedLib != null ? _gameLibraryService.ToManualGames(savedLib) : new();

            // Load engine + addon caches from the saved library so BuildCards can
            // skip expensive filesystem traversals for games seen on a previous run.
            if (savedLib != null)
            {
                _engineTypeCache   = savedLib.EngineTypeCache   ?? new(StringComparer.OrdinalIgnoreCase);
                _resolvedPathCache = savedLib.ResolvedPathCache ?? new(StringComparer.OrdinalIgnoreCase);
                _addonFileCache    = savedLib.AddonFileCache    ?? new(StringComparer.OrdinalIgnoreCase);
                _bitnessCache      = savedLib.BitnessCache      ?? new(StringComparer.OrdinalIgnoreCase);
            }

            if (savedLib != null && !forceRescan)
            {
                StatusText    = $"Library loaded ({savedLib.Games.Count} games, scanned {FormatAge(savedLib.LastScanned)})";
                SubStatusText = "Checking for new games and fetching latest mod info...";
                addonCache    = savedLib.AddonScanCache;

                // Always re-detect games so newly installed titles (especially Xbox) appear
                // without requiring the user to delete cache files or manually refresh.
                var wikiTask     = _wikiService.FetchAllAsync();
                var lumaTask     = _lumaService.FetchCompletedModsAsync();
                var manifestTask = _manifestService.FetchAsync();
                var detectTask   = DetectAllGamesDedupedAsync();
                rsTask           = Task.Run(async () => {
                    try { await _rsUpdateService.EnsureLatestAsync(); }
                    catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] ReShade update task failed — {ex.Message}"); }
                });

                // Await detection first — this never needs network
                var freshGamesResult = await detectTask;

                // Await network tasks individually so failures don't block game display
                try { await wikiTask; } catch (Exception ex) { wikiFetchFailed = true; _crashReporter.Log($"[MainViewModel.InitializeAsync] Wiki fetch failed (offline?) — {ex.Message}"); }
                try { await lumaTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma fetch failed (offline?) — {ex.Message}"); }
                try { _manifest = await manifestTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Manifest fetch failed — {ex.Message}"); }
                // rsTask + SyncReShadeToDisplayCommander deferred until after cards display

                var wikiResult = !wikiFetchFailed ? await wikiTask : default;
                _allMods      = wikiResult.Mods ?? new();
                _genericNotes = wikiResult.GenericNotes ?? new();
                try { _lumaMods = lumaTask.IsCompletedSuccessfully ? await lumaTask : new(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma mods deserialization failed — {ex.Message}"); _lumaMods = new(); }

                var freshGames = freshGamesResult;
                ApplyGameRenames(freshGames);
                var cachedGames = _gameLibraryService.ToDetectedGames(savedLib);

                // Merge: start with fresh scan, then add any cached games that weren't re-detected
                // (e.g. games on a disconnected drive). Fresh scan wins for duplicates.
                // Deduplicate by BOTH normalized name AND install path to prevent renamed games
                // from appearing twice if the rename didn't carry over (e.g. after app update).
                var freshNorms = freshGames.Select(g => _gameDetectionService.NormalizeName(g.Name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var freshPaths = freshGames
                    .Where(g => !string.IsNullOrEmpty(g.InstallPath))
                    .Select(g => g.InstallPath.TrimEnd(Path.DirectorySeparatorChar))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                detectedGames = freshGames
                    .Concat(cachedGames.Where(g =>
                        !freshNorms.Contains(_gameDetectionService.NormalizeName(g.Name))
                        && (string.IsNullOrEmpty(g.InstallPath)
                            || !freshPaths.Contains(g.InstallPath.TrimEnd(Path.DirectorySeparatorChar)))))
                    .ToList();

                _crashReporter.Log($"[MainViewModel.InitializeAsync] Merged library: {freshGames.Count} detected + {cachedGames.Count} cached → {detectedGames.Count} total");
            }
            else
            {
                StatusText    = "Scanning game library...";
                SubStatusText = "Running store scans + wiki fetch simultaneously...";
                var wikiTask     = _wikiService.FetchAllAsync();
                var lumaTask     = _lumaService.FetchCompletedModsAsync();
                var manifestTask = _manifestService.FetchAsync();
                var detectTask   = DetectAllGamesDedupedAsync();
                rsTask           = Task.Run(async () => {
                    try { await _rsUpdateService.EnsureLatestAsync(); }
                    catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] ReShade update task failed — {ex.Message}"); }
                });

                // Await detection first — this never needs network
                detectedGames = await detectTask;

                // Await network tasks individually so failures don't block game display
                try { await wikiTask; } catch (Exception ex) { wikiFetchFailed = true; _crashReporter.Log($"[MainViewModel.InitializeAsync] Wiki fetch failed (offline?) — {ex.Message}"); }
                try { await lumaTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma fetch failed (offline?) — {ex.Message}"); }
                try { _manifest = await manifestTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Manifest fetch failed — {ex.Message}"); }
                // rsTask + SyncReShadeToDisplayCommander deferred until after cards display

                var wikiResult2 = !wikiFetchFailed ? await wikiTask : default;
                _allMods      = wikiResult2.Mods ?? new();
                _genericNotes = wikiResult2.GenericNotes ?? new();
                try { _lumaMods = lumaTask.IsCompletedSuccessfully ? await lumaTask : new(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma mods deserialization failed — {ex.Message}"); _lumaMods = new(); }
                addonCache    = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                _crashReporter.Log($"[MainViewModel.InitializeAsync] Wiki fetch complete: {_allMods.Count} mods. Store scan complete: {detectedGames.Count} games.");
            }

            // Apply persisted renames so user-chosen names survive Refresh.
            ApplyGameRenames(detectedGames);

            // Apply persisted folder overrides so user-chosen paths survive Refresh.
            ApplyFolderOverrides(detectedGames);

            // Combine auto-detected + manual games.
            // Manual games override auto-detected ones with the same name.
            var manualNames = _manualGames.Select(g => _gameDetectionService.NormalizeName(g.Name))
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allGames = detectedGames
                .Where(g => !manualNames.Contains(_gameDetectionService.NormalizeName(g.Name)))
                .Concat(_manualGames)
                .ToList();

            // Apply remote manifest data before building cards (local user overrides take priority)
            ApplyManifest(_manifest);

            // Apply manifest-driven wiki status overrides to mod list
            ApplyManifestStatusOverrides();

            // Remove manifest-blacklisted entries entirely (non-game apps, etc.)
            if (_manifestBlacklist.Count > 0)
                allGames = allGames.Where(g => !_manifestBlacklist.Contains(g.Name)).ToList();

            var records    = _installer.LoadAll();
            var auxRecords = _auxInstaller.LoadAll();

            // Snapshot update statuses from old cards so they survive the rebuild.
            // The background CheckForUpdatesAsync will re-verify, but this avoids
            // a visual gap where the update badge disappears until the network check completes.
            var prevUpdateStatus = new Dictionary<string, (GameStatus mod, GameStatus dc, GameStatus rs)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _allCards)
                prevUpdateStatus[c.GameName] = (c.Status, c.DcStatus, c.RsStatus);

            SubStatusText = "Matching mods and checking install status...";
            _crashReporter.Log($"[MainViewModel.InitializeAsync] Building cards for {allGames.Count} games...");
            _allCards = await Task.Run(() => BuildCards(allGames, records, auxRecords, addonCache, _genericNotes));
            _crashReporter.Log($"[MainViewModel.InitializeAsync] BuildCards complete: {_allCards.Count} cards");

            // Apply manifest DLL name overrides to any existing installs whose filenames don't match
            ApplyManifestDllRenames();

            // Carry forward UpdateAvailable status from previous cards
            foreach (var c in _allCards)
            {
                if (prevUpdateStatus.TryGetValue(c.GameName, out var prev))
                {
                    if (prev.mod == GameStatus.UpdateAvailable && c.Status == GameStatus.Installed)
                        c.Status = GameStatus.UpdateAvailable;
                    if (prev.dc == GameStatus.UpdateAvailable && c.DcStatus == GameStatus.Installed)
                        c.DcStatus = GameStatus.UpdateAvailable;
                    if (prev.rs == GameStatus.UpdateAvailable && c.RsStatus == GameStatus.Installed)
                        c.RsStatus = GameStatus.UpdateAvailable;
                }
            }

            // Check for updates (async, parallel, non-blocking)
            _crashReporter.Log("[MainViewModel.InitializeAsync] Starting background update checks...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckForUpdatesAsync(_allCards, records, auxRecords);
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.InitializeAsync] Background update check failed — {ex}");
                }
            });

            _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();

            // If the previously selected game card was removed during refresh, reset selection.
            if (SelectedGame != null && !_allCards.Contains(SelectedGame))
                SelectedGame = null;

            _ = Task.Run(() => { try { SaveLibrary(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Fire-and-forget SaveLibrary failed — {ex.Message}"); } }); // fire-and-forget — don't block UI
            _filterViewModel.SetAllCards(_allCards);
            _filterViewModel.UpdateCounts();
            _filterViewModel.ApplyFilter();

            // ── Deferred background work: ReShade staging + shader sync ──────────────
            // These are not needed for card display, so we run them after the UI is ready.
            // rsTask (ReShade download/staging) was started earlier but not awaited.
            // _shaderPackReadyTask (shader pack download) was started in MainWindow constructor.
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for ReShade staging to finish, then sync to DC folder
                    await rsTask;
                    AuxInstallService.SyncReShadeToDisplayCommander();
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Deferred ReShade sync failed — {ex.Message}"); }

                // Wait for shader packs to be downloaded/extracted
                if (_shaderPackReadyTask != null)
                {
                    try { await _shaderPackReadyTask; }
                    catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] ShaderPackReady failed — {ex.Message}"); }
                }

                // Deploy shaders to all installed game locations
                try
                {
                    foreach (var card in _allCards)
                    {
                        if (string.IsNullOrEmpty(card.InstallPath)) continue;

                        bool rsInstalled = card.RequiresVulkanInstall
                            ? VulkanFootprintService.Exists(card.InstallPath)
                            : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;
                        bool dcInstalled = card.DcStatus == GameStatus.Installed || card.DcStatus == GameStatus.UpdateAvailable;

                        var effectiveSelection = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);

                        if (rsInstalled || dcInstalled)
                        {
                            _shaderPackService.SyncGameFolder(card.InstallPath, effectiveSelection);
                        }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] SyncShaders failed — {ex.Message}"); }
                finally
                {
                    DispatcherQueue?.TryEnqueue(() => { SubStatusText = ""; });
                }
            });

            var offlineMode = wikiFetchFailed;
            StatusText    = offlineMode
                ? $"{detectedGames.Count} games detected · offline mode (mod info unavailable)"
                : $"{detectedGames.Count} games detected · {InstalledCount} mods installed";
            SubStatusText = "";
        }
        catch (Exception ex)
        {
            StatusText = "Error loading";
            SubStatusText = ex.Message;
            _crashReporter.WriteCrashReport("InitializeAsync", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Update checking ───────────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync(List<GameCardViewModel> cards, List<InstalledModRecord> records, List<AuxInstalledRecord> auxRecords)
    {
        await _updateOrchestrationService.CheckForUpdatesAsync(
            cards, records, auxRecords, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    // Dispatcher reference for cross-thread UI updates
    private Microsoft.UI.Dispatching.DispatcherQueue? DispatcherQueue { get; set; }
    public void SetDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dq) => DispatcherQueue = dq;

    /// <summary>Store the background shader-pack download task so InitializeAsync can await it.</summary>
    public void SetShaderPackReadyTask(Task task) => _shaderPackReadyTask = task;

    // ── Detection ─────────────────────────────────────────────────────────────────

    private Task<List<DetectedGame>> DetectAllGamesDedupedAsync()
        => _gameInitializationService.DetectAllGamesDedupedAsync();

    // ── Card building ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Addon filenames that are hosted at a URL different from the standard RenoDX CDN.
    /// Used to override both the mod's SnapshotUrl (install button) and the
    /// InstalledModRecord.SnapshotUrl (update detection) whenever the file is found on disk.
    /// </summary>
    private static readonly Dictionary<string, string> _addonFileUrlOverrides =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["renodx-ue-extended.addon64"] = "https://marat569.github.io/renodx/renodx-ue-extended.addon64",
    };

    /// <summary>
    /// Per-game install path overrides: maps game name to a sub-path relative to the
    /// detected root. Used when the game exe lives in a non-standard location that the
    /// engine-detection heuristics do not resolve automatically.
    /// Seeded with hardcoded defaults; the remote manifest can add more via ApplyManifest.
    /// </summary>
    private readonly Dictionary<string, string> _installPathOverrides =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cyberpunk 2077"] = @"bin\x64",
    };

    /// <summary>
    /// Returns the authoritative download URL for a given addon filename,
    /// substituting an override when the file has a known alternative source.
    /// Falls back to the generic Unreal URL for all other .addon64 files.
    /// </summary>
    private static string ResolveAddonUrl(string addonFileName)
    {
        if (_addonFileUrlOverrides.TryGetValue(addonFileName, out var url))
            return url;
        // Default: use the standard RenoDX snapshot CDN derived from the filename
        return $"https://clshortfuse.github.io/renodx/{addonFileName}";
    }

    private GameMod MakeGenericUnreal() => new()
    {
        Name = "Generic Unreal Engine", Maintainer = "ShortFuse",
        SnapshotUrl = WikiService.GenericUnrealUrl, Status = "✅", IsGenericUnreal = true
    };
    private GameMod MakeGenericUnity() => new()
    {
        Name = "Generic Unity Engine", Maintainer = "Voosh",
        SnapshotUrl = WikiService.GenericUnityUrl64, SnapshotUrl32 = WikiService.GenericUnityUrl32,
        Status = "✅", IsGenericUnity = true
    };

    private List<GameCardViewModel> BuildCards(
        List<DetectedGame> detectedGames,
        List<InstalledModRecord> records,
        List<AuxInstalledRecord> auxRecords,
        Dictionary<string, bool> addonCache,
        Dictionary<string, string> genericNotes)
    {
        var cards = new List<GameCardViewModel>();
        var genericUnreal = MakeGenericUnreal();
        var genericUnity  = MakeGenericUnity();

        // Thread-safe caches populated during parallel detection, saved to library afterwards.
        var newEngineTypeCache   = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newResolvedPathCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newBitnessCache      = new ConcurrentDictionary<string, MachineType>(StringComparer.OrdinalIgnoreCase);

        var gameInfos = detectedGames.AsParallel().Select(game =>
        {
            string installPath;
            EngineType engine;

            var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();

            // Use cached engine detection when available — avoids expensive filesystem traversals.
            if (_engineTypeCache.TryGetValue(rootKey, out var cachedEngine)
                && _resolvedPathCache.TryGetValue(rootKey, out var cachedPath)
                && Directory.Exists(cachedPath))
            {
                installPath = cachedPath;
                engine = Enum.TryParse<EngineType>(cachedEngine, out var e) ? e : EngineType.Unknown;
            }
            else
            {
                (installPath, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
            }

            // Apply manifest engine override (takes priority over auto-detection and cache)
            var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);
            if (engineOverrideLabel != null) engine = engineOverride;

            // Record for saving
            newEngineTypeCache[rootKey]   = engine.ToString();
            newResolvedPathCache[rootKey] = installPath;

            // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
            if (_installPathOverrides.TryGetValue(game.Name, out var subPath))
            {
                var overridePath = Path.Combine(game.InstallPath, subPath);
                if (Directory.Exists(overridePath))
                    installPath = overridePath;
            }

            // Detect bitness: use cached value if available, otherwise run PE detection.
            MachineType machineType;
            var resolvedKey = installPath.ToLowerInvariant();
            if (_bitnessCache.TryGetValue(resolvedKey, out var cachedMachine))
            {
                machineType = cachedMachine;
            }
            else
            {
                machineType = _peHeaderService.DetectGameArchitecture(installPath);
            }
            newBitnessCache[resolvedKey] = machineType;

            var mod      = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
            // Wiki unlink: discard false fuzzy match so the game uses its generic engine addon
            if (mod != null && _manifestWikiUnlinks.Contains(game.Name)) mod = null;
            // UnrealLegacy (UE3 and below) cannot use the RenoDX addon system — no fallback mod offered.
            var fallback = mod == null ? (engine == EngineType.Unreal      ? genericUnreal
                                        : engine == EngineType.Unity       ? genericUnity : null) : null;

            // If the wiki mod matched but has no download URL (common for games listed
            // in the generic engine tables), inject the generic engine addon URL so the
            // install button works. The wiki mod's status and notes are preserved.
            if (mod != null && mod.SnapshotUrl == null && mod.NexusUrl == null && mod.DiscordUrl == null)
            {
                var engineFallback = engine == EngineType.Unreal ? genericUnreal
                                   : engine == EngineType.Unity  ? genericUnity : null;
                if (engineFallback != null)
                {
                    mod = new GameMod
                    {
                        Name            = mod.Name,
                        Maintainer      = engineFallback.Maintainer,
                        SnapshotUrl     = engineFallback.SnapshotUrl,
                        SnapshotUrl32   = engineFallback.SnapshotUrl32,
                        Status          = mod.Status,
                        Notes           = mod.Notes,
                        NameUrl         = mod.NameUrl,
                        IsGenericUnreal = engineFallback.IsGenericUnreal,
                        IsGenericUnity  = engineFallback.IsGenericUnity,
                    };
                    fallback = engineFallback;
                }
            }

            return (game, installPath, engine, mod, fallback, machineType, engineOverrideLabel);
        }).ToList();

        // Snapshot the new caches for SaveLibrary.
        _engineTypeCache   = new Dictionary<string, string>(newEngineTypeCache, StringComparer.OrdinalIgnoreCase);
        _resolvedPathCache = new Dictionary<string, string>(newResolvedPathCache, StringComparer.OrdinalIgnoreCase);
        _bitnessCache      = new Dictionary<string, MachineType>(newBitnessCache, StringComparer.OrdinalIgnoreCase);
        var newAddonFileCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (game, installPath, engine, mod, origFallback, detectedMachine, engineOverrideLabel) in gameInfos)
        {
            // Always show every detected game — even if no wiki mod exists.
            // The card will have no install button if there's no snapshot URL,
            // but a RenoDX addon already on disk will still be detected and shown.
            // Wiki exclusion overrides everything — user explicitly wants no wiki match
            var fallback     = origFallback;  // mutable local copy
            var effectiveMod = _wikiExclusions.Contains(game.Name) ? null : (mod ?? fallback);

            var record = records.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

            // Fallback: match by InstallPath for records saved with mod name instead of game name
            // (e.g. "Generic Unreal Engine" from before the fix).
            if (record == null)
            {
                record = records.FirstOrDefault(r =>
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
                if (record != null)
                {
                    // Fix the record's GameName so future lookups work correctly
                    record.GameName = game.Name;
                    _installer.SaveRecordPublic(record);
                }
            }

            // Always scan disk for renodx-* addon files — catches manual installs and
            // games not yet on the wiki that already have a mod installed.
            // Use the addon file cache to skip expensive recursive scans on subsequent launches.
            string? addonOnDisk = null;
            var cacheKey = installPath.ToLowerInvariant();

            // If we have a DB record, always verify the file is still on disk — never
            // rely on the addon file cache alone, because the cache may be stale
            // (e.g. mod was installed/uninstalled since the last BuildCards).
            if (record != null)
            {
                var expectedFile = record.AddonFileName;
                if (!string.IsNullOrEmpty(expectedFile)
                    && File.Exists(Path.Combine(installPath, expectedFile)))
                {
                    addonOnDisk = expectedFile;
                }
                else
                {
                    // Record exists but file not at expected location — rescan
                    addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                }
            }
            else if (_addonFileCache.TryGetValue(cacheKey, out var cachedAddonFile))
            {
                if (!string.IsNullOrEmpty(cachedAddonFile)
                    && File.Exists(Path.Combine(installPath, cachedAddonFile)))
                {
                    addonOnDisk = cachedAddonFile;
                }
                else
                {
                    // Always rescan — the cache may be stale if a mod was installed
                    // externally or if a previous bug deleted the DB record.
                    // ScanForInstalledAddon checks the direct folder first (cheap),
                    // then common subdirs, then does a depth-limited recursive search.
                    addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                }
            }
            else if (addonCache.TryGetValue(cacheKey, out _))
            {
                addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
            }
            else
            {
                addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                addonCache[cacheKey] = addonOnDisk != null;
            }
            newAddonFileCache[cacheKey] = addonOnDisk ?? "";

            if (addonOnDisk != null && record == null)
            {
                // Use ResolveAddonUrl so files like renodx-ue-extended.addon64 get their
                // correct source URL rather than the generic CDN URL from effectiveMod.
                record = new InstalledModRecord
                {
                    GameName      = game.Name,
                    InstallPath   = installPath,
                    AddonFileName = addonOnDisk,
                    InstalledAt   = File.GetLastWriteTimeUtc(Path.Combine(installPath, addonOnDisk)),
                    SnapshotUrl   = ResolveAddonUrl(addonOnDisk),
                };
                _installer.SaveRecordPublic(record);
            }
            else if (addonOnDisk == null && record != null)
            {
                // DB record exists but addon file is no longer on disk — user manually removed it.
                // Remove the stale record so the card shows Available rather than Installed.
                _installer.RemoveRecord(record);
                record = null;
            }

            // If the installed addon on disk has a different source URL than what the
            // wiki mod specifies (e.g. renodx-ue-extended.addon64 on a generic UE card),
            // patch effectiveMod so the install/update button uses the correct URL.
            if (addonOnDisk != null && effectiveMod?.SnapshotUrl != null
                && _addonFileUrlOverrides.TryGetValue(addonOnDisk, out var addonOverrideUrl))
            {
                effectiveMod = new GameMod
                {
                    Name        = effectiveMod.Name,
                    Maintainer  = effectiveMod.Maintainer,
                    SnapshotUrl = addonOverrideUrl,
                    Status      = effectiveMod.Status,
                    Notes       = effectiveMod.Notes,
                    NexusUrl    = effectiveMod.NexusUrl,
                    DiscordUrl  = effectiveMod.DiscordUrl,
                    NameUrl     = effectiveMod.NameUrl,
                    IsGenericUnreal = effectiveMod.IsGenericUnreal,
                    IsGenericUnity  = effectiveMod.IsGenericUnity,
                };
            }

            // Named addon found on disk but no wiki entry exists → show Discord link
            // so the user can find support/info for their mod.
            if (addonOnDisk != null && effectiveMod == null)
            {
                effectiveMod = new GameMod
                {
                    Name       = game.Name,
                    Status     = "💬",
                    DiscordUrl = "https://discord.gg/gF4GRJWZ2A",
                };
            }

            // ── Manifest snapshot override ────────────────────────────────────────
            // If the manifest provides a direct snapshot URL for this game, inject it
            // into the effectiveMod. This handles cases where the wiki parser fails to
            // capture the snapshot link or the name mapping doesn't resolve correctly.
            if (_manifest?.SnapshotOverrides != null
                && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOverrideUrl)
                && !string.IsNullOrEmpty(snapshotOverrideUrl))
            {
                if (effectiveMod != null)
                {
                    effectiveMod.SnapshotUrl = snapshotOverrideUrl;
                }
                else
                {
                    effectiveMod = new GameMod
                    {
                        Name        = game.Name,
                        SnapshotUrl = snapshotOverrideUrl,
                        Status      = "✅",
                    };
                }
            }

            // Apply UE-Extended preference: if the game has it saved OR the file is on disk,
            // force the Mod URL to the marat569 source so the install button targets it.
            // Native HDR games always use UE-Extended, regardless of user toggle.
            // UE-Extended whitelist supersedes everything — hide Nexus link and force install/update/reinstall.
            bool isNativeHdr = IsNativeHdrGameMatch(game.Name);
            bool useUeExt = (addonOnDisk == UeExtendedFile)
                            || IsUeExtendedGameMatch(game.Name)
                            || (isNativeHdr && (effectiveMod?.IsGenericUnreal == true || engine == EngineType.Unreal));
            if (useUeExt && effectiveMod != null)
            {
                // Create or override the mod to use UE-Extended URL
                effectiveMod = new GameMod
                {
                    Name            = effectiveMod?.Name ?? "Generic Unreal Engine",
                    Maintainer      = effectiveMod?.Maintainer ?? "ShortFuse",
                    SnapshotUrl     = UeExtendedUrl,
                    Status          = effectiveMod?.Status ?? "✅",
                    Notes           = effectiveMod?.Notes,
                    IsGenericUnreal = true,
                };
                // Persist preference if it was detected from disk or the game is native HDR
                if (addonOnDisk == UeExtendedFile || isNativeHdr)
                    _ueExtendedGames.Add(game.Name);
            }
            // UE-Extended whitelist games that have no engine detected — force them to use UE-Extended
            else if (useUeExt && effectiveMod == null)
            {
                effectiveMod = new GameMod
                {
                    Name            = "Generic Unreal Engine",
                    Maintainer      = "ShortFuse",
                    SnapshotUrl     = UeExtendedUrl,
                    Status          = "✅",
                    IsGenericUnreal = true,
                };
                fallback = effectiveMod;
                if (isNativeHdr)
                    _ueExtendedGames.Add(game.Name);
            }

            // UE-Extended whitelist supersedes Nexus/Discord external links — force installable
            if (useUeExt && effectiveMod != null)
            {
                // Strip Nexus/Discord links so the card shows install/update/reinstall buttons
                effectiveMod.NexusUrl   = null;
                effectiveMod.DiscordUrl = null;
            }

            // Look up aux records for this game
            var dcRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == AuxInstallService.TypeDc);
            var rsRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == AuxInstallService.TypeReShade);

            // Verify DB records against disk — if the file no longer exists the record is stale.
            // This handles the case where the user manually deleted files without using RDXC.
            if (rsRec != null && !File.Exists(Path.Combine(rsRec.InstallPath, rsRec.InstalledAs)))
            {
                _auxInstaller.RemoveRecord(rsRec);
                rsRec = null;
            }
            if (dcRec != null && !File.Exists(Path.Combine(dcRec.InstallPath, dcRec.InstalledAs)))
            {
                _auxInstaller.RemoveRecord(dcRec);
                dcRec = null;
            }

            // ── Disk detection for ReShade & Display Commander ────────────────────
            // If no DB record exists, scan disk for the known filenames so that
            // manually installed or previously installed instances are shown correctly.
            //
            // dxgi.dll is AMBIGUOUS — both ReShade (DC Mode OFF) and DC (DC Mode ON) use it.
            // We use strict positive identification (binary string scan + exact size match
            // against staged/cached copies) to classify it. Files that cannot be positively
            // identified as ReShade or DC are left alone — no record is created.
            if (rsRec == null)
            {
                // ReShade64.dll (DC Mode ON, 64-bit) is unambiguous — always ReShade.
                var rs64Path = Path.Combine(installPath, AuxInstallService.RsDcModeName);
                if (File.Exists(rs64Path))
                {
                    rsRec = new AuxInstalledRecord
                    {
                        GameName    = game.Name,
                        InstallPath = installPath,
                        AddonType   = AuxInstallService.TypeReShade,
                        InstalledAs = AuxInstallService.RsDcModeName,
                        InstalledAt = File.GetLastWriteTimeUtc(rs64Path),
                    };
                }
                else
                {
                    // ReShade32.dll (DC Mode ON, 32-bit) is also unambiguous — always ReShade.
                    var rs32Path = Path.Combine(installPath, AuxInstallService.RsDcModeName32);
                    if (File.Exists(rs32Path))
                    {
                        rsRec = new AuxInstalledRecord
                        {
                            GameName    = game.Name,
                            InstallPath = installPath,
                            AddonType   = AuxInstallService.TypeReShade,
                            InstalledAs = AuxInstallService.RsDcModeName32,
                            InstalledAt = File.GetLastWriteTimeUtc(rs32Path),
                        };
                    }
                    else
                    {
                        // dxgi.dll — only attribute to ReShade if positively identified as ReShade
                        // AND not identified as Display Commander (DC is built on ReShade and
                        // contains ReShade markers, so IsReShadeFile alone is not sufficient).
                        var dxgiPath = Path.Combine(installPath, AuxInstallService.RsNormalName);
                        if (File.Exists(dxgiPath) && AuxInstallService.IsReShadeFile(dxgiPath)
                            && !AuxInstallService.IsDcFileStrict(dxgiPath))
                        {
                            rsRec = new AuxInstalledRecord
                            {
                                GameName    = game.Name,
                                InstallPath = installPath,
                                AddonType   = AuxInstallService.TypeReShade,
                                InstalledAs = AuxInstallService.RsNormalName,
                                InstalledAt = File.GetLastWriteTimeUtc(dxgiPath),
                            };
                        }
                        else
                        {
                            // Content-based fallback: scan known proxy DLL names for ReShade binary signatures.
                            // ReShade can only inject via specific Windows system DLL proxies, so we only
                            // check those names rather than every DLL in the folder.
                            try
                            {
                                foreach (var proxyName in DllOverrideConstants.CommonDllNames)
                                {
                                    // Skip filenames already checked above
                                    if (proxyName.Equals(AuxInstallService.RsDcModeName, StringComparison.OrdinalIgnoreCase) ||
                                        proxyName.Equals(AuxInstallService.RsDcModeName32, StringComparison.OrdinalIgnoreCase) ||
                                        proxyName.Equals(AuxInstallService.RsNormalName, StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    // Skip Display Commander file to avoid false attribution
                                    if (dcRec?.InstalledAs != null &&
                                        proxyName.Equals(dcRec.InstalledAs, StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    var candidatePath = Path.Combine(installPath, proxyName);
                                    if (!File.Exists(candidatePath))
                                        continue;

                                    if (AuxInstallService.IsReShadeFileStrict(candidatePath)
                                        && !AuxInstallService.IsDcFileStrict(candidatePath))
                                    {
                                        rsRec = new AuxInstalledRecord
                                        {
                                            GameName    = game.Name,
                                            InstallPath = installPath,
                                            AddonType   = AuxInstallService.TypeReShade,
                                            InstalledAs = proxyName,
                                            InstalledAt = File.GetLastWriteTimeUtc(candidatePath),
                                        };
                                        break;
                                    }
                                }
                            }
                            catch (Exception) { /* Permission or IO errors — skip gracefully */ }
                        }
                    }
                }
            }
            if (dcRec == null)
            {
                // zzz_display_commander.addon64 is unambiguous — always DC (normal mode).
                var dxgiPath = Path.Combine(installPath, AuxInstallService.DcNormalName);
                if (File.Exists(dxgiPath))
                {
                    dcRec = new AuxInstalledRecord
                    {
                        GameName    = game.Name,
                        InstallPath = installPath,
                        AddonType   = AuxInstallService.TypeDc,
                        InstalledAs = AuxInstallService.DcNormalName,
                        SourceUrl   = AuxInstallService.DcUrl,
                        RemoteFileSize = new FileInfo(dxgiPath).Length,
                        InstalledAt = File.GetLastWriteTimeUtc(dxgiPath),
                    };
                }
                else
                {
                    // dxgi.dll (DC Mode 1) — only attribute to DC if positively identified as DC.
                    // A dxgi.dll that is neither ReShade nor DC is foreign (e.g. DXVK, Special K) — leave it alone.
                    var dcDxgiPath = Path.Combine(installPath, AuxInstallService.DcDxgiName);
                    if (File.Exists(dcDxgiPath) && AuxInstallService.IsDcFileStrict(dcDxgiPath))
                    {
                        dcRec = new AuxInstalledRecord
                        {
                            GameName    = game.Name,
                            InstallPath = installPath,
                            AddonType   = AuxInstallService.TypeDc,
                            InstalledAs = AuxInstallService.DcDxgiName,
                            SourceUrl   = AuxInstallService.DcUrl,
                            RemoteFileSize = new FileInfo(dcDxgiPath).Length,
                            InstalledAt = File.GetLastWriteTimeUtc(dcDxgiPath),
                        };
                    }
                    else
                    {
                        // winmm.dll (DC Mode 2) — only attribute to DC if positively identified.
                        var dcWinmmPath = Path.Combine(installPath, AuxInstallService.DcWinmmName);
                        if (File.Exists(dcWinmmPath) && AuxInstallService.IsDcFileStrict(dcWinmmPath))
                        {
                            dcRec = new AuxInstalledRecord
                            {
                                GameName    = game.Name,
                                InstallPath = installPath,
                                AddonType   = AuxInstallService.TypeDc,
                                InstalledAs = AuxInstallService.DcWinmmName,
                                SourceUrl   = AuxInstallService.DcUrl,
                                RemoteFileSize = new FileInfo(dcWinmmPath).Length,
                                InstalledAt = File.GetLastWriteTimeUtc(dcWinmmPath),
                            };
                        }
                    }
                }
            }

            cards.Add(new GameCardViewModel
            {
                GameName               = game.Name,
                Mod                    = effectiveMod,
                DetectedGame           = game,
                InstallPath            = installPath,
                Source                 = game.Source,
                InstalledRecord        = record,
                Status                 = record != null ? GameStatus.Installed : GameStatus.Available,
                WikiStatus             = (_wikiExclusions.Contains(game.Name)
                                           || (effectiveMod?.SnapshotUrl == null && effectiveMod?.DiscordUrl != null && effectiveMod?.NexusUrl == null))
                                          ? "💬"
                                          : (mod == null && fallback != null && !useUeExt && !isNativeHdr)
                                            ? "?"
                                            : effectiveMod?.Status ?? "—",
                Maintainer             = effectiveMod?.Maintainer ?? "",
                IsGenericMod           = useUeExt || (fallback != null && mod == null),
                EngineHint             = engineOverrideLabel != null
                                       ? (useUeExt && engine == EngineType.Unknown ? "Unreal Engine" : engineOverrideLabel)
                                       : (useUeExt && engine == EngineType.Unknown) ? "Unreal Engine"
                                       : engine == EngineType.Unreal       ? "Unreal Engine"
                                       : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                                       : engine == EngineType.Unity        ? "Unity" : "",
                Notes                  = effectiveMod != null ? BuildNotes(game.Name, effectiveMod, fallback, genericNotes, isNativeHdr) : "",
                InstalledAddonFileName = record?.AddonFileName,
                IsHidden               = _hiddenGames.Contains(game.Name),
                IsFavourite            = _favouriteGames.Contains(game.Name),
                IsManuallyAdded        = game.IsManuallyAdded,
                UseUeExtended          = useUeExt,
                IsExternalOnly         = _wikiExclusions.Contains(game.Name)
                                         ? true
                                         : effectiveMod?.SnapshotUrl == null &&
                                           (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null),
                ExternalUrl            = _wikiExclusions.Contains(game.Name)
                                         ? "https://discord.gg/gF4GRJWZ2A"
                                         : effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "",
                ExternalLabel          = _wikiExclusions.Contains(game.Name)
                                         ? "Download from Discord"
                                         : effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord",
                NexusUrl               = effectiveMod?.NexusUrl,
                DiscordUrl             = _wikiExclusions.Contains(game.Name)
                                         ? "https://discord.gg/gF4GRJWZ2A"
                                         : effectiveMod?.DiscordUrl,
                NameUrl                = effectiveMod?.NameUrl,
                PerGameDcMode          = _perGameDcModeOverride.TryGetValue(game.Name, out var pgdmBc) ? pgdmBc : null,
                DcCustomDllFileName    = _dcCustomDllFileNames.TryGetValue(game.Name, out var dcDllBc) ? dcDllBc : null,
                ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(game.Name),
                ExcludeFromUpdateAllDc      = _gameNameService.UpdateAllExcludedDc.Contains(game.Name),
                ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(game.Name),
                ShaderModeOverride     = _perGameShaderMode.TryGetValue(game.Name, out var smBc) ? smBc : null,
                Is32Bit                = ResolveIs32Bit(game.Name, detectedMachine),
                GraphicsApi            = DetectGraphicsApi(installPath, engine, game.Name),
                DetectedApis           = _DetectAllApisForCard(installPath, game.Name),
                VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(game.Name, out var vrpBc) ? vrpBc : "DirectX",
                DllOverrideEnabled     = _dllOverrides.ContainsKey(game.Name),
                IsNativeHdrGame        = isNativeHdr,
                IsManifestUeExtended   = useUeExt && !isNativeHdr,
                DcRecord               = dcRec,
                DcStatus               = dcRec != null ? GameStatus.Installed : GameStatus.NotInstalled,
                DcInstalledFile        = dcRec?.InstalledAs,
                DcInstalledVersion     = dcRec != null ? AuxInstallService.ReadInstalledVersion(dcRec.InstallPath, dcRec.InstalledAs) : null,
                RsRecord               = rsRec,
                RsStatus               = rsRec != null ? GameStatus.Installed : GameStatus.NotInstalled,
                RsInstalledFile        = rsRec?.InstalledAs,
                RsInstalledVersion     = rsRec != null ? AuxInstallService.ReadInstalledVersion(rsRec.InstallPath, rsRec.InstalledAs) : null,
                RsBlockedByDcMode      = !_dllOverrides.ContainsKey(game.Name)
                                           && (_perGameDcModeOverride.ContainsKey(game.Name) ? pgdmBc != "Off" : DcModeEnabled),
            });

            // ── Luma matching ──────────────────────────────────────────────────────
            var newCard = cards[^1];
            newCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(newCard.DetectedApis);

            // For Vulkan games, RS is installed when reshade.ini exists in the game folder.
            if (newCard.RequiresVulkanInstall)
            {
                bool rsIniExists = File.Exists(Path.Combine(newCard.InstallPath, "reshade.ini"));
                newCard.RsStatus = rsIniExists ? GameStatus.Installed : GameStatus.NotInstalled;
                newCard.RsInstalledVersion = rsIniExists
                    ? AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName)
                    : null;

                // Vulkan games default to DC mode off unless the user or manifest
                // explicitly set a per-game DC mode override.
                if (newCard.PerGameDcMode == null)
                    newCard.RsBlockedByDcMode = false;
            }

            newCard.LumaFeatureEnabled = LumaFeatureEnabled;
            var lumaMatch = MatchLumaGame(game.Name);
            if (lumaMatch != null)
            {
                newCard.LumaMod = lumaMatch;

                // Auto-enable Luma for manifest-listed games (unless user explicitly disabled)
                if (_manifest?.LumaDefaultGames != null
                    && !_lumaEnabledGames.Contains(game.Name)
                    && !_lumaDisabledGames.Contains(game.Name)
                    && _manifest.LumaDefaultGames.Any(g => g.Equals(game.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _lumaEnabledGames.Add(game.Name);
                }

                newCard.IsLumaMode = _lumaEnabledGames.Contains(game.Name);
                // Check if Luma is installed on disk
                var lumaRec = LumaService.GetRecordByPath(installPath);
                if (lumaRec != null)
                {
                    newCard.LumaRecord = lumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
            }

            }
        ApplyCardOverrides(cards);
        ApplyManifestCardOverrides(_manifest, cards);

        // Persist the addon file cache for next launch.
        _addonFileCache = newAddonFileCache;

        return cards;
    }

    private string BuildNotes(string gameName, GameMod effectiveMod, GameMod? fallback, Dictionary<string, string> genericNotes, bool isNativeHdr = false)
    {
        // Native HDR / UE-Extended whitelisted games always get the HDR warning,
        // whether they have a specific wiki mod or are using the generic UE fallback.
        if (isNativeHdr)
        {
            var parts = new List<string>();
            parts.Add("⚠ In-game HDR must be turned ON for UE-Extended to work correctly in this title.");

            // Include wiki tooltip if present (from a specific mod entry)
            if (fallback == null && !string.IsNullOrWhiteSpace(effectiveMod.Notes))
            {
                parts.Add("");
                parts.Add(effectiveMod.Notes);
            }

            // Do NOT include generic UE game-specific settings — these are for the
            // generic addon, not UE-Extended. UE-Extended whitelisted games don't
            // need generic addon installation guidance.

            return string.Join("\n", parts);
        }

        // Specific mod — wiki tooltip note (may be null/empty if no tooltip)
        if (fallback == null) return effectiveMod.Notes ?? "";

        var notesParts = new List<string>();

        if (effectiveMod.IsGenericUnreal)
        {
            var specific = GetGenericNote(gameName, genericNotes);
            if (!string.IsNullOrEmpty(specific))
            {
                notesParts.Add("📋 Game-specific settings:");
                notesParts.Add(specific);
            }
            notesParts.Add(UnrealWarnings);
        }
        else // Unity
        {
            var specific = GetGenericNote(gameName, genericNotes);
            if (!string.IsNullOrEmpty(specific))
            {
                notesParts.Add("📋 Game-specific settings:");
                notesParts.Add(specific);
            }
        }

        return string.Join("\n", notesParts);
    }

    private static string? ScanForInstalledAddon(string installPath, GameMod? mod)
    {
        if (!Directory.Exists(installPath)) return null;
        try
        {
            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            // First try direct files in the folder
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                if (found != null) return Path.GetFileName(found);
            }

            // Search common subdirectories (Binaries/Win64, Binaries/Win32) and fallback to a limited recursive search
            var commonPaths = new[] { "Binaries\\Win64", "Binaries\\Win32", "Binaries\\x86", "x64", "x86" };
            foreach (var sub in commonPaths)
            {
                try
                {
                    var sp = Path.Combine(installPath, sub);
                    if (!Directory.Exists(sp)) continue;
                    foreach (var ext in new[] { "*.addon64", "*.addon32" })
                    {
                        var found = Directory.GetFiles(sp, ext)
                            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                        if (found != null) return Path.GetFileName(found);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Subdir scan failed for '{sub}' in '{installPath}' — {ex.Message}"); }
            }

            // Last resort: depth-limited recursive search (catch and ignore access issues).
            // Addon files are always near the game exe, so 4 levels is sufficient.
            try
            {
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = ScanAddonShallow(installPath, ext, 4);
                    if (found != null) return found;
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Recursive scan failed for '{installPath}' — {ex.Message}"); }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Top-level scan failed for '{installPath}' — {ex.Message}"); }
        return null;
    }

    private static string? ScanAddonShallow(string dir, string pattern, int depth)
    {
        if (depth < 0 || !Directory.Exists(dir)) return null;
        try
        {
            var found = Directory.GetFiles(dir, pattern)
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
            if (found != null) return Path.GetFileName(found);
            if (depth > 0)
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var r = ScanAddonShallow(sub, pattern, depth - 1);
                    if (r != null) return r;
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanAddonShallow] Scan failed for '{dir}' — {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Lightweight addon scan: checks the direct folder and common subdirs only.
    /// Skips the expensive depth-limited recursive search. Used on normal Refresh
    /// when the cache indicates no addon was previously found. Full Refresh forces
    /// a deep rescan via ScanForInstalledAddon.
    /// </summary>
    private static string? ScanForInstalledAddonQuick(string installPath, GameMod? mod)
    {
        if (!Directory.Exists(installPath)) return null;
        try
        {
            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                if (found != null) return Path.GetFileName(found);
            }
            var commonPaths = new[] { "Binaries\\Win64", "Binaries\\Win32", "Binaries\\x86", "x64", "x86" };
            foreach (var sub in commonPaths)
            {
                try
                {
                    var sp = Path.Combine(installPath, sub);
                    if (!Directory.Exists(sp)) continue;
                    foreach (var ext in new[] { "*.addon64", "*.addon32" })
                    {
                        var found = Directory.GetFiles(sp, ext)
                            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                        if (found != null) return Path.GetFileName(found);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddonQuick] Subdir scan failed for '{sub}' in '{installPath}' — {ex.Message}"); }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddonQuick] Scan failed for '{installPath}' — {ex.Message}"); }
        return null;
    }

    public void SaveLibraryPublic() => SaveLibrary();
    private void SaveLibrary()
    {
        var detectedGames = _allCards
            .Where(c => !c.IsManuallyAdded && c.DetectedGame != null)
            .Select(c => c.DetectedGame!)
            .ToList();

        // Build addon cache safely — multiple DLC cards can share the same install path,
        // so use a plain dict with [] assignment instead of ToDictionary (which throws on dupes).
        var addonCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
            addonCache[c.InstallPath.ToLowerInvariant()] = !string.IsNullOrEmpty(c.InstalledAddonFileName);

        // Keep _addonFileCache in sync with current card state so that installs and
        // uninstalls performed since the last BuildCards are reflected on the next Refresh.
        foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
        {
            var key = c.InstallPath.ToLowerInvariant();
            if (!string.IsNullOrEmpty(c.InstalledAddonFileName))
                _addonFileCache[key] = c.InstalledAddonFileName;
            else if (!_addonFileCache.ContainsKey(key))
                _addonFileCache[key] = "";
        }

        _gameLibraryService.Save(detectedGames, addonCache, _hiddenGames, _favouriteGames, _manualGames,
            _engineTypeCache, _resolvedPathCache, _addonFileCache, _bitnessCache);
    }

    private static string FormatAge(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours   < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays    < 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

}


