using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RHI.Linux.Core;
using System.Text;
using Xunit;

namespace RHI.Linux.UiTests;

public sealed class REFrameworkUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-ui-ref-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME"), _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    private MainWindow? _window;
    public REFrameworkUiTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_root, "cache"));
    }
    public void Dispose()
    {
        if (_window != null) { foreach (var child in _window.OwnedWindows.ToArray()) child.Close(); _window.Close(); }
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldData); Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _oldCache);
        Directory.Delete(_root, true);
    }
    private Game ReGame()
    {
        var root = Path.Combine(_root, "Resident Evil Requiem"); Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "re9.exe"); File.WriteAllText(exe, "game"); File.WriteAllText(Path.Combine(root, "re_chunk_000.pak"), "");
        return new() { Name = "Resident Evil Requiem", Root = root, Executable = exe, Executables = [exe] };
    }
    private async Task Open(Game game)
    {
        _window = new MainWindow([game]); _window.Show();
        for (var i = 0; i < 100 && !Text.Contains("Components"); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
    }
    private IEnumerable<T> All<T>() => _window!.GetLogicalDescendants().OfType<T>();
    private string Text => string.Join("\n", All<TextBlock>().Select(t => t.Text));
    private Button Named(string name) => All<Button>().Single(b => b.Name == name);

    [AvaloniaFact] public async Task ReEngineGamesRequireReFrameworkBeforeReShade()
    {
        await Open(ReGame());
        Assert.Contains("RE Engine", Text); Assert.Contains("RE Framework", Text);
        Assert.Equal("Set up Wine detection bypass…", Named("ReEngineWineDetectionToggle").Content);
        Assert.Equal("↓  Install RE Framework", Named("InstallREFramework").Content);
        var reshade = Named("InstallReShade");
        Assert.Equal("⚠  RE Framework required", reshade.Content); Assert.False(reshade.IsEnabled);
        Assert.Contains("RHI will install RE Framework, ReShade", Text);
        // RE Framework sits above ReShade, as in the Windows component table.
        var buttons = All<Button>().Select(b => b.Name).ToList();
        Assert.True(buttons.IndexOf("InstallREFramework") < buttons.IndexOf("InstallReShade"));
    }

    [AvaloniaFact] public async Task NonReEngineGamesDoNotShowWineDetectionBypass()
    {
        var game = ReGame();
        File.Delete(Path.Combine(game.Root, "re_chunk_000.pak"));
        await Open(game);
        Assert.DoesNotContain(All<Button>(), b => b.Name == "ReEngineWineDetectionToggle");
    }

    [AvaloniaFact] public async Task InstalledReFrameworkShowsVersionAndOffersNightlyUpdate()
    {
        var game = ReGame();
        new Installation(game.Root).Install(REFramework.Component, "01400", [new(REFramework.Dll, Encoding.UTF8.GetBytes("ref"))]);
        LinuxPaths.WriteJson(Path.Combine(_root, "cache/rhi-linux/reframework/latest.json"), new NrRelease("01424", "https://example.invalid/REFramework.zip"));
        await Open(game);
        Assert.Contains("01400", Text);
        Assert.Equal("⬆  Update RE Framework", Named("InstallREFramework").Content);
        Assert.Contains("update", Named("InstallREFramework").Classes);
        Assert.True(Named("InstallReShade").IsEnabled);
        Assert.NotNull(Named("RemoveREFramework"));
    }
}
