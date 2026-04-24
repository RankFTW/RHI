using System.Reflection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests verifying that <c>UninstallReShade</c> always calls
/// <c>RemoveFromGameFolder</c> regardless of DC installation status.
///
/// **Validates: Requirements 6.1, 6.3**
/// </summary>
public class UninstallReShadeShaderRemovalTests : IDisposable
{
    private readonly string _tempRoot;

    public UninstallReShadeShaderRemovalTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcRsUninstall_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// Creates a MainViewModel wired to the given tracking shader service,
    /// and injects the provided cards into the private <c>_allCards</c> field.
    /// </summary>
    private MainViewModel CreateViewModelWithCards(
        TrackingShaderPackService tracker,
        List<GameCardViewModel> cards)
    {
        var auxInstaller = new StubAuxInstallService();
        var gameDetection = new StubGameDetectionService();
        var installer = new StubModInstallService();
        var lumaService = new StubLumaService();
        var settingsVm = new SettingsViewModel();
        var filterVm = new FilterViewModel();
        var updateOrch = new UpdateOrchestrationService(installer, auxInstaller, new CrashReporterService(), auxInstaller, new StubREFrameworkService(), lumaService);
        var dllOverride = new DllOverrideService(auxInstaller);
        var gameName = new GameNameService(gameDetection, installer, auxInstaller, lumaService);
        var rsUpdate = new StubReShadeUpdateService();
        var gameInit = new GameInitializationService(
            gameDetection, new StubWikiService(), new StubManifestService(),
            installer, auxInstaller, new StubGameLibraryService(),
            new StubPeHeaderService(), lumaService, rsUpdate, tracker);

        var vm = new MainViewModel(
            new HttpClient(),
            installer,
            auxInstaller,
            new CrashReporterService(),
            new StubWikiService(),
            new StubManifestService(),
            new StubGameLibraryService(),
            gameDetection,
            new StubPeHeaderService(),
            new StubUpdateService(),
            tracker,
            lumaService,
            rsUpdate,
            new StubNormalReShadeUpdateService(),
            settingsVm,
            filterVm,
            updateOrch,
            dllOverride,
            gameName,
            gameInit,
            new StubREFrameworkService(),
            new StubNexusModsService(),
            new StubPcgwService(),
            new StubOptiScalerService(),
            new StubOptiScalerWikiService(),
            new StubHdrDatabaseService(),
            new GitHubETagCache());

        // Inject cards via reflection
        var field = typeof(MainViewModel).GetField("_allCards", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(vm, cards);

        return vm;
    }

    /// <summary>
    /// Creates a game card with the given DC/RS status and a real temp directory.
    /// </summary>
    private GameCardViewModel CreateCard(
        string name,
        GameStatus rsStatus = GameStatus.Installed)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);

        var card = new GameCardViewModel
        {
            GameName = name,
            InstallPath = dir,
            RsStatus = rsStatus,
            RsRecord = new AuxInstalledRecord
            {
                GameName = name,
                InstallPath = dir,
                InstalledAs = "ReShade64.dll",
                AddonType = "ReShade"
            },
        };

        return card;
    }

    // ── Test cases ───────────────────────────────────────────────────────────

    /// <summary>
    /// DC installed + RS installed → UninstallReShade → RemoveFromGameFolder called.
    ///
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Fact]
    public void UninstallReShade_DcInstalled_RemoveFromGameFolderCalled()
    {
        var tracker = new TrackingShaderPackService();
        var card = CreateCard("GameWithDc", rsStatus: GameStatus.Installed);
        var vm = CreateViewModelWithCards(tracker, new List<GameCardViewModel> { card });

        vm.UninstallReShade(card);

        Assert.True(tracker.RemoveFromGameFolderCalled,
            "RemoveFromGameFolder must be called even when DC is installed");
        Assert.Contains(card.InstallPath, tracker.RemoveFromGameFolderPaths);
    }

    /// <summary>
    /// DC not installed + RS installed → UninstallReShade → RemoveFromGameFolder called.
    ///
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Fact]
    public void UninstallReShade_DcNotInstalled_RemoveFromGameFolderCalled()
    {
        var tracker = new TrackingShaderPackService();
        var card = CreateCard("GameNoDc", rsStatus: GameStatus.Installed);
        var vm = CreateViewModelWithCards(tracker, new List<GameCardViewModel> { card });

        vm.UninstallReShade(card);

        Assert.True(tracker.RemoveFromGameFolderCalled,
            "RemoveFromGameFolder must be called when DC is not installed");
        Assert.Contains(card.InstallPath, tracker.RemoveFromGameFolderPaths);
    }

    /// <summary>
    /// DC update available + RS installed → UninstallReShade → RemoveFromGameFolder called.
    ///
    /// **Validates: Requirements 6.1, 6.3**
    /// </summary>
    [Fact]
    public void UninstallReShade_DcUpdateAvailable_RemoveFromGameFolderCalled()
    {
        var tracker = new TrackingShaderPackService();
        var card = CreateCard("GameDcUpdate", rsStatus: GameStatus.Installed);
        var vm = CreateViewModelWithCards(tracker, new List<GameCardViewModel> { card });

        vm.UninstallReShade(card);

        Assert.True(tracker.RemoveFromGameFolderCalled,
            "RemoveFromGameFolder must be called even when DC has an update available");
        Assert.Contains(card.InstallPath, tracker.RemoveFromGameFolderPaths);
    }

    // ── Tracking IShaderPackService ──────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool RemoveFromGameFolderCalled { get; private set; }
        public List<string> RemoveFromGameFolderPaths { get; } = new();

        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } =
            new List<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;
        public string[] GetRequiredPacks(string packId) => Array.Empty<string>();
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }

        public void RemoveFromGameFolder(string gameDir)
        {
            RemoveFromGameFolderCalled = true;
            RemoveFromGameFolderPaths.Add(gameDir);
        }

        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) { }
        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null) { }

        public void SyncShadersToAllLocations(
            IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations,
            IEnumerable<string>? selectedPackIds = null) { }

        public Task EnsurePacksAsync(IEnumerable<string> packIds, IProgress<string>? progress = null) => Task.CompletedTask;
        public bool IsPackCached(string packId) => true;
    }

    // ── Minimal stubs ────────────

    private class StubModInstallService : IModInstallService
    {
        public event Action<InstalledModRecord>? InstallCompleted;
        public Task<InstalledModRecord> InstallAsync(GameMod mod, string gameInstallPath, IProgress<(string, double)>? progress = null, string? gameName = null) => Task.FromResult(new InstalledModRecord());
        public Task<bool> CheckForUpdateAsync(InstalledModRecord record) => Task.FromResult(false);
        public void Uninstall(InstalledModRecord record) { }
        public List<InstalledModRecord> LoadAll() => new();
        public InstalledModRecord? FindRecord(string gameName, string? installPath = null) => null;
        public void SaveRecordPublic(InstalledModRecord record) { }
        public void RemoveRecord(InstalledModRecord record) { }
    }

    private class StubAuxInstallService : IAuxInstallService, IAuxFileService
    {
        public Task<AuxInstalledRecord> InstallReShadeAsync(string gameName, string installPath, string? shaderModeOverride = null, bool use32Bit = false, string? filenameOverride = null, IEnumerable<string>? selectedPackIds = null, IProgress<(string, double)>? progress = null, string? screenshotSavePath = null, bool useNormalReShade = false, string? overlayHotkey = null, string? screenshotHotkey = null) => Task.FromResult(new AuxInstalledRecord());
        public Task<bool> CheckForUpdateAsync(AuxInstalledRecord record) => Task.FromResult(false);
        public void Uninstall(AuxInstalledRecord record) { }
        public void UninstallDllOnly(AuxInstalledRecord record) { }
        public List<AuxInstalledRecord> LoadAll() => new();
        public AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType) => null;
        public void SaveAuxRecord(AuxInstalledRecord record) { }
        public void RemoveRecord(AuxInstalledRecord record) { }
        // IAuxFileService stubs
        public bool EnsureReShadeStaging() => false;
        public AuxInstallService.DxgiFileType IdentifyDxgiFile(string filePath) => AuxInstallService.DxgiFileType.Unknown;
        public bool BackupForeignDll(string dllPath) => false;
        public void RestoreForeignDll(string dllPath) { }
        public bool IsReShadeFileStrict(string filePath) => false;
        public bool IsReShadeFile(string filePath) => false;
        public void EnsureInisDir() { }
        public void MergeRsIni(string gameDir, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null) { }
        public void MergeRsVulkanIni(string gameDir, string? gameName = null, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null) { }
        public void CopyRsIni(string gameDir) { }
        public void CopyRsPresetIniIfPresent(string gameDir) { }
        public string? ReadInstalledVersion(string installPath, string fileName) => null;
        public bool CheckReShadeUpdateLocal(AuxInstalledRecord record) => false;
    }

    private class StubWikiService : IWikiService
    {
        public Task<(List<GameMod> Mods, Dictionary<string, string> GenericNotes)> FetchAllAsync(IProgress<string>? progress = null) => Task.FromResult((new List<GameMod>(), new Dictionary<string, string>()));
        public Task<DateTime?> GetSnapshotLastModifiedAsync(string url) => Task.FromResult<DateTime?>(null);
    }

    private class StubManifestService : IManifestService
    {
        public Task<RemoteManifest?> FetchAsync() => Task.FromResult<RemoteManifest?>(null);
        public RemoteManifest? LoadCached() => null;
    }

    private class StubGameLibraryService : IGameLibraryService
    {
        public SavedGameLibrary? Load() => null;
        public void Save(List<DetectedGame> games, Dictionary<string, bool> addonCache, HashSet<string> hiddenGames, HashSet<string> favouriteGames, List<DetectedGame> manualGames, Dictionary<string, string>? engineTypeCache = null, Dictionary<string, string>? resolvedPathCache = null, Dictionary<string, string>? addonFileCache = null, Dictionary<string, MachineType>? bitnessCache = null, string? lastSelectedGame = null) { }
        public List<DetectedGame> ToDetectedGames(SavedGameLibrary lib) => new();
        public List<DetectedGame> ToManualGames(SavedGameLibrary lib) => new();
    }

    private class StubGameDetectionService : IGameDetectionService
    {
        public List<DetectedGame> FindSteamGames() => new();
        public List<DetectedGame> FindGogGames() => new();
        public List<DetectedGame> FindEpicGames() => new();
        public List<DetectedGame> FindEaGames() => new();
        public List<DetectedGame> FindXboxGames() => new();
        public List<DetectedGame> FindUbisoftGames() => new();
        public List<DetectedGame> FindBattleNetGames() => new();
        public List<DetectedGame> FindRockstarGames() => new();
        public (string installPath, EngineType engine) DetectEngineAndPath(string rootPath) => (rootPath, EngineType.Unknown);
        public GameMod? MatchGame(DetectedGame game, IEnumerable<GameMod> mods, Dictionary<string, string>? nameMappings = null) => null;
        public string NormalizeName(string name) => name.ToLowerInvariant();
    }

    private class StubPeHeaderService : IPeHeaderService
    {
        public MachineType DetectArchitecture(string exePath) => MachineType.Native;
        public string? FindGameExe(string installPath) => null;
        public MachineType DetectGameArchitecture(string installPath) => MachineType.Native;
    }

    private class StubUpdateService : IUpdateService
    {
        public Version CurrentVersion => new(1, 0, 0);
        public Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false) => Task.FromResult<UpdateInfo?>(null);
        public Task<string?> DownloadInstallerAsync(string downloadUrl, IProgress<(string, double)>? progress = null) => Task.FromResult<string?>(null);
        public void LaunchInstallerAndExit(string installerPath, Action closeApp) { }
    }

    private class StubLumaService : ILumaService
    {
        public Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null) => Task.FromResult(new List<LumaMod>());
        public Task<LumaInstalledRecord> InstallAsync(LumaMod mod, string gameInstallPath, IProgress<(string, double)>? progress = null) => Task.FromResult(new LumaInstalledRecord());
        public void Uninstall(LumaInstalledRecord record) { }
        public void SaveLumaRecord(LumaInstalledRecord record) { }
        public void RemoveLumaRecord(string gameName, string installPath) { }
        public Task<int> GetLatestBuildNumberAsync() => Task.FromResult(0);
        public Task<bool> CheckForUpdateAsync(LumaInstalledRecord record) => Task.FromResult(false);
    }

    private class StubReShadeUpdateService : IReShadeUpdateService
    {
        public Task<(string version, string url)?> CheckLatestVersionAsync() => Task.FromResult<(string, string)?>(null);
        public Task<bool> EnsureLatestAsync(IProgress<(string, double)>? progress = null) => Task.FromResult(false);
    }

    private class StubNormalReShadeUpdateService : INormalReShadeUpdateService
    {
        public Task<(string version, string url)?> CheckLatestVersionAsync() => Task.FromResult<(string, string)?>(null);
        public Task<bool> EnsureLatestAsync(IProgress<(string msg, double pct)>? progress = null) => Task.FromResult(false);
    }

    private class StubREFrameworkService : IREFrameworkService
    {
        public Task<REFrameworkInstalledRecord> InstallAsync(string gameName, string installPath, IProgress<(string message, double percent)>? progress = null) => Task.FromResult(new REFrameworkInstalledRecord());
        public void Uninstall(string gameName, string installPath) { }
        public Task<bool> CheckForUpdateAsync(string installedVersion) => Task.FromResult(false);
        public Task<string?> GetLatestVersionAsync() => Task.FromResult<string?>(null);
        public List<REFrameworkInstalledRecord> GetRecords() => new();
        public Task InstallPdUpscalerAsync(string gameName, string installPath, string artifactName, IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public void RestoreStandardREFramework(string gameName, string installPath) { }
    }

    private class StubNexusModsService : INexusModsService
    {
        public Task InitAsync() => Task.CompletedTask;
        public string? ResolveUrl(string gameName, RemoteManifest? manifest) => null;
    }

    private class StubPcgwService : IPcgwService
    {
        public Task LoadCacheAsync() => Task.CompletedTask;
        public Task<string?> ResolveUrlAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest) => Task.FromResult<string?>(null);
        public Task FlushCacheAsync() => Task.CompletedTask;
    }

    private class StubOptiScalerService : IOptiScalerService
    {
        public bool IsStagingReady => false;
        public bool HasUpdate => false;
        public string? StagedVersion => null;
        public bool FirstTimeWarningAcknowledged { get; set; }
        public Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public Task CheckForUpdateAsync() => Task.CompletedTask;
        public void ClearStaging() { }
        public Task EnsureDlssStagingAsync(IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public Task<AuxInstalledRecord?> InstallAsync(GameCardViewModel card, IProgress<(string message, double percent)>? progress = null, string gpuType = "NVIDIA", bool dlssInputs = true, string? hotkey = null) => Task.FromResult<AuxInstalledRecord?>(null);
        public void Uninstall(GameCardViewModel card) { }
        public Task UpdateAsync(GameCardViewModel card, IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public void CopyIniToGame(GameCardViewModel card) { }
        public string? DetectInstallation(string installPath) => null;
        public bool IsOptiScalerFile(string filePath) => false;
        public List<AuxInstalledRecord> LoadAllRecords() => new();
        public AuxInstalledRecord? FindRecord(string gameName, string installPath) => null;
        public string GetEffectiveOsDllName(string gameName) => "dxgi.dll";
        public void SetHotkey(string hotkeyValue) { }
        public void ApplyHotkeyToAllGames(string hotkeyValue) { }
    }

    private class StubOptiScalerWikiService : IOptiScalerWikiService
    {
        public OptiScalerWikiData? CachedData { get; set; }
        public Task<OptiScalerWikiData> FetchAsync(IProgress<string>? progress = null)
            => Task.FromResult(CachedData ?? new OptiScalerWikiData());
    }

    private class StubHdrDatabaseService : IHdrDatabaseService
    {
        public Dictionary<string, string>? CachedData { get; set; }
        public Task<Dictionary<string, string>> FetchAsync(IProgress<string>? progress = null)
            => Task.FromResult(CachedData ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
