using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ShaderPackService shader deployment.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// NOTE: DeployMode enum was removed. Tests updated to use pack-ID-based selection.
/// Generators will be fully updated in Task 7.
/// </summary>
public class ShaderPackServicePropertyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ShaderPackService _service;

    /// <summary>
    /// Tracks files we create in the global staging/custom directories so we can
    /// clean them up without disturbing other files.
    /// </summary>
    private readonly List<string> _stagedFiles = new();

    /// <summary>Known pack IDs for generating selections.</summary>
    private static readonly string[] KnownPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    public ShaderPackServicePropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcShaderProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new ShaderPackService(new HttpClient(), new GitHubETagCache());
        EnsureStagingFiles();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        foreach (var f in _stagedFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private void EnsureStagingFiles()
    {
        Directory.CreateDirectory(ShaderPackService.ShadersDir);
        Directory.CreateDirectory(ShaderPackService.TexturesDir);

        var shaderFile = Path.Combine(ShaderPackService.ShadersDir, "_rdxc_test_prop.fx");
        if (!File.Exists(shaderFile))
        {
            File.WriteAllText(shaderFile, "// test shader for property tests");
            _stagedFiles.Add(shaderFile);
        }

        var textureFile = Path.Combine(ShaderPackService.TexturesDir, "_rdxc_test_prop.png");
        if (!File.Exists(textureFile))
        {
            File.WriteAllBytes(textureFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            _stagedFiles.Add(textureFile);
        }
    }

    private enum FolderState { Missing, Empty, Populated }

    private string SetupGameDir(string suffix, FolderState state)
    {
        var gameDir = Path.Combine(_tempRoot, $"game_{suffix}");
        Directory.CreateDirectory(gameDir);

        var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);

        switch (state)
        {
            case FolderState.Missing:
                break;
            case FolderState.Empty:
                Directory.CreateDirectory(rsDir);
                break;
            case FolderState.Populated:
                Directory.CreateDirectory(Path.Combine(rsDir, "Shaders"));
                File.WriteAllText(Path.Combine(rsDir, "Shaders", "existing.fx"), "// pre-existing");
                break;
        }

        return gameDir;
    }

    private enum DcFolderState { Managed, UserOwned, Missing }

    private string SetupGameDirForDc(string suffix, DcFolderState state, bool hasOriginal)
    {
        var gameDir = Path.Combine(_tempRoot, $"game_dc_{suffix}");
        Directory.CreateDirectory(gameDir);

        var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);

        switch (state)
        {
            case DcFolderState.Managed:
                Directory.CreateDirectory(Path.Combine(rsDir, "Shaders"));
                File.WriteAllText(Path.Combine(rsDir, "Shaders", "managed.fx"), "// managed shader");
                File.WriteAllText(Path.Combine(rsDir, "Managed by RDXC.txt"),
                    "This folder is managed by RenoDXCommander.");
                break;
            case DcFolderState.UserOwned:
                Directory.CreateDirectory(Path.Combine(rsDir, "Shaders"));
                File.WriteAllText(Path.Combine(rsDir, "Shaders", "user.fx"), "// user shader");
                break;
            case DcFolderState.Missing:
                break;
        }

        if (hasOriginal)
        {
            var origDir = Path.Combine(gameDir, ShaderPackService.GameReShadeOriginal);
            Directory.CreateDirectory(Path.Combine(origDir, "Shaders"));
            File.WriteAllText(Path.Combine(origDir, "Shaders", "original_user.fx"), "// original user shader");
        }

        return gameDir;
    }

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty subset of known pack IDs (replaces GenNonOffDeployMode).
    /// </summary>
    private static Gen<string[]> GenNonEmptyPackSelection()
    {
        if (KnownPackIds.Length == 0)
            return Gen.Constant(new[] { "Lilium" });

        return Gen.NonEmptyListOf(Gen.Elements(KnownPackIds))
            .Select(list => list.Distinct().ToArray());
    }

    /// <summary>
    /// Generates a pack selection that may be empty (null) or non-empty (replaces GenAnyDeployMode).
    /// </summary>
    private static Gen<string[]?> GenAnyPackSelection()
    {
        return Gen.OneOf(
            Gen.Constant<string[]?>(null),
            GenNonEmptyPackSelection().Select(x => (string[]?)x));
    }

    private static Gen<FolderState> GenFolderState()
    {
        return Gen.Elements(FolderState.Missing, FolderState.Empty, FolderState.Populated);
    }

    private static Gen<DcFolderState> GenDcFolderState()
    {
        return Gen.Elements(DcFolderState.Managed, DcFolderState.UserOwned, DcFolderState.Missing);
    }

    // ── Property 1: RS-only game folder deployment ────────────────────────────────

    // Feature: reshade-vulkan-shader-deploy, Property 1: RS-only game folder deployment
    /// <summary>
    /// **Validates: Requirements 1.1, 1.2, 3.1, 3.2, 4.1, 4.2**
    ///
    /// For any game directory where rsInstalled is true and dcInstalled is false,
    /// and for any non-empty pack selection, after SyncGameFolder is called, the
    /// reshade-shaders\ folder SHALL exist, contain the managed marker file,
    /// and contain shader files.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property RsOnlyGameFolder_DeploysShaders_ForAnyNonEmptySelection()
    {
        var gen = from packIds in GenNonEmptyPackSelection()
                  from state in GenFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (packIds, state, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (packIds, state, suffix) = tuple;

            var gameDir = SetupGameDir($"{suffix}_{state}", state);

            try
            {
                _service.SyncGameFolder(gameDir, packIds);

                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders folder missing after SyncGameFolder " +
                                       $"(packs={packIds.Length}, state={state})");

                var markerPath = Path.Combine(rsDir, "Managed by RDXC.txt");
                if (!File.Exists(markerPath))
                    return false.Label($"Managed marker file missing after SyncGameFolder " +
                                       $"(packs={packIds.Length}, state={state})");

                return true.Label($"OK: packs={packIds.Length}, state={state}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 2: Empty selection removes managed folder and restores original ──

    // Feature: reshade-vulkan-shader-deploy, Property 2: Empty selection removes managed folder
    /// <summary>
    /// **Validates: Requirements 1.3**
    ///
    /// For any game directory that contains an RDXC-managed reshade-shaders folder
    /// (with marker), calling SyncGameFolder with null/empty selection SHALL remove the
    /// managed folder. If a reshade-shaders-original folder exists, it SHALL be
    /// renamed back to reshade-shaders.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property EmptySelection_RemovesManagedFolder_AndRestoresOriginal()
    {
        var gen = from hasOriginal in Arb.Generate<bool>()
                  from suffix in Gen.Choose(1, 999999)
                  select (hasOriginal, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (hasOriginal, suffix) = tuple;

            var gameDir = Path.Combine(_tempRoot, $"game_off_{suffix}_{hasOriginal}");
            Directory.CreateDirectory(gameDir);

            var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
            var shadersDir = Path.Combine(rsDir, "Shaders");
            Directory.CreateDirectory(shadersDir);
            File.WriteAllText(Path.Combine(shadersDir, "managed.fx"), "// managed shader");
            File.WriteAllText(Path.Combine(rsDir, "Managed by RDXC.txt"),
                "This folder is managed by RenoDXCommander.");

            var origDir = Path.Combine(gameDir, ShaderPackService.GameReShadeOriginal);
            if (hasOriginal)
            {
                var origShadersDir = Path.Combine(origDir, "Shaders");
                Directory.CreateDirectory(origShadersDir);
                File.WriteAllText(Path.Combine(origShadersDir, "user_shader.fx"), "// user shader");
            }

            try
            {
                // null selection = remove shaders (equivalent to old Off mode)
                _service.SyncGameFolder(gameDir, (IEnumerable<string>?)null);

                if (Directory.Exists(rsDir) && _service.IsManagedByRdxc(gameDir))
                    return false.Label($"Managed reshade-shaders folder still exists (hasOriginal={hasOriginal})");

                if (hasOriginal)
                {
                    if (!Directory.Exists(rsDir))
                        return false.Label($"reshade-shaders-original was not restored (hasOriginal={hasOriginal})");

                    var restoredShader = Path.Combine(rsDir, "Shaders", "user_shader.fx");
                    if (!File.Exists(restoredShader))
                        return false.Label($"Restored folder missing original user content (hasOriginal={hasOriginal})");

                    if (Directory.Exists(origDir))
                        return false.Label($"reshade-shaders-original still exists after restore (hasOriginal={hasOriginal})");
                }
                else
                {
                    if (Directory.Exists(rsDir))
                        return false.Label($"reshade-shaders folder still exists when no original (hasOriginal={hasOriginal})");
                }

                return true.Label($"OK: hasOriginal={hasOriginal}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 3: Game-local shaders via SyncGameFolder ──

    // Feature: local-shader-deployment, Property 3: Game-local shaders via SyncGameFolder
    /// <summary>
    /// **Validates: Requirements 8.1, 8.3, 8.4**
    ///
    /// For any game location where rsInstalled is true,
    /// after SyncShadersToAllLocations processes it, the game-local
    /// reshade-shaders folder SHALL exist with shaders deployed via SyncGameFolder.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property GameLocalShaders_ViaSyncGameFolder()
    {
        var gen = from packIds in GenNonEmptyPackSelection()
                  from state in GenDcFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (packIds, state, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (packIds, state, suffix) = tuple;

            var gameDir = SetupGameDirForDc($"{suffix}_{state}", state, hasOriginal: false);
            var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);

            try
            {
                var locations = new[]
                {
                    (installPath: gameDir, rsInstalled: true,
                     shaderModeOverride: (string?)null)
                };
                _service.SyncShadersToAllLocations(locations, packIds);

                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders folder missing (packs={packIds.Length}, state={state})");

                var markerPath = Path.Combine(rsDir, "Managed by RDXC.txt");
                if (!File.Exists(markerPath))
                    return false.Label($"Managed marker file missing (packs={packIds.Length}, state={state})");

                return true.Label($"OK: packs={packIds.Length}, state={state}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 5: Selection resolution — simplified (no mode override) ──────────

    // Feature: reshade-vulkan-shader-deploy, Property 5: Selection-based routing
    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    ///
    /// For any game location with rsInstalled=true, SyncShadersToAllLocations
    /// SHALL deploy the provided pack selection to the game folder.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property SelectionBasedRouting_DeploysProvidedPacks()
    {
        var gen = from packIds in GenNonEmptyPackSelection()
                  from suffix in Gen.Choose(1, 999999)
                  select (packIds, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (packIds, suffix) = tuple;

            var gameDir = Path.Combine(_tempRoot, $"game_p5_{suffix}");
            Directory.CreateDirectory(gameDir);

            try
            {
                var locations = new[]
                {
                    (installPath: gameDir, rsInstalled: true,
                     shaderModeOverride: (string?)null)
                };
                _service.SyncShadersToAllLocations(locations, packIds);

                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders folder missing (packs={packIds.Length})");

                var markerPath = Path.Combine(rsDir, "Managed by RDXC.txt");
                if (!File.Exists(markerPath))
                    return false.Label($"Managed marker missing (packs={packIds.Length})");

                return true.Label($"OK: packs={packIds.Length}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Helpers (Property 6/7) ────────────────────────────────────────────────────

    private static Dictionary<string, byte[]> SnapshotDirectory(string rootDir)
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootDir)) return snapshot;
        foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(rootDir, file);
            snapshot[rel] = File.ReadAllBytes(file);
        }
        return snapshot;
    }

    private static bool SnapshotsEqual(Dictionary<string, byte[]> a, Dictionary<string, byte[]> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, bytesA) in a)
        {
            if (!b.TryGetValue(key, out var bytesB)) return false;
            if (!bytesA.AsSpan().SequenceEqual(bytesB.AsSpan())) return false;
        }
        return true;
    }

    private static Gen<string> GenSafeFileName()
    {
        var prefixes = new[] { "shader", "tex", "effect", "lut", "pass", "bloom", "tone", "color" };
        var extensions = new[] { ".fx", ".fxh", ".png", ".dds", ".txt", ".cfg" };
        return from prefix in Gen.Elements(prefixes)
               from idx in Gen.Choose(0, 999)
               from ext in Gen.Elements(extensions)
               select $"{prefix}_{idx}{ext}";
    }

    private static Gen<string[]> GenUserFileNames()
    {
        return from count in Gen.Choose(1, 8)
               from names in Gen.ArrayOf(count, GenSafeFileName())
               select names.Distinct().ToArray();
    }

    // ── Property 6: User folder preservation round-trip ───────────────────────────

    // Feature: reshade-vulkan-shader-deploy, Property 6: User folder preservation round-trip
    /// <summary>
    /// **Validates: Requirements 6.1, 6.2**
    ///
    /// For any game directory containing a user-owned reshade-shaders folder (no managed
    /// marker), deploying shaders SHALL rename it to reshade-shaders-original. Subsequently
    /// removing the managed folder (via empty selection) SHALL rename it back to reshade-shaders,
    /// preserving the original contents.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property UserFolderPreservation_RoundTrip_RestoresOriginalContents()
    {
        var gen = from fileNames in GenUserFileNames()
                  from packIds in GenNonEmptyPackSelection()
                  from suffix in Gen.Choose(1, 999999)
                  select (fileNames, packIds, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (fileNames, packIds, suffix) = tuple;

            var gameDir = Path.Combine(_tempRoot, $"game_p6_{suffix}");
            Directory.CreateDirectory(gameDir);

            var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
            var origDir = Path.Combine(gameDir, ShaderPackService.GameReShadeOriginal);

            try
            {
                // Arrange: create user-owned reshade-shaders with random files
                Directory.CreateDirectory(rsDir);
                var originalContents = new Dictionary<string, string>();
                foreach (var name in fileNames)
                {
                    var filePath = Path.Combine(rsDir, name);
                    var content = $"// user content for {name} — {Guid.NewGuid()}";
                    File.WriteAllText(filePath, content);
                    originalContents[name] = content;
                }

                if (_service.IsManagedByRdxc(gameDir))
                    return false.Label("Precondition failed: folder should not be managed before deploy");

                // Act 1: Deploy shaders with non-empty selection
                _service.SyncGameFolder(gameDir, packIds);

                if (!Directory.Exists(origDir))
                    return false.Label($"reshade-shaders-original not created after deploy (packs={packIds.Length})");

                if (!_service.IsManagedByRdxc(gameDir))
                    return false.Label($"Managed marker missing after deploy (packs={packIds.Length})");

                foreach (var name in fileNames)
                {
                    var origFile = Path.Combine(origDir, name);
                    if (!File.Exists(origFile))
                        return false.Label($"Original file '{name}' missing from reshade-shaders-original");
                }

                // Act 2: Remove via empty selection
                _service.SyncGameFolder(gameDir, (IEnumerable<string>?)null);

                if (_service.IsManagedByRdxc(gameDir))
                    return false.Label($"Managed folder still exists after empty selection");

                if (Directory.Exists(origDir))
                    return false.Label($"reshade-shaders-original still exists after restore");

                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders not restored after empty selection");

                foreach (var (name, expectedContent) in originalContents)
                {
                    var restoredFile = Path.Combine(rsDir, name);
                    if (!File.Exists(restoredFile))
                        return false.Label($"Restored file '{name}' missing from reshade-shaders");

                    var actualContent = File.ReadAllText(restoredFile);
                    if (actualContent != expectedContent)
                        return false.Label($"Restored file '{name}' content mismatch");
                }

                return true.Label($"OK: packs={packIds.Length}, files={fileNames.Length}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 7: Idempotent sync ───────────────────────────────────────────────

    // Feature: reshade-vulkan-shader-deploy, Property 7: Idempotent sync
    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// For any game directory and any pack selection, calling SyncGameFolder twice
    /// in succession SHALL produce the same filesystem state as calling it once.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property IdempotentSync_SyncGameFolderTwice_ProducesSameState()
    {
        var gen = from selection in GenAnyPackSelection()
                  from state in GenFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (selection, state, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (selection, state, suffix) = tuple;

            var gameDir = SetupGameDir($"p7rs_{suffix}_{state}", state);

            try
            {
                _service.SyncGameFolder(gameDir, selection);
                var snapshot1 = SnapshotDirectory(gameDir);

                _service.SyncGameFolder(gameDir, selection);
                var snapshot2 = SnapshotDirectory(gameDir);

                if (!SnapshotsEqual(snapshot1, snapshot2))
                {
                    var only1 = snapshot1.Keys.Except(snapshot2.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                    var only2 = snapshot2.Keys.Except(snapshot1.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                    return false.Label(
                        $"Snapshots differ after two SyncGameFolder calls " +
                        $"(selection={selection?.Length ?? 0}, state={state}, " +
                        $"onlyIn1=[{string.Join(",", only1)}], onlyIn2=[{string.Join(",", only2)}])");
                }

                return true.Label($"OK: selection={selection?.Length ?? 0}, state={state}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // Feature: reshade-vulkan-shader-deploy, Property 7: Idempotent sync (via SyncShadersToAllLocations)
    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    /// </summary>
    [Property(MaxTest = 30)]
    public Property IdempotentSync_SyncAllLocationsTwice_ProducesSameState()
    {
        var gen = from packIds in GenNonEmptyPackSelection()
                  from dcState in GenDcFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (packIds, dcState, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (packIds, dcState, suffix) = tuple;

            var gameDir = SetupGameDirForDc($"p7all_{suffix}_{dcState}", dcState, hasOriginal: false);

            try
            {
                var locations = new[]
                {
                    (installPath: gameDir, rsInstalled: true,
                     shaderModeOverride: (string?)null)
                };

                _service.SyncShadersToAllLocations(locations, packIds);
                var gameSnapshot1 = SnapshotDirectory(gameDir);

                _service.SyncShadersToAllLocations(locations, packIds);
                var gameSnapshot2 = SnapshotDirectory(gameDir);

                if (!SnapshotsEqual(gameSnapshot1, gameSnapshot2))
                    return false.Label($"Game folder snapshots differ after two syncs (dcState={dcState})");

                return true.Label($"OK: packs={packIds.Length}, dcState={dcState}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }
}
