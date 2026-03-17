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
            client.DefaultRequestHeaders.Add("User-Agent", "RenoDXCommander/2.0");
            // Per-request timeout — generous enough for large files on slow connections.
            // Individual services can set per-request timeouts via CancellationTokenSource
            // if they need tighter control.
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrLower;
            return client;
        });

        // Services — all singletons
        services.AddSingleton<IModInstallService, ModInstallService>();
        services.AddSingleton<IAuxInstallService, AuxInstallService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        services.AddSingleton<IReShadeUpdateService, ReShadeUpdateService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ILumaService, LumaService>();
        services.AddSingleton<IShaderPackService, ShaderPackService>();
        services.AddSingleton<ILiliumShaderService, LiliumShaderService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IPeHeaderService, PeHeaderService>();

        // ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FilterViewModel>();

        // Extracted services
        services.AddSingleton<IUpdateOrchestrationService, UpdateOrchestrationService>();
        services.AddSingleton<IDllOverrideService, DllOverrideService>();
        services.AddSingleton<IGameNameService, GameNameService>();
        services.AddSingleton<IGameInitializationService, GameInitializationService>();
        services.AddSingleton<ISevenZipExtractor, ReShadeExtractor>();

        services.AddSingleton<MainViewModel>();

        // Window — transient so each request creates a new instance
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CrashReporter.Log("[App.OnLaunched] Creating MainWindow");
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
        CrashReporter.Log("[App.OnLaunched] MainWindow activated");
    }
}
