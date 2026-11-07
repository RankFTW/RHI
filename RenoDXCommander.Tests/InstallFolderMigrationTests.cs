using System.Reflection;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Moving a game's RHI installs from its old install folder to a new one: .original sentinel rules,
/// the new folder winning when it already has the component, all-or-nothing behaviour, reshade-shaders
/// handling and the rhi_install.txt-driven inventory.
/// </summary>
public class InstallFolderMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-move-" + Guid.NewGuid().ToString("N"));
    private readonly string _old;
    private readonly string _new;

    public InstallFolderMigrationTests()
    {
        _old = Path.Combine(_root, "Game", "Old", "Win64");
        _new = Path.Combine(_root, "Game", "platform", "steam");
        Directory.CreateDirectory(_old);
        Directory.CreateDirectory(_new);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; left for the OS to clean */ }
    }

    private static void Put(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private FolderMovePlan Plan(params (string Rel, bool Dir)[] entries) => PlanGroup("Test", false, entries);

    private FolderMovePlan PlanGroup(string name, bool newDirWins, params (string Rel, bool Dir)[] entries)
    {
        var plan = new FolderMovePlan { OldDir = _old, NewDir = _new };
        var g = new FolderMoveGroup { Name = name, NewDirWins = newDirWins };
        foreach (var (rel, dir) in entries)
            g.Entries.Add(new FolderMoveEntry { Source = Path.Combine(_old, rel), Dest = Path.Combine(_new, rel), IsDirectory = dir });
        plan.Groups.Add(g);
        return plan;
    }

    // ── Sentinel rules ────────────────────────────────────────────────────────

    [Fact]
    public void Real_backup_is_restored_in_old_folder_and_sentinel_written_in_new()
    {
        Put(Path.Combine(_old, "dxgi.dll"), "rhi");
        Put(Path.Combine(_old, "dxgi.dll.original"), "game");

        var r = InstallFolderMover.Execute(Plan(("dxgi.dll", false)));

        Assert.True(r.Success, r.AbortReason);
        Assert.Equal("rhi", File.ReadAllText(Path.Combine(_new, "dxgi.dll")));
        Assert.Equal(0, new FileInfo(Path.Combine(_new, "dxgi.dll.original")).Length);
        Assert.Equal("game", File.ReadAllText(Path.Combine(_old, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "dxgi.dll.original")));
    }

    [Fact]
    public void Zero_byte_sentinel_is_deleted_in_old_folder()
    {
        Put(Path.Combine(_old, "version.dll"), "rhi");
        File.WriteAllBytes(Path.Combine(_old, "version.dll.original"), Array.Empty<byte>());

        var r = InstallFolderMover.Execute(Plan(("version.dll", false)));

        Assert.True(r.Success, r.AbortReason);
        Assert.False(File.Exists(Path.Combine(_old, "version.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "version.dll.original")));
        Assert.True(File.Exists(Path.Combine(_new, "version.dll")));
        Assert.Equal(0, new FileInfo(Path.Combine(_new, "version.dll.original")).Length);
    }

    [Fact]
    public void Foreign_file_in_new_folder_is_backed_up_like_an_install()
    {
        Put(Path.Combine(_old, "dxgi.dll"), "rhi");
        Put(Path.Combine(_new, "dxgi.dll"), "game-new");

        var r = InstallFolderMover.Execute(Plan(("dxgi.dll", false)));

        Assert.True(r.Success, r.AbortReason);
        Assert.Equal("rhi", File.ReadAllText(Path.Combine(_new, "dxgi.dll")));
        Assert.Equal("game-new", File.ReadAllText(Path.Combine(_new, "dxgi.dll.original")));
        Assert.False(File.Exists(Path.Combine(_old, "dxgi.dll")));
    }

    [Fact]
    public void File_without_backup_replaces_the_new_folders_copy()
    {
        Put(Path.Combine(_old, "ReShade.log"), "old-log");
        Put(Path.Combine(_new, "ReShade.log"), "stale-log");
        var plan = new FolderMovePlan { OldDir = _old, NewDir = _new };
        var g = new FolderMoveGroup { Name = "ReShade" };
        g.Entries.Add(new FolderMoveEntry { Source = Path.Combine(_old, "ReShade.log"), Dest = Path.Combine(_new, "ReShade.log"), BackupSuffix = null });
        plan.Groups.Add(g);

        var r = InstallFolderMover.Execute(plan);

        Assert.True(r.Success, r.AbortReason);
        Assert.Equal("old-log", File.ReadAllText(Path.Combine(_new, "ReShade.log")));
        Assert.False(File.Exists(Path.Combine(_new, "ReShade.log.original")));
        Assert.False(File.Exists(Path.Combine(_old, "ReShade.log")));
        Assert.Empty(Directory.GetDirectories(_old, ".rhi-move-*"));
    }

    [Fact]
    public void Hooked_backup_is_restored_in_old_folder_and_new_game_file_kept_as_hooked()
    {
        Put(Path.Combine(_old, "binkw64.dll"), "proxy");
        Put(Path.Combine(_old, "binkw64Hooked.dll"), "game-old");
        Put(Path.Combine(_new, "binkw64.dll"), "game-new");
        var plan = new FolderMovePlan { OldDir = _old, NewDir = _new };
        var g = new FolderMoveGroup { Name = "RTX Encore" };
        g.Entries.Add(new FolderMoveEntry { Source = Path.Combine(_old, "binkw64.dll"), Dest = Path.Combine(_new, "binkw64.dll"), HookedBackup = true });
        plan.Groups.Add(g);

        var r = InstallFolderMover.Execute(plan);

        Assert.True(r.Success, r.AbortReason);
        Assert.Equal("game-old", File.ReadAllText(Path.Combine(_old, "binkw64.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "binkw64Hooked.dll")));
        Assert.Equal("proxy", File.ReadAllText(Path.Combine(_new, "binkw64.dll")));
        Assert.Equal("game-new", File.ReadAllText(Path.Combine(_new, "binkw64Hooked.dll")));
    }

    [Fact]
    public void Dll_needing_proof_without_backup_is_left_alone()
    {
        Put(Path.Combine(_old, "nvngx_dlss.dll"), "game's own");
        var plan = new FolderMovePlan { OldDir = _old, NewDir = _new };
        var g = new FolderMoveGroup { Name = "Shared DLLs" };
        g.Entries.Add(new FolderMoveEntry { Source = Path.Combine(_old, "nvngx_dlss.dll"), Dest = Path.Combine(_new, "nvngx_dlss.dll"), RequireBackup = true });
        plan.Groups.Add(g);

        var r = InstallFolderMover.Execute(plan);

        Assert.True(r.Success, r.AbortReason);
        Assert.True(File.Exists(Path.Combine(_old, "nvngx_dlss.dll")));
        Assert.False(File.Exists(Path.Combine(_new, "nvngx_dlss.dll")));
    }

    // ── New folder wins ───────────────────────────────────────────────────────

    [Fact]
    public void New_folder_wins_old_copy_removed_and_game_file_restored()
    {
        Put(Path.Combine(_old, "dxgi.dll"), "rhi-old");
        Put(Path.Combine(_old, "dxgi.dll.original"), "game");
        Put(Path.Combine(_new, "dxgi.dll"), "rhi-new");

        var r = InstallFolderMover.Execute(PlanGroup("ReShade", newDirWins: true, ("dxgi.dll", false)));

        Assert.True(r.Success, r.AbortReason);
        Assert.Equal("rhi-new", File.ReadAllText(Path.Combine(_new, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_new, "dxgi.dll.original")));
        Assert.Equal("game", File.ReadAllText(Path.Combine(_old, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "dxgi.dll.original")));
        Assert.Empty(Directory.GetDirectories(_old)); // trash folder gone
    }

    [Fact]
    public void Rhi_copy_already_in_new_folder_wins_per_file()
    {
        Put(Path.Combine(_old, "winmm.dll"), "old");
        File.WriteAllBytes(Path.Combine(_old, "winmm.dll.original"), Array.Empty<byte>());
        Put(Path.Combine(_new, "winmm.dll"), "new");
        File.WriteAllBytes(Path.Combine(_new, "winmm.dll.original"), Array.Empty<byte>());

        var r = InstallFolderMover.Execute(Plan(("winmm.dll", false)));

        Assert.True(r.Success, r.AbortReason);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_new, "winmm.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "winmm.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "winmm.dll.original")));
    }

    // ── All or nothing ────────────────────────────────────────────────────────

    [Fact]
    public void Locked_file_aborts_before_anything_moves()
    {
        Put(Path.Combine(_old, "dxgi.dll"), "rhi");
        Put(Path.Combine(_old, "reshade.ini"), "ini");

        FolderMoveResult r;
        using (new FileStream(Path.Combine(_old, "reshade.ini"), FileMode.Open, FileAccess.Read, FileShare.Read))
            r = InstallFolderMover.Execute(Plan(("dxgi.dll", false), ("reshade.ini", false)));

        Assert.False(r.Success);
        Assert.NotNull(r.AbortReason);
        Assert.True(File.Exists(Path.Combine(_old, "dxgi.dll")));
        Assert.True(File.Exists(Path.Combine(_old, "reshade.ini")));
        Assert.Empty(Directory.GetFileSystemEntries(_new));
    }

    [Fact]
    public void Failure_part_way_undoes_earlier_moves()
    {
        Put(Path.Combine(_old, "a.addon64"), "a");
        File.WriteAllBytes(Path.Combine(_old, "a.addon64.original"), Array.Empty<byte>());
        Put(Path.Combine(_old, "b.addon64"), "b");
        Directory.CreateDirectory(Path.Combine(_new, "b.addon64")); // a folder where the file must go: the move fails

        var r = InstallFolderMover.Execute(Plan(("a.addon64", false), ("b.addon64", false)));

        Assert.False(r.Success);
        Assert.Equal("a", File.ReadAllText(Path.Combine(_old, "a.addon64")));
        Assert.True(File.Exists(Path.Combine(_old, "a.addon64.original")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_old, "b.addon64")));
        Assert.False(File.Exists(Path.Combine(_new, "a.addon64")));
        Assert.False(File.Exists(Path.Combine(_new, "a.addon64.original")));
    }

    // ── reshade-shaders ───────────────────────────────────────────────────────

    private FolderMovePlan ShadersPlan()
    {
        var plan = new FolderMovePlan { OldDir = _old, NewDir = _new };
        var g = new FolderMoveGroup { Name = "Shaders" };
        g.Entries.Add(new FolderMoveEntry
        {
            Source = Path.Combine(_old, "reshade-shaders"),
            Dest = Path.Combine(_new, "reshade-shaders"),
            IsDirectory = true,
            IsManagedShaders = true,
            ManagedMarkers = new[] { "Managed by RDXC.txt" },
        });
        plan.Groups.Add(g);
        return plan;
    }

    [Fact]
    public void Managed_shaders_move_and_old_original_folder_is_restored()
    {
        Put(Path.Combine(_old, "reshade-shaders", "Managed by RDXC.txt"), "rhi");
        Put(Path.Combine(_old, "reshade-shaders", "Shaders", "a.fx"), "fx");
        Put(Path.Combine(_old, "reshade-shaders-original", "Shaders", "user.fx"), "user");

        var r = InstallFolderMover.Execute(ShadersPlan());

        Assert.True(r.Success, r.AbortReason);
        Assert.True(File.Exists(Path.Combine(_new, "reshade-shaders", "Shaders", "a.fx")));
        Assert.True(File.Exists(Path.Combine(_old, "reshade-shaders", "Shaders", "user.fx")));
        Assert.False(Directory.Exists(Path.Combine(_old, "reshade-shaders-original")));
    }

    [Fact]
    public void New_folders_own_shaders_are_kept_as_original()
    {
        Put(Path.Combine(_old, "reshade-shaders", "Managed by RDXC.txt"), "rhi");
        Put(Path.Combine(_new, "reshade-shaders", "Shaders", "mine.fx"), "mine");

        var r = InstallFolderMover.Execute(ShadersPlan());

        Assert.True(r.Success, r.AbortReason);
        Assert.True(File.Exists(Path.Combine(_new, "reshade-shaders", "Managed by RDXC.txt")));
        Assert.True(File.Exists(Path.Combine(_new, "reshade-shaders-original", "Shaders", "mine.fx")));
    }

    // ── rhi_install.txt-driven inventory (service) ───────────────────────────

    [Fact]
    public void OptiScaler_and_nr_files_from_rhi_install_move_with_the_manifest()
    {
        const string game = "Migration Test Game 7f3a";
        Put(Path.Combine(_old, "dxgi.dll"), "optiscaler");
        File.WriteAllBytes(Path.Combine(_old, "dxgi.dll.original"), Array.Empty<byte>());
        Put(Path.Combine(_old, "OptiScaler.ini"), "[ini]");
        Put(Path.Combine(_old, "nvngx_dlss.dll"), "os dlss");
        Put(Path.Combine(_old, "nvngx_dlss.dll.original"), "game dlss");
        Put(Path.Combine(_old, "nvngx_dlssg.dll"), "game fg (not RHI's)");
        Put(Path.Combine(_old, "OptiScaler", "plugins", "OptiPatcher.asi"), "asi");
        Put(Path.Combine(_old, "dlss5-feed.addon64"), "feeder");
        Put(Path.Combine(_old, "unrelated.dll"), "game");
        var manifest = new RhiInstallManifest
        {
            Component = "OptiScaler",
            InstalledAs = "dxgi.dll",
            Files = { "dxgi.dll", "OptiScaler.ini", "nvngx_dlss.dll", "nvngx_dlssg.dll" },
            Folders = { "OptiScaler", "plugins" },
        };
        manifest.SharedFiles["nvngx_dlss.dll"] = new List<string> { "OptiScaler" };
        manifest.Components["Feeder"] = new RhiComponentRecord { Files = { "dlss5-feed.addon64" } };
        File.WriteAllText(Path.Combine(_old, RhiInstallManifest.FileName), JsonSerializer.Serialize(manifest));

        var aux = new List<AuxInstalledRecord>
        {
            new() { GameName = game, InstallPath = _old, AddonType = OptiScalerService.AddonType, InstalledAs = "dxgi.dll" },
        };
        var (svc, auxFake) = MakeService(aux);

        var outcome = svc.Migrate(new FolderMigrationRequest { GameName = game, OldDir = _old, NewDir = _new });

        Assert.True(outcome.Success, outcome.Reason);
        Assert.Equal("optiscaler", File.ReadAllText(Path.Combine(_new, "dxgi.dll")));
        Assert.Equal(0, new FileInfo(Path.Combine(_new, "dxgi.dll.original")).Length);
        Assert.True(File.Exists(Path.Combine(_new, "OptiScaler.ini")));
        Assert.Equal("os dlss", File.ReadAllText(Path.Combine(_new, "nvngx_dlss.dll")));
        Assert.True(File.Exists(Path.Combine(_new, "OptiScaler", "plugins", "OptiPatcher.asi")));
        Assert.True(File.Exists(Path.Combine(_new, "dlss5-feed.addon64")));
        Assert.True(File.Exists(Path.Combine(_new, RhiInstallManifest.FileName)));
        Assert.Equal("Feeder", RhiInstallManifest.Read(_new)!.Components.Keys.Single());

        // Old folder: the game's own files are back, RHI's are gone
        Assert.Equal("game dlss", File.ReadAllText(Path.Combine(_old, "nvngx_dlss.dll")));
        Assert.Equal("game fg (not RHI's)", File.ReadAllText(Path.Combine(_old, "nvngx_dlssg.dll")));
        Assert.True(File.Exists(Path.Combine(_old, "unrelated.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_old, "dxgi.dll.original")));
        Assert.False(File.Exists(Path.Combine(_old, RhiInstallManifest.FileName)));
        Assert.False(Directory.Exists(Path.Combine(_old, "OptiScaler")));
        Assert.Contains(nameof(IAuxInstallService.RepointRecords), auxFake.Calls);
    }

    [Fact]
    public void Nothing_of_rhis_means_nothing_to_move()
    {
        Put(Path.Combine(_old, "game.exe"), "exe");
        var (svc, _) = MakeService(new List<AuxInstalledRecord>());
        var request = new FolderMigrationRequest { GameName = "Migration Test Game 7f3a", OldDir = _old, NewDir = _new };

        Assert.False(svc.HasInstalls(request));
        var outcome = svc.Migrate(request);
        Assert.True(outcome.NothingToMove);
        Assert.True(File.Exists(Path.Combine(_old, "game.exe")));
    }

    [Fact]
    public void Merge_rebases_absolute_paths_and_keeps_new_folders_components()
    {
        var oldM = new RhiInstallManifest { Component = "RHI" };
        oldM.Components["RtxEncore"] = new RhiComponentRecord { Files = { Path.Combine(_old, "version.dll") } };
        oldM.Components["Feeder"] = new RhiComponentRecord { Files = { "old-feeder.addon64" } };
        var newM = new RhiInstallManifest { Component = "RHI" };
        newM.Components["Feeder"] = new RhiComponentRecord { Files = { "new-feeder.addon64" } };

        var json = InstallFolderMigrationService.MergeManifest(oldM, newM, _old, _new, optiScalerNewWins: false);
        var merged = JsonSerializer.Deserialize<RhiInstallManifest>(json)!;

        Assert.Equal(Path.Combine(_new, "version.dll"), merged.Components["RtxEncore"].Files.Single());
        Assert.Equal("new-feeder.addon64", merged.Components["Feeder"].Files.Single());
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private static (InstallFolderMigrationService Svc, FakeStore Aux) MakeService(List<AuxInstalledRecord> aux)
    {
        var auxProxy = FakeStore.Create<IAuxInstallService>(out var auxFake);
        auxFake.Handlers[nameof(IAuxInstallService.LoadAll)] = _ => aux;
        var svc = new InstallFolderMigrationService(auxProxy,
            FakeStore.Create<IModInstallService>(out _),
            FakeStore.Create<ILumaService>(out _),
            FakeStore.Create<IREFrameworkService>(out _),
            FakeStore.Create<IDxvkService>(out _));
        return (svc, auxFake);
    }

    /// <summary>Interface fake: list-returning methods give empty lists, others defaults, unless a handler is set.</summary>
    public class FakeStore : DispatchProxy
    {
        public Dictionary<string, Func<object?[]?, object?>> Handlers { get; } = new();
        public List<string> Calls { get; } = new();

        public static T Create<T>(out FakeStore store) where T : class
        {
            var proxy = DispatchProxy.Create<T, FakeStore>();
            store = (FakeStore)(object)proxy;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            if (Handlers.TryGetValue(method.Name, out var handler)) return handler(args);
            var t = method.ReturnType;
            if (t == typeof(void)) return null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Activator.CreateInstance(t);
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }
}
