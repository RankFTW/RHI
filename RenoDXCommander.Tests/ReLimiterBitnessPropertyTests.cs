using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ReLimiter bitness-aware filename and cache path selection.
/// Feature: 32bit-ogl-dx9-support, Property 1: ReLimiter filename matches game bitness
/// Validates: Requirements 1.1, 1.2, 1.4, 1.7
/// </summary>
public class ReLimiterBitnessPropertyTests
{
    // Feature: 32bit-ogl-dx9-support, Property 1: ReLimiter filename matches game bitness
    // Validates: Requirements 1.1, 1.2, 1.4, 1.7
    [Property(MaxTest = 100)]
    public Property GetUlFileName_ReturnsCorrectAddonForBitness()
    {
        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            is32Bit =>
            {
                var result = MainViewModel.GetUlFileName(is32Bit);
                var expected = is32Bit ? "relimiter.addon32" : "relimiter.addon64";

                return (result == expected)
                    .Label($"is32Bit={is32Bit}: expected '{expected}' but got '{result}'");
            });
    }

    // Feature: 32bit-ogl-dx9-support, Property 1: ReLimiter filename matches game bitness
    // Validates: Requirements 1.1, 1.2, 1.4, 1.7
    [Property(MaxTest = 100)]
    public Property GetUlCachePath_DistinctForEachBitness()
    {
        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            _ =>
            {
                var path32 = MainViewModel.GetUlCachePath(true);
                var path64 = MainViewModel.GetUlCachePath(false);

                return (path32 != path64)
                    .Label($"Cache paths must differ: 32-bit='{path32}', 64-bit='{path64}'");
            });
    }
}
