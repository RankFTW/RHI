using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>RHI overlay screenshots: reshade.ini SavePath and which files belong to a game.</summary>
public class ScreenshotLibraryTests
{
    [Fact]
    public void ReadsSavePathFromTheScreenshotSection() =>
        Assert.Equal(@"D:\Shots\Game", ScreenshotLibraryService.ParseSavePath(new[]
        {
            "[GENERAL]", "SavePath=wrong", "", "[SCREENSHOT]", "ClearAlpha=1", @"SavePath=D:\Shots\Game", "[OVERLAY]",
        }));

    [Fact]
    public void NoSavePathWithoutTheSection() =>
        Assert.Null(ScreenshotLibraryService.ParseSavePath(new[] { "[GENERAL]", "SavePath=x" }));

    [Theory]
    [InlineData("Game 2026-11-08 21-30-00.png", "Game", true)]
    [InlineData("game_2026.png", "Game", true)]
    [InlineData("Game.png", "Game", true)]
    [InlineData("GameOfThrones 2026.png", "Game", false)]
    [InlineData("Other 2026.png", "Game", false)]
    public void MatchesFilesByExeName(string file, string exe, bool expected) =>
        Assert.Equal(expected, ScreenshotLibraryService.MatchesExe(file, exe));

    [Fact]
    public void SharedFolderShowsOnlyThisGamesFiles()
    {
        var files = new List<FileInfo> { new("C:\\s\\Game 1.png"), new("C:\\s\\Other 1.png") };
        var result = ScreenshotLibraryService.Filter(files, @"C:\Games\Game.exe", folderIsGamesOwn: false).ToList();
        Assert.Single(result);
        Assert.Equal("Game 1.png", result[0].Name);
    }

    [Fact]
    public void SharedFolderWithNoneOfThisGamesFilesShowsNothing()
    {
        var files = new List<FileInfo> { new("C:\\s\\Other 1.png") };
        Assert.Empty(ScreenshotLibraryService.Filter(files, @"C:\Games\Game.exe", folderIsGamesOwn: false));
    }

    [Fact]
    public void GamesOwnFolderShowsEverything()
    {
        var files = new List<FileInfo> { new("C:\\s\\renamed shot.png") };
        Assert.Single(ScreenshotLibraryService.Filter(files, @"C:\Games\Game.exe", folderIsGamesOwn: true));
    }

    [Fact]
    public void CustomFolderWinsWhenItExistsAndIsSkippedWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "rhi-shots-" + Guid.NewGuid().ToString("N"));
        var install = Directory.CreateDirectory(Path.Combine(root, "Game")).FullName;
        var custom = Directory.CreateDirectory(Path.Combine(root, "Steam shots")).FullName;
        try
        {
            Assert.Equal(custom, ScreenshotLibraryService.FindFolder(install, null, custom), ignoreCase: true);
            Assert.Equal(install, ScreenshotLibraryService.FindFolder(install, null, Path.Combine(root, "Gone")), ignoreCase: true);
            Assert.Equal(install, ScreenshotLibraryService.FindFolder(install, null), ignoreCase: true);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
