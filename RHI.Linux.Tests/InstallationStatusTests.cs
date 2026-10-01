using System.Text;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;
public sealed class InstallationStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-status-" + Guid.NewGuid().ToString("N"));
    public InstallationStatusTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private Game Game => new() { Root = _root, Executable = Path.Combine(_root, "Game.exe") };
    private void Install(string content = "first") => new Installation(_root).Install("ReShade", "Nightly 2026-09-13", [new("dxgi.dll", Encoding.UTF8.GetBytes(content))], proxy: "dxgi.dll");
    [Fact] public void OrdinarySteamArgumentsAreKeptWithoutManualCommandEditing()
        => Assert.Equal("WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b' %command% -dx12 -nosplash", Proton.LaunchOptions("-dx12 -nosplash", "dxgi.dll"));
    [Theory]
    [InlineData("WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b' %command%", true)]
    [InlineData("WINEDLLOVERRIDES='dxgi,d3dcompiler_47=n' %command%", true)]
    [InlineData("WINEDLLOVERRIDES='dxgi=b,n;d3dcompiler_47=n,b' %command%", false)]
    [InlineData("WINEDLLOVERRIDES='dxgi=n,b' %command%", false)]
    [InlineData("WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b;dxgi=b' %command%", false)]
    [InlineData("WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b' game.exe", false)]
    [InlineData("%command%", false)]
    public void LaunchReadinessRequiresNativeOverridesAndSteamCommand(string options, bool expected)
        => Assert.Equal(expected, InstallationStatus.HasLaunchOverrides(options, "dxgi.dll"));
    [Fact] public void OldLogDoesNotClaimNewPayloadWasApplied()
    {
        Install(); var log = Path.Combine(_root, "ReShade.log");
        File.WriteAllText(log, "Game.exe Initializing crosire's ReShade Recreated runtime environment");
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddMinutes(-10));
        Assert.True(InstallationStatus.Read(Game).Get("ReShade").Installed);
        Assert.False(InstallationStatus.Read(Game).Get("ReShade").Applied);
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddSeconds(1));
        Assert.True(InstallationStatus.Read(Game).Get("ReShade").Applied);
        Install("update"); File.SetLastWriteTimeUtc(Path.Combine(_root, "dxgi.dll"), DateTime.UtcNow.AddSeconds(2));
        Assert.False(InstallationStatus.Read(Game).Get("ReShade").Applied);
    }
    [Fact] public void ChangedFileCannotClaimInstalledDespiteManifestAndLog()
    {
        Install(); File.WriteAllText(Path.Combine(_root, "ReShade.log"), "Game.exe Initializing crosire's ReShade Created runtime environment");
        File.WriteAllText(Path.Combine(_root, "dxgi.dll"), "outside modification");
        var state = InstallationStatus.Read(Game).Get("ReShade");
        Assert.True(state.Damaged); Assert.False(state.Installed); Assert.False(state.Applied);
    }
    [Fact] public void LogFromAnotherGameDoesNotClaimApplied()
    {
        Install(); File.WriteAllText(Path.Combine(_root, "ReShade.log"), "Other.exe Initializing crosire's ReShade Created runtime environment");
        Assert.False(InstallationStatus.Read(Game).Get("ReShade").Applied);
    }
    [Fact] public void SteamReadinessUsesTheSelectedAccount()
    {
        Install(); var game = Game; game.AppId = "42"; game.SteamRoot = _root;
        string Config(string id, string options)
        {
            var path = Path.Combine(_root, "userdata", id, "config", "localconfig.vdf"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "UserLocalConfigStore { Software { Valve { Steam { apps { 42 { LaunchOptions " + Vdf.Quote(options) + " } } } } } }");
            return path;
        }
        var configured = Config("1", "WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b' %command%");
        var other = Config("2", "");
        Assert.False(InstallationStatus.Read(game).LaunchConfigured);
        Assert.True(InstallationStatus.Read(game, configured).LaunchConfigured);
        Assert.False(InstallationStatus.Read(game, other).LaunchConfigured);
    }
    [Fact] public void ChannelComesFromInstalledPayloadRecord()
    {
        Install(); var preferences = new GamePreferences { Channel = "Stable" };
        Assert.Equal("Nightly", InstallationStatus.Read(Game).Get("ReShade").Channel);
        Assert.NotEqual(preferences.Channel, InstallationStatus.Read(Game).Get("ReShade").Channel);
    }
}
