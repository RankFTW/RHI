using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// The application-wide DI service provider.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        // Register crash/error reporting before anything else runs.
        // This catches AppDomain, TaskScheduler, and WinUI exceptions.
        CrashReporter.Register(this);

        // Configure DI container
        var services = new ServiceCollection();

        // Shared HttpClient — singleton with UserAgent header and optimised connection settings
        services.AddSingleton<HttpClient>(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                // Allow modern protocols — HTTP/2 multiplexes streams over a single
                // TCP connection which avoids head-of-line blocking and dramatically
                // improves throughput from CDNs like GitHub Pages / Releases.
                EnableMultipleHttp2Connections = true,

                // Raise the per-server connection cap so parallel downloads from the
                // same host aren't serialised behind two sockets.
                MaxConnectionsPerServer = 16,

                // Keep connections alive between downloads so subsequent requests
                // skip the TCP + TLS handshake.
                PooledConnectionLifetime  = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

                // Larger initial receive buffer reduces syscall overhead on fast links.
                InitialHttp2StreamWindowSize = 1024 * 1024, // 1 MB
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "RHI/2.0");
            // Per-request timeout — generous enough for large files on slow connections.
            // Individual services can set per-request timeouts via CancellationTokenSource
            // if they need tighter control.
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrLower;
            return client;
        });

        // Shared ETag cache for GitHub API conditional requests (304 Not Modified)
        services.AddSingleton<GitHubETagCache>();

        // Services — all singletons
        services.AddSingleton<IModInstallService, ModInstallService>();
        services.AddSingleton<IAuxInstallService, AuxInstallService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        services.AddSingleton<IReShadeUpdateService, ReShadeUpdateService>();
        services.AddSingleton<INormalReShadeUpdateService, NormalReShadeUpdateService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ILumaService, LumaService>();
        services.AddSingleton<IShaderPackService, ShaderPackService>();
        services.AddSingleton<ILiliumShaderService, LiliumShaderService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IPeHeaderService, PeHeaderService>();
        services.AddSingleton<ICrashReporter, CrashReporterService>();
        services.AddSingleton<IAuxFileService>(sp => sp.GetRequiredService<IAuxInstallService>() as AuxInstallService
            ?? throw new InvalidOperationException("IAuxInstallService must be AuxInstallService"));
        services.AddSingleton<IREFrameworkService, REFrameworkService>();
        services.AddSingleton<INexusModsService, NexusModsService>();
        services.AddSingleton<ISteamAppIdResolver, SteamAppIdResolver>();
        services.AddSingleton<IPcgwService, PcgwService>();

        // ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FilterViewModel>();

        // Extracted services
        services.AddSingleton<IUpdateOrchestrationService, UpdateOrchestrationService>();
        services.AddSingleton<IDllOverrideService, DllOverrideService>();
        services.AddSingleton<IGameNameService, GameNameService>();
        services.AddSingleton<IGameInitializationService, GameInitializationService>();
        services.AddSingleton<ISevenZipExtractor, ReShadeExtractor>();
        services.AddSingleton<IOptiScalerService, OptiScalerService>();
        services.AddSingleton<IOptiScalerWikiService, OptiScalerWikiService>();
        services.AddSingleton<IHdrDatabaseService, HdrDatabaseService>();

        services.AddSingleton<MainViewModel>();

        // Window — transient so each request creates a new instance
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // ── One-time migration from legacy AppData folders ───────
        MigrateLegacyAppData();
        DownloadsMigrationService.RunOnce();

        // Single-instance check: if another instance is already running,
        // forward the addon file path and exit immediately.
        var cmdArgs = Environment.GetCommandLineArgs();
        string? addonArg = null;
        if (cmdArgs.Length > 1)
        {
            var ext = Path.GetExtension(cmdArgs[1]);
            var fileName = Path.GetFileName(cmdArgs[1]);
            if ((string.Equals(ext, ".addon64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".addon32", StringComparison.OrdinalIgnoreCase))
                && fileName.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
                addonArg = cmdArgs[1];
        }

        if (!SingleInstanceService.TryAcquire())
        {
            // Another instance is running — forward the file and exit
            if (addonArg != null)
                SingleInstanceService.SendToRunningInstance(addonArg);
            Environment.Exit(0);
            return;
        }

        CrashReporter.Log("[App.OnLaunched] Creating MainWindow");
        GraphicsApiDetector.LoadCache();
        MainViewModel.LoadGameApiCache();
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
        CrashReporter.Log("[App.OnLaunched] MainWindow activated");

        // Start listening for file paths from subsequent instances
        SingleInstanceService.StartListening();
        SingleInstanceService.FileReceived += path =>
        {
            if (_window is MainWindow mw)
                mw.DispatcherQueue.TryEnqueue(() => mw.HandleAddonFile(path));
        };

        // Handle addon file passed on first launch
        if (addonArg != null)
        {
            CrashReporter.Log($"[App.OnLaunched] Addon file passed via command line: {addonArg}");
            if (_window is MainWindow mw)
                mw.HandleAddonFile(addonArg);
        }
    }

    /// <summary>
    /// Migrates legacy %LocalAppData% folders to %LocalAppData%\RHI.
    /// Handles the original RenoDXCommander folder and the UPST folder.
    /// Copies all contents then deletes the old folder. Runs once per legacy folder.
    /// </summary>
    private static void MigrateLegacyAppData()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var newDir = Path.Combine(localAppData, "RHI");

            // Migrate from RenoDXCommander (oldest) first, then UPST
            foreach (var legacyName in new[] { "RenoDXCommander", "UPST" })
            {
                var legacyDir = Path.Combine(localAppData, legacyName);
                if (!Directory.Exists(legacyDir))
                    continue;

                CrashReporter.Log($"[App.MigrateLegacyAppData] Migrating {legacyDir} → {newDir}");
                CopyDirectoryRecursive(legacyDir, newDir);
                Directory.Delete(legacyDir, recursive: true);
                CrashReporter.Log($"[App.MigrateLegacyAppData] Migration from {legacyName} complete");
            }
        }
        catch (Exception ex)
        {
            // Migration failure is non-fatal — the app will recreate files as needed
            CrashReporter.Log($"[App.MigrateLegacyAppData] Migration failed — {ex.Message}");
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            // Don't overwrite if the new folder already has the file (e.g. partial previous migration)
            if (!File.Exists(destFile))
                File.Copy(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}
