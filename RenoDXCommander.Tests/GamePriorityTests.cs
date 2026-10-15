using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Tests for how GamePriorityService decides which processes belong to a launched game.
/// Pure path logic only — no processes are touched.
/// </summary>
public class GamePriorityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\")]
    [InlineData("D:\\")]
    public void NormalizeGameFolder_MissingOrDriveRoot_ReturnsNull(string? installPath)
    {
        Assert.Null(GamePriorityService.NormalizeGameFolder(installPath));
    }

    [Theory]
    [InlineData("C:\\Games\\Foo")]
    [InlineData("C:\\Games\\Foo\\")]
    [InlineData("C:\\Games\\Bar\\..\\Foo")]
    public void NormalizeGameFolder_GameFolder_EndsWithSeparator(string installPath)
    {
        Assert.Equal("C:\\Games\\Foo\\", GamePriorityService.NormalizeGameFolder(installPath));
    }

    [Theory]
    [InlineData("C:\\Games\\Foo\\foo.exe", true)]
    [InlineData("C:\\Games\\Foo\\bin\\x64\\foo.exe", true)]
    [InlineData("c:\\games\\foo\\FOO.EXE", true)]
    [InlineData("C:\\Games\\FooBar\\foo.exe", false)] // sibling folder sharing the name as a prefix
    [InlineData("C:\\Games\\foo.exe", false)]
    [InlineData("C:\\Program Files (x86)\\Steam\\steam.exe", false)]
    public void IsInFolder_MatchesOnlyProcessesInsideTheGameFolder(string exePath, bool expected)
    {
        var folder = GamePriorityService.NormalizeGameFolder("C:\\Games\\Foo")!;
        Assert.Equal(expected, GamePriorityService.IsInFolder(exePath, folder));
    }
}
