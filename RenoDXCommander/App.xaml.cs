using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class App : Application
{
    private Window? _window;
    internal static string? _pendingLaunchGame;
    internal static bool _startMinimized;

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
        services.AddSingleton<ReShadeNightlyService>();
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
        services.AddSingleton<INexusUpdateService, NexusUpdateService>();
        services.AddSingleton<ISteamAppIdResolver, SteamAppIdResolver>();
        services.AddSingleton<IPcgwService, PcgwService>();
        services.AddSingleton<IUltrawideFixService, UltrawideFixService>();
        services.AddSingleton<IUltraPlusService, UltraPlusService>();

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
        services.AddSingleton<IDxvkService, DxvkService>();
        // Lazy<IDxvkService> breaks the circular dependency between OptiScalerService ↔ DxvkService
        services.AddSingleton<Lazy<IDxvkService>>(sp => new Lazy<IDxvkService>(() => sp.GetRequiredService<IDxvkService>()));
        services.AddSingleton<IDlssStreamlineService, DlssStreamlineService>();
        services.AddSingleton<DlssPresetService>();
        services.AddSingleton<DofFixService>();
        services.AddSingleton<UltimateAsiLoaderService>();
        services.AddSingleton<AutoUpdateService>();
        services.AddSingleton<DlssEnablerService>();
        services.AddSingleton<Renodx5AddonService>();
        services.AddSingleton<CustomReShadeHashService>();
        services.AddSingleton<SeenWikiModsService>();
        services.AddSingleton<SeenUltraPlusModsService>();
        services.AddSingleton<SeenLumaModsService>();
        services.AddSingleton<NexusDownloadService>();
        // Lazy<IDlssStreamlineService> breaks the circular dependency between OptiScalerService ↔ DlssStreamlineService
        services.AddSingleton<Lazy<IDlssStreamlineService>>(sp => new Lazy<IDlssStreamlineService>(() => sp.GetRequiredService<IDlssStreamlineService>()));

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
                && fileName.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase)
                && !fileName.StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                && !fileName.StartsWith("renodx-dlss.", StringComparison.OrdinalIgnoreCase))
                addonArg = cmdArgs[1];
        }

        // Handle --nxm argument (from nxm:// protocol handler, dev-unlocked only)
        string? nxmArg = null;
        if (FeatureFlags.NexusMods)
        {
            // Protocol handler sends: RHI.exe --nxm "nxm://..."
            var nxmIdx = Array.IndexOf(cmdArgs, "--nxm");
            if (nxmIdx >= 0 && nxmIdx < cmdArgs.Length - 1)
                nxmArg = cmdArgs[nxmIdx + 1];
            // Some OS protocol registrations pass the URL as the only extra arg directly
            else if (cmdArgs.Length > 1 && cmdArgs[1].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                nxmArg = cmdArgs[1];
        }

        // Handle --launch argument (from jump list or command line)
        var launchIdx = Array.IndexOf(cmdArgs, "--launch");
        string? launchGameArg = null;
        if (launchIdx >= 0 && launchIdx < cmdArgs.Length - 1)
            launchGameArg = cmdArgs[launchIdx + 1];

        // Handle --minimized argument (start minimized to tray)
        var startMinimized = cmdArgs.Contains("--minimized");

        // Also check for signal file (used when Admin Mode relaunches via scheduled task)
        var minimizedSignalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "rhi_start_minimized");
        if (File.Exists(minimizedSignalPath))
        {
            startMinimized = true;
            try { File.Delete(minimizedSignalPath); } catch { }
        }

        CrashReporter.Log($"[App.OnLaunched] Args: [{string.Join(", ", cmdArgs)}], startMinimized={startMinimized}");

        if (!SingleInstanceService.TryAcquire())
        {
            // Another instance is running — forward the file or launch command and exit
            if (launchGameArg != null)
                SingleInstanceService.SendToRunningInstance($"--launch:{launchGameArg}");
            else if (nxmArg != null)
                SingleInstanceService.SendToRunningInstance($"nxm:{nxmArg}");
            else if (addonArg != null)
                SingleInstanceService.SendToRunningInstance(addonArg);
            else
                SingleInstanceService.SendToRunningInstance("--activate");
            try { File.Delete(CrashReporter.CurrentSessionLogPath); } catch { }
            Environment.Exit(0);
            return;
        }

        // Store pending launch for after window initializes
        _pendingLaunchGame = launchGameArg;
        // Set minimized flag BEFORE creating the window so the constructor can read it
        _startMinimized = startMinimized;

        // ── Admin Mode: if the scheduled task exists and we're not elevated, relaunch via task ──
        if (!IsRunningAsAdmin() && IsAdminTaskRegistered())
        {
            try
            {
                CrashReporter.Log("[App.OnLaunched] Admin Mode enabled but not elevated — relaunching via scheduled task");
                
                // Pass minimized flag via signal file (schtasks /Run doesn't support extra args)
                if (startMinimized)
                {
                    var signalDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
                    Directory.CreateDirectory(signalDir);
                    File.WriteAllText(Path.Combine(signalDir, "rhi_start_minimized"), "");
                }

                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/Run /TN \"RHI Admin Mode\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                System.Diagnostics.Process.Start(psi);
                try { File.Delete(CrashReporter.CurrentSessionLogPath); } catch { }
                Environment.Exit(0);
                return;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[App.OnLaunched] Admin Mode relaunch failed — continuing non-elevated: {ex.Message}");
            }
        }

        CrashReporter.Log("[App.OnLaunched] Creating MainWindow");
        GraphicsApiDetector.LoadCache();
        MainViewModel.LoadGameApiCache();

        // Check if first-launch setup is needed
        // Check if first-launch setup is needed.
        // Skip if settings.json already exists with content — that's an existing user.
        var rawSettings = SettingsViewModel.LoadSettingsFile();
        bool setupDone = rawSettings.Count > 0
            || (rawSettings.TryGetValue("FirstLaunchSetupDone", out var fls) && fls == "true");

        if (!setupDone)
        {
            CrashReporter.Log("[App.OnLaunched] First-launch setup not done — showing SetupWindow");
            var setupWindow = new SetupWindow();
            setupWindow.OnComplete = (manageReShade) =>
            {
                rawSettings["FirstLaunchSetupDone"] = "true";
                if (!manageReShade)
                {
                    rawSettings["GlobalSkipRsUpdates"] = "true";
                    rawSettings["CacheAllShaders"] = "false";
                    rawSettings["GlobalShadersOff"] = "true";
                }
                SettingsViewModel.SaveSettingsFile(rawSettings);
                CrashReporter.Log($"[App.OnLaunched] Setup complete — manageReShade={manageReShade}");

                CrashReporter.Log($"[App.OnLaunched] Setup complete — manageReShade={manageReShade}");

                setupWindow.AppWindow.Hide();
                try
                {
                    _window = Services.GetRequiredService<MainWindow>();
                    if (!startMinimized)
                        _window.Activate();
                    SingleInstanceService.StartListening();
                    SingleInstanceService.FileReceived += OnFileReceived;
                    if (addonArg != null && _window is MainWindow mwAddon)
                        mwAddon.HandleAddonFile(addonArg);
                    if (nxmArg != null && _window is MainWindow mwNxm)
                        mwNxm.DispatcherQueue.TryEnqueue(() => mwNxm.HandleNxmUrl(nxmArg));
                    setupWindow.Close();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[App.OnLaunched] MainWindow creation failed after setup — {ex.Message}");
                }
            };
            setupWindow.Activate();
            return;
        }

        _window = Services.GetRequiredService<MainWindow>();

        if (!startMinimized)
        {
            _window.Activate();
            CrashReporter.Log("[App.OnLaunched] MainWindow activated");
        }
        else
        {
            CrashReporter.Log("[App.OnLaunched] Started minimized to tray");
        }

        // Start listening for file paths from subsequent instances
        SingleInstanceService.StartListening();
        SingleInstanceService.FileReceived += OnFileReceived;

        // Handle addon file passed on first launch
        if (addonArg != null)
        {
            CrashReporter.Log($"[App.OnLaunched] Addon file passed via command line: {addonArg}");
            if (_window is MainWindow mw)
                mw.HandleAddonFile(addonArg);
        }

        // Handle NXM protocol URL passed on first launch (dev-unlocked only)
        if (nxmArg != null && _window is MainWindow mwNxmFirst)
        {
            CrashReporter.Log($"[App.OnLaunched] NXM URL passed via command line: {nxmArg}");
            mwNxmFirst.DispatcherQueue.TryEnqueue(() => mwNxmFirst.HandleNxmUrl(nxmArg));
        }
    }

    private void OnFileReceived(string path)
    {
        if (path == "--activate")
        {
            if (_window is MainWindow mw0)
                mw0.DispatcherQueue.TryEnqueue(() => mw0.Activate());
            return;
        }
        if (path.StartsWith("--launch:"))
        {
            var gameName = path.Substring("--launch:".Length);
            if (_window is MainWindow mw)
                mw.DispatcherQueue.TryEnqueue(() =>
                {
                    mw.Activate();
                    var card = mw.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
                    if (card != null) mw.LaunchGame(card);
                });
            return;
        }
        // NXM protocol URL forwarded from a second instance (dev-unlocked only)
        if (path.StartsWith("nxm:", StringComparison.OrdinalIgnoreCase))
        {
            if (_window is MainWindow mwNxm)
                mwNxm.DispatcherQueue.TryEnqueue(() => mwNxm.HandleNxmUrl(path));
            return;
        }
        if (_window is MainWindow mw2)
            mw2.DispatcherQueue.TryEnqueue(() => mw2.HandleAddonFile(path));
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

    private static bool IsRunningAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static bool IsAdminTaskRegistered()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/Query /TN \"RHI Admin Mode\" /FO LIST")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }
}
