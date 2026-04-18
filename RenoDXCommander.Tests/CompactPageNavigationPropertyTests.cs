// Feature: compact-view-mode, Property 6: Compact page navigation is modular arithmetic
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for compact page navigation using modular arithmetic.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
[Collection("StaticShaderMode")]
public class CompactPageNavigationPropertyTests
{
    /// <summary>
    /// Feature: compact-view-mode, Property 6: Compact page navigation is modular arithmetic
    ///
    /// **Validates: Requirements 4.3, 4.4, 4.5, 4.6, 4.7, 4.8**
    ///
    /// For any starting CompactPageIndex in {0, 1, 2} and any navigation delta of +1 or -1,
    /// calling NavigateCompactPage(delta) sets CompactPageIndex to ((start + delta) % 3 + 3) % 3,
    /// and the result is always in the range [0, 2].
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NavigateCompactPage_IsModularArithmetic()
    {
        return Prop.ForAll(
            Gen.Elements(0, 1, 2).ToArbitrary(),
            Gen.Elements(-1, 1).ToArbitrary(),
            (int startIndex, int delta) =>
            {
                var vm = TestHelpers.CreateMainViewModel();
                vm.CompactPageIndex = startIndex;

                vm.NavigateCompactPage(delta);

                var expected = ((startIndex + delta) % 3 + 3) % 3;
                var result = vm.CompactPageIndex;

                return (result == expected && result >= 0 && result <= 2)
                    .Label($"Start={startIndex}, Delta={delta}, Expected={expected}, Got={result}");
            });
    }
}
