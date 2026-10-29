using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class SteamPackagingTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "rhi-steam-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("snap/steam/common/.local/share/Steam", "Steam Snap", "snap", "run steam")]
    [InlineData(".var/app/com.valvesoftware.Steam/.local/share/Steam", "Steam Flatpak", "flatpak", "run com.valvesoftware.Steam")]
    [InlineData(".local/share/Steam", "Steam", "steam", "")]
    public void DiscoveryAndRestartUseOwningClientForExternalLibraries(string location, string source, string launcher, string prefix)
    {
        var steam = Path.Combine(_home, location);
        var library = Path.Combine(_home, "External games 100%");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(library, "steamapps/common/Game"));
        File.WriteAllText(Path.Combine(steam, "steamapps/libraryfolders.vdf"),
            $"libraryfolders {{ 0 {{ path {Vdf.Quote(library)} }} }}");
        File.WriteAllText(Path.Combine(library, "steamapps/appmanifest_42.acf"),
            "AppState { appid 42 name Game installdir Game }");
        File.WriteAllText(Path.Combine(library, "steamapps/common/Game/Game.exe"), "fixture");

        // Restrict roots to the disposable home so the developer's Steam is never inspected.
        var roots = GameDiscovery.DefaultSteamRoots(_home).Where(p => p.StartsWith(_home + "/", StringComparison.Ordinal));
        var game = Assert.Single(new GameDiscovery().Scan(roots));
        Assert.Equal(steam, game.SteamRoot);
        Assert.Equal(source, game.Source);
        Assert.Equal(Path.Combine(library, "steamapps/common/Game"), game.Root);
        foreach (var shutdown in new[] { true, false })
        {
            var command = GameSetup.SteamStartInfo(game, shutdown);
            Assert.Equal(launcher, command.FileName);
            Assert.False(command.UseShellExecute);
            var expected = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Append(shutdown ? "-shutdown" : "-silent");
            Assert.Equal(expected, command.ArgumentList);
        }
    }

    [Theory]
    [InlineData("/home/a/.var/app/com.valvesoftware.Steam-copy/Steam")]
    [InlineData("/home/a/snap/steam-backup/common/Steam")]
    public void SimilarlyNamedNativeFoldersDoNotSelectSandboxLaunchers(string root)
    {
        Assert.Equal("Steam", GameDiscovery.SteamSource(root));
        var command = GameSetup.SteamStartInfo(new Game { Name = "Fixture", Root = "/games", SteamRoot = root }, true);
        Assert.Equal("steam", command.FileName);
        Assert.Equal(new[] { "-shutdown" }, command.ArgumentList);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, true);
    }
}
