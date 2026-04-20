using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for NxmProtocolHandler Format→Parse round-trip.
/// Feature: nexus-api-integration, Property 2: NXM URI parse round-trip
/// </summary>
public class NxmUriParseRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty lowercase alphanumeric string (1–20 chars) for game domain.
    /// </summary>
    private static readonly Gen<string> GenGameDomain =
        from length in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(length, Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
        select new string(chars);

    /// <summary>
    /// Generates a positive integer (1 to int.MaxValue-1) for mod ID and file ID.
    /// </summary>
    private static readonly Gen<int> GenPositiveInt =
        Gen.Choose(1, int.MaxValue - 1);

    /// <summary>
    /// Generates a non-empty alphanumeric string (1–50 chars, only a-z, A-Z, 0-9)
    /// for the key parameter, avoiding URL encoding issues.
    /// </summary>
    private static readonly Gen<string> GenKey =
        from length in Gen.Choose(1, 50)
        from chars in Gen.ArrayOf(length, Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z',
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
            'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
            'U', 'V', 'W', 'X', 'Y', 'Z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
        select new string(chars);

    /// <summary>
    /// Generates a positive long (1 to long.MaxValue/2) for the expires timestamp.
    /// </summary>
    private static readonly Gen<long> GenExpires =
        Gen.Choose(1, int.MaxValue).Select(i => (long)i);

    /// <summary>
    /// Generates a valid NxmUri with all fields populated.
    /// </summary>
    private static readonly Gen<NxmUri> GenNxmUri =
        from domain in GenGameDomain
        from modId in GenPositiveInt
        from fileId in GenPositiveInt
        from key in GenKey
        from expires in GenExpires
        select new NxmUri(domain, modId, fileId, key, expires);

    // ── Property 2 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 2: NXM URI parse round-trip
    // **Validates: Requirements 8.1, 8.2, 8.4, 6.3**

    /// <summary>
    /// For any valid NxmUri (non-empty alphanumeric game domain, positive mod ID,
    /// positive file ID, non-empty alphanumeric key, positive expires timestamp),
    /// formatting it with NxmProtocolHandler.Format and then parsing it back with
    /// NxmProtocolHandler.Parse produces an equivalent NxmUri with all fields matching.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NxmUri_FormatThenParse_RoundTrip_PreservesAllFields()
    {
        var handler = new NxmProtocolHandler();

        return Prop.ForAll(
            Arb.From(GenNxmUri),
            (NxmUri original) =>
            {
                // Act: format to URI string then parse back
                var uriString = handler.Format(original);
                var parsed = handler.Parse(uriString);

                // Assert: parsed is not null
                bool notNull = parsed is not null;

                // Assert: all fields match
                bool domainMatches = parsed?.GameDomain == original.GameDomain;
                bool modIdMatches = parsed?.ModId == original.ModId;
                bool fileIdMatches = parsed?.FileId == original.FileId;
                bool keyMatches = parsed?.Key == original.Key;
                bool expiresMatches = parsed?.Expires == original.Expires;

                return (notNull && domainMatches && modIdMatches && fileIdMatches && keyMatches && expiresMatches)
                    .Label($"notNull={notNull}, domainMatches={domainMatches}, modIdMatches={modIdMatches}, " +
                           $"fileIdMatches={fileIdMatches}, keyMatches={keyMatches}, expiresMatches={expiresMatches} " +
                           $"(original=({original.GameDomain}, {original.ModId}, {original.FileId}, {original.Key}, {original.Expires}), " +
                           $"parsed=({parsed?.GameDomain}, {parsed?.ModId}, {parsed?.FileId}, {parsed?.Key}, {parsed?.Expires}), " +
                           $"uri={uriString})");
            });
    }
}
