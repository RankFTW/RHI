// Feature: compact-view-mode, Property 1: View mode cycling is a 3-cycle
// Feature: compact-view-mode, Property 2: Layout toggle label names the next mode
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ViewLayout cycling and label consistency.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
[Collection("StaticShaderMode")]
public class ViewLayoutCyclePropertyTests
{
    private static Arbitrary<ViewLayout> ViewLayoutArb() =>
        Gen.Elements(ViewLayout.Detail, ViewLayout.Grid, ViewLayout.Compact).ToArbitrary();

    /// <summary>
    /// Feature: compact-view-mode, Property 1: View mode cycling is a 3-cycle
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    ///
    /// For any ViewLayout value, calling NextViewLayout() three times in succession
    /// returns the original value. The cycle is Detail → Grid → Compact → Detail.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NextViewLayout_ThreeTimes_ReturnsOriginal()
    {
        return Prop.ForAll(
            ViewLayoutArb(),
            (ViewLayout initial) =>
            {
                var vm = TestHelpers.CreateMainViewModel();
                vm.CurrentViewLayout = initial;

                // Cycle three times
                vm.CurrentViewLayout = vm.NextViewLayout();
                vm.CurrentViewLayout = vm.NextViewLayout();
                vm.CurrentViewLayout = vm.NextViewLayout();

                return (vm.CurrentViewLayout == initial)
                    .Label($"Expected {initial} after 3 cycles, got {vm.CurrentViewLayout}");
            });
    }

    /// <summary>
    /// Feature: compact-view-mode, Property 2: Layout toggle label names the current mode
    ///
    /// **Validates: Requirements 1.4, 1.5, 1.6**
    ///
    /// For any ViewLayout value, LayoutToggleLabel contains the name of the current view mode:
    /// - Detail → "Detail View"
    /// - Grid → "Grid View"
    /// - Compact → "Compact View"
    /// </summary>
    [Property(MaxTest = 100)]
    public Property LayoutToggleLabel_NamesCurrentMode()
    {
        return Prop.ForAll(
            ViewLayoutArb(),
            (ViewLayout layout) =>
            {
                var vm = TestHelpers.CreateMainViewModel();
                vm.CurrentViewLayout = layout;

                var label = vm.LayoutToggleLabel;

                // The label should contain the name of the current mode
                var expectedLabel = layout switch
                {
                    ViewLayout.Detail => "Detail View",
                    ViewLayout.Grid => "Grid View",
                    ViewLayout.Compact => "Compact View",
                    _ => "Detail View",
                };

                return (label == expectedLabel)
                    .Label($"Layout={layout}, expected label='{expectedLabel}', got='{label}'");
            });
    }
}
