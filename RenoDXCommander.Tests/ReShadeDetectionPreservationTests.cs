using System.Reflection;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Preservation property tests for ReShade detection.
/// These tests capture the EXISTING correct behavior on UNFIXED code and must PASS
/// both before and after the fix is applied — any failure after the fix indicates a regression.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
///
/// EXPECTED OUTCOME on UNFIXED code: All tests PASS.
/// After the fix is applied: All tests MUST still PASS (no regressions).
/// </summary>
public class ReShadeDetectionPreservationTests : IDisposable
{
    /// <summary>
    /// Minimal binary content that passes IsReShadeFileStrict:
    /// Contains "ReShade" (exact case), "reshade.me", and "crosire" ASCII markers, under 15 MB.
    /// </summary>
    private static readonly byte[] FakeReShadeContent = CreateFakeReShadeBytes();

    private readonly string _tempRoot;

    public ReShadeDetectionPreservationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcPres_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Property 2a: Standard-filename ReShade DLLs are detected correctly ───────

    /// <summary>
    /// Property 2a: For all standard-filename ReShade DLLs (ReShade64.dll, ReShade32.dll, dxgi.dll),
    /// detection produces a valid rsRec with correct InstalledAs.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property StandardFilename_ReShade_IsDetected()
    {
        var standardNames = new[] { "ReShade64.dll", "ReShade32.dll", "dxgi.dll" };
        var genName = Gen.Elements(standardNames);

        return Prop.ForAll(
            Arb.From(genName),
            (string dllName) =>
            {
                var gameFolder = Path.Combine(_tempRoot, "StdRS_" + dllName.Replace(".", "_"));
                Directory.CreateDirectory(gameFolder);
                File.WriteAllBytes(Path.Combine(gameFolder, dllName), FakeReShadeContent);

                var vm = TestHelpers.CreateMainViewModel();
                var card = InvokeBuildCardsForSingleGame(vm, "StdTest_" + dllName, gameFolder);

                var rsRec = card.RsRecord;
                var detected = rsRec != null;
                var correctName = rsRec?.InstalledAs == dllName;
                var correctType = rsRec?.AddonType == AuxInstallService.TypeReShade;
                var correctStatus = card.RsStatus == GameStatus.Installed;

                return (detected && correctName && correctType && correctStatus)
                    .Label($"Standard '{dllName}': detected={detected}, " +
                           $"installedAs={rsRec?.InstalledAs ?? "null"}, " +
                           $"addonType={rsRec?.AddonType ?? "null"}, " +
                           $"rsStatus={card.RsStatus}");
            });
    }

    // ── Property 2b: No-ReShade game folders produce null rsRec ──────────────────

    /// <summary>
    /// Property 2b: For all game folders with no DLLs containing ReShade signatures,
    /// rsRec remains null and RsStatus is NotInstalled.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 5)]
    public Property NoReShade_GameFolder_RsRecIsNull()
    {
        // Generate game folders that are either empty or contain only non-DLL files
        var genScenario = Gen.Elements("empty", "txt_only", "exe_only");

        return Prop.ForAll(
            Arb.From(genScenario),
            (string scenario) =>
            {
                var gameFolder = Path.Combine(_tempRoot, "NoRS_" + scenario + "_" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(gameFolder);

                switch (scenario)
                {
                    case "txt_only":
                        File.WriteAllText(Path.Combine(gameFolder, "readme.txt"), "hello");
                        break;
                    case "exe_only":
                        File.WriteAllBytes(Path.Combine(gameFolder, "game.exe"), new byte[256]);
                        break;
                    // "empty" — nothing to create
                }

                var vm = TestHelpers.CreateMainViewModel();
                var card = InvokeBuildCardsForSingleGame(vm, "NoRsTest_" + scenario, gameFolder);

                var rsNull = card.RsRecord == null;
                var statusNotInstalled = card.RsStatus == GameStatus.NotInstalled;

                return (rsNull && statusNotInstalled)
                    .Label($"No-ReShade '{scenario}': rsRec={card.RsRecord?.InstalledAs ?? "null"}, " +
                           $"rsStatus={card.RsStatus}");
            });
    }

    // ── Property 2c: Non-ReShade DLLs are not misidentified ──────────────────────

    /// <summary>
    /// Property 2c: For all non-ReShade DLLs (random content without "reshade.me" / "crosire" markers),
    /// rsRec remains null — they are never misidentified as ReShade.
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property NonReShade_Dll_NotMisidentified()
    {
        // Generate DLL filenames that could be confused with ReShade
        var dllNames = new[] { "d3d11.dll", "dinput8.dll", "version.dll", "dxvk.dll", "SpecialK64.dll" };
        var genDll = Gen.Elements(dllNames);

        // Generate non-ReShade content types
        var genContentType = Gen.Elements("random", "dxvk_like", "specialk_like", "enb_like");

        return Prop.ForAll(
            Arb.From(genDll),
            Arb.From(genContentType),
            (string dllName, string contentType) =>
            {
                var gameFolder = Path.Combine(_tempRoot, "NonRS_" + dllName.Replace(".", "_") + "_" + contentType + "_" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(gameFolder);

                var content = CreateNonReShadeContent(contentType);
                File.WriteAllBytes(Path.Combine(gameFolder, dllName), content);

                var vm = TestHelpers.CreateMainViewModel();
                var card = InvokeBuildCardsForSingleGame(vm, "NonRsTest_" + dllName + "_" + contentType, gameFolder);

                var rsNull = card.RsRecord == null;
                var statusNotInstalled = card.RsStatus == GameStatus.NotInstalled;

                return (rsNull && statusNotInstalled)
                    .Label($"Non-ReShade '{dllName}' ({contentType}): rsRec={card.RsRecord?.InstalledAs ?? "null"}, " +
                           $"rsStatus={card.RsStatus}");
            });
    }

    // ── Property 2d: DLLs > 15 MB are excluded by IsReShadeFileStrict ────────────

    /// <summary>
    /// Property 2d: For all DLLs larger than 15 MB, IsReShadeFileStrict returns false
    /// regardless of content — the size guard excludes them.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property LargeDll_ExcludedBySizeGuard()
    {
        // Generate sizes just over the 15 MB threshold
        var genExtraMB = Gen.Choose(1, 5); // 16-20 MB

        return Prop.ForAll(
            Arb.From(genExtraMB),
            (int extraMB) =>
            {
                var sizeBytes = (15 + extraMB) * 1024 * 1024;
                var gameFolder = Path.Combine(_tempRoot, "LargeDll_" + extraMB + "MB_" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(gameFolder);

                // Create a large DLL with ReShade markers embedded — should still be rejected
                var content = new byte[sizeBytes];
                var rng = new System.Random(42);
                // Only fill first 4KB to avoid huge memory allocation time
                rng.NextBytes(content.AsSpan(0, Math.Min(4096, sizeBytes)));
                var markers = Encoding.ASCII.GetBytes("ReShade version 5.9.2 | https://reshade.me | by crosire");
                markers.CopyTo(content.AsSpan(256));

                var dllPath = Path.Combine(gameFolder, "optiscaler.dll");
                File.WriteAllBytes(dllPath, content);

                var result = AuxInstallService.IsReShadeFileStrict(dllPath);

                return (!result)
                    .Label($"DLL size {sizeBytes / (1024 * 1024)} MB: IsReShadeFileStrict returned {result} (expected false)");
            });
    }

    // ── Property 2e: Existing AuxInstalledRecord preserves scan skip ─────────────

    /// <summary>
    /// Property 2e: When a game already has an AuxInstalledRecord for ReShade,
    /// the disk scan is skipped and the existing record is preserved.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void ExistingAuxRecord_PreservesRecord()
    {
        var gameFolder = Path.Combine(_tempRoot, "ExistingRec_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(gameFolder);

        // Place a ReShade DLL on disk matching the existing record
        File.WriteAllBytes(Path.Combine(gameFolder, "ReShade64.dll"), FakeReShadeContent);

        var existingRecord = new AuxInstalledRecord
        {
            GameName = "ExistingRecTest",
            InstallPath = gameFolder,
            AddonType = AuxInstallService.TypeReShade,
            InstalledAs = "ReShade64.dll",
            InstalledAt = DateTime.UtcNow.AddDays(-1),
        };

        var vm = TestHelpers.CreateMainViewModel();
        var card = InvokeBuildCardsForSingleGame(
            vm, "ExistingRecTest", gameFolder,
            auxRecords: new List<AuxInstalledRecord> { existingRecord });

        Assert.NotNull(card.RsRecord);
        Assert.Equal("ReShade64.dll", card.RsRecord!.InstalledAs);
        Assert.Equal(AuxInstallService.TypeReShade, card.RsRecord.AddonType);
        Assert.Equal(GameStatus.Installed, card.RsStatus);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static byte[] CreateFakeReShadeBytes()
    {
        var content = new byte[1024];
        var rng = new System.Random(42);
        rng.NextBytes(content);
        var markers = Encoding.ASCII.GetBytes("ReShade version 5.9.2 | https://reshade.me | by crosire");
        markers.CopyTo(content.AsSpan(256));
        return content;
    }

    /// <summary>
    /// Creates non-ReShade DLL content that should NOT trigger IsReShadeFileStrict.
    /// None of these contain both "reshade.me" and "crosire" markers.
    /// </summary>
    private static byte[] CreateNonReShadeContent(string contentType)
    {
        var content = new byte[2048];
        var rng = new System.Random(contentType.GetHashCode());
        rng.NextBytes(content);

        switch (contentType)
        {
            case "dxvk_like":
                // DXVK DLLs contain "DXVK" and "dxvk.conf" but not ReShade markers
                Encoding.ASCII.GetBytes("DXVK v2.3 | dxvk.conf").CopyTo(content.AsSpan(100));
                break;
            case "specialk_like":
                // Special K contains "Special K" but not ReShade markers
                Encoding.ASCII.GetBytes("Special K v24.1.1 | wiki.special-k.info").CopyTo(content.AsSpan(100));
                break;
            case "enb_like":
                // ENBSeries contains "ENBSeries" but not ReShade markers
                Encoding.ASCII.GetBytes("ENBSeries v0.492 | enbdev.com").CopyTo(content.AsSpan(100));
                break;
            // "random" — pure random bytes, no markers at all
        }

        return content;
    }

    /// <summary>
    /// Invokes the private BuildCards method via reflection with a single detected game.
    /// </summary>
    private static GameCardViewModel InvokeBuildCardsForSingleGame(
        MainViewModel vm, string gameName, string installPath,
        List<AuxInstalledRecord>? auxRecords = null)
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
            auxRecords ?? new List<AuxInstalledRecord>(),
            new Dictionary<string, bool>(),
            new Dictionary<string, string>(),
        });

        var cards = (List<GameCardViewModel>)result!;
        return cards.Single();
    }
}
