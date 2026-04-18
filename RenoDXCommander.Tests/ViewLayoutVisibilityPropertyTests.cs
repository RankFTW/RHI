// Feature: compact-view-mode, Property 5: Exactly one view container is visible per layout
using FsCheck;
using FsCheck.Xunit;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ViewLayout visibility exclusivity.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
[Collection("StaticShaderMode")]
public class ViewLayoutVisibilityPropertyTests
{
    private static Arbitrary<ViewLayout> ViewLayoutArb() =>
        Gen.Elements(ViewLayout.Detail, ViewLayout.Grid, ViewLayout.Compact).ToArbitrary();

    /// <summary>
    /// Feature: compact-view-mode, Property 5: Exactly one view container is visible per layout
    ///
    /// **Validates: Requirements 2.3, 2.4, 2.5**
    ///
    /// For any ViewLayout value, exactly one of DetailPanelVisibility, CardGridVisibility,
    /// CompactViewVisibility is Visible, and the other two are Collapsed.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExactlyOneViewContainer_IsVisible_PerLayout()
    {
        return Prop.ForAll(
            ViewLayoutArb(),
            (ViewLayout layout) =>
            {
                var vm = TestHelpers.CreateMainViewModel();
                vm.CurrentViewLayout = layout;

                var detailVisible = vm.DetailPanelVisibility == Visibility.Visible;
                var gridVisible = vm.CardGridVisibility == Visibility.Visible;
                var compactVisible = vm.CompactViewVisibility == Visibility.Visible;

                // Count how many are visible
                int visibleCount = (detailVisible ? 1 : 0)
                                 + (gridVisible ? 1 : 0)
                                 + (compactVisible ? 1 : 0);

                bool exactlyOneVisible = visibleCount == 1;

                // The correct container is visible for the current layout
                bool correctContainerVisible = layout switch
                {
                    ViewLayout.Detail => detailVisible && !gridVisible && !compactVisible,
                    ViewLayout.Grid => !detailVisible && gridVisible && !compactVisible,
                    ViewLayout.Compact => !detailVisible && !gridVisible && compactVisible,
                    _ => false,
                };

                return (exactlyOneVisible && correctContainerVisible)
                    .Label($"Layout={layout}, Detail={detailVisible}, Grid={gridVisible}, Compact={compactVisible}");
            });
    }
}
