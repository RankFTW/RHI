using System.Reflection;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Reflection-based verification tests for the MainWindow breakup refactoring.
/// These tests verify the structural correctness of the extraction without
/// instantiating any WinUI classes.
/// </summary>
public class MainWindowBreakupTests
{
    private static readonly Assembly _assembly = typeof(MainWindow).Assembly;

    // ── Property 1: Extracted classes contain all required methods ────────────────
    // Feature: mainwindow-breakup, Property 1: Extracted classes contain all required methods
    // Validates: Requirements 2.1, 3.1, 4.1, 5.1, 8.1, 8.2

    [Fact]
    public void OverridesFlyoutBuilder_ExistsInNamespace_WithRequiredMethods()
    {
        var type = _assembly.GetType("RenoDXCommander.OverridesFlyoutBuilder");
        Assert.NotNull(type);

        var requiredMethods = new[] { "OpenOverridesFlyout" };
        foreach (var name in requiredMethods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(method != null, $"OverridesFlyoutBuilder should have method '{name}'");
        }
    }

    [Fact]
    public void DialogService_ExistsInNamespace_WithRequiredMethods()
    {
        var type = _assembly.GetType("RenoDXCommander.DialogService");
        Assert.NotNull(type);

        var requiredMethods = new[]
        {
            "CheckForAppUpdateAsync",
            "ShowUpdateDialogAsync",
            "DownloadAndInstallUpdateAsync",
            "ShowPatchNotesIfNewVersionAsync",
            "ShowPatchNotesDialogAsync",
            "ShowForeignDxgiConfirmDialogAsync",
            "ShowForeignWinmmConfirmDialogAsync",
            "NotesButton_Click",
            "ShowUeExtendedWarningAsync"
        };

        foreach (var name in requiredMethods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(method != null, $"DialogService should have method '{name}'");
        }
    }

    [Fact]
    public void SettingsHandler_ExistsInNamespace_WithRequiredMethods()
    {
        var type = _assembly.GetType("RenoDXCommander.SettingsHandler");
        Assert.NotNull(type);

        var requiredMethods = new[]
        {
            "SettingsButton_Click",
            "SettingsBack_Click",
            "SkipUpdateToggle_Toggled",
            "BetaOptInToggle_Toggled",
            "VerboseLoggingToggle_Toggled",
            "OpenLogsFolder_Click",
            "OpenDownloadsFolder_Click"
        };

        foreach (var name in requiredMethods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(method != null, $"SettingsHandler should have method '{name}'");
        }
    }

    [Fact]
    public void InstallEventHandler_ExistsInNamespace_WithRequiredMethods()
    {
        var type = _assembly.GetType("RenoDXCommander.InstallEventHandler");
        Assert.NotNull(type);

        var requiredMethods = new[]
        {
            "CombinedInstallButton_Click",
            "InstallButton_Click",
            "Install64Button_Click",
            "Install32Button_Click",
            "EnsurePathAndInstall",
            "UninstallButton_Click",
            "InstallRsButton_Click",
            "UninstallRsButton_Click",
            "InstallLumaButton_Click",
            "UninstallLumaButton_Click",
            "ChooseShadersButton_Click",
            "LumaToggle_Click",
            "SwitchToLumaButton_Click",
            "UeExtendedFlyoutItem_Click"
        };

        foreach (var name in requiredMethods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(method != null, $"InstallEventHandler should have method '{name}'");
        }
    }

    [Fact]
    public void WindowStateManager_ExistsInNamespace_WithRequiredMethods()
    {
        var type = _assembly.GetType("RenoDXCommander.WindowStateManager");
        Assert.NotNull(type);

        var requiredMethods = new[]
        {
            "InstallWndProcSubclass",
            "EnableDragAccept",
            "TryRestoreWindowBounds",
            "CaptureCurrentBounds",
            "RestoreWindowBounds",
            "SaveWindowBounds",
            "WndProc",
            "HandleWin32Drop"
        };

        foreach (var name in requiredMethods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(method != null, $"WindowStateManager should have method '{name}'");
        }
    }

    // ── Property 2: Duplicate drag-drop methods removed from MainWindow ──────────
    // Feature: mainwindow-breakup, Property 2: Duplicate drag-drop methods removed from MainWindow
    // Validates: Requirements 6.1

    [Fact]
    public void MainWindow_DoesNotContain_RemovedDragDropMethods()
    {
        var type = typeof(MainWindow);

        // These methods must NOT exist in MainWindow at all
        var removedMethods = new[]
        {
            "ProcessDroppedExe",
            "ProcessDroppedArchive",
            "ProcessDroppedAddon",
            "InferGameRoot",
            "LooksLikeGameRoot",
            "InferGameName",
            "CleanUnrealExeName",
            "CleanFolderName"
        };

        foreach (var name in removedMethods)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.True(method == null, $"MainWindow should NOT contain method '{name}' — it should be in DragDropHandler");
        }
    }

    // ── Property 4: MainWindow line count reduced ────────────────────────────────
    // Feature: mainwindow-breakup, Property 4: MainWindow line count reduced
    // Validates: Requirements 8.4

    [Fact]
    public void MainWindow_IsUnder1200Lines()
    {
        // Navigate from the test assembly location to the source file
        // The source file is at RenoDXCommander/MainWindow.xaml.cs relative to the repo root
        var testDir = Path.GetDirectoryName(typeof(MainWindowBreakupTests).Assembly.Location)!;
        // Walk up from bin/x64/Debug/net8.0-windows10.0.19041.0 to the repo root
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        var mainWindowPath = Path.Combine(repoRoot, "RenoDXCommander", "MainWindow.xaml.cs");

        Assert.True(File.Exists(mainWindowPath), $"MainWindow.xaml.cs not found at {mainWindowPath}");

        var lines = File.ReadAllLines(mainWindowPath);
        Assert.True(lines.Length < 1200,
            $"MainWindow.xaml.cs should be under 1200 lines but has {lines.Length} lines");
    }
}
