using System.Text;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class REFrameworkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-ref-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    private readonly string? _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    public REFrameworkTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_root, "cache"));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldData);
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _oldCache);
        Directory.Delete(_root, true);
    }
    private Game Game(string name)
    {
        var root = Path.Combine(_root, name); Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "re9.exe"); File.WriteAllText(exe, "game");
        return new() { Name = name, Root = root, Executable = exe, Executables = [exe] };
    }

    [Fact] public void DetectsRePakAndNightlyBuildNumbers()
    {
        var re = Game("Resident Evil"); File.WriteAllText(Path.Combine(re.Root, "RE_CHUNK_000.PAK"), "");
        var nested = Game("Nested"); Directory.CreateDirectory(Path.Combine(nested.Root, "a/b")); File.WriteAllText(Path.Combine(nested.Root, "a/b/re_chunk_000.pak"), "");
        Assert.True(re.IsREEngine); Assert.True(nested.IsREEngine); Assert.False(Game("Unreal").IsREEngine);
        Assert.Equal("01424", REFramework.VersionNumber("nightly-01424-d1461375aee4ec3f313170f8eaad12064eb542d9"));
        Assert.Equal("custom", REFramework.VersionNumber("custom"));
    }

    [Fact] public void ManagedInstallAddsDinput8OverrideAndUpdateState()
    {
        var game = Game("Managed");
        Assert.DoesNotContain("dinput8", GameLaunch.Extras(game, new()).Dlls);
        new Installation(game.InstallDirectory).Install(REFramework.Component, "01400", [new(REFramework.Dll, Encoding.UTF8.GetBytes("ref"))]);
        var extras = GameLaunch.Extras(game, new());
        Assert.Contains("dinput8", extras.Dlls);
        var options = Proton.LaunchOptions("%command%", "dxgi.dll", extras);
        Assert.Contains("dinput8=n,b", options);
        Assert.True(InstallationStatus.HasLaunchOverrides(options, "dxgi.dll", extras));
        Assert.False(InstallationStatus.HasLaunchOverrides("WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b' %command%", "dxgi.dll", extras));
        Assert.False(REFramework.FromWindows(game));
    }

    [Fact] public async Task RemovesRefPlacedByWindowsRhiRestoringTheOriginal()
    {
        var game = Game("Shared");
        File.WriteAllText(Path.Combine(game.Root, "dinput8.dll"), "reframework"); File.WriteAllText(Path.Combine(game.Root, "dinput8.dll.original"), "game dinput8");
        Assert.True(REFramework.FromWindows(game)); Assert.True(REFramework.Installed(game));
        Assert.Contains("dinput8", GameLaunch.Extras(game, new()).Dlls);
        await REFramework.Remove(game);
        Assert.Equal("game dinput8", File.ReadAllText(Path.Combine(game.Root, "dinput8.dll")));
        Assert.False(REFramework.Installed(game));
    }
}
