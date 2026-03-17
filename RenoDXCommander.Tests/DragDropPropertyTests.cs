using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DragDropHandler.InferGameRoot and InferGameName.
/// Feature: codebase-optimization, Property 6: InferGameRoot and InferGameName produce non-empty results
/// </summary>
public class DragDropPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates valid non-empty directory paths that won't cause Path API issues.
    /// Uses realistic Windows-style paths with safe characters.
    /// </summary>
    private static readonly Gen<string> GenDirectoryPath =
        from drive in Gen.Elements("C", "D", "E")
        from count in Gen.Choose(1, 5)
        from segments in Gen.ArrayOf(count,
            Gen.Elements("Games", "SteamLibrary", "common", "Program Files", "GOG Galaxy",
                         "Epic Games", "MyGame", "Binaries", "Win64", "GameFolder",
                         "Cyberpunk 2077", "Elden Ring", "Starfield", "Data"))
        select $@"{drive}:\{string.Join(@"\", segments)}";

    /// <summary>
    /// Generates valid exe file paths (directory + filename.exe).
    /// </summary>
    private static readonly Gen<string> GenExePath =
        from dir in GenDirectoryPath
        from exeName in Gen.Elements(
            "game.exe", "MyGame.exe", "Cyberpunk2077.exe",
            "EldenRing-Win64-Shipping.exe", "Starfield.exe",
            "UnityPlayer.exe", "GameClient.exe", "launcher.exe",
            "HogwartsLegacy_Win64_Shipping.exe", "AlanWake2.exe")
        select $@"{dir}\{exeName}";

    private static readonly Gen<EngineType> GenEngineType =
        Gen.Elements(EngineType.Unknown, EngineType.Unreal, EngineType.UnrealLegacy, EngineType.Unity);

    // ── Property 6a: InferGameRoot returns non-null non-empty string ───────────────
    // Feature: codebase-optimization, Property 6: InferGameRoot and InferGameName produce non-empty results
    // **Validates: Requirements 10.5, 12.5**
    [Property(MaxTest = 100)]
    public Property InferGameRoot_ReturnsNonNullNonEmpty_ForValidDirectoryPaths()
    {
        return Prop.ForAll(
            Arb.From(GenDirectoryPath),
            (string dirPath) =>
            {
                var result = DragDropHandler.InferGameRoot(dirPath);

                return (result != null && result.Length > 0)
                    .Label($"InferGameRoot returned null or empty for path '{dirPath}', got: '{result}'");
            });
    }

    // ── Property 6b: InferGameName returns non-null non-empty string ──────────────
    // Feature: codebase-optimization, Property 6: InferGameRoot and InferGameName produce non-empty results
    // **Validates: Requirements 10.5, 12.5**
    [Property(MaxTest = 100)]
    public Property InferGameName_ReturnsNonNullNonEmpty_ForValidInputs()
    {
        // Use engine types that don't hit the file system (Unity and Unknown use
        // folder/exe name directly; Unreal with space/hyphen in root name also
        // skips the Directory.GetDirectories call).
        var genSafeEngine = Gen.Elements(EngineType.Unknown, EngineType.Unity);

        var genInputs =
            from exePath in GenExePath
            from gameRoot in GenDirectoryPath
            from engine in genSafeEngine
            select (exePath, gameRoot, engine);

        return Prop.ForAll(
            Arb.From(genInputs),
            ((string exePath, string gameRoot, EngineType engine) input) =>
            {
                var result = DragDropHandler.InferGameName(input.exePath, input.gameRoot, input.engine);

                return (result != null && result.Length > 0)
                    .Label($"InferGameName returned null or empty for exePath='{input.exePath}', gameRoot='{input.gameRoot}', engine={input.engine}, got: '{result}'");
            });
    }

    // ── Property 6c: InferGameName with Unreal engine and space/hyphen root name ──
    // Covers the Unreal branch where rootDirName contains space or hyphen (skips file system).
    // **Validates: Requirements 10.5, 12.5**
    [Property(MaxTest = 100)]
    public Property InferGameName_Unreal_WithSpaceOrHyphenRoot_ReturnsNonEmpty()
    {
        // Game roots with spaces or hyphens trigger the early return in the Unreal branch
        var genRootWithSpaceOrHyphen =
            from drive in Gen.Elements("C", "D")
            from name in Gen.Elements("Elden Ring", "Baldurs-Gate-3", "Alan Wake 2",
                                       "Hogwarts Legacy", "Star-Wars-Jedi", "Final Fantasy XVI")
            select $@"{drive}:\Games\{name}";

        var genUnrealEngine = Gen.Elements(EngineType.Unreal, EngineType.UnrealLegacy);

        var genInputs =
            from exePath in GenExePath
            from gameRoot in genRootWithSpaceOrHyphen
            from engine in genUnrealEngine
            select (exePath, gameRoot, engine);

        return Prop.ForAll(
            Arb.From(genInputs),
            ((string exePath, string gameRoot, EngineType engine) input) =>
            {
                var result = DragDropHandler.InferGameName(input.exePath, input.gameRoot, input.engine);

                return (result != null && result.Length > 0)
                    .Label($"InferGameName (Unreal) returned null or empty for gameRoot='{input.gameRoot}', engine={input.engine}, got: '{result}'");
            });
    }

    // ── Property 11: DragDropHandler file extension validation ───────────────────
    // Feature: codebase-optimization, Property 11: DragDropHandler file extension validation
    // **Validates: Requirements 12.1**

    /// <summary>
    /// Generates a file path with one of the allowed extensions.
    /// </summary>
    private static readonly Gen<string> GenAllowedFilePath =
        from dir in GenDirectoryPath
        from baseName in Gen.Elements("game", "MyMod", "renodx_addon", "archive", "patch", "update")
        from ext in Gen.Elements(".exe", ".addon64", ".addon32", ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz")
        select $@"{dir}\{baseName}{ext}";

    /// <summary>
    /// Generates a file path with a disallowed extension.
    /// </summary>
    private static readonly Gen<string> GenDisallowedFilePath =
        from dir in GenDirectoryPath
        from baseName in Gen.Elements("readme", "config", "notes", "data", "save", "log")
        from ext in Gen.Elements(".txt", ".cfg", ".ini", ".json", ".xml", ".dll", ".png", ".jpg", ".mp3", ".pdf", ".doc", ".bat")
        select $@"{dir}\{baseName}{ext}";

    [Property(MaxTest = 100)]
    public Property IsAllowedExtension_ReturnsTrue_ForAllowedExtensions()
    {
        return Prop.ForAll(
            Arb.From(GenAllowedFilePath),
            (string filePath) =>
            {
                var result = DragDropHandler.IsAllowedExtension(filePath);

                return result.Label($"IsAllowedExtension returned false for allowed path '{filePath}'");
            });
    }

    [Property(MaxTest = 100)]
    public Property IsAllowedExtension_ReturnsFalse_ForDisallowedExtensions()
    {
        return Prop.ForAll(
            Arb.From(GenDisallowedFilePath),
            (string filePath) =>
            {
                var result = DragDropHandler.IsAllowedExtension(filePath);

                return (!result).Label($"IsAllowedExtension returned true for disallowed path '{filePath}'");
            });
    }

    [Property(MaxTest = 100)]
    public Property IsAllowedExtension_ReturnsFalse_ForNullOrEmpty()
    {
        var genNullOrEmpty = Gen.Elements<string?>(null, "", "   ", "noextension", "justadot.");

        return Prop.ForAll(
            Arb.From(genNullOrEmpty),
            (string? input) =>
            {
                var result = DragDropHandler.IsAllowedExtension(input);

                return (!result).Label($"IsAllowedExtension returned true for null/empty/no-extension input '{input}'");
            });
    }

    // ── Property 9a: CleanUnrealExeName is idempotent ─────────────────────────────
    // Feature: codebase-optimization, Property 9: CleanUnrealExeName and CleanFolderName are idempotent
    // **Validates: Requirements 12.3, 12.4**
    [Property(MaxTest = 100)]
    public Property CleanUnrealExeName_IsIdempotent()
    {
        // Generate realistic exe name strings (alphanumeric with common suffixes)
        var genExeName =
            from baseName in Gen.Elements(
                "MyGame", "EldenRing", "Cyberpunk2077", "HogwartsLegacy",
                "AlanWake2", "Starfield", "FinalFantasyXVI", "SomeGame_Win64_Shipping",
                "TestGame-Win64-Shipping", "Game_WinGDK_Shipping", "App_Win32",
                "Cool_Game-Win64-Test", "SimpleGame", "A", "GameShipping",
                "My_Game_Win64", "Another-Game-WinGDK", "X_Shipping")
            select baseName;

        return Prop.ForAll(
            Arb.From(genExeName),
            (string exeName) =>
            {
                var once = DragDropHandler.CleanUnrealExeName(exeName);
                var twice = DragDropHandler.CleanUnrealExeName(once);

                return (once == twice)
                    .Label($"CleanUnrealExeName is not idempotent: input='{exeName}', once='{once}', twice='{twice}'");
            });
    }

    // ── Property 9b: CleanFolderName is idempotent ────────────────────────────────
    // Feature: codebase-optimization, Property 9: CleanUnrealExeName and CleanFolderName are idempotent
    // **Validates: Requirements 12.3, 12.4**
    [Property(MaxTest = 100)]
    public Property CleanFolderName_IsIdempotent()
    {
        // Generate realistic folder name strings (with underscores, hyphens, camelCase)
        var genFolderName =
            from name in Gen.Elements(
                "MyGame", "Elden_Ring", "Cyberpunk-2077", "HogwartsLegacy",
                "Alan Wake 2", "Star-Wars-Jedi", "FinalFantasyXVI",
                "some_game_folder", "AnotherGameFolder", "cool-game",
                "Game With Spaces", "camelCaseGame", "PascalCaseGame",
                "under_score_name", "MixedCase_With-Hyphens", "A", "AB",
                "already clean name", "  spaced  ", "Multiple   Spaces")
            select name;

        return Prop.ForAll(
            Arb.From(genFolderName),
            (string folderName) =>
            {
                var once = DragDropHandler.CleanFolderName(folderName);
                var twice = DragDropHandler.CleanFolderName(once);

                return (once == twice)
                    .Label($"CleanFolderName is not idempotent: input='{folderName}', once='{once}', twice='{twice}'");
            });
    }
}
