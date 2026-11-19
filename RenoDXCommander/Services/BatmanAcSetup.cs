// BatmanAcSetup.cs -- after boostedwarr's RenoDX mod for Batman: Arkham City (renodx-batmanac.zip) is installed.
// The mod's DLAA / FSR runs in a 64-bit helper next to the 32-bit game (renodx-batmanac-dlaa-host64.exe), which needs,
// in its own folder (the addon folder, Binaries\Win32): nvngx_dlss.dll 310+ for DLAA (NVIDIA's license keeps it out of
// the mod's zip) and AMD FidelityFX SDK v2.4.0's amd_fidelityfx_loader_dx12.dll + amd_fidelityfx_upscaler_dx12.dll
// (MIT, AMD's license shipped beside them as amd_fidelityfx_license.md) for FSR. Source: the mod's README,
// ldavid1994/renodx branch batmanac-dlaa, src/games/batmanac. Files RHI adds go on the package's list
// (RenoDxZipPackage) so uninstalling the mod removes them; a file RHI didn't place is never replaced.

using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

internal static partial class BatmanAcSetup
{
    internal const string AddonName = "renodx-batmanac.addon32";
    internal const string HelperExe = "renodx-batmanac-dlaa-host64.exe";
    private const string DlssName = "nvngx_dlss.dll";

    /// <summary>A file from AMD's FidelityFX SDK v2.4.0 (pinned by size and SHA-256, checked Nov 2026).</summary>
    private sealed record AmdFile(string Name, string RepoPath, long Size, string Sha256);

    private const string AmdBase = "https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v2.4.0/Kits/FidelityFX/";

    private static readonly AmdFile[] AmdFiles =
    {
        new("amd_fidelityfx_loader_dx12.dll",   "signedbin/amd_fidelityfx_loader_dx12.dll",   39184,
            "e2191d3c0828be45eb8affd4e3ec0378f2293adc345b772a7170edcf3b021ecb"),
        new("amd_fidelityfx_upscaler_dx12.dll", "signedbin/amd_fidelityfx_upscaler_dx12.dll", 57664272,
            "aad4fccccb29084a655f9a4f9f613bd22c8c03fb4a0f8840148cc6543b4ccace"),
        new("amd_fidelityfx_license.md",        "docs/license.md",                            65770,
            "32357259b965d6a8001e9e508ba60eda6034027c5923a7d226ffbaff813c2bfb"),
    };

    private static readonly string StageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "amd-fidelityfx", "2.4.0");

    internal static bool IsBatmanAcAddon(string? addonFileName) =>
        !string.IsNullOrEmpty(addonFileName)
        && AddonFileNames.Canonical(addonFileName).Equals(AddonName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the newest DLSS and AMD's FSR files next to the mod's helper. Returns a short status for the log / UI,
    /// or an error. Never throws.
    /// </summary>
    internal static async Task<string> RunAsync(string gameName, string installPath)
    {
        var addonDir = ModInstallService.GetAddonDeployPath(installPath);
        var placed = new List<string>();
        var notes = new List<string>();
        try
        {
            // ── DLSS (DLAA) ────────────────────────────────────────────────
            var dlssSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
            var newestDlss = await dlssSvc.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
            if (newestDlss == null || !File.Exists(newestDlss)) notes.Add("DLSS isn't cached (no internet?): DLAA falls back to FSR");
            else if (Place(addonDir, DlssName, newestDlss, placed, notes))
                dlssSvc.RecordDlssFound(gameName);

            // ── FSR ────────────────────────────────────────────────────────
            var err = await EnsureAmdStagedAsync().ConfigureAwait(false);
            if (err != null) notes.Add("FSR files: " + err);
            else
                foreach (var f in AmdFiles)
                    Place(addonDir, f.Name, Path.Combine(StageDir, f.Name), placed, notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
        {
            notes.Add(ex.Message);
        }

        // ── Launcher settings (DX11, MSAA / FXAA off, Depth of Field on) ──
        notes.Add("config: " + ApplyLauncherSettings());

        RenoDxZipPackage.AddToList(addonDir, AddonName, placed);
        var summary = $"placed {placed.Count} file(s)" + (notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
        CrashReporter.Log($"[BatmanAcSetup.RunAsync] '{gameName}' in '{addonDir}': {summary}");
        return summary;
    }

    /// <summary>
    /// Copies a file next to the helper unless a file RHI didn't place is already there. Same content = nothing to do.
    /// Returns true when the file is RHI's copy afterwards.
    /// </summary>
    private static bool Place(string addonDir, string name, string source, List<string> placed, List<string> notes)
    {
        var dest = Path.Combine(addonDir, name);
        bool ours = RenoDxZipPackage.IsListed(addonDir, AddonName, name);
        if (File.Exists(dest) && !ours)
        {
            notes.Add($"kept your own {name}");
            return false;
        }
        if (File.Exists(dest) && SameFile(dest, source))
        {
            placed.Add(name);
            return true;
        }
        AuxInstallService.SentinelBackup(dest);
        File.Copy(source, dest, overwrite: true);
        placed.Add(name);
        return true;
    }

    private static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        return fa.Length == fb.Length && Sha256Of(a) == Sha256Of(b);
    }

    private static string Sha256Of(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>Downloads AMD's files into the staging folder when missing or not the pinned build. Returns an error or null.</summary>
    private static async Task<string?> EnsureAmdStagedAsync()
    {
        Directory.CreateDirectory(StageDir);
        var http = App.Services.GetRequiredService<HttpClient>();
        foreach (var f in AmdFiles)
        {
            var path = Path.Combine(StageDir, f.Name);
            if (File.Exists(path) && new FileInfo(path).Length == f.Size && Sha256Of(path) == f.Sha256) continue;

            var tmp = path + ".tmp";
            using (var resp = await http.GetAsync(AmdBase + f.RepoPath, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode) return $"{f.Name}: download failed (HTTP {(int)resp.StatusCode})";
                await using var net = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                await net.CopyToAsync(file).ConfigureAwait(false);
            }
            if (new FileInfo(tmp).Length != f.Size || Sha256Of(tmp) != f.Sha256)
            {
                File.Delete(tmp);
                return $"{f.Name} isn't AMD's FidelityFX SDK v2.4.0 build (size or hash differs)";
            }
            File.Move(tmp, path, overwrite: true);
            CrashReporter.Log($"[BatmanAcSetup] Staged {f.Name} ({f.Size} bytes)");
        }
        return null;
    }
}
