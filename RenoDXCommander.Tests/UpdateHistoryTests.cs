using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Which version changes count as updates in the Update History window.</summary>
public class UpdateHistoryTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("v0.9.7", "v0.9.8", true)]
    [InlineData(null, "1.0.0", false)]        // first download
    [InlineData("", "1.0.0", false)]          // first download
    [InlineData("1.0.0", null, false)]
    [InlineData("1.0.0", "", false)]
    [InlineData("1.0.0", "unknown", false)]
    [InlineData("1.0.0", "Unknown", false)]
    [InlineData("1.0.0", "1.0.0", false)]     // re-download of the same version
    [InlineData("V1.0.0", "v1.0.0", false)]   // case-insensitive
    [InlineData(" 1.0.0 ", "1.0.0", false)]   // whitespace from version.txt
    public void ShouldRecord_OnlyRealUpdates(string? oldVersion, string? newVersion, bool expected)
        => Assert.Equal(expected, UpdateHistory.ShouldRecord(oldVersion, newVersion));

    [Fact]
    public void Record_WithoutDiContainer_DoesNotThrow()
    {
        var ex = Record.Exception(() => UpdateHistory.Record("Test", "Test", "1.0", "2.0"));
        Assert.Null(ex);
    }
}
