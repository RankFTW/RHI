using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Universal MFG Unlocker moves between a proxy DLL next to the exe and MFGUnlocker.asi in
/// OptiScaler\plugins (OptiScaler 10 builds). These run the real file moves in a temp folder.
/// </summary>
public class Rtx40MfgPluginTests : IDisposable
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    private readonly string _root    = Path.Combine(Path.GetTempPath(), "rhi-mfg-" + Guid.NewGuid().ToString("N"));
    private readonly string _staging;
    private readonly string _game;
    private readonly Rtx40MfgService _svc;
    private static readonly byte[] ModBytes = { (byte)'M', (byte)'Z', 1, 2, 3, 4 };

    public Rtx40MfgPluginTests()
    {
        _staging = Path.Combine(_root, "staging");
        _game    = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(_staging, "xess"));
        Directory.CreateDirectory(Path.Combine(_game, "OptiScaler"));
        File.WriteAllBytes(Path.Combine(_staging, Rtx40MfgService.StagedDllName), ModBytes);
        File.WriteAllText(Path.Combine(_staging, "staging_format.txt"), "2");
        File.WriteAllBytes(Path.Combine(_staging, "xess", "libxess_fg.dll"), new byte[] { 7 });
        File.WriteAllBytes(Path.Combine(_staging, "xess", "libxell.dll"), new byte[] { 8 });
        _svc = new Rtx40MfgService(new HttpClient(), new NullReporter(), new GitHubETagCache(), _staging);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private string G(params string[] parts) => Path.Combine(new[] { _game }.Concat(parts).ToArray());
    private string Plugin => Path.Combine(_game, Rtx40MfgService.PluginRelPath);

    [Fact]
    public void IsPluginInstall_AcceptsEitherSlash()
    {
        Assert.True(Rtx40MfgService.IsPluginInstall(@"OptiScaler\plugins\MFGUnlocker.asi"));
        Assert.True(Rtx40MfgService.IsPluginInstall("optiscaler/plugins/mfgunlocker.asi"));
        Assert.False(Rtx40MfgService.IsPluginInstall("version.dll"));
        Assert.False(Rtx40MfgService.IsPluginInstall(null));
    }

    [Fact]
    public void ProxyInstall_DeploysXessFolder()
    {
        Assert.True(_svc.Install(_game, "winmm.dll"));
        Assert.True(File.Exists(G("winmm.dll")));
        Assert.True(File.Exists(G(Rtx40MfgService.XessFolderName, "libxess_fg.dll")));
        Assert.True(File.Exists(G(Rtx40MfgService.XessFolderName, "libxell.dll")));
    }

    [Fact]
    public void MoveIntoPlugin_RemovesProxyAndXess()
    {
        _svc.Install(_game, "winmm.dll");

        var result = _svc.MoveToOptiScalerPlugin(_game, "winmm.dll");

        Assert.Equal(Rtx40MfgService.PluginRelPath, result);
        Assert.True(File.Exists(Plugin));
        Assert.Equal(ModBytes, File.ReadAllBytes(Plugin));
        Assert.False(File.Exists(G("winmm.dll")));
        Assert.False(File.Exists(G("winmm.dll.original")));
        Assert.False(Directory.Exists(G(Rtx40MfgService.XessFolderName)));
        Assert.True(_svc.IsInstalledIn(_game, result));
    }

    [Fact]
    public void MoveIntoPlugin_RestoresGamesOwnProxyFile()
    {
        var gameOwn = new byte[] { 9, 9, 9 };
        File.WriteAllBytes(G("version.dll"), gameOwn);   // the game ships its own version.dll
        _svc.Install(_game, "version.dll");              // backed up to version.dll.original

        _svc.MoveToOptiScalerPlugin(_game, "version.dll");

        Assert.Equal(gameOwn, File.ReadAllBytes(G("version.dll")));
        Assert.False(File.Exists(G("version.dll.original")));
    }

    [Fact]
    public void MoveOut_UsesPreviousNameAndRestoresXess()
    {
        _svc.Install(_game, "winmm.dll");
        _svc.MoveToOptiScalerPlugin(_game, "winmm.dll");

        var name = _svc.MoveOutOfOptiScalerPlugin(_game);

        Assert.Equal("winmm.dll", name);
        Assert.Equal(ModBytes, File.ReadAllBytes(G("winmm.dll")));
        Assert.True(File.Exists(G("winmm.dll.original")));      // sentinel so uninstall cleans it up
        Assert.False(File.Exists(Plugin));
        Assert.Empty(Directory.GetFiles(G("OptiScaler", "plugins"))); // no sentinel / marker left behind
        Assert.True(File.Exists(G(Rtx40MfgService.XessFolderName, "libxess_fg.dll")));
    }

    [Fact]
    public void MoveOut_PreviousNameTaken_PicksFreeName()
    {
        _svc.Install(_game, "winmm.dll");
        _svc.MoveToOptiScalerPlugin(_game, "winmm.dll");
        File.WriteAllBytes(G("winmm.dll"), new byte[] { 5 }); // something else took winmm.dll meanwhile

        var name = _svc.MoveOutOfOptiScalerPlugin(_game);

        Assert.Equal("version.dll", name);
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(G("winmm.dll"))); // left alone
        Assert.Equal(ModBytes, File.ReadAllBytes(G("version.dll")));
    }

    [Fact]
    public void MoveOut_NoMarker_DefaultsToVersionDll()
    {
        Assert.True(_svc.Install(_game, Rtx40MfgService.PluginRelPath)); // installed straight into plugins

        Assert.Equal("version.dll", _svc.MoveOutOfOptiScalerPlugin(_game));
    }

    [Fact]
    public void PluginInstall_NoXessFolder_UninstallCleansUp()
    {
        Assert.True(_svc.Install(_game, Rtx40MfgService.PluginRelPath));
        Assert.True(File.Exists(Plugin));
        Assert.False(Directory.Exists(G(Rtx40MfgService.XessFolderName)));

        _svc.Uninstall(_game, Rtx40MfgService.PluginRelPath);

        Assert.False(File.Exists(Plugin));
        Assert.Empty(Directory.GetFiles(G("OptiScaler", "plugins")));
    }

    [Fact]
    public void MoveIntoPlugin_ProxyMissing_DoesNothing()
    {
        Assert.Null(_svc.MoveToOptiScalerPlugin(_game, "version.dll"));
        Assert.False(File.Exists(Plugin));
    }
}
