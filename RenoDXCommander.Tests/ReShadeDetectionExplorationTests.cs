using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Bug condition exploration tests for ReShade detection by content.
/// These tests exercise the BuildCards ReShade disk-detection logic to surface
/// counterexamples proving the bug exists: a ReShade DLL under a non-standard
/// filename (e.g. d3d11.dll) is invisible to the filename-only detection.
///
/// **Validates: Requirements 1.1, 2.1, 2.2**
///
/// EXPECTED OUTCOME on UNFIXED code: Tests FAIL (confirms the bug exists).
/// After the fix is applied, these same tests should PASS.
/// </summary>
public class ReShadeDetectionExplorationTests : IDisposable
{
    // Non-standard filenames that ReShade can be installed as
    private static readonly string[] NonStandardDllNames = { "d3d11.dll", "dinput8.dll", "version.dll" };

    // Standard filenames that the current code already checks
    private static readonly string[] StandardDllNames = { "ReShade64.dll", "ReShade32.dll", "dxgi.dll" };

    // Minimal binary content that passes IsReShadeFileStrict:
    // Contains "ReShade" (exact case), "reshade.me", and "crosire" ASCII markers, under 15 MB.
    private static readonly byte[] FakeReShadeContent = CreateFakeReShadeBytes();

    private readonly string _tempRoot;

    public ReShadeDetectionExplorationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// Property 1: Bug Condition — Non-Standard ReShade DLL Not Detected
    ///
    /// For any non-standard DLL filename containing valid ReShade binary signatures,
    /// BuildCards SHALL detect it and create an AuxInstalledRecord with the correct filename.
    ///
    /// On UNFIXED code this MUST FAIL — the filename-only checks miss non-standard names,
    /// so rsRec remains null and RsStatus shows NotInstalled.
    ///
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property BugCondition_NonStandardReShade_ShouldBeDetected()
    {
        var genDllName = Gen.Elements(NonStandardDllNames);

        return Prop.ForAll(
            Arb.From(genDllName),
            (string dllName) =>
            {
                // Arrange: create a game folder with a ReShade DLL under a non-standard name
                var gameFolder = Path.Combine(_tempRoot, "Game_" + dllName.Replace(".", "_"));
                Directory.CreateDirectory(gameFolder);
                var dllPath = Path.Combine(gameFolder, dllName);
                File.WriteAllBytes(dllPath, FakeReShadeContent);

                // Sanity: confirm IsReShadeFileStrict recognizes the content
                var isReShade = AuxInstallService.IsReShadeFileStrict(dllPath);
                if (!isReShade)
                    return false.Label("IsReShadeFileStrict should recognize the fake DLL content");

                // Act: invoke BuildCards via reflection with a single detected game
                var vm = TestHelpers.CreateMainViewModel();
                var card = InvokeBuildCardsForSingleGame(vm, "TestGame_" + dllName, gameFolder);

                // Assert: the card should detect the ReShade DLL
                // On UNFIXED code, rsRec will be null because BuildCards only checks
                // ReShade64.dll, ReShade32.dll, and dxgi.dll — this assertion WILL FAIL.
                var rsRec = card.RsRecord;
                var rsDetected = rsRec != null;
                var correctFilename = rsRec?.InstalledAs == dllName;
                var correctAddonType = rsRec?.AddonType == AuxInstallService.TypeReShade;
                var correctStatus = card.RsStatus == GameStatus.Installed;

                return (rsDetected && correctFilename && correctAddonType && correctStatus)
                    .Label($"Expected rsRec for '{dllName}': detected={rsDetected}, " +
                           $"installedAs={rsRec?.InstalledAs ?? "null"}, " +
                           $"addonType={rsRec?.AddonType ?? "null"}, " +
                           $"rsStatus={card.RsStatus}");
            });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates fake DLL bytes that pass IsReShadeFileStrict's binary signature scan.
    /// Contains "ReShade", "reshade.me", and "crosire" as ASCII strings, well under 15 MB.
    /// </summary>
    private static byte[] CreateFakeReShadeBytes()
    {
        // Start with some padding to simulate a real DLL structure
        var content = new byte[1024];
        var rng = new System.Random(42);
        rng.NextBytes(content);

        // Embed the required ASCII markers
        var markers = "ReShade version 5.9.2 | https://reshade.me | by crosire"u8;
        markers.CopyTo(content.AsSpan(256));

        return content;
    }

    /// <summary>
    /// Invokes the private BuildCards method via reflection with a single detected game
    /// pointing to the specified install path, and returns the resulting GameCardViewModel.
    /// </summary>
    private static GameCardViewModel InvokeBuildCardsForSingleGame(
        MainViewModel vm, string gameName, string installPath)
    {
        var game = new DetectedGame
        {
            Name = gameName,
            InstallPath = installPath,
            Source = "Manual",
        };

        var method = typeof(MainViewModel).GetMethod(
            "BuildCards",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("Could not find BuildCards method via reflection");

        var result = method.Invoke(vm, new object[]
        {
            new List<DetectedGame> { game },
            new List<InstalledModRecord>(),
            new List<AuxInstalledRecord>(),
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
        });

        var cards = (List<GameCardViewModel>)result!;
        return cards.Single();
    }
}
