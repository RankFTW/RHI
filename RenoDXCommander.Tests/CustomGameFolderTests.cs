using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Tests for the "Custom Game Folders" feature. Everything runs against temporary directories —
/// nothing depends on a real game drive or on the machine's RHI data.
/// </summary>
public class CustomGameFolderTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rhi-customfolders-" + Guid.NewGuid().ToString("N"));

    public CustomGameFolderTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best effort */ }
    }

    private const long MB = 1024 * 1024;

    /// <summary>Creates a file (and parent folders) of the given size and returns its full path.</summary>
    private string MakeFile(string relative, long size = 2 * MB)
    {
        var path = Path.Combine(_tmp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        fs.SetLength(size);
        return path;
    }

    private string Dir(string relative)
    {
        var path = Path.Combine(_tmp, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private static CustomFolderScanService NewScanner() => new(new GameDetectionService(), new PeHeaderService());

    private static readonly IReadOnlyCollection<string> None = Array.Empty<string>();

    // ═══════════════════════════════════════════════════════════════════════════════
    // Settings persistence
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Settings_MultipleCustomFolders_RoundTripThroughSettingsFile()
    {
        var settings = new SettingsViewModel
        {
            CustomGameFolders = new() { @"F:\Games", @"G:\Standalone Games", @"Z:\Offline Drive\Games" },
            CustomFoldersAutoScan = false,
            CustomFolderDismissed = new() { @"F:\Games\Some Tool" },
        };

        var dict = new Dictionary<string, string>();
        settings.SaveSettingsToDict(dict);

        // Stored as ONE ordinary key holding a JSON array, like the other list settings.
        Assert.Equal(new[] { @"F:\Games", @"G:\Standalone Games", @"Z:\Offline Drive\Games" },
            JsonSerializer.Deserialize<string[]>(dict["CustomGameFolders"]));
        Assert.Equal("false", dict["CustomFoldersAutoScan"]);

        var restored = new SettingsViewModel();
        restored.LoadSettingsFromDict(dict);
        Assert.Equal(settings.CustomGameFolders, restored.CustomGameFolders);
        Assert.False(restored.CustomFoldersAutoScan);
        Assert.Equal(settings.CustomFolderDismissed, restored.CustomFolderDismissed);
    }

    [Fact]
    public void Settings_UnusedFeature_LeavesSettingsFileUntouched()
    {
        // Same cycle the app performs: load settings.json into the view model, then save it back.
        var dict = new Dictionary<string, string>
        {
            ["DefaultSrPreset"] = "11", ["AutoUpdateDlss"] = "true", ["DefaultSrDriverOverride"] = "1", ["CacheAllShaders"] = "true",
        };
        var vm = new SettingsViewModel();
        vm.LoadSettingsFromDict(dict);
        vm.SaveSettingsToDict(dict);

        Assert.DoesNotContain("CustomGameFolders", dict.Keys);
        Assert.DoesNotContain("CustomFoldersAutoScan", dict.Keys);
        Assert.DoesNotContain("CustomFolderDismissed", dict.Keys);
        // Unrelated existing settings (e.g. DLSS defaults) are preserved.
        Assert.Equal("11", dict["DefaultSrPreset"]);
        Assert.Equal("true", dict["AutoUpdateDlss"]);
        Assert.Equal("1", dict["DefaultSrDriverOverride"]);
        Assert.Equal("true", dict["CacheAllShaders"]);
    }

    [Fact]
    public void Settings_AutoScanDefaultsToOn_AndEmptyingListRemovesKeys()
    {
        var restored = new SettingsViewModel();
        restored.LoadSettingsFromDict(new Dictionary<string, string>());
        Assert.True(restored.CustomFoldersAutoScan);
        Assert.Empty(restored.CustomGameFolders);

        var dict = new Dictionary<string, string> { ["CustomGameFolders"] = "[\"F:\\\\Games\"]" };
        var s = new SettingsViewModel { CustomGameFolders = new() };
        s.SaveSettingsToDict(dict);
        Assert.DoesNotContain("CustomGameFolders", dict.Keys);
    }

    [Fact]
    public void Settings_LoadNormalisesAndDeduplicatesStoredFolders()
    {
        var dict = new Dictionary<string, string>
        {
            ["CustomGameFolders"] = JsonSerializer.Serialize(new[] { @"F:\Games\", @"f:\games", @"F:/Games", "  ", @"G:\Other" }),
        };
        var s = new SettingsViewModel();
        s.LoadSettingsFromDict(dict);
        Assert.Equal(new[] { @"F:\Games", @"G:\Other" }, s.CustomGameFolders);
    }

    [Fact]
    public void Settings_CorruptFolderValue_DoesNotThrowAndYieldsEmptyList()
    {
        var s = new SettingsViewModel();
        s.LoadSettingsFromDict(new Dictionary<string, string> { ["CustomGameFolders"] = "not json {" });
        Assert.Empty(s.CustomGameFolders);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Folder management / path normalisation
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(@"F:\Games\")]
    [InlineData(@"f:\GAMES")]
    [InlineData(@"F:/Games")]
    [InlineData(@"""F:\Games""")]
    [InlineData(@"F:\Games\Sub\..")]
    public void TryAdd_SameFolderWrittenDifferently_IsRejectedAsDuplicate(string variant)
    {
        var folders = new List<string>();
        Assert.Equal(CustomFolderAddResult.Added, CustomFolderPaths.TryAdd(folders, @"F:\Games", out var first));
        Assert.Equal(@"F:\Games", first);

        Assert.Equal(CustomFolderAddResult.Duplicate, CustomFolderPaths.TryAdd(folders, variant, out _));
        Assert.Single(folders);
    }

    [Fact]
    public void TryAdd_OfflineDriveFolder_IsAccepted_BecauseExistenceIsNotRequired()
    {
        var folders = new List<string>();
        var result = CustomFolderPaths.TryAdd(folders, @"Q:\Definitely\Not\Plugged In", out _);
        Assert.Equal(CustomFolderAddResult.Added, result);
        Assert.Single(folders);
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\ProgramData")]
    public void TryAdd_SystemLocations_AreRejected(string path)
    {
        var folders = new List<string>();
        var result = CustomFolderPaths.TryAdd(folders, path, out _, windowsDir: @"C:\Windows", programData: @"C:\ProgramData");
        Assert.Equal(CustomFolderAddResult.Unsafe, result);
        Assert.Empty(folders);
    }

    [Fact]
    public void TryAdd_OtherDriveRoot_IsAllowed()
    {
        var folders = new List<string>();
        Assert.Equal(CustomFolderAddResult.Added,
            CustomFolderPaths.TryAdd(folders, @"F:\", out var n, windowsDir: @"C:\Windows", programData: @"C:\ProgramData"));
        Assert.Equal(@"F:\", n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryAdd_EmptyPath_IsInvalid(string? path)
    {
        var folders = new List<string>();
        Assert.Equal(CustomFolderAddResult.Invalid, CustomFolderPaths.TryAdd(folders, path, out _));
    }

    [Fact]
    public void Remove_OnlyRemovesTheConfiguredRoot_NeverTouchesFiles()
    {
        var gameFile = MakeFile(@"Games\Foo\Foo.exe");
        var folders = new List<string> { Path.Combine(_tmp, "Games"), @"G:\Other" };

        Assert.True(CustomFolderPaths.Remove(folders, Path.Combine(_tmp, "GAMES") + "\\"));

        Assert.Equal(new[] { @"G:\Other" }, folders);
        Assert.True(File.Exists(gameFile));
        Assert.False(CustomFolderPaths.Remove(folders, Path.Combine(_tmp, "Games")));
    }

    [Fact]
    public void IsSameOrUnder_DoesNotConfuseSiblingsWithSharedPrefix()
    {
        Assert.True(CustomFolderPaths.IsSameOrUnder(@"F:\Games\Foo", @"F:\Games"));
        Assert.True(CustomFolderPaths.IsSameOrUnder(@"F:\Games", @"F:\Games\"));
        Assert.False(CustomFolderPaths.IsSameOrUnder(@"F:\GamesExtra\Foo", @"F:\Games"));
        Assert.True(CustomFolderPaths.IsSameOrUnder(@"F:\Games", @"F:\"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Executable filtering
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("Uninstall.exe")]
    [InlineData("setup.exe")]
    [InlineData("GameSetup.exe")]
    [InlineData("installer.exe")]
    [InlineData("vc_redist.x64.exe")]
    [InlineData("VC_redist.x86.exe")]
    [InlineData("dxsetup.exe")]
    [InlineData("DXSETUP.exe")]
    [InlineData("dotnet-runtime-6.0.0-win-x64.exe")]
    [InlineData("CrashReportClient.exe")]
    [InlineData("UnityCrashHandler64.exe")]
    [InlineData("UnityCrashHandler32.exe")]
    [InlineData("crashpad_handler.exe")]
    [InlineData("EpicWebHelper.exe")]
    [InlineData("UnrealCEFSubProcess.exe")]
    [InlineData("UE4PrereqSetup_x64.exe")]
    [InlineData("EasyAntiCheat_Setup.exe")]
    [InlineData("EasyAntiCheat_EOS_Setup.exe")]
    [InlineData("BEService.exe")]
    [InlineData("BEService_x64.exe")]
    [InlineData("BattlEye_Installer.exe")]
    [InlineData("Updater.exe")]
    [InlineData("update.exe")]
    [InlineData("AutoUpdate.exe")]
    [InlineData("ModManager.exe")]
    [InlineData("notification_helper.exe")]
    public void Classify_NonGameExecutables_AreExcluded(string fileName)
        => Assert.Equal(ExecutableKind.NonGame, GameExecutableFilter.Classify(fileName));

    [Theory]
    [InlineData("Cyberpunk2077.exe")]
    [InlineData("Hades.exe")]
    [InlineData("MyGame-Win64-Shipping.exe")]
    [InlineData("Crash Bandicoot N. Sane Trilogy.exe")]   // "crash" alone must not exclude a game
    [InlineData("CrashBandicootNSaneTrilogy.exe")]
    public void Classify_NormalGameNames_AreGameExecutables(string fileName)
        => Assert.Equal(ExecutableKind.Game, GameExecutableFilter.Classify(fileName));

    [Theory]
    [InlineData("GameLauncher.exe", ExecutableKind.Launcher)]
    [InlineData("Launcher.exe", ExecutableKind.Launcher)]
    [InlineData("GameConfig.exe", ExecutableKind.Launcher)]
    [InlineData("MyGameDedicatedServer.exe", ExecutableKind.Server)]
    [InlineData("MyGame-Win64-Server.exe", ExecutableKind.Server)]
    public void Classify_LaunchersAndServers_AreSoftKinds(string fileName, ExecutableKind expected)
        => Assert.Equal(expected, GameExecutableFilter.Classify(fileName));

    [Theory]
    [InlineData("Redist", true)]
    [InlineData("_CommonRedist", true)]
    [InlineData("Engine", true)]
    [InlineData("EasyAntiCheat", true)]
    [InlineData("MonoBleedingEdge", true)]
    [InlineData("Artbook", true)]
    [InlineData("Binaries", false)]
    [InlineData("Win64", false)]
    public void ExcludedDirectories(string name, bool excluded)
        => Assert.Equal(excluded, GameExecutableFilter.IsExcludedDirectory(name));

    // ═══════════════════════════════════════════════════════════════════════════════
    // Choosing the real executable
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PickExecutable_Unreal_PrefersWin64ShippingOverLauncherStub()
    {
        var root = Dir("UEGame");
        MakeFile(@"UEGame\UEGame.exe", 300 * 1024);   // tiny UE launcher stub at the root
        var shipping = MakeFile(@"UEGame\UEGame\Binaries\Win64\UEGame-Win64-Shipping.exe", 90 * MB);
        MakeFile(@"UEGame\UEGame\Binaries\Win64\UEGame-Win64-Shipping-Other.exe", 1 * MB);
        MakeFile(@"UEGame\Engine\Binaries\Win64\CrashReportClient.exe", 8 * MB);

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.Equal(shipping, exe!.Path);
    }

    [Fact]
    public void PickExecutable_Unreal_ClientShippingBeatsDedicatedServerShipping()
    {
        var root = Dir("UEGame2");
        MakeFile(@"UEGame2\UEGame2\Binaries\Win64\UEGame2Server-Win64-Shipping.exe", 120 * MB);
        var client = MakeFile(@"UEGame2\UEGame2\Binaries\Win64\UEGame2-Win64-Shipping.exe", 80 * MB);

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.Equal(client, exe!.Path);
    }

    [Fact]
    public void PickExecutable_Unity_PrefersExeNextToUnityPlayer()
    {
        var root = Dir("UnityGame");
        var game = MakeFile(@"UnityGame\UnityGame.exe", 650 * 1024);
        MakeFile(@"UnityGame\UnityPlayer.dll", 30 * MB);
        Dir(@"UnityGame\UnityGame_Data");
        MakeFile(@"UnityGame\UnityCrashHandler64.exe", 1 * MB);
        MakeFile(@"UnityGame\Tools\BigHelperTool.exe", 40 * MB);   // larger, but not next to UnityPlayer

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.Equal(game, exe!.Path);
    }

    [Fact]
    public void PickExecutable_DoesNotPickLauncherWhenAStrongerGameExeExists()
    {
        var root = Dir("Adventure Game");
        MakeFile(@"Adventure Game\Launcher.exe", 50 * MB);
        var game = MakeFile(@"Adventure Game\bin\Adventure.exe", 5 * MB);

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.Equal(game, exe!.Path);
        Assert.Equal(ExecutableKind.Game, exe.Kind);
    }

    [Fact]
    public void PickExecutable_LauncherOnly_IsReturnedAsLauncherKind()
    {
        var root = Dir("OnlyLauncher");
        MakeFile(@"OnlyLauncher\Launcher.exe", 5 * MB);

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.NotNull(exe);
        Assert.Equal(ExecutableKind.Launcher, exe!.Kind);
    }

    [Fact]
    public void PickExecutable_OnlyInstallersAndHelpers_ReturnsNull()
    {
        var root = Dir("JustJunk");
        MakeFile(@"JustJunk\setup.exe");
        MakeFile(@"JustJunk\unins000.exe");
        MakeFile(@"JustJunk\CrashReportClient.exe");

        Assert.Null(CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root)));
    }

    [Fact]
    public void CollectExecutables_SkipsRedistAndEngineFolders()
    {
        var root = Dir("Skippy");
        var game = MakeFile(@"Skippy\Skippy.exe");
        MakeFile(@"Skippy\_CommonRedist\DirectX\Jun2010\dxsetup.exe");
        MakeFile(@"Skippy\Engine\Binaries\Win64\SomethingBig.exe", 50 * MB);
        MakeFile(@"Skippy\EasyAntiCheat\Launcher.exe", 10 * MB);

        var all = CustomGameHeuristics.CollectExecutables(root);

        Assert.Equal(new[] { game }, all.Select(e => e.Path).ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Discovery, confidence and scanning rules
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scan_UnrealGame_IsHighConfidence_WithGameRootAsInstallPath()
    {
        var lib = Dir("Library");
        var gameRoot = Dir(@"Library\Deep Rock Clone");
        MakeFile(@"Library\Deep Rock Clone\Engine\Binaries\ThirdParty\x.dll", 1 * MB);
        var shipping = MakeFile(@"Library\Deep Rock Clone\FSD\Binaries\Win64\FSD-Win64-Shipping.exe", 80 * MB);
        MakeFile(@"Library\Deep Rock Clone\Engine\Binaries\Win64\CrashReportClient.exe", 5 * MB);

        var result = NewScanner().Scan(new[] { lib }, None, None);

        var c = Assert.Single(result.Candidates);
        Assert.Equal("Deep Rock Clone", c.Name);
        Assert.Equal(gameRoot, c.InstallPath);
        Assert.Equal(shipping, c.ExePath);
        Assert.Equal(EngineType.Unreal, c.Engine);
        Assert.Equal(CustomCandidateConfidence.High, c.Confidence);
        Assert.Equal(lib, c.ScanRoot);
    }

    [Fact]
    public void Scan_UnityGame_IsHighConfidence()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\Cool Unity Game\Cool Unity Game.exe", 650 * 1024);
        MakeFile(@"Library\Cool Unity Game\UnityPlayer.dll", 30 * MB);
        Dir(@"Library\Cool Unity Game\Cool Unity Game_Data");
        MakeFile(@"Library\Cool Unity Game\UnityCrashHandler64.exe", 1 * MB);

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, None).Candidates);

        Assert.Equal("Cool Unity Game", c.Name);
        Assert.Equal(EngineType.Unity, c.Engine);
        Assert.Equal(CustomCandidateConfidence.High, c.Confidence);
        Assert.EndsWith("Cool Unity Game.exe", c.ExePath);
    }

    [Fact]
    public void Scan_GameWithManyExecutables_IsOneCandidate()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\Big Game\BigGame.exe", 40 * MB);
        MakeFile(@"Library\Big Game\steam_api64.dll", 300 * 1024);
        MakeFile(@"Library\Big Game\Editor\LevelEditor.exe", 60 * MB);
        MakeFile(@"Library\Big Game\Tools\ModTool.exe", 5 * MB);
        MakeFile(@"Library\Big Game\setup.exe");

        var result = NewScanner().Scan(new[] { lib }, None, None);

        var c = Assert.Single(result.Candidates);
        Assert.Equal("Big Game", c.Name);
    }

    [Fact]
    public void Scan_HelpersAndInstallersOnly_AreNotAdded()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\_CommonRedist\vcredist\vc_redist.x64.exe", 25 * MB);
        MakeFile(@"Library\Installers\setup.exe", 10 * MB);
        MakeFile(@"Library\Crash Tools\CrashReportClient.exe", 8 * MB);
        MakeFile(@"Library\Crash Tools\UnityCrashHandler64.exe", 1 * MB);
        Dir(@"Library\Empty Folder");
        MakeFile(@"Library\Notes\readme.txt", 10);

        Assert.Empty(NewScanner().Scan(new[] { lib }, None, None).Candidates);
    }

    [Fact]
    public void Scan_StrongGenericGame_HighConfidence_WeakGenericExe_LowConfidence()
    {
        var lib = Dir("Library");
        // Strong: exe named like the folder, >=1 MB, store SDK dll next to it.
        MakeFile(@"Library\Stardew Clone\StardewClone.exe", 8 * MB);
        MakeFile(@"Library\Stardew Clone\steam_api.dll", 200 * 1024);
        // Weak: small generic tool with no supporting signals.
        MakeFile(@"Library\Random Utility\util.exe", 400 * 1024);

        var result = NewScanner().Scan(new[] { lib }, None, None);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(CustomCandidateConfidence.High, result.Candidates.Single(c => c.Name == "Stardew Clone").Confidence);
        Assert.Equal(CustomCandidateConfidence.Low, result.Candidates.Single(c => c.Name == "Random Utility").Confidence);
        // Strong candidates are listed first.
        Assert.Equal("Stardew Clone", result.Candidates[0].Name);
    }

    [Fact]
    public void Scan_LauncherOnlyFolder_IsLowConfidence()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\Some Launcher App\Launcher.exe", 12 * MB);

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, None).Candidates);

        Assert.Equal(CustomCandidateConfidence.Low, c.Confidence);
    }

    [Fact]
    public void Scan_StrayExeInConfiguredFolder_DoesNotSwallowTheGamesBelowIt()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\stray-tool.exe", 5 * MB);
        MakeFile(@"Library\Game A\GameA.exe", 10 * MB);
        MakeFile(@"Library\Game B\GameB.exe", 10 * MB);

        var names = NewScanner().Scan(new[] { lib }, None, None).Candidates.Select(c => c.Name).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "Game A", "Game B" }, names);
    }

    [Fact]
    public void Scan_ConfiguredFolderThatIsItselfAGame_YieldsThatGame()
    {
        var gameRoot = Dir("Library\\Direct Game");
        MakeFile(@"Library\Direct Game\DirectGame.exe", 10 * MB);
        MakeFile(@"Library\Direct Game\steam_api64.dll", 200 * 1024);

        var c = Assert.Single(NewScanner().Scan(new[] { gameRoot }, None, None).Candidates);

        Assert.Equal(gameRoot, c.InstallPath);
    }

    [Fact]
    public void Scan_GameWithExeInBinSubfolder_IsRootedAtGameFolder()
    {
        var lib = Dir("Library");
        var gameRoot = Dir(@"Library\Binfolder Game");
        MakeFile(@"Library\Binfolder Game\bin\x64\BinfolderGame.exe", 12 * MB);

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, None).Candidates);

        Assert.Equal(gameRoot, c.InstallPath);
        Assert.Equal("Binfolder Game", c.Name);
    }

    [Fact]
    public void Scan_ExeNamedAfterWrapperFolder_LiftsRootToTheWrapper()
    {
        // "Game Name\Sources\Game.exe": the game is the wrapper, not "Sources".
        var lib = Dir("Library");
        var wrapper = Dir(@"Library\Expeditions - A Dirt Game");
        MakeFile(@"Library\Expeditions - A Dirt Game\Sources\Expeditions.exe", 60 * MB);
        MakeFile(@"Library\Expeditions - A Dirt Game\Sources\steam_api64.dll", 200 * 1024);

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, None).Candidates);

        Assert.Equal(wrapper, c.InstallPath);
        Assert.Equal("Expeditions - A Dirt Game", c.Name);
    }

    [Fact]
    public void Scan_ContainerWithSeveralGames_StaysSplit_AndEachGameKeepsItsOwnFolder()
    {
        // "Publisher\Game One", "Publisher\Game Two": the publisher folder is a container, not a game.
        var lib = Dir("Library");
        var one = Dir(@"Library\Some Publisher\Game One");
        var two = Dir(@"Library\Some Publisher\Game Two");
        MakeStrongGame(@"Library\Some Publisher\Game One", "GameOne");
        MakeStrongGame(@"Library\Some Publisher\Game Two", "GameTwo");

        var paths = NewScanner().Scan(new[] { lib }, None, None).Candidates.Select(c => c.InstallPath).OrderBy(p => p).ToArray();

        Assert.Equal(new[] { one, two }, paths);
    }

    [Fact]
    public void Scan_WrapperAlreadyKnown_IsSkipped_EvenWhenTheExeIsInASubfolder()
    {
        var lib = Dir("Library");
        var wrapper = Dir(@"Library\Wrapped Game");
        MakeFile(@"Library\Wrapped Game\Sources\WrappedGame.exe", 30 * MB);
        MakeFile(@"Library\Wrapped Game\Sources\steam_api64.dll", 200 * 1024);

        Assert.Empty(NewScanner().Scan(new[] { lib }, new[] { wrapper }, None).Candidates);
    }

    [Theory]
    [InlineData(@"F:\Games\Foo\Sources\Foo.exe", @"F:\Games", @"F:\Games\Foo")]              // lifts to the wrapper
    [InlineData(@"F:\Games\Foo\Sources\Bar.exe", @"F:\Games", @"F:\Games\Foo\Sources")]     // exe not named after the wrapper
    [InlineData(@"F:\Games\Foo\Foo.exe", @"F:\Games", @"F:\Games\Foo")]                      // already at the root
    [InlineData(@"F:\Games\Sources\Games.exe", @"F:\Games", @"F:\Games\Sources")]            // never lifts into the configured folder
    public void LiftToWrapperFolder_Cases(string exe, string scanRoot, string expectedRoot)
    {
        var startFolder = Path.GetDirectoryName(exe)!;   // the folder the scanner found the exe in
        Assert.Equal(expectedRoot, CustomGameHeuristics.LiftToWrapperFolder(startFolder, scanRoot, exe));
    }

    [Theory]
    [InlineData("S7GameUpdate.exe")]
    [InlineData("GameUpdate.exe")]
    [InlineData("game_update.exe")]
    [InlineData("Game Update.exe")]
    [InlineData("PatchUpdate64.exe")]
    public void Classify_UpdateWordInAnySpelling_IsNonGame(string fileName)
        => Assert.Equal(ExecutableKind.NonGame, GameExecutableFilter.Classify(fileName));

    [Theory]
    [InlineData("Updated Realm.exe")]
    [InlineData("UpdatedRealm.exe")]
    [InlineData("Upgrade Simulator.exe")]
    public void Classify_WordsThatMerelyContainUpdate_AreStillGames(string fileName)
        => Assert.Equal(ExecutableKind.Game, GameExecutableFilter.Classify(fileName));

    [Fact]
    public void Scan_VeryLargeExecutable_IsEnoughEvidenceOnItsOwn()
    {
        // Unreal 4 games whose exe is not "*-Shipping" (e.g. Ace7Game.exe): >= 60 MB is a strong game signal.
        var lib = Dir("Library");
        MakeFile(@"Library\Unlabelled Big Game\Ace7Game.exe", 90 * MB);

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, None).Candidates);

        Assert.Equal(CustomCandidateConfidence.High, c.Confidence);
    }

    [Fact]
    public void Scan_DebugBuildFolders_AreNotGames()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\Data\Base\_Dbg\GameDbg.exe", 10 * MB);
        MakeFile(@"Library\Real Game\RealGame.exe", 10 * MB);
        MakeFile(@"Library\Real Game\steam_api64.dll", 200 * 1024);

        Assert.Equal(new[] { "Real Game" }, NewScanner().Scan(new[] { lib }, None, None).Candidates.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Scan_TopLevelFolderWithOneGameBelowIt_IsTheGame_ForNestedAndWrappedLayouts()
    {
        var lib = Dir("Library");
        // exe buried several levels down, folder names inside are not descriptive ("ph_ft\work")
        var deep = Dir(@"Library\Dying Deep - The Beast");
        MakeFile(@"Library\Dying Deep - The Beast\ph_ft\work\bin\x64\DyingGame_TheBeast_x64.exe", 90 * MB);
        // "Lost in Play\LostInPlay\LostInPlay.exe": the exe folder repeats the game name
        var wrapped = Dir(@"Library\Lost in Play");
        MakeStrongGame(@"Library\Lost in Play\LostInPlay", "LostInPlay");

        var byPath = NewScanner().Scan(new[] { lib }, None, None).Candidates.ToDictionary(c => c.InstallPath);

        Assert.Equal("Dying Deep - The Beast", byPath[deep].Name);
        Assert.Equal("Lost in Play", byPath[wrapped].Name);
        Assert.Equal(2, byPath.Count);
    }

    [Theory]
    [InlineData("Gears.of.War.Reloaded-InsaneRamZes", "Gears of War Reloaded")]
    [InlineData("Wreckfest.2-InsaneRamZes", "Wreckfest 2")]
    [InlineData("The.Midnight.Walk-InsaneRamZes", "The Midnight Walk")]
    [InlineData("inZOI-InsaneRamZes", "inZOI")]
    [InlineData("Jackal-GoldBerg", "Jackal")]
    [InlineData("Half-Life", "Half-Life")]                               // real hyphenated titles are untouched
    [InlineData("X-COM", "X-COM")]
    [InlineData("Half-Life 2", "Half-Life 2")]
    [InlineData("Hades II", "Hades II")]
    [InlineData("Hell.Let.Loose", "Hell.Let.Loose")]                    // dots without a release tag are left alone
    [InlineData("icytower1.3", "icytower1.3")]
    public void StripSceneReleaseName_RemovesReleaseTagsOnly(string folder, string expected)
        => Assert.Equal(expected, CustomGameHeuristics.StripSceneReleaseName(folder));

    [Fact]
    public void InferName_UsesTheCleanedFolderName()
    {
        Assert.Equal("Town To City", CustomGameHeuristics.InferName(@"G:\Games\Town.To.City-InsaneRamZes"));
        Assert.Equal("My Cool Game", CustomGameHeuristics.InferName(@"G:\Games\My_Cool_Game"));
        Assert.Equal("Some Game", CustomGameHeuristics.InferName(@"G:\Games\Some Game [FitGirl Repack]"));
    }

    [Fact]
    public void PickExecutable_TinyStubLosesToRealExecutables_EvenWhenItsNameMatchesTheFolder()
    {
        // Control.exe (0.1 MB stub) next to Control_DX12.exe (19.5 MB): the real game executable is picked.
        var root = Dir("CONTROL - Ultimate Edition");
        MakeFile(@"CONTROL - Ultimate Edition\Control.exe", 100 * 1024);
        var dx12 = MakeFile(@"CONTROL - Ultimate Edition\Control_DX12.exe", 20 * MB);
        MakeFile(@"CONTROL - Ultimate Edition\Control_DX11.exe", 19 * MB);

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.Equal(dx12, exe!.Path);
    }

    [Fact]
    public void PickExecutable_BiggerRealExecutableBeatsAShallowerSmallTool()
    {
        // The Sims 4 layout: a small tool at the root, the real exe in Game\Bin.
        var root = Dir("The Sims Like");
        MakeFile(@"The Sims Like\dlc-toggler.exe", 150 * 1024);
        var real = MakeFile(@"The Sims Like\Game\Bin\TS4_x64.exe", 43 * MB);

        var exe = CustomGameHeuristics.PickExecutable(root, CustomGameHeuristics.CollectExecutables(root));

        Assert.Equal(real, exe!.Path);
    }

    [Fact]
    public void Scan_GameMiddlewareDlls_AndABigExe_AreEnoughEvidence()
    {
        var lib = Dir("Library");
        // "DIRT 5"-like: unrelated exe name, 16 MB, Bink + AMD AGS runtime DLLs
        MakeFile(@"Library\Racing Game\game_release.exe", 16 * MB);
        MakeFile(@"Library\Racing Game\bink2w64.dll", 500 * 1024);
        MakeFile(@"Library\Racing Game\amd_ags_x64.dll", 300 * 1024);
        // "The Sims 4"-like: 43 MB exe two levels down, small tool at the root
        MakeFile(@"Library\Life Sim\dlc-toggler.exe", 150 * 1024);
        MakeFile(@"Library\Life Sim\Game\Bin\TS4_x64.exe", 43 * MB);
        Dir(@"Library\Life Sim\Data");

        var byName = NewScanner().Scan(new[] { lib }, None, None).Candidates.ToDictionary(c => c.Name);

        Assert.Equal(CustomCandidateConfidence.High, byName["Racing Game"].Confidence);
        Assert.Equal(CustomCandidateConfidence.High, byName["Life Sim"].Confidence);
        Assert.EndsWith("TS4_x64.exe", byName["Life Sim"].ExePath);
    }

    [Fact]
    public void Scan_AmbiguousSmallGame_StaysLowConfidence_SoTheUserIsAsked()
    {
        // "Virtua Tennis 4"-like: 4.8 MB exe, a launcher, no store SDK / middleware, no engine markers.
        var lib = Dir("Library");
        MakeFile(@"Library\Old Tennis\OT4.exe", 5 * MB);
        MakeFile(@"Library\Old Tennis\Launcher.exe", 300 * 1024);
        MakeFile(@"Library\Old Tennis\unins000.exe", 1 * MB);

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, None).Candidates);

        Assert.Equal(CustomCandidateConfidence.Low, c.Confidence);
    }

    [Fact]
    public void Scan_RespectsMaxDepth()
    {
        var lib = Dir("Library");
        // Two games at depth 4 under a container (kept split): found. A game at depth 5: beyond MaxScanDepth.
        MakeFile(@"Library\a\b\c\Depth4 Game A\Depth4A.exe", 10 * MB);
        MakeFile(@"Library\a\b\c\Depth4 Game B\Depth4B.exe", 10 * MB);
        MakeFile(@"Library\a\b\c\d\Depth5 Game\Depth5.exe", 10 * MB);

        var names = NewScanner().Scan(new[] { lib }, None, None).Candidates.Select(c => c.Name).ToArray();

        Assert.Contains("Depth4 Game A", names);
        Assert.Contains("Depth4 Game B", names);
        Assert.DoesNotContain("Depth5 Game", names);
    }

    [Fact]
    public void Scan_OnlyScansConfiguredFolders()
    {
        Dir("Configured");
        MakeFile(@"Configured\Inside\Inside.exe", 10 * MB);
        MakeFile(@"NotConfigured\Outside\Outside.exe", 10 * MB);   // sibling folder that was never added

        var scanner = NewScanner();
        var configured = scanner.Scan(new[] { Path.Combine(_tmp, "Configured") }, None, None);
        var nothingConfigured = scanner.Scan(Array.Empty<string>(), None, None);

        Assert.Equal(new[] { "Inside" }, configured.Candidates.Select(c => c.Name).ToArray());
        Assert.Empty(nothingConfigured.Candidates);
    }

    [Fact]
    public void Scan_UnavailableFolder_IsReportedAndSkipped_NotDeleted()
    {
        var offline = Path.Combine(_tmp, "unplugged-drive", "Games");   // never created
        var configured = new List<string> { offline };

        var result = NewScanner().Scan(configured, None, None);

        Assert.Empty(result.Candidates);
        Assert.Equal(new[] { offline }, result.UnavailableRoots);
        // The scan does not touch the configured list: the folder is still there to be used when the drive returns.
        Assert.Equal(new[] { offline }, configured);
    }

    [Fact]
    public void Scan_SystemFolder_IsSkipped()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var result = NewScanner().Scan(new[] { windows }, None, None);

        Assert.Empty(result.Candidates);
        Assert.Single(result.SkippedRoots);
    }

    [Fact]
    public void Scan_AlreadyCancelled_ThrowsOperationCancelled()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\Game\Game.exe", 10 * MB);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => NewScanner().Scan(new[] { lib }, None, None, cts.Token));
    }

    [Fact]
    public async Task ScanAsync_ReturnsSameResultsOffTheCallingThread()
    {
        var lib = Dir("Library");
        MakeFile(@"Library\Async Game\AsyncGame.exe", 10 * MB);
        MakeFile(@"Library\Async Game\steam_api64.dll", 200 * 1024);

        var result = await NewScanner().ScanAsync(new[] { lib }, None, None);

        Assert.Equal("Async Game", Assert.Single(result.Candidates).Name);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // De-duplication
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>A folder that scores as a strong game: exe named like the folder, >= 1 MB, store SDK dll.</summary>
    private void MakeStrongGame(string relative, string exeName)
    {
        MakeFile($@"{relative}\{exeName}.exe", 10 * MB);
        MakeFile($@"{relative}\steam_api64.dll", 200 * 1024);
    }

    [Fact]
    public void Scan_GameAlreadyKnownFromAStore_IsNotAddedAgain_ByPath()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Steam Game", "SteamGame");
        MakeStrongGame(@"Library\Other Game", "OtherGame");
        var storePath = Path.Combine(_tmp, "Library", "Steam Game");

        var result = NewScanner().Scan(new[] { lib }, new[] { storePath }, None);

        Assert.Equal(new[] { "Other Game" }, result.Candidates.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Scan_StoreEntryPointingAtExeFolderInsideTheGame_StillCountsAsDuplicate()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Nested Game\bin", "NestedGame");
        var storeExeFolder = Path.Combine(_tmp, "Library", "Nested Game", "bin");   // manual/store entries often point here

        Assert.Empty(NewScanner().Scan(new[] { lib }, new[] { storeExeFolder }, None).Candidates);
    }

    [Fact]
    public void Scan_StorePathDifferingOnlyByCaseAndTrailingSlash_IsDuplicate()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Case Game", "CaseGame");
        var variant = Path.Combine(_tmp, "LIBRARY", "case game") + "\\";

        Assert.Empty(NewScanner().Scan(new[] { lib }, new[] { variant }, None).Candidates);
    }

    [Fact]
    public void Scan_SameGameNameButDifferentInstallPath_IsNotDeduplicatedByName()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Portal", "Portal");
        var storeCopy = Path.Combine(_tmp, "SteamLibrary", "steamapps", "common", "Portal");   // same name, different install

        var result = NewScanner().Scan(new[] { lib }, new[] { storeCopy }, None);

        Assert.Equal("Portal", Assert.Single(result.Candidates).Name);
    }

    [Fact]
    public void Scan_OverlappingConfiguredFolders_ReportEachGameOnce_PreferringTheMoreSpecificFolder()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Sub\Twice Found", "TwiceFound");
        var inner = Path.Combine(_tmp, "Library", "Sub");

        var result = NewScanner().Scan(new[] { lib, inner, lib.ToUpperInvariant() + "\\" }, None, None);

        // "Sub" holds exactly one game, so scanning "Library" alone would call the game "Sub"; the folder the user
        // added on purpose ("Library\Sub") knows the game is "Twice Found", and that finding is kept.
        var c = Assert.Single(result.Candidates);
        Assert.Equal("Twice Found", c.Name);
    }

    [Fact]
    public void Scan_TwoGamesWithSameFolderName_GetUniqueNames_AndBothAreKept()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Publisher A\Same Name", "SameName");
        MakeStrongGame(@"Library\Publisher A\Other A", "OtherA");
        MakeStrongGame(@"Library\Publisher B\Same Name", "SameName");
        MakeStrongGame(@"Library\Publisher B\Other B", "OtherB");

        var names = NewScanner().Scan(new[] { lib }, None, None).Candidates.Select(c => c.Name).Where(n => n.StartsWith("Same Name")).OrderBy(n => n).ToArray();

        // First path (sorted) keeps the plain name; the second is disambiguated with its parent folder.
        Assert.Equal(new[] { "Same Name", "Same Name (Publisher B)" }, names);
    }

    [Fact]
    public void Scan_NameOfExistingManualOrCustomGame_IsNotReused()
    {
        var lib = Dir("Library");
        MakeStrongGame(@"Library\Halo", "Halo");

        var c = Assert.Single(NewScanner().Scan(new[] { lib }, None, new[] { "Halo" }).Candidates);

        Assert.NotEqual("Halo", c.Name);
        Assert.StartsWith("Halo (", c.Name);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Merge / Refresh behaviour
    // ═══════════════════════════════════════════════════════════════════════════════

    private static DetectedGame Game(string name, string path, string source)
        => new() { Name = name, InstallPath = path, Source = source };

    private static Func<string, bool> Missing => _ => false;
    private static Func<string, bool> Present => _ => true;

    [Fact]
    public void Merge_StoreAndCustomAtSamePath_KeepsTheStoreEntry()
    {
        var store = new[] { Game("Elden Ring", @"G:\SteamLibrary\steamapps\common\ELDEN RING", "Steam") };
        var known = new[] { Game("ELDEN RING", @"G:\SteamLibrary\steamapps\common\ELDEN RING\", "Custom") };

        var merged = CustomGameMerger.Merge(store, known, Array.Empty<DetectedGame>(), new[] { @"G:\SteamLibrary" }, Present, Present);

        var only = Assert.Single(merged);
        Assert.Equal("Steam", only.Source);
    }

    [Fact]
    public void Merge_StoreEntriesAreReturnedUntouchedAndInOrder()
    {
        var store = new[] { Game("A", @"C:\a", "Steam"), Game("B", @"C:\b", "GOG"), Game("C", @"C:\c", "Epic") };

        var merged = CustomGameMerger.Merge(store, Array.Empty<DetectedGame>(), Array.Empty<DetectedGame>(), Array.Empty<string>());

        Assert.Equal(store.Select(g => g.Name), merged.Select(g => g.Name));
        Assert.Equal(store.Select(g => g.Source), merged.Select(g => g.Source));
    }

    [Fact]
    public void Merge_KnownCustomGame_SurvivesRefresh_WhenItsDriveIsOffline()
    {
        var known = new[] { Game("Offline Game", @"F:\Games\Offline Game", "Custom") };

        var merged = CustomGameMerger.Merge(Array.Empty<DetectedGame>(), known, Array.Empty<DetectedGame>(),
            new[] { @"F:\Games" }, pathExists: Missing, rootAvailable: Missing);

        Assert.Equal("Offline Game", Assert.Single(merged).Name);
    }

    [Fact]
    public void Merge_KnownCustomGame_IsDropped_OnlyWhenItsFolderIsGoneFromAReachableConfiguredRoot()
    {
        var known = new[] { Game("Uninstalled Game", @"F:\Games\Uninstalled Game", "Custom") };

        var merged = CustomGameMerger.Merge(Array.Empty<DetectedGame>(), known, Array.Empty<DetectedGame>(),
            new[] { @"F:\Games" }, pathExists: Missing, rootAvailable: Present);

        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_KnownCustomGame_IsKept_WhenItsRootWasRemovedFromTheList()
    {
        var known = new[] { Game("Kept Game", @"F:\Games\Kept Game", "Custom") };

        var merged = CustomGameMerger.Merge(Array.Empty<DetectedGame>(), known, Array.Empty<DetectedGame>(),
            configuredRoots: Array.Empty<string>(), pathExists: Missing, rootAvailable: Present);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_ExistingCustomGameOnDisk_IsKept()
    {
        var known = new[] { Game("On Disk", @"F:\Games\On Disk", "Custom") };

        var merged = CustomGameMerger.Merge(Array.Empty<DetectedGame>(), known, Array.Empty<DetectedGame>(),
            new[] { @"F:\Games" }, pathExists: Present, rootAvailable: Present);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_CustomCustomDuplicate_NewlyFoundCopyOfAKnownGameIsIgnored()
    {
        var known = new[] { Game("Known", @"F:\Games\Known", "Custom") };
        var found = new[] { Game("Known (again)", @"f:\games\known\", "Custom") };

        var merged = CustomGameMerger.Merge(Array.Empty<DetectedGame>(), known, found, new[] { @"F:\Games" }, Present, Present);

        Assert.Single(merged);
        Assert.Equal("Known", merged[0].Name);
    }

    [Fact]
    public void Merge_NewlyDiscoveredGame_IsAddedWithCustomSource_AndStoreGameStaysFirst()
    {
        var store = new[] { Game("Steam Game", @"C:\Steam\Steam Game", "Steam") };
        var found = new[] { Game("Fresh Game", @"F:\Games\Fresh Game", "whatever") };

        var merged = CustomGameMerger.Merge(store, Array.Empty<DetectedGame>(), found, new[] { @"F:\Games" }, Present, Present);

        Assert.Equal(new[] { "Steam Game", "Fresh Game" }, merged.Select(g => g.Name).ToArray());
        Assert.Equal("Custom", merged[1].Source);
    }

    [Fact]
    public void Merge_NewlyDiscoveredGameAlreadyFoundByAStore_IsIgnored()
    {
        var store = new[] { Game("Steam Game", @"F:\Games\Steam Game", "Steam") };
        var found = new[] { Game("Steam Game", @"F:\Games\Steam Game", "Custom") };

        var merged = CustomGameMerger.Merge(store, Array.Empty<DetectedGame>(), found, new[] { @"F:\Games" }, Present, Present);

        Assert.Equal("Steam", Assert.Single(merged).Source);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Startup / Refresh policy
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    // trigger, autoScan, folderCount, expected
    [InlineData(CustomScanTrigger.Startup, true, 1, true)]
    [InlineData(CustomScanTrigger.Startup, false, 1, true)]      // startup always scans
    [InlineData(CustomScanTrigger.Refresh, true, 1, true)]       // Refresh scans when Auto-scan is On …
    [InlineData(CustomScanTrigger.Refresh, false, 1, false)]     // … and not when it is Off
    [InlineData(CustomScanTrigger.FullRefresh, false, 1, true)]  // Full Refresh always includes custom folders
    [InlineData(CustomScanTrigger.Manual, false, 1, true)]       // Scan Now always scans
    [InlineData(CustomScanTrigger.Startup, true, 0, false)]      // nothing configured -> nothing is ever scanned
    [InlineData(CustomScanTrigger.Refresh, true, 0, false)]
    [InlineData(CustomScanTrigger.FullRefresh, true, 0, false)]
    [InlineData(CustomScanTrigger.Manual, true, 0, false)]
    public void ShouldScanForNew_FollowsStartupRefreshAndAutoScanRules(
        CustomScanTrigger trigger, bool autoScan, int folders, bool expected)
        => Assert.Equal(expected, CustomGameMerger.ShouldScanForNew(trigger, autoScan, folders));

    [Fact]
    public void SourceLabel_IsCustom_AndNotAStoreName()
    {
        Assert.Equal("Custom", CustomGameMerger.SourceName);
        Assert.True(CustomGameMerger.IsCustomSource("custom"));
        Assert.False(CustomGameMerger.IsCustomSource("Steam"));
        var candidate = new CustomGameCandidate { Name = "X", InstallPath = @"F:\X" };
        Assert.Equal("Custom", candidate.ToDetectedGame().Source);
        Assert.False(candidate.ToDetectedGame().IsManuallyAdded);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Automatic scans: what is imported vs. reviewed vs. left alone
    // ═══════════════════════════════════════════════════════════════════════════════

    private static CustomGameCandidate Candidate(string path, CustomCandidateConfidence confidence)
        => new() { Name = Path.GetFileName(path), InstallPath = path, Confidence = confidence };

    [Fact]
    public void SplitForAutomaticScan_ImportsStrongOnes_AndQueuesAmbiguousOnesForReview()
    {
        var strong = Candidate(@"F:\Games\Strong", CustomCandidateConfidence.High);
        var weak = Candidate(@"F:\Games\Weak", CustomCandidateConfidence.Low);

        var (auto, review) = CustomGameMerger.SplitForAutomaticScan(new[] { strong, weak }, Array.Empty<string>());

        Assert.Equal(new[] { strong }, auto);
        Assert.Equal(new[] { weak }, review);
    }

    [Fact]
    public void SplitForAutomaticScan_DeclinedCandidates_AreNeitherImportedNorSuggestedAgain()
    {
        var declinedStrong = Candidate(@"F:\Games\Declined Strong", CustomCandidateConfidence.High);
        var declinedWeak = Candidate(@"F:\Games\Declined Weak", CustomCandidateConfidence.Low);
        var fresh = Candidate(@"F:\Games\Fresh", CustomCandidateConfidence.High);
        var declined = new[] { @"f:\games\declined strong\", @"F:\Games\Declined Weak" };   // spelling differences don't matter

        var (auto, review) = CustomGameMerger.SplitForAutomaticScan(new[] { declinedStrong, declinedWeak, fresh }, declined);

        Assert.Equal(new[] { fresh }, auto);
        Assert.Empty(review);
    }
}
