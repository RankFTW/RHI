using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RHI.Linux;
using RHI.Linux.Core;
using System.Text;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(RHI.Linux.UiTests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace RHI.Linux.UiTests;
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public sealed class WindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-ui-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME"), _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    private MainWindow? _window;
    public WindowTests()
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
    private Game Game(string name)
    {
        var root = Path.Combine(_root, name); Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "Game.exe"); File.WriteAllText(exe, "test game");
        return new() { Name = name, Root = root, Executable = exe, Executables = [exe] };
    }
    private async Task Open(params Game[] games)
    {
        _window = new MainWindow(games); _window.Show();
        for (var i = 0; i < 100 && !_window.GetLogicalDescendants().OfType<ListBox>().Any(l => l.ItemCount > 0); i++) await Task.Delay(10);
        Dispatcher.UIThread.RunJobs();
    }
    private IEnumerable<T> All<T>() => _window!.GetLogicalDescendants().OfType<T>();
    private string Text => string.Join("\n", All<TextBlock>().Select(t => t.Text));
    private Button Button(string content) => All<Button>().First(b => b.Content?.ToString() == content);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact] public async Task InstalledAndAppliedAreVisibleWithoutOpeningAdvancedSettings()
    {
        var game = Game("Fixture Game");
        var install = new Installation(game.Root);
        install.Install("ReShade", "Nightly 2026-09-13", [new("dxgi.dll", Encoding.UTF8.GetBytes("payload"))], proxy: "dxgi.dll");
        File.WriteAllText(Path.Combine(game.Root, "ReShade.log"), "Game.exe Initializing crosire's ReShade Created runtime environment");
        await Open(game);
        Assert.Contains("✓ Installed", Text); Assert.Contains("Applied · Nightly", Text); Assert.Contains("One step left: Steam setup", Text);
        Assert.DoesNotContain("WINEDLLOVERRIDES", Text);
        Assert.NotNull(Button("Advanced settings  ›"));
    }
    [AvaloniaFact] public async Task SearchAndFiltersChangeTheLibrary()
    {
        await Open(Game("Alpha"), Game("Beta"));
        var search = All<TextBox>().Single(t => t.Name == "GameSearch"); search.Text = "Beta"; Dispatcher.UIThread.RunJobs();
        Assert.Equal("Beta", ((Game)All<ListBox>().Single().SelectedItem!).Name);
        search.Text = ""; Click(Button("Installed")); Assert.Contains("No games here", Text);
        Click(Button("All Games")); Assert.Equal(2, All<ListBox>().Single().ItemCount);
        Click(Button("Favourite")); Click(Button("Favourites")); Assert.Single(All<ListBox>().Single().Items);
    }
    [AvaloniaFact] public async Task ChannelDialogShowsInstalledChannelAndCancellingDoesNotChangeIt()
    {
        var game = Game("Fixture Game");
        var settings = Settings.Load(); settings.For(game).Channel = "Nightly"; settings.Save();
        new Installation(game.Root).Install("ReShade", "Nightly 2026-09-13", [new("dxgi.dll", Encoding.UTF8.GetBytes("payload"))], proxy: "dxgi.dll");
        await Open(game); Click(All<Button>().Single(b => b.Name == "ReShadeSettings"));
        var dialog = Assert.Single(_window!.OwnedWindows);
        var text = string.Join("\n", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("Installed: Nightly", text); Assert.Contains("black screen", text);
        var picker = dialog.GetLogicalDescendants().OfType<ComboBox>().Single(); picker.SelectedItem = "Stable"; dialog.Close();
        Assert.Equal("Nightly", Settings.Load().For(game).Channel);
        Assert.Contains("Nightly", Text);
    }
    [AvaloniaFact] public async Task MissingPayloadIsNotShownAsInstalledOrApplied()
    {
        var game = Game("Missing Plugin");
        new Installation(game.Root).Install("ReShade", "Nightly", [new("dxgi.dll", Encoding.UTF8.GetBytes("payload"))], proxy: "dxgi.dll");
        File.Delete(Path.Combine(game.Root, "dxgi.dll")); await Open(game);
        Assert.Contains("Needs repair", Text); Assert.DoesNotContain("Applied ·", Text); Assert.Contains("Installation needs attention", Text);
    }
}
