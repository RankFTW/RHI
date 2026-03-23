using System.Reflection;
using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that UI handler classes have no Display Commander methods.
/// Feature: dc-removal, Property 6: UI handler classes have no DC methods
/// **Validates: Requirements 8.2, 8.3**
/// </summary>
public class UiHandlerNoDcMethodsPropertyTests
{
    /// <summary>
    /// DC method names that must not exist on InstallEventHandler.
    /// </summary>
    private static readonly string[] InstallEventHandlerDcMethods =
    [
        "InstallDcButton_Click",
        "UninstallDcButton_Click",
        "DeployDcModeButton_Click",
    ];

    /// <summary>
    /// DC method names that must not exist on MainWindow.
    /// </summary>
    private static readonly string[] MainWindowDcMethods =
    [
        "SyncDcDllPickerText",
        "DcDllPicker_SelectionChanged",
        "DcDllPicker_TextSubmitted",
        "UpdateAllDc_Click",
        "DcIniButton_Click",
    ];

    /// <summary>
    /// Combined list of (TypeName, MethodName) pairs for all DC methods across both UI handler classes.
    /// </summary>
    private static readonly (string TypeName, string MethodName)[] AllDcMethodEntries =
        InstallEventHandlerDcMethods
            .Select(m => ("InstallEventHandler", m))
            .Concat(MainWindowDcMethods.Select(m => ("MainWindow", m)))
            .ToArray();

    private static readonly Gen<(string TypeName, string MethodName)> GenDcMethodEntry =
        Gen.Elements(AllDcMethodEntries);

    // ── Property 6: UI handler classes have no DC methods ─────────────────────────
    // Feature: dc-removal, Property 6: UI handler classes have no DC methods
    // **Validates: Requirements 8.2, 8.3**
    [Property(MaxTest = 100)]
    public Property UI_Handler_Classes_Have_No_DC_Methods()
    {
        var installEventHandlerType = typeof(RenoDXCommander.InstallEventHandler);
        var mainWindowType = typeof(RenoDXCommander.MainWindow);

        const BindingFlags allMembers =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        return Prop.ForAll(
            Arb.From(GenDcMethodEntry),
            ((string TypeName, string MethodName) entry) =>
            {
                var type = entry.TypeName == "InstallEventHandler"
                    ? installEventHandlerType
                    : mainWindowType;

                var methods = type.GetMember(entry.MethodName, allMembers);
                return (methods.Length == 0)
                    .Label($"DC method '{entry.MethodName}' still exists on {entry.TypeName}");
            });
    }
}
