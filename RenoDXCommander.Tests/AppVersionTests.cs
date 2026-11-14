using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// RHI's own version rules (Services/AppVersion.cs, docs/beta-channel.md): betas keep the release's
/// number and carry the beta as a label; the 4th number is a real (hotfix) version.
/// </summary>
public class AppVersionTests
{
    private static RdxcVersion V(string s)
    {
        Assert.True(RdxcVersion.TryParse(s, out var v), $"could not parse '{s}'");
        return v;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.9.5", 2, 9, 5, 0, null)]
    [InlineData("RHI-2.9.5", 2, 9, 5, 0, null)]
    [InlineData("RHI 2.9.5", 2, 9, 5, 0, null)]
    [InlineData("v2.9.5.1", 2, 9, 5, 1, null)]
    [InlineData("RHI-2.9.5.1", 2, 9, 5, 1, null)]
    [InlineData("RHI 2.9.5 Beta 1", 2, 9, 5, 0, 1)]
    [InlineData("RHI-2.9.5-beta-2", 2, 9, 5, 0, 2)]
    [InlineData("RHI-2.9.5-beta.3", 2, 9, 5, 0, 3)]
    [InlineData("2.9.5-beta4", 2, 9, 5, 0, 4)]
    [InlineData("2.9.5.0-beta.1", 2, 9, 5, 0, 1)]
    [InlineData("2.10.0 beta 12", 2, 10, 0, 0, 12)]
    public void TryParse_ReadsNumbersAndBeta(string input, int major, int minor, int build, int revision, int? beta)
    {
        var v = V(input);
        Assert.Equal(new RdxcVersion(major, minor, build, revision, beta), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("no version here")]
    public void TryParse_RejectsTextWithoutAVersion(string? input)
    {
        Assert.False(RdxcVersion.TryParse(input, out _));
    }

    // ── Display ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.9.5", "2.9.5", "2.9.5")]
    [InlineData("2.9.5.1", "2.9.5.1", "2.9.5.1")]
    [InlineData("2.9.5-beta.1", "2.9.5 beta 1", "2.9.5-beta.1")]
    [InlineData("2.9.5.0-beta.2", "2.9.5 beta 2", "2.9.5-beta.2")]
    public void DisplayAndTagStrings(string input, string display, string tag)
    {
        var v = V(input);
        Assert.Equal(display, v.ToDisplayString());
        Assert.Equal(tag, v.ToTagString());
    }

    // ── Order: 2.9.4 < 2.9.5 beta 1 < 2.9.5 beta 2 < 2.9.5 < 2.9.5.1 < 2.9.6 beta 1 ──

    [Fact]
    public void Ordering_BetasBeforeTheirRelease_HotfixAfter()
    {
        string[] ascending = { "2.9.4", "2.9.4.1", "2.9.5 beta 1", "2.9.5 beta 2", "2.9.5 beta 10", "2.9.5", "2.9.5.1", "2.9.6 beta 1", "2.10.0" };
        for (int i = 0; i < ascending.Length - 1; i++)
        {
            var lower = V(ascending[i]);
            var higher = V(ascending[i + 1]);
            Assert.True(lower < higher, $"{ascending[i]} should be older than {ascending[i + 1]}");
            Assert.True(higher > lower, $"{ascending[i + 1]} should be newer than {ascending[i]}");
        }
    }

    [Fact]
    public void Ordering_SameVersionIsEqual()
    {
        Assert.Equal(0, V("RHI 2.9.5 Beta 1").CompareTo(V("2.9.5-beta.1")));
        Assert.Equal(0, V("2.9.5").CompareTo(V("RHI-2.9.5")));
        Assert.Equal(0, V("2.9.5").CompareTo(V("2.9.5.0")));
    }

    // ── Which update to offer ────────────────────────────────────────────────

    private static string? Offer(string current, string? stable, string? beta)
    {
        var r = VersionResolver.Resolve(V(current), stable == null ? null : V(stable), beta == null ? null : V(beta));
        return r?.ToDisplayString();
    }

    [Theory]
    // Release users (beta off: no beta passed in)
    [InlineData("2.9.4", "2.9.4", null, null)]           // up to date
    [InlineData("2.9.4", "2.9.5", null, "2.9.5")]        // new release
    [InlineData("2.9.5", "2.9.5.1", null, "2.9.5.1")]    // hotfix
    [InlineData("2.9.5.1", "2.9.5", null, null)]         // hotfix installed, release older
    // Beta users: release → beta
    [InlineData("2.9.4", "2.9.4", "2.9.5 beta 1", "2.9.5 beta 1")]
    // beta → next beta
    [InlineData("2.9.5 beta 1", "2.9.4", "2.9.5 beta 2", "2.9.5 beta 2")]
    // beta → its release (the case the old rule got wrong)
    [InlineData("2.9.5 beta 2", "2.9.5", "2.9.5 beta 2", "2.9.5")]
    // release out, beta repo still has the last beta: the release wins
    [InlineData("2.9.5 beta 2", "2.9.5", null, "2.9.5")]
    // up to date on the latest beta
    [InlineData("2.9.5 beta 2", "2.9.4", "2.9.5 beta 2", null)]
    // an old beta of a version that's already released is never offered
    [InlineData("2.9.5", "2.9.5", "2.9.5 beta 3", null)]
    // beta of the next version while on a hotfix
    [InlineData("2.9.5.1", "2.9.5.1", "2.9.6 beta 1", "2.9.6 beta 1")]
    // a hotfix to the release a beta user is waiting past: beta 2.9.6 is still newer
    [InlineData("2.9.6 beta 1", "2.9.5.1", "2.9.6 beta 1", null)]
    // beta turned off on a beta: no downgrade, wait for the release
    [InlineData("2.9.5 beta 2", "2.9.4", null, null)]
    // nothing known (both checks failed)
    [InlineData("2.9.4", null, null, null)]
    public void Resolve_OffersTheNewestWhenNewerThanInstalled(string current, string? stable, string? beta, string? expected)
    {
        Assert.Equal(expected, Offer(current, stable, beta));
    }

    // ── This build's version from the assembly ─────────────────────────────────

    [Theory]
    [InlineData("2.9.5.0", "2.9.5.0", "2.9.5")]
    [InlineData("2.9.5.0", "2.9.5.0-beta.1", "2.9.5 beta 1")]
    [InlineData("2.9.5.0", "2.9.5.0-beta.2+abc123", "2.9.5 beta 2")]
    [InlineData("2.9.5.1", "2.9.5.1", "2.9.5.1")]
    [InlineData("2.9.5.0", null, "2.9.5")]
    [InlineData("2.9.5.0", "1.0.0", "2.9.5")]   // SDK default label: the number still comes from the assembly
    [InlineData("2.9.5.0", "2.9.5.0-beta.0", "2.9.5")] // beta 0 isn't a beta
    public void FromAssembly_NumberFromAssemblyBetaFromLabel(string number, string? label, string display)
    {
        Assert.Equal(display, AppVersionInfo.FromAssembly(Version.Parse(number), label).ToDisplayString());
    }
}
