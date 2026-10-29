using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RHI.Linux.Core;
using System.Text;
using Xunit;

namespace RHI.Linux.UiTests;

public sealed class NeuralRenderingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-ui-nr-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME"), _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    private MainWindow? _window;
    public NeuralRenderingTests()
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
    private Game Game(string name, bool dlss)
    {
        var root = Path.Combine(_root, name); var bin = Path.Combine(root, "Binaries"); Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, "Game.exe"); File.WriteAllText(exe, "test game");
        if (dlss)
        {
            Directory.CreateDirectory(Path.Combine(root, "Engine/Plugins"));
            File.WriteAllText(Path.Combine(root, "Engine/Plugins/nvngx_dlss.dll"), "sr"); File.WriteAllText(Path.Combine(bin, "nvngx_dlssg.dll"), "fg");
        }
        return new() { Name = name, Root = root, Executable = exe, Executables = [exe] };
    }
    private async Task Open(params Game[] games)
    {
        _window = new MainWindow(games); _window.Show();
        // DLSS detection runs in the background; wait for both sections to finish loading.
        for (var i = 0; i < 300 && (!Text.Contains("Nvidia Profile Overrides") || Text.Contains("Checking this game's DLSS files")); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
    }
    private IEnumerable<T> All<T>() => _window!.GetLogicalDescendants().OfType<T>();
    private string Text => string.Join("\n", All<TextBlock>().Select(t => t.Text));
    private T Named<T>(string name) where T : Control => All<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static async Task Settle() { for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); } }

    [AvaloniaFact] public async Task SectionsShowDetectedDlssAndRecommendTheWindowsMethod()
    {
        await Open(Game("Native DLSS", dlss: true));
        Assert.Contains("Neural Rendering", Text); Assert.Contains("DLSS Super Resolution", Text); Assert.Contains("Frame Generation", Text);
        var method = Named<ComboBox>("NrMethod");
        Assert.Equal(NrMethod.ShortFuse, (method.SelectedItem as ComboBoxItem)?.Tag);
        Assert.Equal("Install Neural Rendering", Named<Button>("InstallNeuralRendering").Content);
        Assert.Contains("✓ DLSS SR", Text); Assert.Contains("✗ NR DLL", Text);
        Assert.True(Named<Button>("MultiFrameGen").IsEnabled);
        Assert.True(Named<ComboBox>("DlssVersionRR").IsEnabled); // NVIDIA Override remains available
        Assert.False(Named<ComboBox>("DlssVersionNR").IsEnabled);
        Assert.DoesNotContain(All<Button>(), b => b.Name == "RemoveNeuralRendering");
    }

    [AvaloniaFact] public async Task GamesWithoutDlssDefaultToTheFeeder()
    {
        await Open(Game("No DLSS", dlss: false));
        Assert.Equal(NrMethod.Feeder, (Named<ComboBox>("NrMethod").SelectedItem as ComboBoxItem)?.Tag);
        Assert.Equal("Install Feeder Addon", Named<Button>("InstallNeuralRendering").Content);
        Assert.Contains("No DLSS or Streamline DLLs were found", Text);
        var items = Named<ComboBox>("NrMethod").Items.OfType<ComboBoxItem>().ToList();
        Assert.False(items.Single(i => (string?)i.Tag == NrMethod.Dlss5Tool).IsEnabled);
    }

    [AvaloniaFact] public async Task PresetChangesAreSavedAndAskForLaunchSettings()
    {
        var game = Game("Preset Game", dlss: true);
        await Open(game);
        var preset = Named<ComboBox>("DlssPresetSR");
        preset.SelectedIndex = Array.FindIndex(DlssProfile.SrPresets, p => p.Name == "K - TF1");
        await Settle();
        var prefs = Settings.Load().For(game);
        Assert.Equal(0x0Bu, DlssProfile.Preset(prefs, DlssKind.SR));
        Assert.Contains("Launch settings need updating", Text);
        Assert.NotNull(Named<Button>("ApplyDlssLaunch"));
    }

    [AvaloniaFact] public async Task SectionsCollapseAndRememberIt()
    {
        await Open(Game("Collapse Game", dlss: true));
        var header = All<StackPanel>().Single(p => p.Name == "NeuralRenderingHeader");
        header.RaiseEvent(new PointerPressedEventArgs(header, new Pointer(0, PointerType.Mouse, true), _window!, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("NeuralRendering", Settings.Load().CollapsedSections);
        Assert.False(Named<Button>("InstallNeuralRendering").IsEffectivelyVisible);
    }

    [AvaloniaFact] public async Task NeuralRenderingPlacedByWindowsCanBeRemoved()
    {
        var game = Game("Shared Library", dlss: false);
        var bin = Path.GetDirectoryName(game.Executable)!;
        File.WriteAllText(Path.Combine(bin, "renodx-dlss5.addon64"), "addon");
        File.WriteAllText(Path.Combine(bin, "dlss5-feed.addon64"), "feeder");
        File.WriteAllText(Path.Combine(bin, "nvngx_dlssnr.dll"), "nr"); File.WriteAllBytes(Path.Combine(bin, "nvngx_dlssnr.dll.original"), []);
        await Open(game);
        Assert.Equal(NrMethod.Feeder, (Named<ComboBox>("NrMethod").SelectedItem as ComboBoxItem)?.Tag);
        Assert.Contains("✓ Feeder Addon", Text);
        Click(Named<Button>("RemoveNeuralRendering"));
        var dialog = Assert.Single(_window!.OwnedWindows);
        Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Remove"));
        // Removal runs in the background; wait until the whole operation has reported completion.
        for (var i = 0; i < 500 && !Text.Contains("Neural Rendering removed"); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.False(File.Exists(Path.Combine(bin, "dlss5-feed.addon64")));
        Assert.False(File.Exists(Path.Combine(bin, "nvngx_dlssnr.dll")));
        Assert.False(File.Exists(Path.Combine(bin, "nvngx_dlssnr.dll.original")));
    }
}
