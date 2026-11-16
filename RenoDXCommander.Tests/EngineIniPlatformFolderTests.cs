using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Engine.ini platform folder by store (issue #138): a game played from Steam and Game Pass has both
/// Saved\Config\Windows and Saved\Config\WinGDK, and each copy must get its own.
/// </summary>
public class EngineIniPlatformFolderTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "rhi-engineini-" + Guid.NewGuid().ToString("N"));

    public EngineIniPlatformFolderTests() => Directory.CreateDirectory(_base);
    public void Dispose() { try { Directory.Delete(_base, true); } catch (IOException) { } }

    private void Make(params string[] folders) { foreach (var f in folders) Directory.CreateDirectory(Path.Combine(_base, f)); }

    [Theory]
    [InlineData("Steam")]
    [InlineData("Epic")]
    [InlineData("")]
    public void OtherStores_PreferWindows_WhenBothExist(string store)
    {
        Make("WinGDK", "Windows");
        Assert.Equal(Path.Combine(_base, "Windows"), AuxInstallService.FindPlatformConfigDir(_base, store));
    }

    [Theory]
    [InlineData("Xbox")]
    [InlineData("Game Pass")]
    public void GamePass_PrefersWinGdk_WhenBothExist(string store)
    {
        Make("WinGDK", "Windows");
        Assert.Equal(Path.Combine(_base, "WinGDK"), AuxInstallService.FindPlatformConfigDir(_base, store));
    }

    [Fact]
    public void GamePass_NeverTakesTheSteamCopysWindowsFolder()
    {
        Make("Windows"); // Steam demo played, Game Pass copy not launched yet
        Assert.Null(AuxInstallService.FindPlatformConfigDir(_base, "Xbox"));
    }

    [Fact]
    public void OtherStores_FallBackToWinGdk_WhenItsTheOnlyOne()
    {
        Make("WinGDK");
        Assert.Equal(Path.Combine(_base, "WinGDK"), AuxInstallService.FindPlatformConfigDir(_base, "Steam"));
    }

    [Fact]
    public void OtherStores_UseWindowsNoEditor_BeforeWinGdk()
    {
        Make("WinGDK", "WindowsNoEditor");
        Assert.Equal(Path.Combine(_base, "WindowsNoEditor"), AuxInstallService.FindPlatformConfigDir(_base, "Steam"));
    }
}
