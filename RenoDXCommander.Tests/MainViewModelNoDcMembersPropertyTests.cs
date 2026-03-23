using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that MainViewModel has no Display Commander members.
/// Feature: dc-removal, Property 3: MainViewModel has no DC members
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public class MainViewModelNoDcMembersPropertyTests
{
    /// <summary>
    /// All DC member names that must not exist on MainViewModel.
    /// Includes properties, commands, and methods that were removed as part of DC removal.
    /// </summary>
    private static readonly string[] DcMemberNames =
    [
        // ── Properties removed (Requirement 6.1) ──────────────────────────────
        "DcModeEnabled",
        "DcDllFileName",
        "DcLegacyMode",
        "DcLegacySettingsVisibility",
        "DcLegacyHiddenVisibility",
        "DcDllPickerVisibility",

        // ── Commands removed (Requirement 6.2) ────────────────────────────────
        "InstallDcCommand",
        "UninstallDcCommand",

        // ── Methods removed (Requirement 6.3) ─────────────────────────────────
        "InstallDcAsync",
        "UninstallDc",
        "ApplyDcModeSwitch",
        "ApplyDcModeSwitchForCard",
        "ResolveEffectiveDcMode",
        "UpdateAllDcAsync",
        "OnDcModeEnabledChanged",
        "OnDcLegacyModeChanged",
        "OnDcDllFileNameChanged",

        // ── Per-game DC accessors removed (Requirement 6.4) ───────────────────
        "GetPerGameDcModeOverride",
        "SetPerGameDcModeOverride",
        "GetDcCustomDllFileName",
        "SetDcCustomDllFileName",
    ];

    private static readonly Gen<string> GenDcMemberName =
        Gen.Elements(DcMemberNames);

    // ── Property 3: MainViewModel has no DC members ───────────────────────────────
    // Feature: dc-removal, Property 3: MainViewModel has no DC members
    // **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    [Property(MaxTest = 100)]
    public Property MainViewModel_Has_No_DC_Members()
    {
        var type = typeof(MainViewModel);
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
                    .Label($"DC member '{memberName}' still exists on MainViewModel");
            });
    }
}
