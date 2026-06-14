using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DLSS and Streamline DLL detection, version swapping, backup/restore,
/// and on-demand downloading from the dlss_manifest.json hosted on GitHub.
/// </summary>
public interface IDlssStreamlineService
{
    // ── Manifest ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches and caches the dlss_manifest.json. Called during init.
    /// </summary>
    Task FetchManifestAsync();

    /// <summary>Available DLSS SR versions from the manifest (newest first).</summary>
    IReadOnlyList<string> DlssVersions { get; }

    /// <summary>Available DLSS RR versions from the manifest (newest first).</summary>
    IReadOnlyList<string> DlssdVersions { get; }

    /// <summary>Available DLSS FG versions from the manifest (newest first).</summary>
    IReadOnlyList<string> DlssgVersions { get; }

    /// <summary>Available Streamline versions from the manifest (newest first).</summary>
    IReadOnlyList<string> StreamlineVersions { get; }

    // ── Detection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a game's install path for DLSS/Streamline DLLs.
    /// Returns a detection result with paths and versions for each found component.
    /// </summary>
    DlssDetectionResult Detect(string installPath);

    // ── Swap ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Swaps the DLSS SR DLL to the specified version. Downloads on-demand if not cached.
    /// Backs up the original with .original extension if no backup exists.
    /// </summary>
    Task SwapDlssAsync(string dllPath, string version);

    /// <summary>
    /// Swaps the DLSS RR DLL to the specified version.
    /// </summary>
    Task SwapDlssdAsync(string dllPath, string version);

    /// <summary>
    /// Swaps the DLSS FG DLL to the specified version.
    /// </summary>
    Task SwapDlssgAsync(string dllPath, string version);

    /// <summary>
    /// Swaps Streamline DLLs to the specified version.
    /// Only replaces files that already exist in the game folder.
    /// </summary>
    Task SwapStreamlineAsync(string gameFolder, string version);

    /// <summary>
    /// Swaps the DLSS SR DLL using the custom file from the custom folder.
    /// </summary>
    Task SwapDlssCustomAsync(string dllPath);

    /// <summary>
    /// Swaps Streamline DLLs using custom files from the custom folder.
    /// Only replaces files that already exist in the game folder.
    /// </summary>
    Task SwapStreamlineCustomAsync(string gameFolder);

    // ── Restore ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores a single DLL from its .original backup.
    /// </summary>
    void Restore(string dllPath);

    /// <summary>
    /// Restores all Streamline .original backups in the given folder.
    /// </summary>
    void RestoreStreamline(string gameFolder);

    /// <summary>
    /// Restores all DLSS and Streamline .original backups for a game.
    /// </summary>
    void RestoreAll(DlssDetectionResult detection);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the file version string for a DLL, or null if the file doesn't exist.
    /// </summary>
    string? GetFileVersion(string dllPath);

    /// <summary>
    /// Returns true if a .original backup exists for the given DLL path.
    /// </summary>
    bool HasBackup(string dllPath);

    /// <summary>
    /// Returns the newest DLSS SR version from the manifest (for OptiScaler integration).
    /// </summary>
    string? GetNewestDlssVersion();

    /// <summary>
    /// Returns true if this game has been scanned 3+ times with no DLSS found.
    /// </summary>
    bool ShouldSkipScan(string gameName);

    /// <summary>
    /// Records that a scan found no DLSS for this game.
    /// </summary>
    void RecordNoDlssFound(string gameName);

    /// <summary>
    /// Records that DLSS was found — removes the game from the skip cache.
    /// </summary>
    void RecordDlssFound(string gameName);

    /// <summary>
    /// Clears both the scan skip cache and trusted path cache.
    /// Called on Full Refresh to force fresh detection.
    /// </summary>
    void ClearScanCaches();

    /// <summary>
    /// Attempts fast detection using trusted cached paths. Returns null if full scan needed.
    /// </summary>
    DlssDetectionResult? TryFastDetect(string gameName, string installPath);

    /// <summary>
    /// Records a successful detection for trusted path confirmation.
    /// </summary>
    void RecordTrustedPath(string gameName, DlssDetectionResult detection);

    /// <summary>
    /// Returns the cached path for the newest DLSS SR DLL, downloading if needed.
    /// Used by OptiScaler to source its DLSS DLL.
    /// </summary>
    Task<string?> EnsureNewestDlssCachedAsync();

    /// <summary>
    /// Returns the cached path for the newest DLSS RR DLL, downloading if needed.
    /// </summary>
    Task<string?> EnsureNewestDlssdCachedAsync();

    /// <summary>
    /// Returns the cached path for the newest DLSS FG DLL, downloading if needed.
    /// </summary>
    Task<string?> EnsureNewestDlssgCachedAsync();
}

/// <summary>
/// Result of scanning a game folder for DLSS/Streamline DLLs.
/// </summary>
public class DlssDetectionResult
{
    /// <summary>Full path to nvngx_dlss.dll, or null if not found.</summary>
    public string? DlssPath { get; set; }

    /// <summary>Full path to nvngx_dlssd.dll, or null if not found.</summary>
    public string? DlssdPath { get; set; }

    /// <summary>Full path to nvngx_dlssg.dll, or null if not found.</summary>
    public string? DlssgPath { get; set; }

    /// <summary>Full path to sl.interposer.dll, or null if not found.</summary>
    public string? StreamlineInterposerPath { get; set; }

    /// <summary>The folder containing Streamline DLLs (derived from sl.interposer.dll location).</summary>
    public string? StreamlineFolder { get; set; }

    /// <summary>List of sl.*.dll filenames actually present in the game folder.</summary>
    public List<string> StreamlineFiles { get; set; } = new();

    /// <summary>Installed DLSS SR version, or null.</summary>
    public string? DlssVersion { get; set; }

    /// <summary>Installed DLSS RR version, or null.</summary>
    public string? DlssdVersion { get; set; }

    /// <summary>Installed DLSS FG version, or null.</summary>
    public string? DlssgVersion { get; set; }

    /// <summary>Installed Streamline version (from sl.interposer.dll), or null.</summary>
    public string? StreamlineVersion { get; set; }

    /// <summary>Original/default DLSS SR version (from .original backup if exists, else current). Cached for UI display.</summary>
    public string? OriginalDlssVersion { get; set; }

    /// <summary>Original/default DLSS RR version. Cached for UI display.</summary>
    public string? OriginalDlssdVersion { get; set; }

    /// <summary>Original/default DLSS FG version. Cached for UI display.</summary>
    public string? OriginalDlssgVersion { get; set; }

    /// <summary>Original/default Streamline version. Cached for UI display.</summary>
    public string? OriginalStreamlineVersion { get; set; }

    /// <summary>True if any DLSS or Streamline DLL was found.</summary>
    public bool HasAny => DlssPath != null || DlssdPath != null || DlssgPath != null || StreamlineInterposerPath != null;

    // ── Internal fallbacks (OptiScaler directory copies, used only if no deeper game copy found) ──
    internal string? _optiScalerDlssPath;
    internal string? _optiScalerDlssdPath;
    internal string? _optiScalerDlssgPath;
}
