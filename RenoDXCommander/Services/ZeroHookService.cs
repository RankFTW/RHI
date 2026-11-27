// ZeroHookService.cs — ZeroHook (GS-ZeroHook/ZeroHook): a third-party Windows tool that blocks overlay hook DLLs and
// background bloat machine-wide and shows which DLLs are loaded in a running game. System-wide, not per game.
//
// RHI only downloads the author's release (no licence file in the repo, so it's never shipped with RHI). The release
// asset is a single ZeroHook.exe (unsigned, manifest requireAdministrator), kept at a FIXED path:
//   %LocalAppData%\RHI\zerohook\ZeroHook.exe  + version.txt
// ZeroHook's own "Auto-Run" (scheduled task ZeroHook_AutoBlock, at logon, highest privileges) points straight at the
// exe it was run from, so the path must never change: no version-numbered folders, updates swap the file in place
// (download to .tmp, verify size + SHA-256 digest from the API, then File.Move overwrite) and only while ZeroHook isn't
// running. Purge Staging Files never touches this folder. Removal is refused while ZeroHook runs or while its Auto-Run
// task starts this copy (the user turns Auto-Run off inside ZeroHook first).
// Tags are inconsistent ("v1.1.0", "v.1.0.6"): NormaliseTag strips the leading "v" and ".". The exe's FileVersion = tag.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public enum ZeroHookLaunchResult { Started, Cancelled, NotDownloaded, Failed }

/// <summary>What ZeroHook's Auto-Run scheduled task points at.</summary>
public enum ZeroHookAutoRunState { NotRegistered, OurExe, OtherExe, Unknown }

/// <summary>Result of one schtasks query: exit code, stdout (task XML) and stderr.</summary>
public readonly record struct ZeroHookTaskQuery(int ExitCode, string Output, string Error, bool TimedOut = false);

public interface IZeroHookService
{
    string Folder { get; }
    string ExePath { get; }
    bool IsDownloaded { get; }
    /// <summary>"1.1.0" from version.txt (falls back to the exe's FileVersion), or null when not downloaded.</summary>
    string? InstalledVersion { get; }
    /// <summary>Latest release version from the last check, or null.</summary>
    string? LatestVersion { get; }
    /// <summary>True while a silent update is downloading / swapping the exe.</summary>
    bool IsUpdating { get; }
    /// <summary>Why the last download failed, for the UI (null after a success).</summary>
    string? LastError { get; }
    bool IsRunning();
    ZeroHookAutoRunState QueryAutoRun();
    bool AutoRunUsesOurExe();
    Task<bool> EnsureDownloadedAsync(IProgress<double>? progress = null);
    Task UpdateIfNewerAsync(string caller);
    (ZeroHookLaunchResult Result, string? Error) Launch();
    /// <summary>Deletes the downloaded exe. Returns why it was refused / failed, or null when removed.</summary>
    string? Remove();
}

public class ZeroHookService : IZeroHookService
{
    private const string LatestApiUrl = "https://api.github.com/repos/GS-ZeroHook/ZeroHook/releases/latest";
    public  const string RepoUrl      = "https://github.com/GS-ZeroHook/ZeroHook";
    public  const string AssetName    = "ZeroHook.exe";
    public  const string ProcessName  = "ZeroHook";
    public  const string TaskName     = "ZeroHook_AutoBlock";

    /// <summary>The fixed folder (%LocalAppData%\RHI\zerohook). ZeroHook's Auto-Run task depends on this path.</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "zerohook");

    private readonly HttpClient?      _http;
    private readonly GitHubETagCache? _etagCache;
    private readonly Func<ZeroHookTaskQuery> _queryTask;
    private readonly Func<bool>? _isRunningOverride;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _latestVersion;
    private string? _assetUrl;
    private long _assetSize = -1;
    private string? _assetSha256;
    private volatile bool _isUpdating;

    public string Folder { get; }
    public string ExePath => Path.Combine(Folder, AssetName);
    private string VersionFile => Path.Combine(Folder, "version.txt");
    private string TmpPath => ExePath + ".tmp";

    public ZeroHookService(HttpClient http, GitHubETagCache etagCache)
    {
        _http      = http;
        _etagCache = etagCache;
        Folder     = DefaultFolder;
        _queryTask = RunSchtasksQuery;
    }

    /// <summary>Tests: a folder of their own, no network; the schtasks query and running check can be replaced.</summary>
    public ZeroHookService(string folder, Func<ZeroHookTaskQuery>? queryTask = null, Func<bool>? isRunning = null)
    {
        Folder = folder;
        _queryTask = queryTask ?? RunSchtasksQuery;
        _isRunningOverride = isRunning;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsDownloaded => File.Exists(ExePath);
    public bool IsUpdating => _isUpdating;
    public string? LatestVersion => _latestVersion;
    public string? LastError { get; private set; }

    public string? InstalledVersion
    {
        get
        {
            if (!IsDownloaded) return null;
            try
            {
                if (File.Exists(VersionFile) && File.ReadAllText(VersionFile).Trim() is { Length: > 0 } v) return v;
                var fv = FileVersionInfo.GetVersionInfo(ExePath).FileVersion;
                return string.IsNullOrWhiteSpace(fv) ? null : NormaliseTag(fv);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[ZeroHookService.InstalledVersion] {ex.Message}");
                return null;
            }
        }
    }

    // ── Pure helpers (tests) ──────────────────────────────────────────────────

    /// <summary>"v1.1.0" → "1.1.0", "v.1.0.6" → "1.0.6", "1.2" → "1.2".</summary>
    public static string NormaliseTag(string tag)
    {
        var t = (tag ?? "").Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t[1..];
        if (t.StartsWith('.')) t = t[1..];
        return t.Trim();
    }

    private static Version? ParseVersion(string v)
    {
        var t = NormaliseTag(v);
        if (int.TryParse(t, out var major) && major >= 0) return new Version(major, 0);
        return Version.TryParse(t, out var ver) ? ver : null;
    }

    /// <summary>True when <paramref name="latest"/> is newer than <paramref name="installed"/> (as versions when both parse, otherwise when they differ).</summary>
    public static bool IsNewer(string? latest, string? installed)
    {
        if (string.IsNullOrWhiteSpace(latest)) return false;
        if (string.IsNullOrWhiteSpace(installed)) return true;
        var l = ParseVersion(latest);
        var i = ParseVersion(installed);
        if (l != null && i != null) return l > i;
        return !string.Equals(NormaliseTag(latest), NormaliseTag(installed), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"sha256:ABC…" → lower-case hex, or null when it isn't a SHA-256 digest.</summary>
    public static string? ParseSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var d = digest.Trim();
        const string prefix = "sha256:";
        if (!d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = d[prefix.Length..].Trim();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : null;
    }

    /// <summary>The first &lt;Command&gt; of a task's XML (XML-decoded, quotes removed, variables expanded), or null.</summary>
    public static string? ParseTaskCommand(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        var m = Regex.Match(xml, @"<Command>\s*(.*?)\s*</Command>", RegexOptions.Singleline);
        if (!m.Success) return null;
        var cmd = Environment.ExpandEnvironmentVariables(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim().Trim('"'));
        return cmd.Length > 0 ? cmd : null;
    }

    /// <summary>Same file path, ignoring case, slash direction and a trailing separator.</summary>
    public static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        static string Norm(string p)
        {
            var s = p.Trim().Trim('"').Replace('/', '\\');
            try { s = Path.GetFullPath(s); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
            return s.TrimEnd('\\');
        }
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Auto-Run state from a schtasks /Query /XML result, compared against <paramref name="exePath"/>.</summary>
    /// <param name="taskFileExists">Whether the task's file is in System32\Tasks (readable without admin). Decides a failed
    /// query without relying on schtasks' localised error text: no file = no task, a file = it exists but couldn't be read.
    /// Null = unknown, then the English "denied" text is the fallback.</param>
    public static ZeroHookAutoRunState AutoRunStateFrom(ZeroHookTaskQuery q, string exePath, bool? taskFileExists = null)
    {
        if (q.TimedOut) return ZeroHookAutoRunState.Unknown;
        if (q.ExitCode != 0)
        {
            if (taskFileExists is bool exists)
                return exists ? ZeroHookAutoRunState.Unknown : ZeroHookAutoRunState.NotRegistered;
            // A missing task is a non-zero exit; "Access is denied" means it may exist but can't be read
            return q.Error.Contains("denied", StringComparison.OrdinalIgnoreCase)
                ? ZeroHookAutoRunState.Unknown : ZeroHookAutoRunState.NotRegistered;
        }
        var cmd = ParseTaskCommand(q.Output);
        if (cmd == null) return ZeroHookAutoRunState.Unknown;
        return PathsEqual(cmd, exePath) ? ZeroHookAutoRunState.OurExe : ZeroHookAutoRunState.OtherExe;
    }

    /// <summary>
    /// Keeps <paramref name="tmpPath"/> as the exe when its size (and SHA-256, when known) match; otherwise deletes it and
    /// leaves the current exe alone. Writes version.txt on success. Never deletes the current exe first.
    /// </summary>
    public bool TryInstallVerified(string tmpPath, long expectedSize, string? expectedSha256, string version)
    {
        try
        {
            var len = new FileInfo(tmpPath).Length;
            if (expectedSize > 0 && len != expectedSize)
            {
                CrashReporter.Log($"[ZeroHookService.TryInstallVerified] Size mismatch ({len} of {expectedSize} bytes) — keeping the current copy");
                DeleteQuietly(tmpPath);
                return false;
            }
            if (expectedSha256 != null)
            {
                string hash;
                using (var fs = File.OpenRead(tmpPath))
                    hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    CrashReporter.Log($"[ZeroHookService.TryInstallVerified] SHA-256 mismatch ({hash}) — keeping the current copy");
                    DeleteQuietly(tmpPath);
                    return false;
                }
            }
            else CrashReporter.Log("[ZeroHookService.TryInstallVerified] No digest in the release: checked the size only");

            File.Move(tmpPath, ExePath, overwrite: true);
            File.WriteAllText(VersionFile, version);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[ZeroHookService.TryInstallVerified] {ex.Message} — keeping the current copy");
            DeleteQuietly(tmpPath);
            return false;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CrashReporter.Log($"[ZeroHookService.DeleteQuietly] Couldn't delete '{path}' — {ex.Message}"); }
    }

    // ── Running / Auto-Run ────────────────────────────────────────────────────

    /// <summary>
    /// A ZeroHook process started from our exe. A ZeroHook process whose path can't be read (it's elevated and the
    /// query fails) counts as running, to be safe.
    /// </summary>
    public bool IsRunning()
    {
        if (_isRunningOverride != null) return _isRunningOverride();
        Process[] procs;
        try { procs = Process.GetProcessesByName(ProcessName); }
        catch (InvalidOperationException ex) { CrashReporter.Log($"[ZeroHookService.IsRunning] {ex.Message}"); return false; }
        try
        {
            foreach (var p in procs)
            {
                var path = GetProcessImagePath(p.Id);
                if (path == null || PathsEqual(path, ExePath)) return true;
            }
            return false;
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint flags, System.Text.StringBuilder exeName, ref uint size);

    private static string? GetProcessImagePath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { NativeInterop.CloseHandle(h); }
    }

    /// <summary>schtasks /Query /TN ZeroHook_AutoBlock /XML, hidden, 5 s timeout.</summary>
    private static ZeroHookTaskQuery RunSchtasksQuery()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\" /XML")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return new ZeroHookTaskQuery(-1, "", "schtasks didn't start", TimedOut: true);
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                { CrashReporter.Log($"[ZeroHookService.RunSchtasksQuery] Couldn't stop schtasks — {ex.Message}"); }
                CrashReporter.Log("[ZeroHookService.RunSchtasksQuery] Timed out");
                return new ZeroHookTaskQuery(-1, "", "", TimedOut: true);
            }
            return new ZeroHookTaskQuery(proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            CrashReporter.Log($"[ZeroHookService.RunSchtasksQuery] {ex.Message}");
            return new ZeroHookTaskQuery(-1, "", ex.Message, TimedOut: true);
        }
    }

    public ZeroHookAutoRunState QueryAutoRun()
    {
        var q = _queryTask();
        bool? taskFile = null;
        if (q.ExitCode != 0 && !q.TimedOut)
        {
            try { taskFile = File.Exists(Path.Combine(Environment.SystemDirectory, "Tasks", TaskName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[ZeroHookService.QueryAutoRun] Couldn't check the task file — {ex.Message}"); }
        }
        var state = AutoRunStateFrom(q, ExePath, taskFile);
        if (state == ZeroHookAutoRunState.Unknown)
            CrashReporter.Log($"[ZeroHookService.QueryAutoRun] Couldn't read the {TaskName} task (exit {q.ExitCode}{(q.TimedOut ? ", timed out" : "")}): {q.Error.Trim()}");
        return state;
    }

    public bool AutoRunUsesOurExe() => QueryAutoRun() == ZeroHookAutoRunState.OurExe;

    // ── Release / download ────────────────────────────────────────────────────

    /// <summary>Reads the latest release (ETag-cached). True when it has a ZeroHook.exe asset.</summary>
    private async Task<bool> CheckLatestAsync()
    {
        if (_http == null || _etagCache == null) return false;
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, LatestApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                CrashReporter.Log("[ZeroHookService.CheckLatest] Release request failed");
                return false;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = null, digest = null;
            long size = -1;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : -1;
                }
            if (string.IsNullOrEmpty(tag) || url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[ZeroHookService.CheckLatest] No {AssetName} in the latest release (tag={tag ?? "null"})");
                return false;
            }
            _latestVersion = NormaliseTag(tag);
            _assetUrl = url;
            _assetSize = size;
            _assetSha256 = ParseSha256Digest(digest);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            CrashReporter.Log($"[ZeroHookService.CheckLatest] {ex.Message}");
            return false;
        }
    }

    /// <summary>Downloads the latest release to .tmp and swaps it in after verification. Caller holds _lock.</summary>
    private async Task<bool> DownloadAndSwapAsync(IProgress<double>? progress)
    {
        if (_http == null || _assetUrl == null || _latestVersion == null) return false;
        Directory.CreateDirectory(Folder);
        var tmp = TmpPath;
        try
        {
            using (var response = await _http.GetAsync(_assetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[ZeroHookService.Download] HTTP {(int)response.StatusCode} for {_assetUrl}");
                    LastError = $"Couldn't download ZeroHook (HTTP {(int)response.StatusCode}).";
                    return false;
                }
                long total = response.Content.Headers.ContentLength ?? _assetSize;
                await using var net  = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await net.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }
            if (!TryInstallVerified(tmp, _assetSize, _assetSha256, _latestVersion))
            {
                LastError = "ZeroHook's download didn't match the release (size or checksum). Try again later.";
                return false;
            }
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[ZeroHookService.Download] Failed — {ex.Message}");
            LastError = $"Couldn't download ZeroHook: {ex.Message}";
            DeleteQuietly(tmp);
            return false;
        }
    }

    public async Task<bool> EnsureDownloadedAsync(IProgress<double>? progress = null)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            LastError = null;
            if (IsDownloaded) return true;
            if (!await CheckLatestAsync().ConfigureAwait(false))
            {
                LastError = "Couldn't find ZeroHook's latest release on GitHub. Try again later.";
                return false;
            }
            bool ok = await DownloadAndSwapAsync(progress).ConfigureAwait(false);
            CrashReporter.Log($"[ZeroHookService.EnsureDownloaded] {(ok ? $"Downloaded {_latestVersion}" : "Failed — " + LastError)}");
            return ok;
        }
        finally { _lock.Release(); }
    }

    public Task UpdateIfNewerAsync(string caller) => Task.Run(async () =>
    {
        // No network call at all unless the user downloaded ZeroHook
        if (!IsDownloaded) return;
        if (!await _lock.WaitAsync(0).ConfigureAwait(false)) return; // a download is already under way
        try
        {
            if (IsRunning())
            {
                CrashReporter.Log($"[{caller}] ZeroHook is running — update check skipped until next time");
                return;
            }
            if (!await CheckLatestAsync().ConfigureAwait(false)) return;
            var installed = InstalledVersion;
            if (!IsNewer(_latestVersion, installed))
            {
                CrashReporter.Log($"[{caller}] ZeroHook {installed} is current");
                return;
            }
            _isUpdating = true;
            bool ok = await DownloadAndSwapAsync(null).ConfigureAwait(false);
            CrashReporter.Log($"[{caller}] ZeroHook {installed} → {_latestVersion}: {(ok ? "updated" : "update failed, kept the current copy — " + LastError)}");
            if (!ok) LastError = null; // the current copy still works: don't show an error for a silent update
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashReporter.Log($"[{caller}] ZeroHook update failed — {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
            _lock.Release();
        }
    });

    // ── Launch / remove ───────────────────────────────────────────────────────

    public (ZeroHookLaunchResult Result, string? Error) Launch()
    {
        if (!IsDownloaded) return (ZeroHookLaunchResult.NotDownloaded, "ZeroHook isn't downloaded.");
        try
        {
            // UseShellExecute: Windows shows the UAC prompt (the exe asks for administrator)
            using var p = Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = Folder });
            CrashReporter.Log("[ZeroHookService.Launch] Started");
            return (ZeroHookLaunchResult.Started, null);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            CrashReporter.Log("[ZeroHookService.Launch] UAC prompt cancelled");
            return (ZeroHookLaunchResult.Cancelled, null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            CrashReporter.Log($"[ZeroHookService.Launch] Failed — {ex.Message}");
            return (ZeroHookLaunchResult.Failed, $"Couldn't open ZeroHook: {ex.Message}");
        }
    }

    public const string RefuseRunningText = "ZeroHook is running. Close it first, then remove it.";
    public const string RefuseAutoRunText =
        "ZeroHook's Auto-Run is on and starts this copy every time you sign in. Open ZeroHook, turn Auto-Run off there, "
        + "then remove it here. (Removing it first would leave a scheduled task pointing at a missing file.)";
    public const string RefuseUnknownText =
        "RHI couldn't check whether ZeroHook's Auto-Run is on, so it left ZeroHook in place. Open ZeroHook, make sure "
        + "Auto-Run is off, then try again.";

    public string? Remove()
    {
        if (!IsDownloaded) return null;
        if (IsRunning()) return RefuseRunningText;
        switch (QueryAutoRun())
        {
            case ZeroHookAutoRunState.OurExe:  CrashReporter.Log("[ZeroHookService.Remove] Refused: Auto-Run points at our exe"); return RefuseAutoRunText;
            case ZeroHookAutoRunState.Unknown: CrashReporter.Log("[ZeroHookService.Remove] Refused: Auto-Run task couldn't be read"); return RefuseUnknownText;
        }
        try
        {
            // Only RHI's files: anything else ZeroHook keeps in its folder (such as what its Restore buttons need) stays
            File.Delete(ExePath);
            DeleteQuietly(VersionFile);
            DeleteQuietly(TmpPath);
            if (Directory.Exists(Folder) && !Directory.EnumerateFileSystemEntries(Folder).Any()) Directory.Delete(Folder);
            CrashReporter.Log("[ZeroHookService.Remove] Removed");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[ZeroHookService.Remove] Failed — {ex.Message}");
            return $"Couldn't remove ZeroHook: {ex.Message}";
        }
    }
}
