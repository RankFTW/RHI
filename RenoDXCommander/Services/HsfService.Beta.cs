// HsfService.Beta.cs: HSF test builds dropped on RHI (zips shared on Discord). The build is installed like a
// release (same files, same record, HSF.ini merged so the player's settings stay) and the marker remembers it:
// beta=<zip name>, betaover=<the release staged when it went in>. The update pass leaves the game alone until a
// different release is staged (the next HSF release), then installs that over it. Reinstall in the Extras row goes
// back to the release at once. Test builds may carry files next to HSF.ini (the XeMFG test: Intel's libxess_fg.dll
// and libxell.dll); a game's own copy of such a file is kept as <name>.hsf-original and put back when RHI removes it.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class HsfService
{
    private const string BetaKey = "beta";
    private const string BetaOverKey = "betaover";
    public const string BetaVersionText = "beta";
    private const string GameCopySuffix = ".hsf-original";

    /// <summary>The test build in this game (its zip name) and the release staged when it went in; nulls for a release.</summary>
    public static (string? Label, string? Over) ReadBeta(string installPath)
    {
        string? label = null, over = null;
        try
        {
            var path = MarkerPath(installPath);
            if (!File.Exists(path)) return (null, null);
            foreach (var line in File.ReadAllLines(path))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                var key = line[..i].Trim();
                var val = line[(i + 1)..].Trim();
                if (key == BetaKey && val.Length > 0) label = val;
                else if (key == BetaOverKey) over = val;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (label, over);
    }

    /// <summary>True when the update pass must leave this game's test build alone (no newer release staged yet).</summary>
    internal static bool KeepBeta(string? label, string? over, string? stagedVersion) =>
        label != null && (stagedVersion == null || string.Equals(over ?? "", stagedVersion, StringComparison.OrdinalIgnoreCase));

    /// <summary>The folder of an extracted archive that holds HSF's add-on and HSF_streamline\, or null.</summary>
    public static string? FindPackageRoot(string dir)
    {
        try
        {
            foreach (var addon in Directory.EnumerateFiles(dir, "hsf.addon*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(addon);
                if (!name.Equals(Addon64, StringComparison.OrdinalIgnoreCase) && !name.Equals(Addon32, StringComparison.OrdinalIgnoreCase)) continue;
                var root = Path.GetDirectoryName(addon)!;
                if (Directory.Exists(Path.Combine(root, StreamlineFolder))) return root;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>
    /// Where a file of a dropped package goes (relative to the addon folder), or null to skip it. As a release
    /// (<see cref="StagedPathFor"/>), but only the add-on of the game's bitness, never HSF.fx (the shader sync owns
    /// it) or HSF.ini (merged separately), plus DLLs and README files at the package's top level.
    /// </summary>
    internal static string? BetaPathFor(string relInPackage, bool is32Bit)
    {
        var p = relInPackage.Replace('\\', '/').TrimStart('/');
        var name = Path.GetFileName(p);
        if (name.Length == 0) return null;
        bool top = !p.Contains('/');
        if (name.Equals(Addon64, StringComparison.OrdinalIgnoreCase) || name.Equals(Addon32, StringComparison.OrdinalIgnoreCase))
            return top && name.Equals(AddonFileFor(is32Bit), StringComparison.OrdinalIgnoreCase) ? AddonFileFor(is32Bit) : null;
        if (name.Equals(ShaderFx, StringComparison.OrdinalIgnoreCase) || (top && name.Equals(IniName, StringComparison.OrdinalIgnoreCase)))
            return null;
        if (StagedPathFor(p) is { } staged) return staged;
        if (top && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return name;
        if (top && name.StartsWith("README", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(StreamlineFolder, name);
        return null;
    }

    /// <summary>
    /// Installs a dropped test build from <paramref name="packageRoot"/>. <paramref name="label"/> names it (the zip).
    /// The game's HSF.ini is merged with the build's clean one (settings kept, the build's new keys added).
    /// </summary>
    public void InstallBeta(string packageRoot, string label, string installPath, bool is32Bit, bool launchpadOn)
    {
        if (!File.Exists(Path.Combine(packageRoot, AddonFileFor(is32Bit))))
            throw new InvalidOperationException($"This HSF build has no {(is32Bit ? "32" : "64")}-bit add-on ({AddonFileFor(is32Bit)}).");
        var addonDir = AddonDir(installPath);
        Directory.CreateDirectory(addonDir);
        var previous = RhiInstallManifest.GetComponentFiles(installPath, Component);
        var recorded = new List<string>();

        foreach (var file in Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories))
        {
            if (BetaPathFor(Path.GetRelativePath(packageRoot, file), is32Bit) is not { } rel) continue;
            var dest = FileHelper.SafeCombine(addonDir, rel);
            if (dest == null) continue;
            // A file next to HSF.ini that RHI didn't place (the game's own XeSS files): keep it to put back later
            if (!rel.Contains(Path.DirectorySeparatorChar) && File.Exists(dest)
                && !previous.Contains(rel, StringComparer.OrdinalIgnoreCase) && !File.Exists(dest + GameCopySuffix))
                File.Copy(dest, dest + GameCopySuffix);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            recorded.Add(rel);
        }

        var ini = Path.Combine(addonDir, IniName);
        var buildIni = Path.Combine(packageRoot, IniName);
        if (File.Exists(buildIni))
            File.WriteAllText(ini, MergeIni(File.ReadAllText(buildIni), File.Exists(ini) ? File.ReadAllText(ini) : null));
        else if (!File.Exists(ini))
            File.WriteAllText(ini, "[HSF]\r\n");
        recorded.Add(IniName);

        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath(installPath))!);
        File.WriteAllLines(MarkerPath(installPath), new[]
        {
            $"version={BetaVersionText}", $"launchpad={(launchpadOn ? "on" : "off")}",
            $"{BetaKey}={label}", $"{BetaOverKey}={StagedVersion ?? ""}",
        });
        recorded.Add(Path.Combine(StreamlineFolder, MarkerName));

        RemoveStale(addonDir, previous, recorded);
        RhiInstallManifest.SetComponent(installPath, Component, recorded);
        _crashReporter.Log($"[HsfService.InstallBeta] Test build '{label}' ({(is32Bit ? "32" : "64")}-bit, {recorded.Count} files) → '{addonDir}'");
    }

    /// <summary>After RHI deletes a file it placed: puts the game's own copy back when it kept one.</summary>
    private void RestoreGameCopy(string path)
    {
        var copy = path + GameCopySuffix;
        try
        {
            if (!File.Exists(copy) || File.Exists(path)) return;
            File.Move(copy, path);
            _crashReporter.Log($"[HsfService] Put the game's own '{Path.GetFileName(path)}' back");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[HsfService] Couldn't put back '{copy}' — {ex.Message}");
        }
    }
}
