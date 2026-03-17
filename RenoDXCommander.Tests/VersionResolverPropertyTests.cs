using System;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for VersionResolver.Resolve.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class VersionResolverPropertyTests
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

    /// <summary>
    /// Generator for a stable version (no beta suffix).
    /// </summary>
    private static Gen<RdxcVersion> GenStableVersion()
    {
        return from major in Gen.Choose(0, 99)
               from minor in Gen.Choose(0, 99)
               from build in Gen.Choose(0, 99)
               select new RdxcVersion(major, minor, build, null);
    }

    /// <summary>
    /// Generator for a beta version (always has a beta suffix).
    /// </summary>
    private static Gen<RdxcVersion> GenBetaVersion()
    {
        return from major in Gen.Choose(0, 99)
               from minor in Gen.Choose(0, 99)
               from build in Gen.Choose(0, 99)
               from beta in Gen.Choose(1, 20)
               select new RdxcVersion(major, minor, build, beta);
    }

    /// <summary>
    /// Generator for a beta version whose BaseVersion is &lt;= the given stable version's BaseVersion.
    /// Picks one of three strategies: same base, lower major, or lower minor/build.
    /// </summary>
    private static Gen<RdxcVersion> GenBetaAtOrBelowBase(RdxcVersion stable)
    {
        // Strategy 1: Same base version as stable
        var sameBase = Gen.Choose(1, 20)
            .Select(b => new RdxcVersion(stable.Major, stable.Minor, stable.Build, b));

        // Strategy 2: Lower major
        var lowerMajor = stable.Major > 0
            ? from major in Gen.Choose(0, stable.Major - 1)
              from minor in Gen.Choose(0, 99)
              from build in Gen.Choose(0, 99)
              from beta in Gen.Choose(1, 20)
              select new RdxcVersion(major, minor, build, beta)
            : sameBase; // fallback if major is 0

        // Strategy 3: Same major, lower or equal minor/build
        var lowerMinorBuild =
            from minor in Gen.Choose(0, stable.Minor)
            from build in Gen.Choose(0, minor == stable.Minor ? stable.Build : 99)
            from beta in Gen.Choose(1, 20)
            select new RdxcVersion(stable.Major, minor, build, beta);

        return Gen.OneOf(sameBase, lowerMajor, lowerMinorBuild);
    }

    /// <summary>
    /// Generator for a constrained triple where the beta's base version is strictly greater
    /// than both the stable's and the current's base version.
    /// Generates stable S and current C first, then builds beta B with a base version
    /// guaranteed to exceed both.
    /// </summary>
    private static Gen<(RdxcVersion Current, RdxcVersion Stable, RdxcVersion Beta)> GenBetaAheadTriple()
    {
        return from stable in GenStableVersion()
               from current in GenRdxcVersion()
               let maxMajor = Math.Max(stable.Major, current.Major)
               let maxMinor = Math.Max(stable.Minor, current.Minor)
               let maxBuild = Math.Max(stable.Build, current.Build)
               from strategy in Gen.Choose(1, 3)
               from betaVersion in strategy switch
               {
                   // Strategy 1: Higher major
                   1 => from major in Gen.Choose(maxMajor + 1, maxMajor + 50)
                        from minor in Gen.Choose(0, 99)
                        from build in Gen.Choose(0, 99)
                        from beta in Gen.Choose(1, 20)
                        select new RdxcVersion(major, minor, build, beta),
                   // Strategy 2: Same max major, higher minor
                   2 => from minor in Gen.Choose(maxMinor + 1, maxMinor + 50)
                        from build in Gen.Choose(0, 99)
                        from beta in Gen.Choose(1, 20)
                        select new RdxcVersion(maxMajor, minor, build, beta),
                   // Strategy 3: Same max major and minor, higher build
                   _ => from build in Gen.Choose(maxBuild + 1, maxBuild + 50)
                        from beta in Gen.Choose(1, 20)
                        select new RdxcVersion(maxMajor, maxMinor, build, beta),
               }
               select (current, stable, betaVersion);
    }

    /// <summary>
    /// Generator for a beta version that the resolver would NOT offer as an update over <paramref name="current"/>.
    /// If current is stable, any beta at the same or lower base version qualifies.
    /// If current is a beta, the generated beta must have a lower base version OR the same base
    /// version with an equal or lower beta number.
    /// </summary>
    private static Gen<RdxcVersion> GenBetaAtOrBelowCurrent(RdxcVersion current)
    {
        // Lower base version — always safe
        var lowerBase = current.Major > 0
            ? from major in Gen.Choose(0, current.Major - 1)
              from minor in Gen.Choose(0, 99)
              from build in Gen.Choose(0, 99)
              from beta in Gen.Choose(1, 20)
              select new RdxcVersion(major, minor, build, beta)
            : Gen.Choose(1, 20).Select(b => new RdxcVersion(0, 0, 0, b));

        // Same base version — beta number must be <= current's beta number (or any if current is stable)
        var sameBase = current.IsBeta
            ? Gen.Choose(1, current.BetaNumber!.Value)
                .Select(b => new RdxcVersion(current.Major, current.Minor, current.Build, b))
            : Gen.Choose(1, 20)
                .Select(b => new RdxcVersion(current.Major, current.Minor, current.Build, b));

        return Gen.OneOf(lowerBase, sameBase);
    }

    /// <summary>
    /// Generator for a stable version whose BaseVersion is &lt;= the given version's BaseVersion.
    /// Picks one of three strategies: same base, lower major, or lower minor/build.
    /// </summary>
    private static Gen<RdxcVersion> GenStableAtOrBelowBase(RdxcVersion current)
    {
        // Strategy 1: Same base version as current
        var sameBase = Gen.Constant(new RdxcVersion(current.Major, current.Minor, current.Build, null));

        // Strategy 2: Lower major
        var lowerMajor = current.Major > 0
            ? from major in Gen.Choose(0, current.Major - 1)
              from minor in Gen.Choose(0, 99)
              from build in Gen.Choose(0, 99)
              select new RdxcVersion(major, minor, build, null)
            : sameBase; // fallback if major is 0

        // Strategy 3: Same major, lower or equal minor/build
        var lowerMinorBuild =
            from minor in Gen.Choose(0, current.Minor)
            from build in Gen.Choose(0, minor == current.Minor ? current.Build : 99)
            select new RdxcVersion(current.Major, minor, build, null);

        return Gen.OneOf(sameBase, lowerMajor, lowerMinorBuild);
    }

    // Feature: beta-opt-in, Property 2: Stable priority over beta at same or higher base version
    /// <summary>
    /// **Validates: Requirements 3.1, 3.3, 3.4, 6.4**
    ///
    /// For any current version, stable version S, and beta version B where
    /// S.BaseVersion >= B.BaseVersion, the VersionResolver.Resolve method should
    /// either select S (if S > current) or return null — it should never select B.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StablePriorityOverBeta_WhenStableBaseAtOrAboveBetaBase()
    {
        var gen = from stable in GenStableVersion()
                  from beta in GenBetaAtOrBelowBase(stable)
                  from current in GenRdxcVersion()
                  select (current, stable, beta);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (current, stable, beta) = tuple;
            var result = VersionResolver.Resolve(current, stable, beta);

            // Result must be either the stable version or null — never the beta
            var neverBeta = !result.HasValue || result.Value == stable;

            // If stable is ahead of current, it should be selected
            var stableAhead = stable.BaseVersion.CompareTo(current.BaseVersion) > 0;
            var correctResult = stableAhead
                ? result.HasValue && result.Value == stable
                : !result.HasValue || result.Value == stable;

            return (neverBeta && correctResult)
                .Label($"Current={current.ToDisplayString()}, Stable={stable.ToDisplayString()}, " +
                       $"Beta={beta.ToDisplayString()}, Result={result?.ToDisplayString() ?? "null"}, " +
                       $"StableAhead={stableAhead}");
        });
    }

    // Feature: beta-opt-in, Property 3: Beta selected when ahead of latest stable
    /// <summary>
    /// **Validates: Requirements 3.2**
    ///
    /// For any current version C, stable version S, and beta version B where
    /// B.BaseVersion > S.BaseVersion and B.BaseVersion > C.BaseVersion,
    /// the VersionResolver.Resolve method should select B.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BetaSelected_WhenBetaBaseAheadOfStableAndCurrent()
    {
        return Prop.ForAll(GenBetaAheadTriple().ToArbitrary(), tuple =>
        {
            var (current, stable, beta) = tuple;
            var result = VersionResolver.Resolve(current, stable, beta);

            return (result.HasValue && result.Value == beta)
                .Label($"Current={current.ToDisplayString()}, Stable={stable.ToDisplayString()}, " +
                       $"Beta={beta.ToDisplayString()}, Result={result?.ToDisplayString() ?? "null"}");
        });
    }

    // Feature: beta-opt-in, Property 4: No update when all candidates are at or below current version
    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// For any current version C and candidate versions (stable and/or beta) where all
    /// candidates have BaseVersion &lt;= C.BaseVersion, the VersionResolver.Resolve method
    /// should return null.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NoUpdate_WhenAllCandidatesAtOrBelowCurrent()
    {
        // Generate current version with Major >= 1 so we have room for lower candidates
        var genCurrent = from major in Gen.Choose(1, 99)
                         from minor in Gen.Choose(0, 99)
                         from build in Gen.Choose(0, 99)
                         from beta in Gen.Frequency(
                             Tuple.Create(1, Gen.Constant((int?)null)),
                             Tuple.Create(1, Gen.Choose(1, 20).Select(n => (int?)n)))
                         select new RdxcVersion(major, minor, build, beta);

        // Four scenarios: both candidates present, stable only, beta only, both null
        var gen = from current in genCurrent
                  from scenario in Gen.Choose(1, 4)
                  from stable in scenario == 1 || scenario == 2
                      ? GenStableAtOrBelowBase(current).Select(v => (RdxcVersion?)v)
                      : Gen.Constant((RdxcVersion?)null)
                  from beta in scenario == 1 || scenario == 3
                      ? GenBetaAtOrBelowCurrent(current).Select(v => (RdxcVersion?)v)
                      : Gen.Constant((RdxcVersion?)null)
                  select (current, stable, beta);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (current, stable, beta) = tuple;
            var result = VersionResolver.Resolve(current, stable, beta);

            return (!result.HasValue)
                .Label($"Current={current.ToDisplayString()}, " +
                       $"Stable={stable?.ToDisplayString() ?? "null"}, " +
                       $"Beta={beta?.ToDisplayString() ?? "null"}, " +
                       $"Result={result?.ToDisplayString() ?? "null"} (expected null)");
        });
    }
}
