using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that AuxInstallService has no Display Commander members.
/// Feature: dc-removal, Property 2: AuxInstallService has no DC members
/// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 9.3, 12.3**
/// </summary>
public class AuxInstallServiceNoDcMembersPropertyTests
{
    /// <summary>
    /// All DC member names that must not exist on AuxInstallService.
    /// Includes constants, properties, and methods that were removed as part of DC removal.
    /// </summary>
    private static readonly string[] DcMemberNames =
    [
        // ── Constants removed (Requirement 2.2) ───────────────────────────────
        "DcUrl",
        "DcUrl32",
        "DcCacheFile",
        "DcCacheFile32",
        "DcNormalName",
        "DcNormalName32",
        "DcDxgiName",
        "DcWinmmName",
        "DcReShadeFolderPath",

        // ── Property removed (Requirement 2.4) ───────────────────────────────
        "DcIniPath",

        // ── Constant removed (Requirement 2.5) ───────────────────────────────
        "TypeDc",

        // ── Constants removed (Requirement 12.3) ──────────────────────────────
        "RsDcModeName",
        "RsDcModeName32",

        // ── Methods removed (Requirement 2.3, 9.3) ───────────────────────────
        "InstallDcAsync",
        "CopyDcIni",
        "SyncReShadeToDisplayCommander",
        "IsDcFileStrict",
    ];

    private static readonly Gen<string> GenDcMemberName =
        Gen.Elements(DcMemberNames);

    // ── Property 2: AuxInstallService has no DC members ───────────────────────────
    // Feature: dc-removal, Property 2: AuxInstallService has no DC members
    // **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 9.3, 12.3**
    [Property(MaxTest = 100)]
    public Property AuxInstallService_Has_No_DC_Members()
    {
        var type = typeof(AuxInstallService);
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
                    .Label($"DC member '{memberName}' still exists on AuxInstallService");
            });
    }
}
