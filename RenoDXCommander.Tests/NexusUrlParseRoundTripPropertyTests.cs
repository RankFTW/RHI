using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for NexusUrlParser Format→Parse round-trip.
/// Feature: nexus-api-integration, Property 1: Nexus URL parse round-trip
/// </summary>
public class NexusUrlParseRoundTripPropertyTests
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
    /// Generates an optional positive integer for mod ID.
    /// </summary>
    private static readonly Gen<int?> GenModId =
        Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Choose(1, int.MaxValue - 1).Select(i => (int?)i));

    /// <summary>
    /// Generates a valid NexusModReference with lowercase alphanumeric domain and optional positive mod ID.
    /// </summary>
    private static readonly Gen<NexusModReference> GenNexusModReference =
        from domain in GenGameDomain
        from modId in GenModId
        select new NexusModReference(domain, modId);

    // ── Property 1 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 1: Nexus URL parse round-trip
    // **Validates: Requirements 2.1, 2.2, 2.4**

    /// <summary>
    /// For any valid NexusModReference (non-empty lowercase alphanumeric game domain,
    /// optional positive mod ID), formatting it into a URL string with NexusUrlParser.Format
    /// and then parsing it back with NexusUrlParser.Parse produces an equivalent NexusModReference.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NexusUrl_FormatThenParse_RoundTrip_PreservesReference()
    {
        return Prop.ForAll(
            Arb.From(GenNexusModReference),
            (NexusModReference original) =>
            {
                // Act: format to URL then parse back
                var url = NexusUrlParser.Format(original);
                var parsed = NexusUrlParser.Parse(url);

                // Assert: parsed is not null
                bool notNull = parsed is not null;

                // Assert: fields match
                bool domainMatches = parsed?.GameDomain == original.GameDomain;
                bool modIdMatches = parsed?.ModId == original.ModId;

                return (notNull && domainMatches && modIdMatches)
                    .Label($"notNull={notNull}, domainMatches={domainMatches}, modIdMatches={modIdMatches} " +
                           $"(original=({original.GameDomain}, {original.ModId}), " +
                           $"parsed=({parsed?.GameDomain}, {parsed?.ModId}), url={url})");
            });
    }
}
