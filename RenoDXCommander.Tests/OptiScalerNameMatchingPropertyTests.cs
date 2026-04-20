using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for OptiScaler game name matching with manifest overrides.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
///
/// **Validates: Requirements 7.2**
/// </summary>
public class OptiScalerNameMatchingPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static Gen<string> GenGameName() =>
        Gen.Elements(
            "Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Starfield",
            "Resident Evil 4", "Final Fantasy XVI", "Hogwarts Legacy",
            "Spider-Man Remastered", "God of War", "Horizon Zero Dawn",
            "Death Stranding", "Control", "Alan Wake 2", "Black Myth: Wukong");

    private static Gen<string> GenWikiName() =>
        Gen.Elements(
            "Cyberpunk 2077", "ELDEN RING", "Baldurs Gate 3", "Starfield",
            "RE4", "FFXVI", "Hogwarts Legacy",
            "Marvel's Spider-Man Remastered", "God of War (2018)", "Horizon Zero Dawn Complete Edition",
            "Death Stranding: Director's Cut", "Control Ultimate Edition", "Alan Wake II", "Black Myth Wukong");

    private static Gen<OptiScalerCompatEntry> GenCompatEntry(string gameName) =>
        from status in Gen.Elements("Working", "Not Working", "Partial")
        from upscalerCount in Gen.Choose(0, 3)
        from upscalers in Gen.ListOf(upscalerCount, Gen.Elements("DLSS", "FSR", "XeSS"))
        from hasNotes in Arb.Generate<bool>()
        from hasUrl in Arb.Generate<bool>()
        select new OptiScalerCompatEntry
        {
            GameName = gameName,
            Status = status,
            Upscalers = upscalers.Distinct().ToList(),
            Notes = hasNotes ? "Some compatibility notes" : null,
            DetailPageUrl = hasUrl ? $"https://github.com/optiscaler/OptiScaler/wiki/{gameName.Replace(" ", "-")}" : null
        };

    // ── Property 6: OptiScaler game name matching with overrides ──────────────────

    /// <summary>
    /// **Validates: Requirements 7.2**
    ///
    /// For any game name and OptiScaler wiki dataset, when an optiScalerWikiNames
    /// override maps the game name to a wiki name, the matcher SHALL use the override
    /// name for lookup. When no override exists, the matcher SHALL use the original
    /// game name. In both cases, a matching entry SHALL be returned if one exists
    /// in the wiki data.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OptiScaler_NameMatching_WithOverrides()
    {
        var gen = from gameName in GenGameName()
                  from wikiName in GenWikiName()
                  from useOverride in Arb.Generate<bool>()
                  from entry in GenCompatEntry(wikiName)
                  select (gameName, wikiName, useOverride, entry);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, wikiName, useOverride, entry) = tuple;

            // Arrange: wiki data keyed by wikiName
            var osWikiData = new OptiScalerWikiData
            {
                StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [wikiName] = entry
                }
            };

            // Arrange: manifest with optional override
            RemoteManifest? manifest = null;
            if (useOverride)
            {
                manifest = new RemoteManifest
                {
                    OptiScalerWikiNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [gameName] = wikiName
                    }
                };
            }

            // Act: resolve the wiki name
            var resolvedName = AddonInfoResolver.ResolveOptiScalerWikiName(gameName, manifest);

            // Assert
            if (useOverride)
            {
                // With override: should use the wiki name from the override
                var overrideUsed = resolvedName == wikiName;
                // And the entry should be found in wiki data
                var entryFound = osWikiData.StandardCompat.ContainsKey(resolvedName);
                return (overrideUsed && entryFound)
                    .Label($"override: resolved='{resolvedName}', expected='{wikiName}', found={entryFound}");
            }
            else
            {
                // Without override: should use the original game name
                var originalUsed = resolvedName == gameName;
                return originalUsed
                    .Label($"no override: resolved='{resolvedName}', expected='{gameName}'");
            }
        });
    }

    /// <summary>
    /// When an override maps to a wiki name that exists in the data, the entry SHALL be returned.
    /// When no override exists and the game name directly matches, the entry SHALL also be returned.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OptiScaler_NameMatching_CaseInsensitive()
    {
        var gen = from gameName in GenGameName()
                  select gameName;

        return Prop.ForAll(gen.ToArbitrary(), gameName =>
        {
            // Arrange: wiki data with the game name in different case
            var upperName = gameName.ToUpperInvariant();
            var entry = new OptiScalerCompatEntry
            {
                GameName = upperName,
                Status = "Working",
                Upscalers = ["DLSS"]
            };

            var osWikiData = new OptiScalerWikiData
            {
                StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [upperName] = entry
                }
            };

            // Act: resolve using original case (no override)
            var resolvedName = AddonInfoResolver.ResolveOptiScalerWikiName(gameName, null);

            // The resolved name should be the original game name (no override)
            var nameOk = resolvedName == gameName;

            // But the case-insensitive dictionary should still find the entry
            var entryFound = osWikiData.StandardCompat.ContainsKey(resolvedName);

            return (nameOk && entryFound)
                .Label($"resolved='{resolvedName}', game='{gameName}', upper='{upperName}', found={entryFound}");
        });
    }
}
