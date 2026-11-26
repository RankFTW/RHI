using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// The log's privacy filter. Users post their logs publicly, so a mistake here would put
/// Windows user names and Steam account ids into them.
/// </summary>
public class LogSanitiserTests
{
    [Theory]
    [InlineData(@"C:\Users\SomeName\AppData\x", @"%USERPROFILE%\AppData\x")]
    [InlineData(@"c:\users\Some Name\Documents\file.ini", @"%USERPROFILE%\Documents\file.ini")]
    [InlineData(@"Copied C:\Users\A\one.dll to C:\Users\B\two.dll", @"Copied %USERPROFILE%\one.dll to %USERPROFILE%\two.dll")]
    public void UserProfile_IsReplaced(string input, string expected)
        => Assert.Equal(expected, CrashReporter.SanitisePath(input));

    [Theory]
    [InlineData(@"D:\Steam\userdata\12345678\760\remote", @"D:\Steam\userdata\[userid]\760\remote")]
    [InlineData(@"D:\Steam\UserData\42\config\grid", @"D:\Steam\UserData\[userid]\config\grid")]
    public void SteamUserId_IsReplaced(string input, string expected)
        => Assert.Equal(expected, CrashReporter.SanitisePath(input));

    [Fact]
    public void WindowsAppsPackage_LosesVersionAndPublisher()
        => Assert.Equal(
            @"C:\Program Files\WindowsApps\Microsoft.Game_[ver]\x",
            CrashReporter.SanitisePath(@"C:\Program Files\WindowsApps\Microsoft.Game_1.2.3.0_x64__8wekyb3d8bbwe\x"));

    [Theory]
    [InlineData("")]
    [InlineData("[BuildCards] 109 games, nothing to hide")]
    [InlineData("https://github.com/Users/someone/repo")]
    [InlineData(@"D:\Games\Some Game\reshade.ini")]
    public void OtherText_IsUnchanged(string input)
        => Assert.Equal(input, CrashReporter.SanitisePath(input));
}
