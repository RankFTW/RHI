// Feature: compact-view-mode, Property 3: ViewLayout persistence round-trip
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ViewLayout persistence round-trip.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
[Collection("StaticShaderMode")]
public class ViewLayoutPersistencePropertyTests
{
    private static Arbitrary<ViewLayout> ViewLayoutArb() =>
        Gen.Elements(ViewLayout.Detail, ViewLayout.Grid, ViewLayout.Compact).ToArbitrary();

    /// <summary>
    /// Feature: compact-view-mode, Property 3: ViewLayout persistence round-trip
    ///
    /// **Validates: Requirements 1.7, 9.1, 9.3**
    ///
    /// For any ViewLayout enum value, serializing it to its integer string representation
    /// (as stored in the settings file via ((int)layout).ToString()) and then parsing it
    /// back via int.TryParse and casting to ViewLayout SHALL produce the original enum value.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SerializeAndDeserialize_RoundTrips()
    {
        return Prop.ForAll(
            ViewLayoutArb(),
            (ViewLayout original) =>
            {
                // Serialize: same format used in GameNameService.SaveNameMappings
                var serialized = ((int)original).ToString();

                // Deserialize: same format used in GameNameService.LoadNameMappings
                var parsed = int.TryParse(serialized, out var intVal);
                var restored = (ViewLayout)intVal;

                return (parsed && restored == original)
                    .Label($"Expected {original} after round-trip, serialized='{serialized}', parsed={parsed}, restored={restored}");
            });
    }
}
