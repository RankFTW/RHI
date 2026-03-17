using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ShaderPackService shader deployment.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
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

    public ShaderPackServicePropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcShaderProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new ShaderPackService(new HttpClient());

        // Ensure staging directories exist and have at least one test shader file.
        // DeployPacksIfAbsent falls back to DeployFolderIfAbsent (copies everything
        // from staging) when no per-pack records exist in settings.json — which is
        // the case in a clean test environment.
        EnsureStagingFiles();
    }

    public void Dispose()
    {
        // Clean up temp game directories
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }

        // Clean up any files we placed in global staging/custom directories
        foreach (var f in _stagedFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places a small test shader file in the global staging Shaders directory
    /// and a test texture in the Textures directory so that DeployFolderIfAbsent
    /// has something to copy. Files are tracked for cleanup.
    /// </summary>
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
            File.WriteAllBytes(textureFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header stub
            _stagedFiles.Add(textureFile);
        }
    }

    /// <summary>
    /// Places a small test shader file in the global Custom staging directories
    /// so that DeployCustomIfAbsent has something to copy. Files are tracked for cleanup.
    /// </summary>
    private void EnsureCustomStagingFiles()
    {
        Directory.CreateDirectory(ShaderPackService.CustomShadersDir);
        Directory.CreateDirectory(ShaderPackService.CustomTexturesDir);

        var shaderFile = Path.Combine(ShaderPackService.CustomShadersDir, "_rdxc_test_custom.fx");
        if (!File.Exists(shaderFile))
        {
            File.WriteAllText(shaderFile, "// test custom shader for property tests");
            _stagedFiles.Add(shaderFile);
        }

        var textureFile = Path.Combine(ShaderPackService.CustomTexturesDir, "_rdxc_test_custom.png");
        if (!File.Exists(textureFile))
        {
            File.WriteAllBytes(textureFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            _stagedFiles.Add(textureFile);
        }
    }

    /// <summary>
    /// Enum representing the pre-existing state of the reshade-shaders folder
    /// before SyncGameFolder is called.
    /// </summary>
    private enum FolderState { Missing, Empty, Populated }

    /// <summary>
    /// Sets up the game directory with the specified pre-existing folder state.
    /// Returns the game directory path.
    /// </summary>
    private string SetupGameDir(string suffix, FolderState state)
    {
        var gameDir = Path.Combine(_tempRoot, $"game_{suffix}");
        Directory.CreateDirectory(gameDir);

        var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);

        switch (state)
        {
            case FolderState.Missing:
                // Do nothing — folder does not exist
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

    /// <summary>
    /// Enum representing the pre-existing state of the reshade-shaders folder
    /// specifically for DC mode tests: managed by RDXC, user-owned, or missing.
    /// </summary>
    private enum DcFolderState { Managed, UserOwned, Missing }

    /// <summary>
    /// Sets up a game directory with the specified DC folder state.
    /// Returns the game directory path.
    /// </summary>
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
                // No reshade-shaders folder
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
    /// Generator for non-Off DeployMode values (Minimum, All, User).
    /// </summary>
    private static Gen<ShaderPackService.DeployMode> GenNonOffDeployMode()
    {
        return Gen.Elements(
            ShaderPackService.DeployMode.Minimum,
            ShaderPackService.DeployMode.All,
            ShaderPackService.DeployMode.User);
    }

    /// <summary>
    /// Generator for FolderState values.
    /// </summary>
    private static Gen<FolderState> GenFolderState()
    {
        return Gen.Elements(FolderState.Missing, FolderState.Empty, FolderState.Populated);
    }

    /// <summary>
    /// Generator for DcFolderState values.
    /// </summary>
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
    /// and for any non-Off DeployMode, after SyncGameFolder is called, the
    /// reshade-shaders\ folder SHALL exist, contain the managed marker file,
    /// and contain shader files matching the effective mode.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RsOnlyGameFolder_DeploysShaders_ForAnyNonOffMode()
    {
        var gen = from mode in GenNonOffDeployMode()
                  from state in GenFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (mode, state, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (mode, state, suffix) = tuple;

            // Ensure custom staging files exist for User mode
            if (mode == ShaderPackService.DeployMode.User)
                EnsureCustomStagingFiles();

            var gameDir = SetupGameDir($"{suffix}_{state}_{mode}", state);

            try
            {
                // Act
                _service.SyncGameFolder(gameDir, mode);

                // Assert: reshade-shaders folder exists
                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders folder missing after SyncGameFolder " +
                                       $"(mode={mode}, state={state})");

                // Assert: managed marker file exists
                var markerPath = Path.Combine(rsDir, "Managed by RDXC.txt");
                if (!File.Exists(markerPath))
                    return false.Label($"Managed marker file missing after SyncGameFolder " +
                                       $"(mode={mode}, state={state})");

                // Assert: shader files exist matching the mode
                var shadersDir = Path.Combine(rsDir, "Shaders");
                var texturesDir = Path.Combine(rsDir, "Textures");

                if (mode == ShaderPackService.DeployMode.User)
                {
                    // User mode: custom shader files should be deployed
                    var hasCustomShader = File.Exists(Path.Combine(shadersDir, "_rdxc_test_custom.fx"));
                    if (!hasCustomShader)
                        return false.Label($"Custom shader file not deployed for User mode " +
                                           $"(state={state})");
                }
                else
                {
                    // Minimum or All mode: staging shader files should be deployed
                    // The fallback path in DeployPacksIfAbsent copies everything from staging
                    var hasShaderFiles = Directory.Exists(shadersDir) &&
                                         Directory.EnumerateFiles(shadersDir, "*", SearchOption.AllDirectories).Any();
                    if (!hasShaderFiles)
                        return false.Label($"No shader files deployed for {mode} mode " +
                                           $"(state={state})");
                }

                return true.Label($"OK: mode={mode}, state={state}");
            }
            finally
            {
                // Clean up game directory for this iteration
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 2: Off mode removes managed folder and restores original ─────────

    // Feature: reshade-vulkan-shader-deploy, Property 2: Off mode removes managed folder and restores original
    /// <summary>
    /// **Validates: Requirements 1.3**
    ///
    /// For any game directory that contains an RDXC-managed reshade-shaders folder
    /// (with marker), calling SyncGameFolder with DeployMode.Off SHALL remove the
    /// managed folder. If a reshade-shaders-original folder exists, it SHALL be
    /// renamed back to reshade-shaders.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OffMode_RemovesManagedFolder_AndRestoresOriginal()
    {
        var gen = from hasOriginal in Arb.Generate<bool>()
                  from suffix in Gen.Choose(1, 999999)
                  select (hasOriginal, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (hasOriginal, suffix) = tuple;

            // Set up a game dir with an RDXC-managed reshade-shaders folder
            var gameDir = Path.Combine(_tempRoot, $"game_off_{suffix}_{hasOriginal}");
            Directory.CreateDirectory(gameDir);

            var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
            var shadersDir = Path.Combine(rsDir, "Shaders");
            Directory.CreateDirectory(shadersDir);
            File.WriteAllText(Path.Combine(shadersDir, "managed.fx"), "// managed shader");

            // Write the RDXC marker so IsManagedByRdxc returns true
            File.WriteAllText(Path.Combine(rsDir, "Managed by RDXC.txt"),
                "This folder is managed by RenoDXCommander.");

            // Optionally create reshade-shaders-original with user content
            var origDir = Path.Combine(gameDir, ShaderPackService.GameReShadeOriginal);
            if (hasOriginal)
            {
                var origShadersDir = Path.Combine(origDir, "Shaders");
                Directory.CreateDirectory(origShadersDir);
                File.WriteAllText(Path.Combine(origShadersDir, "user_shader.fx"), "// user shader");
            }

            try
            {
                // Act
                _service.SyncGameFolder(gameDir, ShaderPackService.DeployMode.Off);

                // Assert: managed folder is removed
                if (Directory.Exists(rsDir) && _service.IsManagedByRdxc(gameDir))
                    return false.Label($"Managed reshade-shaders folder still exists " +
                                       $"(hasOriginal={hasOriginal})");

                if (hasOriginal)
                {
                    // Assert: original was restored to reshade-shaders
                    if (!Directory.Exists(rsDir))
                        return false.Label($"reshade-shaders-original was not restored to reshade-shaders " +
                                           $"(hasOriginal={hasOriginal})");

                    // Assert: the restored folder contains the original user content
                    var restoredShader = Path.Combine(rsDir, "Shaders", "user_shader.fx");
                    if (!File.Exists(restoredShader))
                        return false.Label($"Restored folder missing original user content " +
                                           $"(hasOriginal={hasOriginal})");

                    // Assert: reshade-shaders-original no longer exists (it was renamed)
                    if (Directory.Exists(origDir))
                        return false.Label($"reshade-shaders-original still exists after restore " +
                                           $"(hasOriginal={hasOriginal})");
                }
                else
                {
                    // Assert: no reshade-shaders folder exists (it was removed, nothing to restore)
                    if (Directory.Exists(rsDir))
                        return false.Label($"reshade-shaders folder still exists when no original " +
                                           $"(hasOriginal={hasOriginal})");
                }

                return true.Label($"OK: hasOriginal={hasOriginal}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    /// <summary>
    /// Snapshots the files currently in the DC global Reshade folder so we can
    /// clean up only the files our test created.
    /// </summary>
    private HashSet<string> SnapshotDcFolder()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(ShaderPackService.DcShadersDir))
            foreach (var f in Directory.EnumerateFiles(ShaderPackService.DcShadersDir, "*", SearchOption.AllDirectories))
                files.Add(f);
        if (Directory.Exists(ShaderPackService.DcTexturesDir))
            foreach (var f in Directory.EnumerateFiles(ShaderPackService.DcTexturesDir, "*", SearchOption.AllDirectories))
                files.Add(f);
        return files;
    }

    /// <summary>
    /// Removes files from the DC global folder that were NOT in the pre-test snapshot.
    /// </summary>
    private void CleanupDcFolder(HashSet<string> preExisting)
    {
        if (Directory.Exists(ShaderPackService.DcShadersDir))
            foreach (var f in Directory.EnumerateFiles(ShaderPackService.DcShadersDir, "*", SearchOption.AllDirectories))
                if (!preExisting.Contains(f))
                    try { File.Delete(f); } catch { }
        if (Directory.Exists(ShaderPackService.DcTexturesDir))
            foreach (var f in Directory.EnumerateFiles(ShaderPackService.DcTexturesDir, "*", SearchOption.AllDirectories))
                if (!preExisting.Contains(f))
                    try { File.Delete(f); } catch { }
    }

    // ── Property 3: DC mode cleanup removes game-local shaders and restores original ──

    // Feature: reshade-vulkan-shader-deploy, Property 3: DC mode cleanup removes game-local shaders and restores original
    /// <summary>
    /// **Validates: Requirements 2.1, 2.2**
    ///
    /// For any game location where dcInstalled is true and dcMode is true,
    /// after SyncShadersToAllLocations processes it, the RDXC-managed
    /// reshade-shaders folder SHALL not exist in the game directory.
    /// If a reshade-shaders-original folder was present, it SHALL be
    /// restored to reshade-shaders.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DcModeCleanup_RemovesGameLocalShaders_AndRestoresOriginal()
    {
        var gen = from state in GenDcFolderState()
                  from hasOriginal in Arb.Generate<bool>()
                  from suffix in Gen.Choose(1, 999999)
                  select (state, hasOriginal, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (state, hasOriginal, suffix) = tuple;

            // Skip invalid combination: user-owned folder + hasOriginal both present
            // because RemoveFromGameFolder would try to rename reshade-shaders to
            // reshade-shaders-original, but it already exists — the rename is skipped.
            // We still test this case but adjust expectations below.

            var gameDir = SetupGameDirForDc($"{suffix}_{state}_{hasOriginal}", state, hasOriginal);
            var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
            var origDir = Path.Combine(gameDir, ShaderPackService.GameReShadeOriginal);

            try
            {
                // Act: call SyncShadersToAllLocations with dcInstalled=true, dcMode=true
                var locations = new[]
                {
                    (installPath: gameDir, dcInstalled: true, rsInstalled: true,
                     dcMode: true, shaderModeOverride: (string?)null)
                };
                _service.SyncShadersToAllLocations(locations);

                // Assert: RDXC-managed folder is gone
                if (_service.IsManagedByRdxc(gameDir))
                    return false.Label($"RDXC-managed reshade-shaders still exists " +
                                       $"(state={state}, hasOriginal={hasOriginal})");

                if (state == DcFolderState.Managed)
                {
                    // Managed folder should be deleted entirely
                    if (hasOriginal)
                    {
                        // Original should be restored to reshade-shaders
                        if (!Directory.Exists(rsDir))
                            return false.Label($"reshade-shaders-original was not restored after managed removal " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                        var restoredShader = Path.Combine(rsDir, "Shaders", "original_user.fx");
                        if (!File.Exists(restoredShader))
                            return false.Label($"Restored folder missing original user content " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                        if (Directory.Exists(origDir))
                            return false.Label($"reshade-shaders-original still exists after restore " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                    }
                    else
                    {
                        // No original to restore — reshade-shaders should be gone
                        if (Directory.Exists(rsDir))
                            return false.Label($"reshade-shaders still exists when no original to restore " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                    }
                }
                else if (state == DcFolderState.UserOwned)
                {
                    // User-owned folder gets renamed to reshade-shaders-original by RemoveFromGameFolder.
                    // If hasOriginal was already true, the rename is skipped (original already exists).
                    if (hasOriginal)
                    {
                        // reshade-shaders-original already existed, so the user-owned folder
                        // could not be renamed — it stays in place (not managed, so no deletion).
                        // RestoreOriginalIfPresent won't restore because reshade-shaders still exists.
                    }
                    else
                    {
                        // User-owned folder was renamed to reshade-shaders-original,
                        // then RestoreOriginalIfPresent restores it back to reshade-shaders
                        // (since reshade-shaders is now gone after the rename).
                        if (!Directory.Exists(rsDir))
                            return false.Label($"User-owned folder was not restored after rename round-trip " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                        var userShader = Path.Combine(rsDir, "Shaders", "user.fx");
                        if (!File.Exists(userShader))
                            return false.Label($"Restored user-owned folder missing content " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                    }
                }
                else // Missing
                {
                    if (hasOriginal)
                    {
                        // Original should be restored to reshade-shaders
                        if (!Directory.Exists(rsDir))
                            return false.Label($"reshade-shaders-original was not restored when folder was missing " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                        var restoredShader = Path.Combine(rsDir, "Shaders", "original_user.fx");
                        if (!File.Exists(restoredShader))
                            return false.Label($"Restored folder missing original user content " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                        if (Directory.Exists(origDir))
                            return false.Label($"reshade-shaders-original still exists after restore " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                    }
                    else
                    {
                        // Nothing existed, nothing should exist
                        if (Directory.Exists(rsDir))
                            return false.Label($"reshade-shaders appeared from nowhere " +
                                               $"(state={state}, hasOriginal={hasOriginal})");
                    }
                }

                return true.Label($"OK: state={state}, hasOriginal={hasOriginal}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    /// <summary>
    /// Generator for any DeployMode value (Off, Minimum, All, User).
    /// </summary>
    private static Gen<ShaderPackService.DeployMode> GenAnyDeployMode()
    {
        return Gen.Elements(
            ShaderPackService.DeployMode.Off,
            ShaderPackService.DeployMode.Minimum,
            ShaderPackService.DeployMode.All,
            ShaderPackService.DeployMode.User);
    }

    /// <summary>
    /// Generator for shader mode override: null or a valid mode string.
    /// </summary>
    private static Gen<string?> GenShaderModeOverride()
    {
        return Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>("Off"),
            Gen.Constant<string?>("Minimum"),
            Gen.Constant<string?>("All"),
            Gen.Constant<string?>("User"));
    }

    // ── Property 4: DC mode syncs to DC global folder ─────────────────────────────

    // Feature: reshade-vulkan-shader-deploy, Property 4: DC mode syncs to DC global folder
    /// <summary>
    /// **Validates: Requirements 2.3**
    ///
    /// For any set of game locations containing at least one DC-mode game,
    /// after SyncShadersToAllLocations processes them, the DC global Reshade
    /// folder SHALL contain shader files matching the global DeployMode.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DcMode_SyncsToDcGlobalFolder_MatchingGlobalMode()
    {
        var gen = from mode in GenNonOffDeployMode()
                  from suffix in Gen.Choose(1, 999999)
                  select (mode, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (mode, suffix) = tuple;

            // Ensure custom staging files exist for User mode
            if (mode == ShaderPackService.DeployMode.User)
                EnsureCustomStagingFiles();

            // Snapshot DC global folder before test so we only clean up our files
            var preExisting = SnapshotDcFolder();

            // Set the global mode so SyncShadersToAllLocations uses it for DC folder
            ShaderPackService.CurrentMode = mode;

            // Create a temp game dir for the DC-mode location
            var gameDir = Path.Combine(_tempRoot, $"game_dc4_{suffix}_{mode}");
            Directory.CreateDirectory(gameDir);

            try
            {
                // Act: call SyncShadersToAllLocations with at least one DC-mode location
                var locations = new[]
                {
                    (installPath: gameDir, dcInstalled: true, rsInstalled: true,
                     dcMode: true, shaderModeOverride: (string?)null)
                };
                _service.SyncShadersToAllLocations(locations, mode);

                // Assert: DC global Reshade folder contains shader files matching the global mode
                var dcShadersDir = ShaderPackService.DcShadersDir;
                var dcTexturesDir = ShaderPackService.DcTexturesDir;

                if (mode == ShaderPackService.DeployMode.User)
                {
                    // User mode: custom shader files should be deployed to DC folder
                    var hasCustomShader = File.Exists(Path.Combine(dcShadersDir, "_rdxc_test_custom.fx"));
                    if (!hasCustomShader)
                        return false.Label($"Custom shader file not deployed to DC folder for User mode");
                }
                else
                {
                    // Minimum or All mode: staging shader files should be deployed
                    var hasShaderFiles = Directory.Exists(dcShadersDir) &&
                                         Directory.EnumerateFiles(dcShadersDir, "*", SearchOption.AllDirectories).Any();
                    if (!hasShaderFiles)
                        return false.Label($"No shader files deployed to DC folder for {mode} mode");
                }

                return true.Label($"OK: mode={mode}");
            }
            finally
            {
                // Clean up: remove only files we created in the DC global folder
                CleanupDcFolder(preExisting);
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Property 5: Effective mode resolution ─────────────────────────────────────

    // Feature: reshade-vulkan-shader-deploy, Property 5: Effective mode resolution
    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    ///
    /// For any game location with a per-game shaderModeOverride set,
    /// SyncShadersToAllLocations SHALL use the override as the effective mode.
    /// For any game location with no override (null), it SHALL use the global DeployMode.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EffectiveModeResolution_OverrideWinsWhenSet_GlobalWhenNull()
    {
        var gen = from globalMode in GenAnyDeployMode()
                  from overrideStr in GenShaderModeOverride()
                  from suffix in Gen.Choose(1, 999999)
                  select (globalMode, overrideStr, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (globalMode, overrideStr, suffix) = tuple;

            // Resolve the effective mode the same way the production code does
            var effectiveMode = overrideStr != null
                && Enum.TryParse<ShaderPackService.DeployMode>(overrideStr, true, out var parsed)
                ? parsed
                : globalMode;

            // Skip when effective mode is Off — nothing to verify in the folder
            if (effectiveMode == ShaderPackService.DeployMode.Off)
                return true.Label($"Skipped: effectiveMode=Off (global={globalMode}, override={overrideStr ?? "null"})");

            // Ensure custom staging files exist for User mode
            if (effectiveMode == ShaderPackService.DeployMode.User)
                EnsureCustomStagingFiles();

            var gameDir = Path.Combine(_tempRoot, $"game_p5_{suffix}_{globalMode}_{overrideStr ?? "null"}");
            Directory.CreateDirectory(gameDir);

            try
            {
                // Act: call SyncShadersToAllLocations with a single RS-only location
                var locations = new[]
                {
                    (installPath: gameDir, dcInstalled: false, rsInstalled: true,
                     dcMode: false, shaderModeOverride: overrideStr)
                };
                _service.SyncShadersToAllLocations(locations, globalMode);

                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);

                // Assert: reshade-shaders folder exists (effective mode is non-Off)
                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders folder missing " +
                                       $"(global={globalMode}, override={overrideStr ?? "null"}, effective={effectiveMode})");

                // Assert: managed marker exists
                var markerPath = Path.Combine(rsDir, "Managed by RDXC.txt");
                if (!File.Exists(markerPath))
                    return false.Label($"Managed marker missing " +
                                       $"(global={globalMode}, override={overrideStr ?? "null"}, effective={effectiveMode})");

                var shadersDir = Path.Combine(rsDir, "Shaders");

                if (effectiveMode == ShaderPackService.DeployMode.User)
                {
                    // User mode: custom shader files should be deployed
                    var hasCustomShader = File.Exists(Path.Combine(shadersDir, "_rdxc_test_custom.fx"));
                    if (!hasCustomShader)
                        return false.Label($"Custom shader not deployed for User effective mode " +
                                           $"(global={globalMode}, override={overrideStr ?? "null"})");
                }
                else
                {
                    // Minimum or All: staging shader files should be deployed
                    var hasShaderFiles = Directory.Exists(shadersDir) &&
                                         Directory.EnumerateFiles(shadersDir, "*", SearchOption.AllDirectories).Any();
                    if (!hasShaderFiles)
                        return false.Label($"No shader files deployed for {effectiveMode} effective mode " +
                                           $"(global={globalMode}, override={overrideStr ?? "null"})");
                }

                return true.Label($"OK: global={globalMode}, override={overrideStr ?? "null"}, effective={effectiveMode}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // ── Helpers (Property 7) ──────────────────────────────────────────────────────

    /// <summary>
    /// Captures a snapshot of all files under a directory: relative paths and their contents.
    /// Returns an empty dictionary if the directory does not exist.
    /// </summary>
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

    /// <summary>
    /// Compares two filesystem snapshots for equality (same keys, same byte contents).
    /// </summary>
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

    // ── Generators (Property 6) ───────────────────────────────────────────────────

    /// <summary>
    /// Generator for safe file names that are valid on Windows.
    /// Produces names like "shader_a.fx", "tex_b.png", etc.
    /// </summary>
    private static Gen<string> GenSafeFileName()
    {
        var prefixes = new[] { "shader", "tex", "effect", "lut", "pass", "bloom", "tone", "color" };
        var extensions = new[] { ".fx", ".fxh", ".png", ".dds", ".txt", ".cfg" };
        return from prefix in Gen.Elements(prefixes)
               from idx in Gen.Choose(0, 999)
               from ext in Gen.Elements(extensions)
               select $"{prefix}_{idx}{ext}";
    }

    /// <summary>
    /// Generator for a non-empty list of safe file names (1–8 files).
    /// </summary>
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
    /// removing the managed folder (via Off mode) SHALL rename it back to reshade-shaders,
    /// preserving the original contents.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UserFolderPreservation_RoundTrip_RestoresOriginalContents()
    {
        var gen = from fileNames in GenUserFileNames()
                  from mode in Gen.Elements(
                      ShaderPackService.DeployMode.Minimum,
                      ShaderPackService.DeployMode.All)
                  from suffix in Gen.Choose(1, 999999)
                  select (fileNames, mode, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (fileNames, mode, suffix) = tuple;

            var gameDir = Path.Combine(_tempRoot, $"game_p6_{suffix}_{mode}");
            Directory.CreateDirectory(gameDir);

            var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
            var origDir = Path.Combine(gameDir, ShaderPackService.GameReShadeOriginal);

            try
            {
                // ── Arrange: create user-owned reshade-shaders with random files ──
                Directory.CreateDirectory(rsDir);
                var originalContents = new Dictionary<string, string>();
                foreach (var name in fileNames)
                {
                    var filePath = Path.Combine(rsDir, name);
                    var content = $"// user content for {name} — {Guid.NewGuid()}";
                    File.WriteAllText(filePath, content);
                    originalContents[name] = content;
                }

                // Verify: no marker → user-owned
                if (_service.IsManagedByRdxc(gameDir))
                    return false.Label("Precondition failed: folder should not be managed before deploy");

                // ── Act 1: Deploy shaders (non-Off mode) ──
                // This should rename user folder to reshade-shaders-original and deploy managed
                _service.SyncGameFolder(gameDir, mode);

                // Assert: user folder was renamed to reshade-shaders-original
                if (!Directory.Exists(origDir))
                    return false.Label($"reshade-shaders-original not created after deploy " +
                                       $"(mode={mode}, files={fileNames.Length})");

                // Assert: managed folder now exists with marker
                if (!_service.IsManagedByRdxc(gameDir))
                    return false.Label($"Managed marker missing after deploy " +
                                       $"(mode={mode}, files={fileNames.Length})");

                // Assert: original files are in reshade-shaders-original
                foreach (var name in fileNames)
                {
                    var origFile = Path.Combine(origDir, name);
                    if (!File.Exists(origFile))
                        return false.Label($"Original file '{name}' missing from reshade-shaders-original " +
                                           $"(mode={mode})");
                }

                // ── Act 2: Remove via Off mode ──
                // This should remove managed folder and restore original
                _service.SyncGameFolder(gameDir, ShaderPackService.DeployMode.Off);

                // Assert: managed folder is gone (no marker)
                if (_service.IsManagedByRdxc(gameDir))
                    return false.Label($"Managed folder still exists after Off mode " +
                                       $"(mode={mode}, files={fileNames.Length})");

                // Assert: reshade-shaders-original no longer exists (was renamed back)
                if (Directory.Exists(origDir))
                    return false.Label($"reshade-shaders-original still exists after Off mode restore " +
                                       $"(mode={mode}, files={fileNames.Length})");

                // Assert: reshade-shaders is restored with original contents
                if (!Directory.Exists(rsDir))
                    return false.Label($"reshade-shaders not restored after Off mode " +
                                       $"(mode={mode}, files={fileNames.Length})");

                foreach (var (name, expectedContent) in originalContents)
                {
                    var restoredFile = Path.Combine(rsDir, name);
                    if (!File.Exists(restoredFile))
                        return false.Label($"Restored file '{name}' missing from reshade-shaders " +
                                           $"(mode={mode})");

                    var actualContent = File.ReadAllText(restoredFile);
                    if (actualContent != expectedContent)
                        return false.Label($"Restored file '{name}' content mismatch " +
                                           $"(mode={mode})");
                }

                return true.Label($"OK: mode={mode}, files={fileNames.Length}");
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
    /// For any game directory where rsInstalled is true and dcInstalled is false,
    /// and for any DeployMode, calling SyncGameFolder twice in succession SHALL
    /// produce the same filesystem state as calling it once (idempotent deployment).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IdempotentSync_SyncGameFolderTwice_ProducesSameState()
    {
        var gen = from mode in GenAnyDeployMode()
                  from state in GenFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (mode, state, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (mode, state, suffix) = tuple;

            // Ensure custom staging files exist for User mode
            if (mode == ShaderPackService.DeployMode.User)
                EnsureCustomStagingFiles();

            var gameDir = SetupGameDir($"p7rs_{suffix}_{state}_{mode}", state);

            try
            {
                // Act: call SyncGameFolder once
                _service.SyncGameFolder(gameDir, mode);

                // Snapshot after first call
                var snapshot1 = SnapshotDirectory(gameDir);

                // Act: call SyncGameFolder a second time
                _service.SyncGameFolder(gameDir, mode);

                // Snapshot after second call
                var snapshot2 = SnapshotDirectory(gameDir);

                // Assert: both snapshots are identical
                if (!SnapshotsEqual(snapshot1, snapshot2))
                {
                    var only1 = snapshot1.Keys.Except(snapshot2.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                    var only2 = snapshot2.Keys.Except(snapshot1.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                    var diffContent = snapshot1.Keys
                        .Intersect(snapshot2.Keys, StringComparer.OrdinalIgnoreCase)
                        .Where(k => !snapshot1[k].AsSpan().SequenceEqual(snapshot2[k].AsSpan()))
                        .ToList();

                    return false.Label(
                        $"Snapshots differ after two SyncGameFolder calls " +
                        $"(mode={mode}, state={state}, " +
                        $"onlyIn1=[{string.Join(",", only1)}], " +
                        $"onlyIn2=[{string.Join(",", only2)}], " +
                        $"contentDiff=[{string.Join(",", diffContent)}])");
                }

                return true.Label($"OK: mode={mode}, state={state}");
            }
            finally
            {
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }

    // Feature: reshade-vulkan-shader-deploy, Property 7: Idempotent sync
    /// <summary>
    /// **Validates: Requirements 7.1, 7.2**
    ///
    /// For any DC-mode game location, calling SyncShadersToAllLocations twice in
    /// succession SHALL leave the game folder and DC global folder in the same state
    /// as a single call.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IdempotentSync_DcModeTwice_ProducesSameState()
    {
        var gen = from mode in GenNonOffDeployMode()
                  from dcState in GenDcFolderState()
                  from suffix in Gen.Choose(1, 999999)
                  select (mode, dcState, suffix);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (mode, dcState, suffix) = tuple;

            // Ensure custom staging files exist for User mode
            if (mode == ShaderPackService.DeployMode.User)
                EnsureCustomStagingFiles();

            // Snapshot DC global folder before test so we only clean up our files
            var preExisting = SnapshotDcFolder();

            var gameDir = SetupGameDirForDc($"p7dc_{suffix}_{dcState}_{mode}", dcState, hasOriginal: false);

            try
            {
                var locations = new[]
                {
                    (installPath: gameDir, dcInstalled: true, rsInstalled: true,
                     dcMode: true, shaderModeOverride: (string?)null)
                };

                // Act: call SyncShadersToAllLocations once
                _service.SyncShadersToAllLocations(locations, mode);

                // Snapshot game dir and DC folder after first call
                var gameSnapshot1 = SnapshotDirectory(gameDir);
                var dcSnapshot1 = SnapshotDirectory(ShaderPackService.DcReshadeDir);

                // Act: call SyncShadersToAllLocations a second time
                _service.SyncShadersToAllLocations(locations, mode);

                // Snapshot after second call
                var gameSnapshot2 = SnapshotDirectory(gameDir);
                var dcSnapshot2 = SnapshotDirectory(ShaderPackService.DcReshadeDir);

                // Assert: game folder snapshots are identical
                if (!SnapshotsEqual(gameSnapshot1, gameSnapshot2))
                    return false.Label(
                        $"Game folder snapshots differ after two DC-mode syncs " +
                        $"(mode={mode}, dcState={dcState})");

                // Assert: DC global folder snapshots are identical
                if (!SnapshotsEqual(dcSnapshot1, dcSnapshot2))
                    return false.Label(
                        $"DC global folder snapshots differ after two DC-mode syncs " +
                        $"(mode={mode}, dcState={dcState})");

                return true.Label($"OK: mode={mode}, dcState={dcState}");
            }
            finally
            {
                CleanupDcFolder(preExisting);
                try { Directory.Delete(gameDir, recursive: true); } catch { }
            }
        });
    }
}
