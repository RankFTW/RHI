using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based and unit tests for file structure and naming conventions
/// after the large-file-refactor spec.
/// Feature: large-file-refactor
/// </summary>
public class LargeFileRefactorStructurePropertyTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the RenoDXCommander project root by navigating up from the
    /// test assembly output directory (bin/x64/Debug/net8.0-windows...).
    /// </summary>
    private static string GetProjectRoot()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(LargeFileRefactorStructurePropertyTests).Assembly.Location)!;
        // Navigate up from bin/x64/Debug/net8.0-windows10.0.19041.0 to the repo root
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "RenoDXCommander");
    }

    /// <summary>
    /// Known partial class files created by this refactoring, expressed as
    /// (subdirectory relative to RenoDXCommander, filename) tuples.
    /// </summary>
    private static readonly (string SubDir, string FileName, string ClassName, string Concern)[] KnownPartialFiles =
    {
        ("ViewModels", "MainViewModel.Init.cs",            "MainViewModel",        "Init"),
        ("ViewModels", "MainViewModel.Install.cs",         "MainViewModel",        "Install"),
        ("ViewModels", "MainViewModel.Update.cs",          "MainViewModel",        "Update"),
        ("ViewModels", "MainViewModel.Settings.cs",        "MainViewModel",        "Settings"),
        ("ViewModels", "MainViewModel.GameMatching.cs",    "MainViewModel",        "GameMatching"),
        ("Services",   "GameDetectionService.Steam.cs",    "GameDetectionService", "Steam"),
        ("Services",   "GameDetectionService.Platform.cs", "GameDetectionService", "Platform"),
        ("Services",   "GameDetectionService.Xbox.cs",     "GameDetectionService", "Xbox"),
        ("Services",   "ShaderPackService.Download.cs",    "ShaderPackService",    "Download"),
        ("Services",   "ShaderPackService.Deploy.cs",      "ShaderPackService",    "Deploy"),
        ("Services",   "AuxInstallService.Ini.cs",         "AuxInstallService",    "Ini"),
        ("Services",   "AuxInstallService.DllIdentification.cs", "AuxInstallService", "DllIdentification"),
        ("Services",   "AuxInstallService.Install.cs",     "AuxInstallService",    "Install"),
        ("",           "CardBuilder.Flyout.cs",            "CardBuilder",          "Flyout"),
        ("",           "DetailPanelBuilder.Components.cs", "DetailPanelBuilder",   "Components"),
        ("",           "DetailPanelBuilder.Overrides.cs",  "DetailPanelBuilder",   "Overrides"),
        ("",           "DialogService.Update.cs",          "DialogService",        "Update"),
        ("",           "DialogService.Game.cs",            "DialogService",        "Game"),
        ("",           "DragDropHandler.Exe.cs",           "DragDropHandler",      "Exe"),
        ("",           "DragDropHandler.Addon.cs",         "DragDropHandler",      "Addon"),
        ("",           "MainWindow.Events.cs",             "MainWindow",           "Events"),
        ("",           "MainWindow.UISync.cs",             "MainWindow",           "UISync"),
    };

    /// <summary>
    /// Maps each class name to its primary (original) filename and subdirectory.
    /// </summary>
    private static readonly (string SubDir, string PrimaryFileName)[] PrimaryFiles =
    {
        ("ViewModels", "MainViewModel.cs"),
        ("Services",   "GameDetectionService.cs"),
        ("Services",   "ShaderPackService.cs"),
        ("Services",   "AuxInstallService.cs"),
        ("",           "CardBuilder.cs"),
        ("",           "DetailPanelBuilder.cs"),
        ("",           "DialogService.cs"),
        ("",           "DragDropHandler.cs"),
        ("",           "MainWindow.xaml.cs"),
    };

    /// <summary>
    /// Generator that picks one of the known partial files by index.
    /// </summary>
    private static readonly Gen<int> GenPartialFileIndex =
        Gen.Choose(0, KnownPartialFiles.Length - 1);

    // ── Property 3: File naming and location convention ───────────────────────
    // Feature: large-file-refactor, Property 3: File naming and location convention
    // **Validates: Requirements 12.1, 12.2, 12.3**

    [Property(MaxTest = 100)]
    public Property PartialFiles_FollowNamingConvention_ClassNameDotConcernDotCs()
    {
        return Prop.ForAll(
            Arb.From(GenPartialFileIndex),
            (int idx) =>
            {
                var (subDir, fileName, className, concern) = KnownPartialFiles[idx];
                var expectedName = $"{className}.{concern}.cs";
                var matchesPattern = fileName == expectedName;

                return matchesPattern
                    .Label($"File '{fileName}' should match pattern '{{ClassName}}.{{Concern}}.cs' = '{expectedName}'");
            });
    }

    [Property(MaxTest = 100)]
    public Property PartialFiles_ResideInSameDirectoryAsPrimaryFile()
    {
        var projectRoot = GetProjectRoot();

        return Prop.ForAll(
            Arb.From(GenPartialFileIndex),
            (int idx) =>
            {
                var (subDir, fileName, className, _) = KnownPartialFiles[idx];
                var dir = string.IsNullOrEmpty(subDir)
                    ? projectRoot
                    : Path.Combine(projectRoot, subDir);
                var fullPath = Path.Combine(dir, fileName);
                var exists = File.Exists(fullPath);

                return exists
                    .Label($"Partial file '{fileName}' should exist at '{fullPath}'");
            });
    }

    [Property(MaxTest = 100)]
    public Property PrimaryFiles_RetainOriginalFilename()
    {
        var projectRoot = GetProjectRoot();
        var genPrimaryIdx = Gen.Choose(0, PrimaryFiles.Length - 1);

        return Prop.ForAll(
            Arb.From(genPrimaryIdx),
            (int idx) =>
            {
                var (subDir, primaryFileName) = PrimaryFiles[idx];
                var dir = string.IsNullOrEmpty(subDir)
                    ? projectRoot
                    : Path.Combine(projectRoot, subDir);
                var fullPath = Path.Combine(dir, primaryFileName);
                var exists = File.Exists(fullPath);

                return exists
                    .Label($"Primary file '{primaryFileName}' should exist at '{fullPath}'");
            });
    }

    // ── Property 4: File size constraints ─────────────────────────────────────
    // Feature: large-file-refactor, Property 4: File size constraints
    // **Validates: Requirements 12.4, 13.1**

    [Property(MaxTest = 100)]
    public Property AllCsFiles_InProject_HaveFewerThan800Lines()
    {
        var projectRoot = GetProjectRoot();
        var csFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();

        var genFileIdx = Gen.Choose(0, csFiles.Length - 1);

        return Prop.ForAll(
            Arb.From(genFileIdx),
            (int idx) =>
            {
                var file = csFiles[idx];
                var lineCount = File.ReadAllLines(file).Length;
                var relativePath = Path.GetRelativePath(projectRoot, file);

                return (lineCount < 800)
                    .Label($"File '{relativePath}' has {lineCount} lines (must be < 800)");
            });
    }

    [Property(MaxTest = 100)]
    public Property PartialFiles_HaveFewerThan600Lines()
    {
        var projectRoot = GetProjectRoot();

        return Prop.ForAll(
            Arb.From(GenPartialFileIndex),
            (int idx) =>
            {
                var (subDir, fileName, _, _) = KnownPartialFiles[idx];
                var dir = string.IsNullOrEmpty(subDir)
                    ? projectRoot
                    : Path.Combine(projectRoot, subDir);
                var fullPath = Path.Combine(dir, fileName);
                var lineCount = File.ReadAllLines(fullPath).Length;

                return (lineCount < 600)
                    .Label($"Partial file '{fileName}' has {lineCount} lines (must be < 600)");
            });
    }

    // ── Property: Partial keyword presence ────────────────────────────────────
    // Feature: large-file-refactor
    // **Validates: Requirements 11.5, 11.6**

    [Property(MaxTest = 100)]
    public Property PartialFiles_ContainPartialClassKeyword()
    {
        var projectRoot = GetProjectRoot();

        return Prop.ForAll(
            Arb.From(GenPartialFileIndex),
            (int idx) =>
            {
                var (subDir, fileName, className, _) = KnownPartialFiles[idx];
                var dir = string.IsNullOrEmpty(subDir)
                    ? projectRoot
                    : Path.Combine(projectRoot, subDir);
                var fullPath = Path.Combine(dir, fileName);
                var content = File.ReadAllText(fullPath);

                var hasPartialClass = Regex.IsMatch(content, @"\bpartial\s+class\s+" + Regex.Escape(className) + @"\b");

                return hasPartialClass
                    .Label($"File '{fileName}' should contain 'partial class {className}'");
            });
    }

    [Property(MaxTest = 100)]
    public Property PartialFiles_ContainCorrectNamespaceDeclaration()
    {
        var projectRoot = GetProjectRoot();

        return Prop.ForAll(
            Arb.From(GenPartialFileIndex),
            (int idx) =>
            {
                var (subDir, fileName, _, _) = KnownPartialFiles[idx];
                var dir = string.IsNullOrEmpty(subDir)
                    ? projectRoot
                    : Path.Combine(projectRoot, subDir);
                var fullPath = Path.Combine(dir, fileName);
                var content = File.ReadAllText(fullPath);

                // Determine expected namespace based on subdirectory
                var expectedNamespace = string.IsNullOrEmpty(subDir)
                    ? "RenoDXCommander"
                    : $"RenoDXCommander.{subDir}";

                var hasNamespace = content.Contains($"namespace {expectedNamespace}")
                                || content.Contains($"namespace {expectedNamespace};");

                return hasNamespace
                    .Label($"File '{fileName}' should declare namespace '{expectedNamespace}'");
            });
    }

    // ── Unit tests: Unsplit files ─────────────────────────────────────────────
    // Feature: large-file-refactor
    // **Validates: Requirements 10.1, 10.2**

    [Fact]
    public void ModInstallService_RemainsASingleFile_NoPartialClassFiles()
    {
        var projectRoot = GetProjectRoot();
        var servicesDir = Path.Combine(projectRoot, "Services");
        var partialFiles = Directory.GetFiles(servicesDir, "ModInstallService.*.cs");

        Assert.Empty(partialFiles);
    }

    [Fact]
    public void OverridesFlyoutBuilder_RemainsASingleFile_NoPartialClassFiles()
    {
        var projectRoot = GetProjectRoot();
        var partialFiles = Directory.GetFiles(projectRoot, "OverridesFlyoutBuilder.*.cs");

        Assert.Empty(partialFiles);
    }
}
