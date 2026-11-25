// FeederShaders.cs -- DLSS5 Feeder's effects as component shader files (like HSF.fx or URTI's): DLSS5_Feed.fx, plus
// lumenite_Kernel.fx when its motion-vector provider uses LumeniteFX. They go into every shader sync of a game where
// RHI installed Feeder (rhi_install.txt component "Feeder"), whatever the game's shader mode, through
// ShaderPackService.ExtraGameFiles. Before Nov 2026 (issue #182) the Feeder install switched the game to Select with only
// its own packs, so a game on Global (or Custom) shaders lost every other shader.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

internal static class FeederShaders
{
    internal const string Component = "Feeder";
    internal const string FeedFx = "DLSS5_Feed.fx";
    internal const string FeederPack = "DLSS5Feeder";
    internal const string LumeniteFx = "lumenite_Kernel.fx";
    internal const string LumenitePack = "LumeniteFX";

    /// <summary>(path inside reshade-shaders, staged source) for the Feeder effects this game uses; empty without RHI's Feeder.</summary>
    internal static IReadOnlyList<(string Rel, string Source)> ShaderFilesFor(string gameDir)
    {
        var recorded = RhiInstallManifest.GetComponentFiles(gameDir, Component);
        if (recorded.Count == 0) return Array.Empty<(string, string)>();

        var result = new List<(string, string)>();
        if (recorded.Any(f => f.EndsWith(FeedFx, StringComparison.OrdinalIgnoreCase)))
            Add(FeederPack, FeedFx);
        if (recorded.Any(f => f.EndsWith(LumeniteFx, StringComparison.OrdinalIgnoreCase)))
            Add(LumenitePack, LumeniteFx);
        return result;

        void Add(string pack, string leaf)
        {
            // Staged as Shaders\<pack>\...\<leaf>; the game gets the same relative path (as the pack itself would)
            var packDir = Path.Combine(ShaderPackService.ShadersDir, pack);
            string? staged = null;
            try
            {
                if (Directory.Exists(packDir))
                    staged = Directory.EnumerateFiles(packDir, leaf, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[FeederShaders.ShaderFilesFor] {packDir}: {ex.Message}");
            }
            // Not staged (yet): the sync keeps the game's own copy of this path
            var rel = staged != null ? Path.GetRelativePath(ShaderPackService.ShadersDir, staged) : Path.Combine(pack, leaf);
            result.Add((Path.Combine("Shaders", rel), staged ?? Path.Combine(packDir, leaf)));
        }
    }
}
