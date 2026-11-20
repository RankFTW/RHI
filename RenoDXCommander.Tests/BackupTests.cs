using System.IO.Compression;
using System.Text.Json.Nodes;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Backup and restore: the .rhibackup file (what goes in, what comes back out, path safety) and game matching.</summary>
public class BackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-backup-tests-" + Guid.NewGuid().ToString("N"));

    public BackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Dir(string name)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Put(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>An RHI folder and one game folder as they'd look on the old PC.</summary>
    private (string RhiDir, string GameDir) OldPc()
    {
        var rhi = Dir("old-rhi");
        Put(Path.Combine(rhi, "settings.json"), "{\"BackupFolder\":\"D:\\\\Backups\"}");
        Put(Path.Combine(rhi, "inis", "reshade.ini"), "[GENERAL]\nTemplate=1\n");
        Put(Path.Combine(rhi, "optiscaler", "saved-inis", "mine.ini"), "x=1");
        Put(Path.Combine(rhi, "optiscaler", "OptiScaler.dll"), "staged build: not backed up");
        Put(Path.Combine(rhi, "installed.json"), "[]");             // install record: never backed up
        Put(Path.Combine(rhi, "github_api.txt"), "ghp_secret");      // secret: never backed up
        Put(Path.Combine(rhi, "github_etag_cache.json"), "{}");      // cache: never backed up
        Put(Path.Combine(rhi, "game_library.json"),
            "{\"Games\":[{\"Name\":\"Cached\"}],\"ManualGames\":[{\"Name\":\"Manual\"}],\"HiddenGames\":[\"Hidden|Steam\"],\"BitnessCache\":{}}");

        var game = Dir("old-game");
        Put(Path.Combine(game, "reshade.ini"), "[GENERAL]\nPresetPath=.\\Presets\\Mine.ini\n");
        Put(Path.Combine(game, "Presets", "Mine.ini"), "Techniques=Lilium");
        Put(Path.Combine(game, "relimiter.ini"), "target_fps=117");
        Put(Path.Combine(game, "game.exe"), "exe");
        return (rhi, game);
    }

    private static BackupManifest OneGame(string gameDir) => new()
    {
        RhiVersion = "2.9.5",
        CreatedUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
        Games =
        {
            new BackupGame
            {
                Name = "Hogwarts Legacy", Store = "Steam", InstallPath = gameDir, ConfigFolder = "games/0",
                Components = { new BackupComponent { Id = BackupComponents.ReShade, Detail = "6.6.2" } },
                ConfigFiles = BackupArchive.FindGameConfigFiles(gameDir),
            },
        },
    };

    [Fact]
    public void FindGameConfigFiles_FindsKnownFilesAndThePresetReShadeUses()
    {
        var (_, game) = OldPc();
        var files = BackupArchive.FindGameConfigFiles(game);
        Assert.Contains("reshade.ini", files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("relimiter.ini", files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("Presets", "Mine.ini"), files, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("game.exe", files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindGameConfigFiles_IgnoresAPresetOutsideTheGameFolder()
    {
        var game = Dir("g");
        var outside = Path.Combine(_root, "elsewhere.ini");
        Put(outside, "x");
        Put(Path.Combine(game, "reshade.ini"), $"[GENERAL]\nPresetPath={outside}\n");
        Assert.Equal(new[] { "reshade.ini" }, BackupArchive.FindGameConfigFiles(game));
    }

    [Fact]
    public void Write_CarriesOnlyUserDataNeverRecordsSecretsOrCaches()
    {
        var (rhi, game) = OldPc();
        var zipPath = Path.Combine(_root, "b.rhibackup");
        BackupArchive.Write(zipPath, OneGame(game), rhi, new Dictionary<int, string> { [0] = game }, "{\"Global\":{}}");

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("backup.json", names);
        Assert.Contains("rhi/settings.json", names);
        Assert.Contains("rhi/inis/reshade.ini", names);
        Assert.Contains("rhi/optiscaler/saved-inis/mine.ini", names);
        Assert.Contains("rhi/game_library.json", names);
        Assert.Contains("games/0/reshade.ini", names);
        Assert.Contains("games/0/Presets/Mine.ini", names);
        Assert.Contains("nvidia.json", names);
        Assert.DoesNotContain("rhi/installed.json", names);
        Assert.DoesNotContain("rhi/github_api.txt", names);
        Assert.DoesNotContain("rhi/github_etag_cache.json", names);
        Assert.DoesNotContain("rhi/optiscaler/OptiScaler.dll", names);
        Assert.False(File.Exists(zipPath + ".tmp"));
    }

    [Fact]
    public void ApplyData_RestoresDataAndPlan_KeepsOnlyUserPartsOfTheLibrary()
    {
        var (rhi, game) = OldPc();
        var zipPath = Path.Combine(_root, "b.rhibackup");
        BackupArchive.Write(zipPath, OneGame(game), rhi, new Dictionary<int, string> { [0] = game }, null);

        var newRhi = Dir("new-rhi");
        var plan = BackupArchive.ApplyData(zipPath, newRhi);

        Assert.True(File.Exists(Path.Combine(newRhi, "settings.json")));
        Assert.True(File.Exists(Path.Combine(newRhi, "inis", "reshade.ini")));
        Assert.False(File.Exists(Path.Combine(newRhi, "installed.json")));
        Assert.False(File.Exists(Path.Combine(newRhi, "github_api.txt")));
        var lib = JsonNode.Parse(File.ReadAllText(Path.Combine(newRhi, "game_library.json")))!.AsObject();
        Assert.True(lib.ContainsKey("ManualGames"));
        Assert.True(lib.ContainsKey("HiddenGames"));
        Assert.False(lib.ContainsKey("Games"));        // scan cache isn't restored
        Assert.False(lib.ContainsKey("BitnessCache"));

        Assert.Single(plan.Games);
        Assert.False(plan.HasNvidia);
        Assert.NotNull(BackupArchive.LoadPlan(newRhi));
        Assert.True(File.Exists(Path.Combine(newRhi, "restore", "games", "0", "reshade.ini")));
    }

    [Fact]
    public void ApplyData_OverExistingData_KeepsCurrentFilesAndTheCurrentScanCache()
    {
        var (rhi, game) = OldPc();
        var zipPath = Path.Combine(_root, "b.rhibackup");
        BackupArchive.Write(zipPath, OneGame(game), rhi, new Dictionary<int, string> { [0] = game }, null);

        var current = Dir("current-rhi");
        Put(Path.Combine(current, "settings.json"), "{\"Current\":true}");
        Put(Path.Combine(current, "game_library.json"), "{\"Games\":[{\"Name\":\"Detected here\"}],\"ManualGames\":[]}");
        BackupArchive.ApplyData(zipPath, current);

        Assert.Contains("BackupFolder", File.ReadAllText(Path.Combine(current, "settings.json")));
        var kept = Directory.GetFiles(Path.Combine(current, "Backups"), "settings.json", SearchOption.AllDirectories);
        Assert.Single(kept);
        Assert.Contains("Current", File.ReadAllText(kept[0]));
        var lib = JsonNode.Parse(File.ReadAllText(Path.Combine(current, "game_library.json")))!.AsObject();
        Assert.Equal("Detected here", lib["Games"]![0]!["Name"]!.GetValue<string>());
        Assert.Equal("Manual", lib["ManualGames"]![0]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyData_IgnoresEntriesThatEscapeOrArentRestorable()
    {
        var zipPath = Path.Combine(_root, "evil.rhibackup");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            void Add(string name, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
            Add("backup.json", "{\"Format\":1,\"Games\":[]}");
            Add("rhi/../escaped.txt", "x");
            Add("rhi/inis/../../escaped2.txt", "x");
            Add("rhi/installed.json", "[]");
            Add("rhi/unlock.txt", "");
            Add("games/../../escaped3.txt", "x");
        }
        var rhi = Dir("target-rhi");
        BackupArchive.ApplyData(zipPath, rhi);

        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped2.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped3.txt")));
        Assert.False(File.Exists(Path.Combine(rhi, "installed.json")));
        Assert.False(File.Exists(Path.Combine(rhi, "unlock.txt")));
    }

    [Fact]
    public void RestoreGameConfigs_KeepsFilesThatWereAlreadyThere()
    {
        var (rhi, game) = OldPc();
        var zipPath = Path.Combine(_root, "b.rhibackup");
        var manifest = OneGame(game);
        BackupArchive.Write(zipPath, manifest, rhi, new Dictionary<int, string> { [0] = game }, null);
        var newRhi = Dir("new-rhi");
        var plan = BackupArchive.ApplyData(zipPath, newRhi);

        var newGame = Dir("new-game");
        Put(Path.Combine(newGame, "relimiter.ini"), "target_fps=60"); // the user's file on this PC
        var pre = BackupArchive.ExistingConfigs(plan.Games[0], newGame);
        Put(Path.Combine(newGame, "reshade.ini"), "fresh from the ReShade install");  // created by the restore's install

        int written = BackupArchive.RestoreGameConfigs(newRhi, plan.Games[0], newGame, pre);

        Assert.Equal(2, written); // reshade.ini + the preset
        Assert.Contains("PresetPath", File.ReadAllText(Path.Combine(newGame, "reshade.ini")));
        Assert.Equal("target_fps=60", File.ReadAllText(Path.Combine(newGame, "relimiter.ini")));
        Assert.True(File.Exists(Path.Combine(newGame, "Presets", "Mine.ini")));
    }

    [Fact]
    public void ReadManifest_RejectsFilesThatArentBackups()
    {
        var notZip = Path.Combine(_root, "x.rhibackup");
        File.WriteAllText(notZip, "hello");
        Assert.Throws<InvalidDataException>(() => BackupArchive.ReadManifest(notZip));

        var noManifest = Path.Combine(_root, "y.rhibackup");
        using (var zip = ZipFile.Open(noManifest, ZipArchiveMode.Create)) zip.CreateEntry("other.txt");
        Assert.Throws<InvalidDataException>(() => BackupArchive.ReadManifest(noManifest));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAnInstallChanges()
    {
        var (rhi, game) = OldPc();
        var paths = new Dictionary<int, string> { [0] = game };
        var m = OneGame(game);
        var a = BackupArchive.Fingerprint(rhi, m, paths);
        Assert.Equal(a, BackupArchive.Fingerprint(rhi, OneGame(game), paths));
        m.Games[0].Components.Add(new BackupComponent { Id = BackupComponents.ReLimiter });
        Assert.NotEqual(a, BackupArchive.Fingerprint(rhi, m, paths));
    }

    // ── Matching ─────────────────────────────────────────────────────────────────

    private static BackupGame G(string name, string store) => new() { Name = name, Store = store };

    [Fact]
    public void Match_ExactNameAndStoreFirst()
    {
        var backup = new List<BackupGame> { G("Hades", "Steam"), G("Hades", "Epic") };
        var current = new List<(string, string)> { ("Hades", "Epic"), ("Hades", "Steam") };
        Assert.Equal(new[] { 1, 0 }, BackupMatching.Match(backup, current));
    }

    [Fact]
    public void Match_SameNameInAnotherStore_WhenUnambiguous()
    {
        var backup = new List<BackupGame> { G("Control", "Epic") };
        Assert.Equal(new[] { 0 }, BackupMatching.Match(backup, new List<(string, string)> { ("control", "Steam") }));
        // Two copies here: don't guess
        Assert.Equal(new[] { -1 }, BackupMatching.Match(backup,
            new List<(string, string)> { ("Control", "Steam"), ("Control", "GOG") }));
    }

    [Fact]
    public void Match_NotFound_AndEachGameUsedOnce()
    {
        var backup = new List<BackupGame> { G("Gone", "Steam"), G("Tunic", "Steam"), G("Tunic", "Xbox") };
        var current = new List<(string, string)> { ("Tunic", "Steam") };
        Assert.Equal(new[] { -1, 0, -1 }, BackupMatching.Match(backup, current));
    }
}
