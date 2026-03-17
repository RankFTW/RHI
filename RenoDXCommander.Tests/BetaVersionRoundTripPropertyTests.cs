using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for RdxcVersion parse/format round-trip.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class BetaVersionRoundTripPropertyTests
{
    /// <summary>
    /// Generator for random RdxcVersion values:
    /// Major 0–99, Minor 0–99, Build 0–99, optional BetaNumber 1–20.
    /// </summary>
    private static Gen<RdxcVersion> GenRdxcVersion()
    {
        var genMajor = Gen.Choose(0, 99);
        var genMinor = Gen.Choose(0, 99);
        var genBuild = Gen.Choose(0, 99);
        var genBeta = Gen.Frequency(
            Tuple.Create(1, Gen.Constant((int?)null)),
            Tuple.Create(1, Gen.Choose(1, 20).Select(n => (int?)n)));

        return from major in genMajor
               from minor in genMinor
               from build in genBuild
               from beta in genBeta
               select new RdxcVersion(major, minor, build, beta);
    }

    // Feature: beta-opt-in, Property 5: Version parse/format round-trip
    /// <summary>
    /// **Validates: Requirements 4.1, 4.2, 4.4, 5.4**
    ///
    /// For any valid RdxcVersion (with or without beta suffix), calling ToDisplayString()
    /// then RdxcVersion.TryParse() on the result should produce an RdxcVersion equal to the original.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VersionRoundTrip_ParseOfDisplayStringEqualsOriginal()
    {
        return Prop.ForAll(GenRdxcVersion().ToArbitrary(), version =>
        {
            var display = version.ToDisplayString();
            var parsed = RdxcVersion.TryParse(display, out var result);

            return (parsed && result == version)
                .Label($"Original={version}, Display='{display}', Parsed={parsed}, Result={result}");
        });
    }

    // ── Unit tests for RdxcVersion parsing edge cases ──
    // Validates: Requirements 4.1, 4.2, 4.3

    [Theory]
    [InlineData("1.4.8", 1, 4, 8, null)]
    [InlineData("1.4.8-beta1", 1, 4, 8, 1)]
    [InlineData("1.4.8 beta 1", 1, 4, 8, 1)]
    [InlineData("v1.4.8-beta2", 1, 4, 8, 2)]
    [InlineData("RDXC-1.4.8-beta1", 1, 4, 8, 1)]
    public void TryParse_ValidInput_ReturnsExpectedVersion(
        string input, int major, int minor, int build, int? beta)
    {
        var success = RdxcVersion.TryParse(input, out var version);

        Assert.True(success, $"TryParse should succeed for '{input}'");
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
        Assert.Equal(beta, version.BetaNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("beta")]
    [InlineData("1.2")]
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        var success = RdxcVersion.TryParse(input, out _);

        Assert.False(success, $"TryParse should fail for '{input}'");
    }
}
