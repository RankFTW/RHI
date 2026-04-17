using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Provides factory methods for creating MainViewModel instances in tests
/// with minimal stub service implementations.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Creates a MainViewModel with stub services suitable for property/layout tests
    /// that don't exercise service I/O.
    /// </summary>
    public static MainViewModel CreateMainViewModel()
    {
        var installer = new StubModInstallService();
        var auxInstaller = new StubAuxInstallService();
        var wikiService = new StubWikiService();
        var manifestService = new StubManifestService();
        var gameLibraryService = new StubGameLibraryService();
        var gameDetectionService = new StubGameDetectionService();
        var peHeaderService = new StubPeHeaderService();
        var updateService = new StubUpdateService();
        var shaderPackService = new StubShaderPackService();
        var lumaService = new StubLumaService();
        var rsUpdateService = new StubReShadeUpdateService();
        var normalRsUpdateService = new StubNormalReShadeUpdateService();
        var settingsVm = new SettingsViewModel();
        var filterVm = new FilterViewModel();
        var updateOrch = new UpdateOrchestrationService(installer, auxInstaller, new CrashReporterService(), auxInstaller, new StubREFrameworkService());
        var dllOverride = new DllOverrideService(auxInstaller);
        var gameName = new GameNameService(gameDetectionService, installer, auxInstaller, lumaService);
        var gameInit = new GameInitializationService(
            gameDetectionService, wikiService, manifestService, installer, auxInstaller,
            gameLibraryService, peHeaderService, lumaService, rsUpdateService, shaderPackService);

        return new MainViewModel(
            new HttpClient(),
            installer,
            auxInstaller,
            new CrashReporterService(),
            wikiService,
            manifestService,
            gameLibraryService,
            gameDetectionService,
            peHeaderService,
            updateService,
            shaderPackService,
            lumaService,
            rsUpdateService,
            normalRsUpdateService,
            settingsVm,
            filterVm,
            updateOrch,
            dllOverride,
            gameName,
            gameInit,
            new StubREFrameworkService(),
            new StubNexusModsService(),
            new StubPcgwService(),
            new StubOptiScalerService());
    }

    // ── Stub implementations ──────────────────────────────────────────────────

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
        public Task<AuxInstalledRecord> InstallReShadeAsync(string gameName, string installPath, string? shaderModeOverride = null, bool use32Bit = false, string? filenameOverride = null, IEnumerable<string>? selectedPackIds = null, IProgress<(string, double)>? progress = null, string? screenshotSavePath = null, bool useNormalReShade = false, string? overlayHotkey = null) => Task.FromResult(new AuxInstalledRecord());
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
        public void MergeRsIni(string gameDir, string? screenshotSavePath = null, string? overlayHotkey = null) { }
        public void MergeRsVulkanIni(string gameDir, string? gameName = null, string? screenshotSavePath = null, string? overlayHotkey = null) { }
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

    internal class StubShaderPackService : IShaderPackService
    {
        /// <summary>Records each call to SyncGameFolder with its parameters.</summary>
        public List<(string GameDir, IEnumerable<string>? SelectedPackIds)> SyncGameFolderCalls { get; } = new();

        /// <summary>Records each call to RemoveFromGameFolder with the gameDir argument.</summary>
        public List<string> RemoveFromGameFolderCalls { get; } = new();

        /// <summary>Records each call to RestoreOriginalIfPresent with the gameDir argument.</summary>
        public List<string> RestoreOriginalIfPresentCalls { get; } = new();

        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } = new List<(string, string, ShaderPackService.PackCategory)>();
        public string? GetPackDescription(string packId) => null;
        public string[] GetRequiredPacks(string packId) => Array.Empty<string>();
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }
        public void RemoveFromGameFolder(string gameDir) => RemoveFromGameFolderCalls.Add(gameDir);
        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) => RestoreOriginalIfPresentCalls.Add(gameDir);
        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
            => SyncGameFolderCalls.Add((gameDir, selectedPackIds));
        public void SyncShadersToAllLocations(IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations, IEnumerable<string>? selectedPackIds = null) { }
    }

    private class StubLumaService : ILumaService
    {
        public Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null) => Task.FromResult(new List<LumaMod>());
        public Task<LumaInstalledRecord> InstallAsync(LumaMod mod, string gameInstallPath, IProgress<(string, double)>? progress = null) => Task.FromResult(new LumaInstalledRecord());
        public void Uninstall(LumaInstalledRecord record) { }
        public void SaveLumaRecord(LumaInstalledRecord record) { }
        public void RemoveLumaRecord(string gameName, string installPath) { }
    }

    private class StubReShadeUpdateService : IReShadeUpdateService
    {
        public Task<(string version, string url)?> CheckLatestVersionAsync() => Task.FromResult<(string, string)?>(null);
        public Task<bool> EnsureLatestAsync(IProgress<(string, double)>? progress = null) => Task.FromResult(false);
    }

    internal class StubNormalReShadeUpdateService : INormalReShadeUpdateService
    {
        public Task<(string version, string url)?> CheckLatestVersionAsync() => Task.FromResult<(string, string)?>(null);
        public Task<bool> EnsureLatestAsync(IProgress<(string msg, double pct)>? progress = null) => Task.FromResult(false);
    }

    internal class StubREFrameworkService : IREFrameworkService
    {
        public Task<REFrameworkInstalledRecord> InstallAsync(string gameName, string installPath, IProgress<(string message, double percent)>? progress = null) => Task.FromResult(new REFrameworkInstalledRecord());
        public void Uninstall(string gameName, string installPath) { }
        public Task<bool> CheckForUpdateAsync(string installedVersion) => Task.FromResult(false);
        public Task<string?> GetLatestVersionAsync() => Task.FromResult<string?>(null);
        public List<REFrameworkInstalledRecord> GetRecords() => new();
    }

    internal class StubNexusModsService : INexusModsService
    {
        public Task InitAsync() => Task.CompletedTask;
        public string? ResolveUrl(string gameName, RemoteManifest? manifest) => null;
    }

    internal class StubSteamAppIdResolver : ISteamAppIdResolver
    {
        public Task<int?> ResolveAsync(string gameName, int? detectedAppId, string installPath, RemoteManifest? manifest, Dictionary<string, int>? appIdCache = null) => Task.FromResult<int?>(null);
        public int? FindMatchingAppId(string gameName, List<SteamStoreSearchItem> results) => null;
    }

    internal class StubPcgwService : IPcgwService
    {
        public Task LoadCacheAsync() => Task.CompletedTask;
        public Task<string?> ResolveUrlAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest) => Task.FromResult<string?>(null);
        public Task FlushCacheAsync() => Task.CompletedTask;
    }

    internal class StubOptiScalerService : IOptiScalerService
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
}
