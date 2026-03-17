using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for mod author splitting and trimming.
/// Feature: version-display-and-mod-authors
/// </summary>
public class ModAuthorPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>Generates a single segment that may be empty, whitespace-only, or a name.</summary>
    private static readonly Gen<string> GenSegment =
        Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant("   "),
            Gen.Constant(" \t "),
            Gen.Elements("Alice", "Bob", "Charlie", "Dave", "Eve"));

    /// <summary>Generates a segment with extra leading/trailing whitespace around a name.</summary>
    private static readonly Gen<string> GenPaddedSegment =
        Gen.OneOf(
            Gen.Elements("Alice", "Bob", "Charlie", "Dave", "Eve")
               .Select(name =>
               {
                   // Add random whitespace padding
                   return Gen.Elements("", " ", "  ", "   ", "\t", " \t ")
                       .Two()
                       .Select(pair => pair.Item1 + name + pair.Item2);
               })
               .SelectMany(g => g));

    /// <summary>
    /// Builds a Maintainer string from 0–5 segments joined by "&amp;".
    /// Segments may be empty, whitespace-only, or valid names.
    /// </summary>
    private static readonly Gen<string> GenMaintainerString =
        Gen.Choose(0, 5)
           .SelectMany(count => Gen.ListOf(count, GenSegment))
           .Select(segments => string.Join("&", segments));

    /// <summary>
    /// Builds a Maintainer string with padded name segments joined by "&amp;".
    /// Guarantees at least one real name so AuthorList is non-empty.
    /// </summary>
    private static readonly Gen<string> GenPaddedMaintainerString =
        Gen.Choose(1, 5)
           .SelectMany(count => Gen.ListOf(count, GenPaddedSegment))
           .Select(segments => string.Join("&", segments));

    // ── Property 4: AuthorList count matches non-empty ampersand-separated segments ──
    // Feature: version-display-and-mod-authors, Property 4: AuthorList count matches non-empty segments
    // **Validates: Requirements 2.1, 2.5, 2.6**

    [Property(MaxTest = 100)]
    public Property AuthorList_Count_MatchesNonEmptySegments()
    {
        return Prop.ForAll(
            Arb.From(GenMaintainerString),
            (string maintainer) =>
            {
                var card = new GameCardViewModel { Maintainer = maintainer };

                int expectedCount = maintainer
                    .Split('&')
                    .Select(s => s.Trim())
                    .Count(s => s.Length > 0);

                return card.AuthorList.Length == expectedCount;
            });
    }

    [Property(MaxTest = 100)]
    public Property AuthorList_NullOrWhitespace_ReturnsEmpty()
    {
        var genNullOrWhitespace = Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant("   "),
            Gen.Constant(" \t "));

        return Prop.ForAll(
            Arb.From(genNullOrWhitespace),
            (string maintainer) =>
            {
                var card = new GameCardViewModel { Maintainer = maintainer };
                return card.AuthorList.Length == 0 && !card.HasAuthors;
            });
    }

    // ── Property 5: AuthorList entries are whitespace-trimmed ──────────────────────
    // Feature: version-display-and-mod-authors, Property 5: AuthorList entries are whitespace-trimmed
    // **Validates: Requirements 2.7**

    [Property(MaxTest = 100)]
    public Property AuthorList_Entries_AreWhitespaceTrimmed()
    {
        return Prop.ForAll(
            Arb.From(GenPaddedMaintainerString),
            (string maintainer) =>
            {
                var card = new GameCardViewModel { Maintainer = maintainer };

                return card.AuthorList.All(entry => entry == entry.Trim());
            });
    }

    // ── Unit tests: author badge edge cases ───────────────────────────────────────
    // **Validates: Requirements 2.1, 2.5, 2.6, 2.7**

    [Fact]
    public void AuthorList_TwoAuthors_SplitsCorrectly()
    {
        var card = new GameCardViewModel { Maintainer = "Alice & Bob" };
        Assert.Equal(new[] { "Alice", "Bob" }, card.AuthorList);
        Assert.True(card.HasAuthors);
    }

    [Fact]
    public void AuthorList_SingleAuthor_ReturnsSingleElement()
    {
        var card = new GameCardViewModel { Maintainer = "Solo" };
        Assert.Equal(new[] { "Solo" }, card.AuthorList);
        Assert.True(card.HasAuthors);
    }

    [Fact]
    public void AuthorList_EmptyString_ReturnsEmpty()
    {
        var card = new GameCardViewModel { Maintainer = "" };
        Assert.Empty(card.AuthorList);
        Assert.False(card.HasAuthors);
    }

    [Fact]
    public void AuthorList_OnlyAmpersands_ReturnsEmpty()
    {
        var card = new GameCardViewModel { Maintainer = "& &" };
        Assert.Empty(card.AuthorList);
        Assert.False(card.HasAuthors);
    }

    [Fact]
    public void AuthorList_LeadingTrailingAmpersands_TrimsCorrectly()
    {
        var card = new GameCardViewModel { Maintainer = " Alice & Bob & " };
        Assert.Equal(new[] { "Alice", "Bob" }, card.AuthorList);
        Assert.True(card.HasAuthors);
    }
}
