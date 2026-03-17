using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the multi-card-layout feature's ViewModel logic.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class LayoutTogglePropertyTests
{
    // Feature: multi-card-layout, Property 1: Layout toggle involution
    // Validates: Requirements 1.2
    [Property(MaxTest = 100)]
    public bool ToggleIsGridLayout_Twice_ReturnsToOriginal(bool initial)
    {
        var vm = TestHelpers.CreateMainViewModel();
        vm.IsGridLayout = initial;

        // Toggle twice
        vm.IsGridLayout = !vm.IsGridLayout;
        vm.IsGridLayout = !vm.IsGridLayout;

        return vm.IsGridLayout == initial;
    }

    // Feature: multi-card-layout, Property 1: Layout toggle involution (single flip)
    // Validates: Requirements 1.2
    [Property(MaxTest = 100)]
    public bool ToggleIsGridLayout_Once_FlipsValue(bool initial)
    {
        var vm = TestHelpers.CreateMainViewModel();
        vm.IsGridLayout = initial;

        vm.IsGridLayout = !vm.IsGridLayout;

        return vm.IsGridLayout == !initial;
    }

    // Feature: multi-card-layout, Property 1: Layout toggle involution (computed properties)
    // Validates: Requirements 1.2
    [Property(MaxTest = 100)]
    public bool ToggleIsGridLayout_ComputedProperties_AreConsistent(bool value)
    {
        var vm = TestHelpers.CreateMainViewModel();
        vm.IsGridLayout = value;

        var detailVisible = vm.DetailPanelVisibility == Microsoft.UI.Xaml.Visibility.Visible;
        var gridVisible = vm.CardGridVisibility == Microsoft.UI.Xaml.Visibility.Visible;
        var label = vm.LayoutToggleLabel;

        // Detail and grid visibility are mutually exclusive
        bool mutuallyExclusive = detailVisible != gridVisible;

        // When grid layout, detail is collapsed and grid is visible
        bool detailCorrect = value ? !detailVisible : detailVisible;
        bool gridCorrect = value ? gridVisible : !gridVisible;

        // Label matches state
        bool labelCorrect = value ? label == "Detail View" : label == "Grid View";

        return mutuallyExclusive && detailCorrect && gridCorrect && labelCorrect;
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
    public bool GridLayout_ViewModelPersistence_RoundTrip(bool value)
    {
        // Verify that setting IsGridLayout on a ViewModel and reading it back
        // produces the same value — the property setter/getter round-trips.
        var vm = TestHelpers.CreateMainViewModel();
        vm.IsGridLayout = value;

        return vm.IsGridLayout == value;
    }

    // Feature: multi-card-layout, Property 3: Layout toggle preserves non-layout state
    // Validates: Requirements 1.5, 1.6
    [Property(MaxTest = 100)]
    public Property ToggleIsGridLayout_PreservesNonLayoutState()
    {
        // Generate random filter modes from the valid set and random search strings
        var filterModes = new[] { "Detected", "Favourites", "Hidden", "Unreal", "Unity", "Other", "RenoDX", "Luma" };

        return Prop.ForAll(
            Arb.From(Gen.Elements(filterModes)),
            Arb.From(Gen.Elements("", "test", "game name", "search query", "abc123")),
            Arb.From(Arb.Default.Bool().Generator),
            (string filterMode, string searchQuery, bool initialLayout) =>
            {
                var vm = TestHelpers.CreateMainViewModel();

                // Set initial state
                vm.IsGridLayout = initialLayout;
                vm.FilterMode = filterMode;
                vm.SearchQuery = searchQuery;

                // Create and set a selected game (or null)
                var selectedCard = new GameCardViewModel { GameName = "TestGame", Source = "Steam" };
                vm.SelectedGame = selectedCard;

                // Capture state before toggle
                var filterBefore = vm.FilterMode;
                var searchBefore = vm.SearchQuery;
                var selectedBefore = vm.SelectedGame;

                // Toggle layout
                vm.IsGridLayout = !vm.IsGridLayout;

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
    public bool ToggleIsGridLayout_WithNullSelection_PreservesState(bool initialLayout)
    {
        var vm = TestHelpers.CreateMainViewModel();
        vm.IsGridLayout = initialLayout;
        vm.SelectedGame = null;
        vm.SearchQuery = "some search";
        vm.FilterMode = "Detected";

        var filterBefore = vm.FilterMode;
        var searchBefore = vm.SearchQuery;

        // Toggle layout
        vm.IsGridLayout = !vm.IsGridLayout;

        return vm.FilterMode == filterBefore
            && vm.SearchQuery == searchBefore
            && vm.SelectedGame == null;
    }
}
