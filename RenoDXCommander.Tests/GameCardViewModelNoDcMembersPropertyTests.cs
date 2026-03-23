using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that GameCardViewModel has no Display Commander members.
/// Feature: dc-removal, Property 1: GameCardViewModel has no DC members
/// **Validates: Requirements 1.1, 1.3**
/// </summary>
public class GameCardViewModelNoDcMembersPropertyTests
{
    /// <summary>
    /// All DC member names that must not exist on GameCardViewModel.
    /// Includes fields from the main class and all computed properties/methods
    /// from the deleted GameCardViewModel.DisplayCommander.cs partial class.
    /// </summary>
    private static readonly string[] DcMemberNames =
    [
        // ── Fields removed from GameCardViewModel.cs ──────────────────────────
        "DcStatus",
        "DcIsInstalling",
        "DcProgress",
        "DcActionMessage",
        "DcInstalledFile",
        "DcInstalledVersion",
        "DcCustomDllFileName",
        "DcRecord",
        "PerGameDcMode",
        "DcModeExcluded",
        "ExcludeFromUpdateAllDc",
        "DcLegacyMode",
        "RsBlockedByDcMode",

        // ── Backing fields (source-generator naming convention) ───────────────
        "_dcStatus",
        "_dcIsInstalling",
        "_dcProgress",
        "_dcActionMessage",
        "_dcInstalledFile",
        "_dcInstalledVersion",
        "_dcCustomDllFileName",
        "_rsBlockedByDcMode",

        // ── Computed properties from deleted DisplayCommander.cs partial ──────
        "DcStatusDot",
        "DcActionLabel",
        "DcBtnBackground",
        "DcBtnForeground",
        "DcBtnBorderBrush",
        "DcProgressVisibility",
        "DcMessageVisibility",
        "DcInstalledVisible",
        "DcDeleteVisibility",
        "DcStatusText",
        "DcStatusColor",
        "DcShortAction",
        "IsDcNotInstalling",
        "IsDcInstalled",
        "DcInstallCornerRadius",
        "DcInstallBorderThickness",
        "DcInstallMargin",
        "DcIniExists",
        "DcIniCornerRadius",
        "DcIniBorderThickness",
        "DcIniMargin",
        "DcRowVisibility",
        "DcDeleteVisible",

        // ── Methods from deleted DisplayCommander.cs partial ──────────────────
        "NotifyDcStatusDependents",
        "NotifyDcIsInstallingDependents",
        "OnDcStatusChanged",
        "OnDcIsInstallingChanged",
        "OnDcActionMessageChanged",

        // ── Card-level DC computed properties from UI.cs partial ──────────────
        "CardDcStatusDot",
        "CardDcInstallEnabled",
    ];

    private static readonly Gen<string> GenDcMemberName =
        Gen.Elements(DcMemberNames);

    // ── Property 1: GameCardViewModel has no DC members ───────────────────────────
    // Feature: dc-removal, Property 1: GameCardViewModel has no DC members
    // **Validates: Requirements 1.1, 1.3**
    [Property(MaxTest = 100)]
    public Property GameCardViewModel_Has_No_DC_Members()
    {
        var type = typeof(GameCardViewModel);
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
                    .Label($"DC member '{memberName}' still exists on GameCardViewModel");
            });
    }
}
