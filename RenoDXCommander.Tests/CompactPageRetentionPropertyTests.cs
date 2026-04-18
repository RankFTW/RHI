// Feature: compact-view-mode, Property 7: Page index retained across game selection
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for compact page index retention across game selection changes.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
[Collection("StaticShaderMode")]
public class CompactPageRetentionPropertyTests
{
    /// <summary>
    /// Feature: compact-view-mode, Property 7: Page index retained across game selection
    ///
    /// **Validates: Requirements 10.4**
    ///
    /// For any CompactPageIndex value in {0, 1, 2} and any change to SelectedGame
    /// (including setting to null or a new GameCardViewModel), the CompactPageIndex
    /// remains unchanged after the selection change.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CompactPageIndex_RetainedAcrossGameSelection()
    {
        return Prop.ForAll(
            Gen.Elements(0, 1, 2).ToArbitrary(),
            Gen.Elements(true, false).ToArbitrary(),
            (int pageIndex, bool setToNull) =>
            {
                var vm = TestHelpers.CreateMainViewModel();
                vm.CompactPageIndex = pageIndex;

                // Change SelectedGame — either to null or to a new GameCardViewModel
                if (setToNull)
                {
                    vm.SelectedGame = null;
                }
                else
                {
                    vm.SelectedGame = new GameCardViewModel { GameName = "TestGame" };
                }

                return (vm.CompactPageIndex == pageIndex)
                    .Label($"PageIndex={pageIndex}, SetToNull={setToNull}, Got={vm.CompactPageIndex}");
            });
    }
}
