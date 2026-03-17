using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for GameDetectionService.NormalizeName().
/// </summary>
public class NormalizeNamePropertyTests
{
    private readonly GameDetectionService _service = new();

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates game-name-like strings with trademark symbols, diacritics,
    /// mixed case, colons, dashes, and other characters commonly found in game titles.
    /// </summary>
    private static readonly Gen<string> GenGameName =
        Gen.Elements(
            "Cyberpunk 2077", "ELDEN RING", "NieR:Automata™",
            "STAR WARS™ Jedi: Fallen Order", "Baldur's Gate 3",
            "The Witcher® 3: Wild Hunt", "Résident Evil 4",
            "Halo Infinite©", "FINAL FANTASY VII REMAKE",
            "Pokémon Legends: Arceus", "Señor Game: Édition",
            "", "   ", "a", "123", "---", "™®©");

    /// <summary>
    /// Combines FsCheck's built-in string generator with game-name-like strings
    /// to cover both random and realistic inputs.
    /// </summary>
    private static readonly Gen<string> GenInput =
        Gen.OneOf(
            Arb.Default.NonNull<string>().Generator.Select(x => x.Get),
            GenGameName);

    // ── Property 4: NormalizeName is idempotent ───────────────────────────────────
    // Feature: codebase-optimization, Property 4: NormalizeName is idempotent
    // **Validates: Requirements 10.3**
    [Property(MaxTest = 100)]
    public Property NormalizeName_IsIdempotent()
    {
        return Prop.ForAll(
            Arb.From(GenInput),
            (string input) =>
            {
                var once = _service.NormalizeName(input);
                var twice = _service.NormalizeName(once);

                return (once == twice)
                    .Label($"NormalizeName(\"{input}\") = \"{once}\", " +
                           $"NormalizeName(\"{once}\") = \"{twice}\"");
            });
    }
}
