using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Guards against archive entries escaping the target folder and the GitHub token
/// being sent to non-GitHub hosts.
/// </summary>
public class SecurityGuardTests
{
    private const string Base = @"C:\Games\Some Game";

    [Theory]
    [InlineData("dxgi.dll", @"C:\Games\Some Game\dxgi.dll")]
    [InlineData("Luma/Luma.addon64", @"C:\Games\Some Game\Luma\Luma.addon64")]
    [InlineData(@"bin\x64\file.ini", @"C:\Games\Some Game\bin\x64\file.ini")]
    [InlineData("sub/../file.txt", @"C:\Games\Some Game\file.txt")]
    public void SafeCombine_InsideBase_ReturnsFullPath(string rel, string expected)
    {
        Assert.Equal(expected, FileHelper.SafeCombine(Base, rel));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData(@"..\..\Windows\System32\evil.dll")]
    [InlineData("a/../../evil.dll")]
    [InlineData(@"C:\Windows\evil.dll")]
    [InlineData(@"\Windows\evil.dll")]
    [InlineData(@"C:evil.dll")]
    [InlineData(@"\\server\share\evil.dll")]
    [InlineData("../Some Game2/evil.dll")] // sibling folder sharing the base name prefix
    [InlineData("")]
    [InlineData(".")]
    public void SafeCombine_Escaping_ReturnsNull(string rel)
    {
        Assert.Null(FileHelper.SafeCombine(Base, rel));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/a/b/releases", true)]
    [InlineData("https://github.com/a/b/releases/download/v1/x.zip", false)]   // download hosts: no token
    [InlineData("https://raw.githubusercontent.com/a/b/main/x.json", false)]  // (404 with an unaccepted token)
    [InlineData("https://codeload.github.com/a/b/zip/refs/heads/main", false)]
    [InlineData("http://api.github.com/repos/a/b", false)]          // not HTTPS
    [InlineData("https://api.github.com.evil.com/x", false)]        // lookalike host
    [InlineData("https://evilgithub.com/x", false)]
    [InlineData("https://objects.githubusercontent.com/x", false)]  // release CDN needs no token
    [InlineData("https://codeberg.org/a/b", false)]
    [InlineData("https://reshade.me/downloads/x.exe", false)]
    public void GitHubTokenHandler_OnlyGitHubHosts(string url, bool expected)
    {
        Assert.Equal(expected, GitHubTokenHandler.IsGitHubHost(new Uri(url)));
    }
}
