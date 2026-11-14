// AppVersion.cs — RHI's own version: what this build is (AppVersionInfo), how versions compare
// (RdxcVersion) and which update to offer (VersionResolver).
//
// Betas keep the release's number and carry the beta as a label (csproj: AssemblyVersion 2.9.5.0 +
// RhiBeta 1 → InformationalVersion "2.9.5.0-beta.1", shown as "2.9.5 beta 1"). The 4th number is a
// real version: 2.9.5.1 is a hotfix after 2.9.5. Order: 2.9.4 < 2.9.5 beta 1 < 2.9.5 beta 2 <
// 2.9.5 < 2.9.5.1. Plan: docs/beta-channel.md.

using System.Reflection;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// A RHI version: Major.Minor.Build[.Revision] with an optional beta number.
/// Examples: "2.9.5", "2.9.5.1", "2.9.5 beta 1", "2.9.5-beta.1", "RHI-2.9.5-beta-1".
/// </summary>
public readonly record struct RdxcVersion(int Major, int Minor, int Build, int Revision = 0, int? BetaNumber = null)
    : IComparable<RdxcVersion>
{
    /// <summary>True when this version is a beta.</summary>
    public bool IsBeta => BetaNumber.HasValue;

    /// <summary>
    /// Compares the numbers first (Major, Minor, Build, Revision), then: a release is newer than any
    /// beta of the same number, and a higher beta is newer than a lower one.
    /// </summary>
    public int CompareTo(RdxcVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        cmp = Build.CompareTo(other.Build);
        if (cmp != 0) return cmp;
        cmp = Revision.CompareTo(other.Revision);
        if (cmp != 0) return cmp;

        if (!IsBeta && !other.IsBeta) return 0;
        if (!IsBeta) return 1;    // release > beta
        if (!other.IsBeta) return -1;
        return BetaNumber!.Value.CompareTo(other.BetaNumber!.Value);
    }

    public static bool operator >(RdxcVersion a, RdxcVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(RdxcVersion a, RdxcVersion b) => a.CompareTo(b) < 0;

    // Leading text allowed ("RHI-", "v", "RHI "). Major.Minor.Build, optional .Revision, optional
    // beta: "beta 1", "Beta1", "-beta-1", "-beta.1", "_beta_1".
    private static readonly Regex VersionPattern = new(
        @"(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?(?:[\s._-]*beta[\s._-]*(\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parses a version from a release name, tag or label. Returns false (and logs) when there is none.</summary>
    public static bool TryParse(string? input, out RdxcVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            CrashReporter.Log("[RdxcVersion.TryParse] Cannot parse null or empty version string");
            return false;
        }

        var match = VersionPattern.Match(input);
        if (!match.Success)
        {
            CrashReporter.Log($"[RdxcVersion.TryParse] Cannot parse version from '{input}'");
            return false;
        }

        version = new RdxcVersion(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value),
            match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0,
            match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : null);
        return true;
    }

    /// <summary>The numbers: "2.9.5", or "2.9.5.1" when there is a 4th number.</summary>
    public string NumberString => Revision > 0 ? $"{Major}.{Minor}.{Build}.{Revision}" : $"{Major}.{Minor}.{Build}";

    /// <summary>For people: "2.9.5", "2.9.5.1", "2.9.5 beta 1".</summary>
    public string ToDisplayString() => IsBeta ? $"{NumberString} beta {BetaNumber}" : NumberString;

    /// <summary>For file names and tags (no spaces): "2.9.5", "2.9.5.1", "2.9.5-beta.1".</summary>
    public string ToTagString() => IsBeta ? $"{NumberString}-beta.{BetaNumber}" : NumberString;

    public override string ToString() => ToDisplayString();
}

/// <summary>Decides which update (if any) to offer.</summary>
internal static class VersionResolver
{
    /// <summary>
    /// The newest of the latest release and (only for users who get betas) the latest beta, if it's
    /// newer than what's installed; else null. So a beta user moves to the release of the same version
    /// (2.9.5 beta 2 → 2.9.5), and a beta is only offered while it's ahead of the latest release.
    /// Turning betas off never downgrades: a beta user stays put until a newer release.
    /// </summary>
    public static RdxcVersion? Resolve(RdxcVersion current, RdxcVersion? latestStable, RdxcVersion? latestBeta)
    {
        RdxcVersion? best = latestStable;
        if (latestBeta.HasValue && (!best.HasValue || latestBeta.Value > best.Value))
            best = latestBeta;
        return best.HasValue && best.Value > current ? best : null;
    }
}

/// <summary>The version of the running RHI build.</summary>
public static class AppVersionInfo
{
    // Declared first: static fields initialise in file order, and Current (below) uses it.
    private static readonly Regex BetaLabel = new(@"beta[\s._-]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Assembly AppAssembly = typeof(AppVersionInfo).Assembly;

    /// <summary>The assembly number (AssemblyVersion), e.g. 2.9.5.0. The same for every beta of a version.</summary>
    public static Version Number { get; } = AppAssembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>The label (InformationalVersion), e.g. "2.9.5.0-beta.1" on a beta build.</summary>
    public static string? Label { get; } =
        AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>This build as a comparable version (number + beta from the label).</summary>
    public static RdxcVersion Current { get; } = FromAssembly(Number, Label);

    /// <summary>"2.9.5", "2.9.5.1" or "2.9.5 beta 1".</summary>
    public static string Display => Current.ToDisplayString();

    public static bool IsBeta => Current.IsBeta;

    /// <summary>
    /// The number comes from the assembly version; only the beta comes from the label (anything after
    /// a '+', e.g. a commit hash, is ignored). A label without "beta N" means a release.
    /// </summary>
    internal static RdxcVersion FromAssembly(Version number, string? label)
    {
        int? beta = null;
        if (!string.IsNullOrEmpty(label))
        {
            var plus = label.IndexOf('+');
            var text = plus >= 0 ? label[..plus] : label;
            var m = BetaLabel.Match(text);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0) beta = n;
        }
        return new RdxcVersion(number.Major, number.Minor, Math.Max(0, number.Build), Math.Max(0, number.Revision), beta);
    }
}
