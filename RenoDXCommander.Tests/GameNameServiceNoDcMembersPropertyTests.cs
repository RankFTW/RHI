using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that GameNameService has no Display Commander members.
/// Feature: dc-removal, Property 4: GameNameService has no DC members
/// **Validates: Requirements 4.1, 4.2**
/// </summary>
public class GameNameServiceNoDcMembersPropertyTests
{
    /// <summary>
    /// All DC member names that must not exist on GameNameService.
    /// Includes private fields and public properties that were removed as part of DC removal.
    /// </summary>
    private static readonly string[] DcMemberNames =
    [
        // ── Fields removed (Requirement 4.1) ──────────────────────────────────
        "_perGameDcModeOverride",
        "_dcCustomDllFileNames",
        "_updateAllExcludedDc",

        // ── Properties removed (Requirement 4.2) ─────────────────────────────
        "PerGameDcModeOverride",
        "DcCustomDllFileNames",
        "UpdateAllExcludedDc",
    ];

    private static readonly Gen<string> GenDcMemberName =
        Gen.Elements(DcMemberNames);

    // ── Property 4: GameNameService has no DC members ─────────────────────────
    // Feature: dc-removal, Property 4: GameNameService has no DC members
    // **Validates: Requirements 4.1, 4.2**
    [Property(MaxTest = 100)]
    public Property GameNameService_Has_No_DC_Members()
    {
        var type = typeof(GameNameService);
        const BindingFlags allMembers =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        return Prop.ForAll(
            Arb.From(GenDcMemberName),
            (string memberName) =>
            {
                var members = type.GetMember(memberName, allMembers);
                return (members.Length == 0)
                    .Label($"DC member '{memberName}' still exists on GameNameService");
            });
    }
}
