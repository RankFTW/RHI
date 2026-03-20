using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Collections;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for FilterViewModel persistence (SetFilter, RestoreFilterMode, round-trip).
/// </summary>
public class FilterModePersistencePropertyTests
{
    // ── Constants ──────────────────────────────────────────────────────────────────

    private static readonly string[] ExclusiveFilters = { "Detected", "Favourites", "Hidden", "Installed" };
    private static readonly string[] CombinableFilters = { "Unreal", "Unity", "Other", "RenoDX", "Luma" };

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>Picks one exclusive filter name.</summary>
    private static readonly Gen<string> GenValidExclusiveFilter =
        Gen.Elements(ExclusiveFilters);

    /// <summary>Generates a non-empty subset of combinable filters.</summary>
    private static readonly Gen<HashSet<string>> GenValidCombinableFilterSet =
        Gen.SubListOf(CombinableFilters)
           .Where(l => l.Count > 0)
           .Select(l => new HashSet<string>(l, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Generates a valid filter mode: either a single exclusive filter
    /// or a comma-joined non-empty subset of combinable filters.
    /// </summary>
    private static readonly Gen<string> GenValidFilterMode =
        Gen.OneOf(
            GenValidExclusiveFilter,
            GenValidCombinableFilterSet.Select(s => string.Join(",", s)));

    /// <summary>
    /// Generates an invalid filter mode string: unrecognised tokens,
    /// mixed exclusive+combinable, or whitespace-only.
    /// </summary>
    private static readonly Gen<string> GenInvalidFilterMode =
        Gen.OneOf(
            // Unrecognised token (none of these match valid filter names case-insensitively)
            Gen.Elements("Bogus", "FooBar", "notafilter", "123", "Detected!", "xyzzy", "All"),
            // Mixed exclusive + combinable
            from exc in GenValidExclusiveFilter
            from comb in Gen.Elements(CombinableFilters)
            select $"{exc},{comb}",
            // Whitespace-only
            Gen.Elements("", " ", "  ", "\t", " \t "));

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Creates a fresh, initialized FilterViewModel ready for testing.</summary>
    private static FilterViewModel CreateFilterViewModel()
    {
        var displayed = new BatchObservableCollection<GameCardViewModel>();
        var vm = new FilterViewModel();
        vm.Initialize(displayed);
        vm.SetAllCards(Array.Empty<GameCardViewModel>());
        return vm;
    }

    /// <summary>Parses a filter mode string into a set of filter names.</summary>
    private static HashSet<string> ParseFilterSet(string filterMode)
    {
        return new HashSet<string>(
            filterMode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    // ── Property 1 ────────────────────────────────────────────────────────────────
    // Feature: persist-filter-mode, Property 1: SetFilter produces correct FilterMode string
    // **Validates: Requirements 1.1, 1.2**

    [Property(MaxTest = 100)]
    public Property SetFilter_ProducesCorrectFilterModeString()
    {
        return Prop.ForAll(
            Arb.From(GenValidExclusiveFilter),
            (string exclusiveFilter) =>
            {
                var vm = CreateFilterViewModel();
                bool callbackInvoked = false;
                vm.FilterModeChanged = () => callbackInvoked = true;

                vm.SetFilter(exclusiveFilter);

                return (vm.FilterMode == exclusiveFilter && callbackInvoked)
                    .Label($"Exclusive: FilterMode='{vm.FilterMode}', expected='{exclusiveFilter}', callback={callbackInvoked}");
            })
        .And(Prop.ForAll(
            Arb.From(GenValidCombinableFilterSet),
            (HashSet<string> combinableSet) =>
            {
                var vm = CreateFilterViewModel();
                int callbackCount = 0;
                vm.FilterModeChanged = () => callbackCount++;

                // Apply each combinable filter via SetFilter
                foreach (var filter in combinableSet)
                    vm.SetFilter(filter);

                // ActiveFilters should contain exactly the combinable set
                var activeSet = new HashSet<string>(vm.ActiveFilters, StringComparer.OrdinalIgnoreCase);
                bool setsMatch = activeSet.SetEquals(combinableSet);

                // FilterMode should be a comma-separated string whose parsed tokens match the set
                var parsedMode = ParseFilterSet(vm.FilterMode);
                bool modeMatchesSet = parsedMode.SetEquals(combinableSet);

                // Callback should have been invoked once per SetFilter call
                bool callbackFired = callbackCount == combinableSet.Count;

                return (setsMatch && modeMatchesSet && callbackFired)
                    .Label($"Combinable: setsMatch={setsMatch}, modeMatchesSet={modeMatchesSet}, callbackFired={callbackFired} " +
                           $"(set=[{string.Join(",", combinableSet)}], FilterMode='{vm.FilterMode}', callbackCount={callbackCount})");
            }));
    }

    // ── Property 2 ────────────────────────────────────────────────────────────────
    // Feature: persist-filter-mode, Property 2: RestoreFilterMode correctly restores valid state
    // **Validates: Requirements 2.1, 2.2**

    [Property(MaxTest = 100)]
    public Property RestoreFilterMode_CorrectlyRestoresValidState()
    {
        return Prop.ForAll(
            Arb.From(GenValidFilterMode),
            (string validMode) =>
            {
                var vm = CreateFilterViewModel();

                vm.RestoreFilterMode(validMode);

                var expectedSet = ParseFilterSet(validMode);
                var actualSet = new HashSet<string>(vm.ActiveFilters, StringComparer.OrdinalIgnoreCase);

                bool setsMatch = actualSet.SetEquals(expectedSet);
                bool modeMatchesSet = ParseFilterSet(vm.FilterMode).SetEquals(expectedSet);

                return (setsMatch && modeMatchesSet)
                    .Label($"RestoreValid: setsMatch={setsMatch}, modeMatchesSet={modeMatchesSet} " +
                           $"(input='{validMode}', FilterMode='{vm.FilterMode}', ActiveFilters=[{string.Join(",", vm.ActiveFilters)}])");
            });
    }

    // ── Property 3 ────────────────────────────────────────────────────────────────
    // Feature: persist-filter-mode, Property 3: Invalid input falls back to Detected
    // **Validates: Requirements 2.3, 2.4, 3.1, 3.2**

    [Property(MaxTest = 100)]
    public Property RestoreFilterMode_InvalidInputFallsBackToDetected()
    {
        return Prop.ForAll(
            Arb.From(GenInvalidFilterMode),
            (string invalidMode) =>
            {
                var vm = CreateFilterViewModel();

                vm.RestoreFilterMode(invalidMode);

                bool modeIsDetected = vm.FilterMode == "Detected";
                var activeSet = new HashSet<string>(vm.ActiveFilters, StringComparer.OrdinalIgnoreCase);
                bool activeIsDetected = activeSet.SetEquals(new HashSet<string> { "Detected" });

                return (modeIsDetected && activeIsDetected)
                    .Label($"InvalidFallback: modeIsDetected={modeIsDetected}, activeIsDetected={activeIsDetected} " +
                           $"(input='{invalidMode}', FilterMode='{vm.FilterMode}', ActiveFilters=[{string.Join(",", vm.ActiveFilters)}])");
            });
    }

    // ── Property 4 ────────────────────────────────────────────────────────────────
    // Feature: persist-filter-mode, Property 4: Round-trip persistence preserves filter set
    // **Validates: Requirements 4.1, 4.2**

    [Property(MaxTest = 100)]
    public Property RoundTrip_PersistencePreservesFilterSet()
    {
        return Prop.ForAll(
            Arb.From(GenValidFilterMode),
            (string validMode) =>
            {
                // Step 1: Establish the filter mode via SetFilter
                var vm1 = CreateFilterViewModel();
                var expectedSet = ParseFilterSet(validMode);

                if (expectedSet.Count == 1 && (
                    expectedSet.Contains("Detected") || expectedSet.Contains("Favourites") ||
                    expectedSet.Contains("Hidden") || expectedSet.Contains("Installed")))
                {
                    // Exclusive filter — single SetFilter call
                    vm1.SetFilter(expectedSet.First());
                }
                else
                {
                    // Combinable filters — call SetFilter for each
                    foreach (var filter in expectedSet)
                        vm1.SetFilter(filter);
                }

                var filterModeAfterSet = vm1.FilterMode;
                var activeAfterSet = new HashSet<string>(vm1.ActiveFilters, StringComparer.OrdinalIgnoreCase);

                // Step 2: Restore from the persisted FilterMode string
                var vm2 = CreateFilterViewModel();
                vm2.RestoreFilterMode(filterModeAfterSet);

                var activeAfterRestore = new HashSet<string>(vm2.ActiveFilters, StringComparer.OrdinalIgnoreCase);

                bool setsMatch = activeAfterSet.SetEquals(activeAfterRestore);

                return setsMatch
                    .Label($"RoundTrip: setsMatch={setsMatch} " +
                           $"(input='{validMode}', filterMode='{filterModeAfterSet}', " +
                           $"afterSet=[{string.Join(",", activeAfterSet)}], afterRestore=[{string.Join(",", activeAfterRestore)}])");
            });
    }
}
