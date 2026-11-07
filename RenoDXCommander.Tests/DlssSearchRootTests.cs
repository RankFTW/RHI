using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// DLSS/Streamline search root for Unreal games whose exe runs outside {ContentFolder}\Binaries
/// (Phantom Blade Zero: {GameRoot}\platform\steam, DLLs under {GameRoot}\Engine\Plugins).
/// </summary>
public class DlssSearchRootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-dlssroot-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; left for the OS to clean */ }
    }

    [Fact]
    public void Platform_subfolder_searches_from_the_unreal_game_root()
    {
        var game = Path.Combine(_root, "PBZ");
        var steam = Path.Combine(game, "platform", "steam");
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(Path.Combine(game, "Engine", "Plugins", "Runtime", "Nvidia", "DLSS"));

        Assert.Equal(game, DlssStreamlineService.ResolveSearchRoot(steam));
    }

    [Fact]
    public void Folder_without_engine_above_is_left_alone()
    {
        var game = Path.Combine(_root, "SomeGame", "bin64");
        Directory.CreateDirectory(game);

        Assert.Equal(game, DlssStreamlineService.ResolveSearchRoot(game));
    }

    [Fact]
    public void Game_root_with_its_own_engine_folder_stays_itself()
    {
        var game = Path.Combine(_root, "UeGame");
        Directory.CreateDirectory(Path.Combine(game, "Engine", "Binaries"));

        Assert.Equal(game, DlssStreamlineService.ResolveSearchRoot(game));
    }
}
