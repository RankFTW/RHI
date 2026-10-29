using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RenoDXCommander.Services;
using RHI.Linux.Core;
using System.Text;
using Xunit;

namespace RHI.Linux.UiTests;

public sealed class OptiScalerUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-ui-os-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME"), _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    private MainWindow? _window;
    public OptiScalerUiTests()
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
    private Game Game(string name, MachineType machine = MachineType.x64)
    {
        var root = Path.Combine(_root, name); Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "Game.exe");
        var bytes = new byte[512]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; BitConverter.GetBytes(128).CopyTo(bytes, 0x3c);
        bytes[128] = (byte)'P'; bytes[129] = (byte)'E'; BitConverter.GetBytes((ushort)machine).CopyTo(bytes, 132);
        File.WriteAllBytes(exe, bytes);
        return new() { Name = name, Root = root, Executable = exe, Executables = [exe] };
    }
    private async Task Open(Game game)
    {
        _window = new MainWindow([game]); _window.Show();
        for (var i = 0; i < 100 && !Text.Contains("Components"); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
    }
    private IEnumerable<T> All<T>(ILogical? root = null) => (root ?? _window!).GetLogicalDescendants().OfType<T>();
    private string Text => string.Join("\n", All<TextBlock>().Select(t => t.Text));
    private T Named<T>(string name, ILogical? root = null) where T : Control => All<T>(root).Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static async Task Settle() { for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); } }

    [AvaloniaFact] public async Task ExtrasOffersOptiScalerForSixtyFourBitGamesOnly()
    {
        await Open(Game("Modern"));
        var extras = Named<Border>("Extras");
        Assert.Contains("OptiScaler", Text);
        var install = Named<Button>("InstallOptiScaler", extras);
        Assert.Equal("↓  Install OptiScaler", install.Content); Assert.True(install.IsEnabled);
        Assert.DoesNotContain(All<Button>(extras), b => b.Name == "RemoveOptiScaler");
        // Extras follows the Nvidia Profile Overrides section, as on Windows.
        var sections = All<Border>().Select(b => b.Name).ToList();
        Assert.True(sections.IndexOf("NvidiaProfile") < sections.IndexOf("Extras"));
        _window!.Close(); _window = null;

        await Open(Game("Old", MachineType.I386));
        var old = Named<Button>("InstallOptiScaler", Named<Border>("Extras"));
        Assert.Equal("64-bit games only", old.Content); Assert.False(old.IsEnabled);
    }

    [AvaloniaFact] public async Task InstalledOptiScalerShowsUpdateAndWritesSettings()
    {
        var game = Game("Installed");
        var dir = game.InstallDirectory;
        new Installation(dir).Install(OptiScaler.Component, "v0.9.3", [new("dxgi.dll", Encoding.UTF8.GetBytes("optiscaler"))]);
        LinuxPaths.WriteJson(Path.Combine(dir, ".rhi-linux", "optiscaler.json"), new OptiScalerRecord { Version = "v0.9.3", Ini = true });
        File.WriteAllText(Path.Combine(dir, "OptiScaler.ini"), "[Upscalers]\nDx12Upscaler=auto\n[Framerate]\nFramerateLimit=0\n");
        File.WriteAllText(Path.Combine(dir, "OptiScaler.ini.original"), "");
        LinuxPaths.WriteJson(Path.Combine(_root, "cache/rhi-linux/optiscaler/latest.json"), new Dictionary<string, NrRelease> { ["Stable"] = new("v0.9.4", "https://example.invalid/os.7z") });
        await Open(game);
        var extras = Named<Border>("Extras");
        var install = Named<Button>("InstallOptiScaler", extras);
        Assert.Equal("⬆  Update OptiScaler", install.Content); Assert.Contains("update", install.Classes);
        Assert.NotNull(Named<Button>("RemoveOptiScaler", extras));
        Assert.Contains("Stable · loads as dxgi.dll", Text);
        Assert.Contains("v0.9.3", Text);

        Click(All<Button>(extras).Single(b => b.Content as string == "⚙"));
        await Settle();
        var dialog = Assert.Single(_window!.OwnedWindows);
        Assert.Contains("Frame generation, DLSS preset and render scale settings are available with the Nightly and DLSS NR versions.",
            string.Join("\n", All<TextBlock>(dialog).Select(t => t.Text)));
        var fps = Named<ComboBox>("OsFramerate", dialog);
        fps.SelectedItem = "116 (120Hz VRR)";
        Named<ComboBox>("OsUpscalerApi", dialog).SelectedItem = "DX12"; Dispatcher.UIThread.RunJobs();
        Assert.Equal("Auto (Default)", Named<ComboBox>("OsUpscaler", dialog).SelectedItem);
        Named<ComboBox>("OsUpscaler", dialog).SelectedItem = "XeSS";
        Dispatcher.UIThread.RunJobs();
        var ini = File.ReadAllText(Path.Combine(dir, "OptiScaler.ini"));
        Assert.Equal("116.000000", OptiScaler.IniGet(ini, "Framerate", "FramerateLimit"));
        Assert.Equal("xess", OptiScaler.IniGet(ini, "Upscalers", "Dx12Upscaler"));
        Assert.Null(OptiScaler.IniGet(ini, "Upscalers", "Dx11Upscaler"));
        dialog.Close(); await Settle();
    }

    [AvaloniaFact] public async Task NightlySettingsAreGatedUntilInstalled()
    {
        var game = Game("Nightly");
        await Open(game);
        var settings = new Settings(); settings.For(game).OsVariant = OsVariant.Nightly;
        _window!.Close();
        LinuxPaths.WriteJson(Path.Combine(_root, "data/rhi-linux/settings.json"), settings);
        _window = null;
        await Open(game);
        Click(All<Button>(Named<Border>("Extras")).Single(b => b.Content as string == "⚙"));
        await Settle();
        var dialog = Assert.Single(_window!.OwnedWindows);
        Assert.True(Named<ComboBox>("OsVariant", dialog).IsEnabled);
        foreach (var name in new[] { "OsFgInput", "OsFgOutput", "OsSrPreset", "OsRenderScale", "OsDeployStreamline", "OsFramerate" })
            Assert.False(Named<ComboBox>(name, dialog).IsEnabled, name);
        Assert.Contains("Settings below are available once OptiScaler is installed.", string.Join("\n", All<TextBlock>(dialog).Select(t => t.Text)));
        dialog.Close(); await Settle();
    }
}
