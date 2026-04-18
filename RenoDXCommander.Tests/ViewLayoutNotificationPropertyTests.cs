// Feature: compact-view-mode, Property 4: ViewLayout change raises all dependent property notifications
using System.ComponentModel;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ViewLayout property change notifications.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
[Collection("StaticShaderMode")]
public class ViewLayoutNotificationPropertyTests
{
    private static Arbitrary<ViewLayout> ViewLayoutArb() =>
        Gen.Elements(ViewLayout.Detail, ViewLayout.Grid, ViewLayout.Compact).ToArbitrary();

    /// <summary>
    /// Feature: compact-view-mode, Property 4: ViewLayout change raises all dependent property notifications
    ///
    /// **Validates: Requirements 2.2**
    ///
    /// For any ViewLayout value, setting CurrentViewLayout on the MainViewModel SHALL raise
    /// PropertyChanged events for DetailPanelVisibility, CardGridVisibility,
    /// CompactViewVisibility, LayoutToggleLabel, and IsGridLayout.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SettingCurrentViewLayout_RaisesAllDependentNotifications()
    {
        return Prop.ForAll(
            ViewLayoutArb(),
            (ViewLayout layout) =>
            {
                var vm = TestHelpers.CreateMainViewModel();

                // Set to a different value first so the change always fires
                vm.CurrentViewLayout = layout == ViewLayout.Detail
                    ? ViewLayout.Grid
                    : ViewLayout.Detail;

                var raisedProperties = new HashSet<string>();
                ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != null)
                        raisedProperties.Add(e.PropertyName);
                };

                // Act: set to the target layout
                vm.CurrentViewLayout = layout;

                // Assert: all five dependent properties were notified
                var hasDetailPanel = raisedProperties.Contains(nameof(vm.DetailPanelVisibility));
                var hasCardGrid = raisedProperties.Contains(nameof(vm.CardGridVisibility));
                var hasCompactView = raisedProperties.Contains(nameof(vm.CompactViewVisibility));
                var hasToggleLabel = raisedProperties.Contains(nameof(vm.LayoutToggleLabel));
                var hasIsGridLayout = raisedProperties.Contains(nameof(vm.IsGridLayout));

                return (hasDetailPanel && hasCardGrid && hasCompactView && hasToggleLabel && hasIsGridLayout)
                    .Label($"Layout={layout}, raised=[{string.Join(", ", raisedProperties)}], " +
                           $"missing=[{string.Join(", ", new[] {
                               !hasDetailPanel ? "DetailPanelVisibility" : null,
                               !hasCardGrid ? "CardGridVisibility" : null,
                               !hasCompactView ? "CompactViewVisibility" : null,
                               !hasToggleLabel ? "LayoutToggleLabel" : null,
                               !hasIsGridLayout ? "IsGridLayout" : null
                           }.Where(x => x != null))}]");
            });
    }
}
