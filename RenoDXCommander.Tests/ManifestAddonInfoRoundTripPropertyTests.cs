// Feature: per-addon-info-buttons, Property 5: Manifest round-trip preserves addon info entries
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based and unit tests for RemoteManifest addon info field serialization.
/// Verifies that the new per-addon info dictionaries (reshadeGameInfo, relimiterGameInfo,
/// displayCommanderGameInfo, reframeworkGameInfo, optiScalerGameInfo, lumaGameInfo)
/// and optiScalerWikiNames survive JSON serialize → deserialize without data loss.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class ManifestAddonInfoRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a nullable GameNoteEntry with random notes, URL, and URL label.
    /// </summary>
    private static Gen<GameNoteEntry> GenGameNoteEntry() =>
        from notes in Gen.OneOf(
            Gen.Constant((string?)null),
            Gen.Elements<string?>("Custom note", "Use special settings", "Known issue with HDR", ""))
        from hasUrl in Arb.Generate<bool>()
        from hasLabel in Arb.Generate<bool>()
        select new GameNoteEntry
        {
            Notes = notes,
            NotesUrl = hasUrl ? "https://example.com/notes" : null,
            NotesUrlLabel = hasLabel ? "View details" : null
        };

    /// <summary>
    /// Generates a nullable Dictionary&lt;string, GameNoteEntry&gt; with 0–4 entries.
    /// </summary>
    private static Gen<Dictionary<string, GameNoteEntry>?> GenNullableGameNoteDict()
    {
        var keys = new[] { "Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Starfield", "Resident Evil 4" };

        var nonNull =
            from subKeys in Gen.SubListOf(keys)
            from entries in Gen.Sequence(subKeys.Select(_ => GenGameNoteEntry()))
            select (Dictionary<string, GameNoteEntry>?)subKeys
                .Zip(entries)
                .ToDictionary(kv => kv.First, kv => kv.Second);

        return Gen.OneOf(Gen.Constant<Dictionary<string, GameNoteEntry>?>(null), nonNull);
    }

    /// <summary>
    /// Generates a nullable Dictionary&lt;string, string&gt; with 0–4 entries.
    /// </summary>
    private static Gen<Dictionary<string, string>?> GenNullableStringDict()
    {
        var keys = new[] { "Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Starfield" };
        var values = new[] { "Cyberpunk 2077 Wiki", "ELDEN RING", "BG3", "Starfield Game" };

        var nonNull =
            from subKeys in Gen.SubListOf(keys)
            from vals in Gen.Sequence(subKeys.Select(_ => Gen.Elements(values)))
            select (Dictionary<string, string>?)subKeys
                .Zip(vals)
                .ToDictionary(kv => kv.First, kv => kv.Second);

        return Gen.OneOf(Gen.Constant<Dictionary<string, string>?>(null), nonNull);
    }

    // ── Sub-task 5.1: Unit tests for serialization/deserialization ─────────────────

    /// <summary>
    /// Verifies that a RemoteManifest with populated addon info dictionaries
    /// serializes to JSON containing the expected property names and deserializes
    /// back with identical values.
    /// </summary>
    [Fact]
    public void AddonInfoProperties_SerializeAndDeserialize_Correctly()
    {
        // Arrange
        var original = new RemoteManifest
        {
            Version = 1,
            ReshadeGameInfo = new Dictionary<string, GameNoteEntry>
            {
                ["Cyberpunk 2077"] = new() { Notes = "ReShade note", NotesUrl = "https://reshade.example.com" }
            },
            RelimiterGameInfo = new Dictionary<string, GameNoteEntry>
            {
                ["Elden Ring"] = new() { Notes = "ReLimiter note" }
            },
            DisplayCommanderGameInfo = new Dictionary<string, GameNoteEntry>
            {
                ["Starfield"] = new() { Notes = "DC note", NotesUrlLabel = "DC Link" }
            },
            ReframeworkGameInfo = new Dictionary<string, GameNoteEntry>
            {
                ["Resident Evil 4"] = new() { Notes = "REF note", NotesUrl = "https://ref.example.com", NotesUrlLabel = "REF" }
            },
            OptiScalerGameInfo = new Dictionary<string, GameNoteEntry>
            {
                ["Baldur's Gate 3"] = new() { Notes = "OptiScaler note" }
            },
            LumaGameInfo = new Dictionary<string, GameNoteEntry>
            {
                ["God of War"] = new() { Notes = "Luma note" }
            },
            OptiScalerWikiNames = new Dictionary<string, string>
            {
                ["Cyberpunk 2077"] = "Cyberpunk 2077 Wiki",
                ["Elden Ring"] = "ELDEN RING"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RemoteManifest>(json);

        // Assert: JSON contains expected property names
        Assert.Contains("\"reshadeGameInfo\"", json);
        Assert.Contains("\"relimiterGameInfo\"", json);
        Assert.Contains("\"displayCommanderGameInfo\"", json);
        Assert.Contains("\"reframeworkGameInfo\"", json);
        Assert.Contains("\"optiScalerGameInfo\"", json);
        Assert.Contains("\"lumaGameInfo\"", json);
        Assert.Contains("\"optiScalerWikiNames\"", json);

        // Assert: deserialized values match
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.ReshadeGameInfo);
        Assert.Equal("ReShade note", deserialized.ReshadeGameInfo!["Cyberpunk 2077"].Notes);
        Assert.Equal("https://reshade.example.com", deserialized.ReshadeGameInfo["Cyberpunk 2077"].NotesUrl);

        Assert.NotNull(deserialized.RelimiterGameInfo);
        Assert.Equal("ReLimiter note", deserialized.RelimiterGameInfo!["Elden Ring"].Notes);

        Assert.NotNull(deserialized.DisplayCommanderGameInfo);
        Assert.Equal("DC note", deserialized.DisplayCommanderGameInfo!["Starfield"].Notes);
        Assert.Equal("DC Link", deserialized.DisplayCommanderGameInfo["Starfield"].NotesUrlLabel);

        Assert.NotNull(deserialized.ReframeworkGameInfo);
        Assert.Equal("REF note", deserialized.ReframeworkGameInfo!["Resident Evil 4"].Notes);

        Assert.NotNull(deserialized.OptiScalerGameInfo);
        Assert.Equal("OptiScaler note", deserialized.OptiScalerGameInfo!["Baldur's Gate 3"].Notes);

        Assert.NotNull(deserialized.LumaGameInfo);
        Assert.Equal("Luma note", deserialized.LumaGameInfo!["God of War"].Notes);

        Assert.NotNull(deserialized.OptiScalerWikiNames);
        Assert.Equal("Cyberpunk 2077 Wiki", deserialized.OptiScalerWikiNames!["Cyberpunk 2077"]);
        Assert.Equal("ELDEN RING", deserialized.OptiScalerWikiNames["Elden Ring"]);
    }

    /// <summary>
    /// Verifies that null addon info dictionaries serialize as absent (not "null")
    /// and deserialize back as null, without affecting existing manifest fields.
    /// </summary>
    [Fact]
    public void NullAddonInfoProperties_DoNotAffectExistingFields()
    {
        // Arrange: manifest with existing fields but no addon info
        var original = new RemoteManifest
        {
            Version = 3,
            GameNotes = new Dictionary<string, GameNoteEntry>
            {
                ["TestGame"] = new() { Notes = "Existing game note" }
            },
            ReshadeGameInfo = null,
            RelimiterGameInfo = null,
            DisplayCommanderGameInfo = null,
            ReframeworkGameInfo = null,
            OptiScalerGameInfo = null,
            LumaGameInfo = null,
            OptiScalerWikiNames = null
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RemoteManifest>(json);

        // Assert: existing fields preserved
        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized!.Version);
        Assert.NotNull(deserialized.GameNotes);
        Assert.Equal("Existing game note", deserialized.GameNotes!["TestGame"].Notes);

        // Assert: addon info fields are null
        Assert.Null(deserialized.ReshadeGameInfo);
        Assert.Null(deserialized.RelimiterGameInfo);
        Assert.Null(deserialized.DisplayCommanderGameInfo);
        Assert.Null(deserialized.ReframeworkGameInfo);
        Assert.Null(deserialized.OptiScalerGameInfo);
        Assert.Null(deserialized.LumaGameInfo);
        Assert.Null(deserialized.OptiScalerWikiNames);
    }

    // ── Sub-task 5.2: Property test for manifest round-trip (Property 5) ──────────

    /// <summary>
    /// **Validates: Requirements 6.1, 6.2, 6.4**
    ///
    /// Property 5: Manifest round-trip preserves addon info entries.
    /// For any RemoteManifest object with arbitrary per-addon info dictionaries
    /// (reshadeGameInfo, relimiterGameInfo, displayCommanderGameInfo,
    /// reframeworkGameInfo, optiScalerGameInfo, lumaGameInfo, optiScalerWikiNames),
    /// serializing to JSON and deserializing back SHALL produce an equivalent
    /// manifest with identical addon info dictionaries.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ManifestAddonInfoFields_SurviveJsonRoundTrip()
    {
        var gen =
            from reshade in GenNullableGameNoteDict()
            from relimiter in GenNullableGameNoteDict()
            from dc in GenNullableGameNoteDict()
            from reframework in GenNullableGameNoteDict()
            from optiScaler in GenNullableGameNoteDict()
            from luma in GenNullableGameNoteDict()
            from wikiNames in GenNullableStringDict()
            select new RemoteManifest
            {
                Version = 1,
                ReshadeGameInfo = reshade,
                RelimiterGameInfo = relimiter,
                DisplayCommanderGameInfo = dc,
                ReframeworkGameInfo = reframework,
                OptiScalerGameInfo = optiScaler,
                LumaGameInfo = luma,
                OptiScalerWikiNames = wikiNames
            };

        return Prop.ForAll(gen.ToArbitrary(), (RemoteManifest original) =>
        {
            // Act: serialize → deserialize
            var json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<RemoteManifest>(json);

            // Assert: all addon info dictionaries are preserved
            var reshadeOk = GameNoteDictsEqual(original.ReshadeGameInfo, deserialized?.ReshadeGameInfo);
            var relimiterOk = GameNoteDictsEqual(original.RelimiterGameInfo, deserialized?.RelimiterGameInfo);
            var dcOk = GameNoteDictsEqual(original.DisplayCommanderGameInfo, deserialized?.DisplayCommanderGameInfo);
            var refOk = GameNoteDictsEqual(original.ReframeworkGameInfo, deserialized?.ReframeworkGameInfo);
            var osOk = GameNoteDictsEqual(original.OptiScalerGameInfo, deserialized?.OptiScalerGameInfo);
            var lumaOk = GameNoteDictsEqual(original.LumaGameInfo, deserialized?.LumaGameInfo);
            var wikiNamesOk = StringDictsEqual(original.OptiScalerWikiNames, deserialized?.OptiScalerWikiNames);

            return (reshadeOk && relimiterOk && dcOk && refOk && osOk && lumaOk && wikiNamesOk)
                .Label($"reshade={reshadeOk}, relimiter={relimiterOk}, dc={dcOk}, ref={refOk}, os={osOk}, luma={lumaOk}, wikiNames={wikiNamesOk}");
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compares two nullable GameNoteEntry dictionaries for value equality.
    /// </summary>
    private static bool GameNoteDictsEqual(
        Dictionary<string, GameNoteEntry>? a,
        Dictionary<string, GameNoteEntry>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var bVal)) return false;
            if (kvp.Value.Notes != bVal.Notes) return false;
            if (kvp.Value.NotesUrl != bVal.NotesUrl) return false;
            if (kvp.Value.NotesUrlLabel != bVal.NotesUrlLabel) return false;
        }
        return true;
    }

    /// <summary>
    /// Compares two nullable string dictionaries for value equality.
    /// </summary>
    private static bool StringDictsEqual(
        Dictionary<string, string>? a,
        Dictionary<string, string>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var bVal)) return false;
            if (kvp.Value != bVal) return false;
        }
        return true;
    }
}
