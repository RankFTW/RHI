using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DxvkService.IsValidDeploymentPath.
/// Feature: dxvk-integration, Property 6: Path safety validation
/// </summary>
public class DxvkPathSafetyValidationPropertyTests
{
    // ── Actual system directories on this machine ─────────────────────────────────

    private static readonly string SystemRoot =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static readonly string ProgramFiles =
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates subdirectory suffixes to append to protected base paths.
    /// </summary>
    private static readonly Gen<string> GenSubDir =
        Gen.Elements(
            "", "drivers", "config", "Wbem",
            @"subfolder\deep\nested", "SomeGame", "test");

    /// <summary>
    /// Generates paths that are under protected system directories.
    /// These should ALL be rejected by IsValidDeploymentPath.
    /// </summary>
    private static readonly Gen<string> GenProtectedPath =
        Gen.OneOf(
            // Paths under %SystemRoot% (e.g. C:\Windows)
            GenSubDir.Select(sub => string.IsNullOrEmpty(sub)
                ? SystemRoot
                : Path.Combine(SystemRoot, sub)),
            // Paths under %SystemRoot%\System32
            GenSubDir.Select(sub => string.IsNullOrEmpty(sub)
                ? Path.Combine(SystemRoot, "System32")
                : Path.Combine(SystemRoot, "System32", sub)),
            // Paths under %SystemRoot%\SysWOW64
            GenSubDir.Select(sub => string.IsNullOrEmpty(sub)
                ? Path.Combine(SystemRoot, "SysWOW64")
                : Path.Combine(SystemRoot, "SysWOW64", sub)),
            // Paths under %ProgramFiles%\WindowsApps
            GenSubDir.Select(sub => string.IsNullOrEmpty(sub)
                ? Path.Combine(ProgramFiles, "WindowsApps")
                : Path.Combine(ProgramFiles, "WindowsApps", sub)));

    /// <summary>
    /// Generates valid game directory paths that are outside all protected locations.
    /// These should ALL be accepted by IsValidDeploymentPath.
    /// </summary>
    private static readonly Gen<string> GenValidGamePath =
        Gen.Elements(
            @"C:\Games\Cyberpunk 2077\bin\x64",
            @"D:\SteamLibrary\steamapps\common\Elden Ring\Game",
            @"C:\Program Files\Steam\steamapps\common\Starfield",
            @"E:\Games\BG3\bin",
            @"C:\Games\Test",
            @"D:\GOG\The Witcher 3",
            @"C:\Users\Player\Games\FFXIV",
            @"F:\EpicGames\AlanWake2",
            @"C:\Program Files (x86)\Steam\steamapps\common\DarkSouls3");

    /// <summary>
    /// Generates null or whitespace-only strings that should be rejected.
    /// </summary>
    private static readonly Gen<string?> GenNullOrEmpty =
        Gen.Elements<string?>(null, "", "   ", "\t", "\n");

    // ── Property 6: Path safety validation ────────────────────────────────────────
    // Feature: dxvk-integration, Property 6: Path safety validation
    // **Validates: Requirements 9.5, 9.6, 21.2**

    /// <summary>
    /// For any path under a protected system directory (%SystemRoot%, System32,
    /// SysWOW64, %ProgramFiles%\WindowsApps), IsValidDeploymentPath SHALL return false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ProtectedPaths_Are_Rejected()
    {
        return Prop.ForAll(
            Arb.From(GenProtectedPath),
            (string protectedPath) =>
            {
                var result = DxvkService.IsValidDeploymentPath(protectedPath);

                return (!result)
                    .Label($"Expected rejection for protected path: '{protectedPath}', got accepted");
            });
    }

    /// <summary>
    /// For any valid game directory path outside protected locations,
    /// IsValidDeploymentPath SHALL return true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidGamePaths_Are_Accepted()
    {
        return Prop.ForAll(
            Arb.From(GenValidGamePath),
            (string gamePath) =>
            {
                var result = DxvkService.IsValidDeploymentPath(gamePath);

                return result
                    .Label($"Expected acceptance for valid game path: '{gamePath}', got rejected");
            });
    }

    /// <summary>
    /// For any null or whitespace-only path, IsValidDeploymentPath SHALL return false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NullOrEmpty_Paths_Are_Rejected()
    {
        return Prop.ForAll(
            Arb.From(GenNullOrEmpty),
            (string? path) =>
            {
                var result = DxvkService.IsValidDeploymentPath(path);

                return (!result)
                    .Label($"Expected rejection for null/empty path: '{path ?? "(null)"}', got accepted");
            });
    }
}
