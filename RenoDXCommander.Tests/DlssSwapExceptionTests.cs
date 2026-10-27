using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// The messages from DlssSwapException are shown to users when a DLSS swap fails,
/// so check each failure maps to the right plain-English explanation.
/// </summary>
public class DlssSwapExceptionTests
{
    private const string GamePassPath =
        @"C:\Program Files\WindowsApps\Microsoft.Fairlightbasegame_1.1.3.0_x64__8wekyb3d8bbwe\Engine\nvngx_dlssg.dll";
    private const string SteamPath = @"D:\SteamLibrary\steamapps\common\Some Game\nvngx_dlss.dll";

    [Theory]
    [InlineData(GamePassPath, true)]
    [InlineData(@"C:/Program Files/WindowsApps/Game_1.0_x64__abc/nvngx_dlss.dll", true)]
    [InlineData(SteamPath, false)]
    [InlineData(@"D:\XboxGames\Some Game\Content\nvngx_dlss.dll", false)]
    public void IsWindowsAppsPath_DetectsProtectedFolder(string path, bool expected)
        => Assert.Equal(expected, DlssSwapException.IsWindowsAppsPath(path));

    [Fact]
    public void MissingFolder_GamePass_SuggestsRefreshAfterUpdate()
    {
        var msg = DlssSwapException.Describe(GamePassPath, new DirectoryNotFoundException());
        Assert.Contains("Game Pass", msg);
        Assert.Contains("Refresh", msg);
    }

    [Fact]
    public void MissingFolder_Regular_SuggestsRefresh()
    {
        var msg = DlssSwapException.Describe(SteamPath, new DirectoryNotFoundException());
        Assert.Contains("no longer exists", msg);
        Assert.Contains("Refresh", msg);
    }

    [Fact]
    public void AccessDenied_GamePass_ExplainsProtectedFolder()
    {
        var msg = DlssSwapException.Describe(GamePassPath, new UnauthorizedAccessException());
        Assert.Contains("WindowsApps", msg);
    }

    [Fact]
    public void AccessDenied_Regular_SuggestsAdmin()
    {
        var msg = DlssSwapException.Describe(SteamPath, new UnauthorizedAccessException());
        Assert.Contains("administrator", msg);
    }

    [Fact]
    public void FileInUse_SuggestsClosingGame()
    {
        var msg = DlssSwapException.Describe(SteamPath, new IOException("The process cannot access the file"));
        Assert.Contains("Close the game", msg);
    }

    [Fact]
    public void From_KeepsOriginalExceptionAndPath()
    {
        var inner = new DirectoryNotFoundException("raw");
        var ex = DlssSwapException.From(SteamPath, inner);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal(SteamPath, ex.TargetPath);
    }
}
