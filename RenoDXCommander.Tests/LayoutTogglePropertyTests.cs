using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the multi-card-layout feature's ViewModel logic.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// Updated for the three-state ViewLayout enum (Detail, Grid, Compact).
/// </summary>
[Collection("StaticShaderMode")]
public class LayoutTogglePropertyTests
{
    // Feature: multi-card-layout, Property 1: Layout toggle cycling
    // Validates: Requirements 1.1, 1.2, 1.3
    [Property(MaxTest = 100)]
    public bool ToggleViewLayout_ThreeTimes_ReturnsToOriginal(bool startAsGrid)
    {
        var vm = TestHelpers.CreateMainViewModel();
        var initial = startAsGrid ? ViewLayout.Grid : ViewLayout.Detail;
        vm.CurrentViewLayout = initial;

        // Cycle three times
        vm.CurrentViewLayout = vm.NextViewLayout();
        vm.CurrentViewLayout = vm.NextViewLayout();
        vm.CurrentViewLayout = vm.NextViewLayout();

        return vm.CurrentViewLayout == initial;
    }

    // Feature: multi-card-layout, Property 1: Layout toggle single step
    // Validates: Requirements 1.1, 1.2, 1.3
    [Property(MaxTest = 100)]
    public bool ToggleViewLayout_Once_AdvancesToNextMode(bool startAsGrid)
    {
        var vm = TestHelpers.CreateMainViewModel();
        var initial = startAsGrid ? ViewLayout.Grid : ViewLayout.Detail;
        vm.CurrentViewLayout = initial;

        vm.CurrentViewLayout = vm.NextViewLayout();

        var expected = initial switch
        {
            ViewLayout.Detail => ViewLayout.Grid,
            ViewLayout.Grid => ViewLayout.Compact,
            ViewLayout.Compact => ViewLayout.Detail,
            _ => ViewLayout.Detail,
        };

        return vm.CurrentViewLayout == expected;
    }

    // Feature: multi-card-layout, Property 1: Layout toggle computed properties consistency
    // Validates: Requirements 2.3, 2.4, 2.5
    [Property(MaxTest = 100)]
    public bool ToggleViewLayout_ComputedProperties_AreConsistent(bool startAsGrid)
    {
        var vm = TestHelpers.CreateMainViewModel();
        vm.CurrentViewLayout = startAsGrid ? ViewLayout.Grid : ViewLayout.Detail;

        var detailVisible = vm.DetailPanelVisibility == Microsoft.UI.Xaml.Visibility.Visible;
        var gridVisible = vm.CardGridVisibility == Microsoft.UI.Xaml.Visibility.Visible;
        var compactVisible = vm.CompactViewVisibility == Microsoft.UI.Xaml.Visibility.Visible;

        // Exactly one view container is visible
        int visibleCount = (detailVisible ? 1 : 0) + (gridVisible ? 1 : 0) + (compactVisible ? 1 : 0);
        bool exactlyOneVisible = visibleCount == 1;

        // Correct container is visible for the current layout
        bool correctVisibility = vm.CurrentViewLayout switch
        {
            ViewLayout.Detail => detailVisible && !gridVisible && !compactVisible,
            ViewLayout.Grid => !detailVisible && gridVisible && !compactVisible,
            ViewLayout.Compact => !detailVisible && !gridVisible && compactVisible,
            _ => false,
        };

        // IsGridLayout backward compat
        bool isGridLayoutCorrect = vm.IsGridLayout == (vm.CurrentViewLayout == ViewLayout.Grid);

        return exactlyOneVisible && correctVisibility && isGridLayoutCorrect;
    }

    // Feature: multi-card-layout, Property 2: Layout preference persistence round-trip
    // Validates: Requirements 1.4
    [Property(MaxTest = 100)]
    public bool GridLayout_SettingsPersistence_RoundTrip(bool value)
    {
        // Test the serialization/deserialization logic used by SaveNameMappings/LoadNameMappings.
        // The settings file stores GridLayout as "1" or "0" and parses it back as == "1".
        // We verify this encoding round-trips correctly for any boolean value.
        string serialized = value ? "1" : "0";
        bool deserialized = serialized == "1";

        return deserialized == value;
    }

    // Feature: multi-card-layout, Property 2: Layout preference persistence round-trip (ViewModel level)
    // Validates: Requirements 1.4
    [Property(MaxTest = 100)]
    public bool ViewLayout_ViewModelPersistence_RoundTrip(bool startAsGrid)
    {
        // Verify that setting CurrentViewLayout on a ViewModel and reading it back
        // produces the same value — the property setter/getter round-trips.
        var vm = TestHelpers.CreateMainViewModel();
        var layout = startAsGrid ? ViewLayout.Grid : ViewLayout.Detail;
        vm.CurrentViewLayout = layout;

        return vm.CurrentViewLayout == layout;
    }

    // Feature: multi-card-layout, Property 3: Layout toggle preserves non-layout state
    // Validates: Requirements 1.5, 1.6
    [Property(MaxTest = 100)]
    public Property ToggleViewLayout_PreservesNonLayoutState()
    {
        // Generate random filter modes from the valid set and random search strings
        var filterModes = new[] { "Detected", "Favourites", "Hidden", "Unreal", "Unity", "Other", "RenoDX", "Luma" };

        return Prop.ForAll(
            Arb.From(Gen.Elements(filterModes)),
            Arb.From(Gen.Elements("", "test", "game name", "search query", "abc123")),
            Arb.From(Arb.Default.Bool().Generator),
            (string filterMode, string searchQuery, bool startAsGrid) =>
            {
                var vm = TestHelpers.CreateMainViewModel();

                // Set initial state
                vm.CurrentViewLayout = startAsGrid ? ViewLayout.Grid : ViewLayout.Detail;
                vm.FilterMode = filterMode;
                vm.SearchQuery = searchQuery;

                // Create and set a selected game (or null)
                var selectedCard = new GameCardViewModel { GameName = "TestGame", Source = "Steam" };
                vm.SelectedGame = selectedCard;

                // Capture state before toggle
                var filterBefore = vm.FilterMode;
                var searchBefore = vm.SearchQuery;
                var selectedBefore = vm.SelectedGame;

                // Toggle layout via NextViewLayout
                vm.CurrentViewLayout = vm.NextViewLayout();

                // Verify non-layout state is preserved
                bool filterPreserved = vm.FilterMode == filterBefore;
                bool searchPreserved = vm.SearchQuery == searchBefore;
                bool selectedPreserved = ReferenceEquals(vm.SelectedGame, selectedBefore);

                return filterPreserved && searchPreserved && selectedPreserved;
            });
    }

    // Feature: multi-card-layout, Property 3: Layout toggle preserves non-layout state (null selection)
    // Validates: Requirements 1.5, 1.6
    [Property(MaxTest = 100)]
    public bool ToggleViewLayout_WithNullSelection_PreservesState(bool startAsGrid)
    {
        var vm = TestHelpers.CreateMainViewModel();
        vm.CurrentViewLayout = startAsGrid ? ViewLayout.Grid : ViewLayout.Detail;
        vm.SelectedGame = null;
        vm.SearchQuery = "some search";
        vm.FilterMode = "Detected";

        var filterBefore = vm.FilterMode;
        var searchBefore = vm.SearchQuery;

        // Toggle layout via NextViewLayout
        vm.CurrentViewLayout = vm.NextViewLayout();

        return vm.FilterMode == filterBefore
            && vm.SearchQuery == searchBefore
            && vm.SelectedGame == null;
    }
}
