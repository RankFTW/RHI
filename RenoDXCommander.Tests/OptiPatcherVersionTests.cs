using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>OptiPatcher's version is the build commit in its rolling release notes.</summary>
public class OptiPatcherVersionTests
{
    [Theory]
    // Since Nov 2026 the notes carry a literal "^G" before the hash
    [InlineData("## Rolling Release\r\n\r\n**Base version:** v0.42 \r\n**Build commit:** ^Gc49dee353d303d423226a0073a03bbd4814bf46 \r\n**Build date:** 2026-11-11", "c49dee353d303d423226a0073a03bbd4814bf46")]
    [InlineData("**Build commit:** C49DEE3\r\n", "c49dee3")]
    [InlineData("Build commit: abc1234", "abc1234")]
    public void ReadsBuildCommit(string body, string expected)
    {
        Assert.Equal(expected, OptiScalerService.ParseOptiPatcherCommit(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("## Rolling Release\r\n**Base version:** v0.42")]
    [InlineData("**Build commit:**\r\n**Build date:** 2026-11-11")]
    public void NoCommit_ReturnsNull(string body)
    {
        Assert.Null(OptiScalerService.ParseOptiPatcherCommit(body));
    }
}
