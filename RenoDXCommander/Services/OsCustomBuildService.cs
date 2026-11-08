// OsCustomBuildService.cs -- custom OptiScaler builds the user adds from anywhere (a download link such as
// GitHub or Discord, a zip / 7z / rar, a folder, or a bare OptiScaler.dll). Each is a fixed copy: RHI
// installs it like a built-in version and never checks it for updates (decided 7 Nov 2026: keeping it
// legit and current is up to the user). Stored in %LocalAppData%\RHI\optiscaler-custom\<slug>\:
//   build\                   the build root (the folder that held OptiScaler.dll), used as staging
//   rhi_custom_build.json    name, origin, added date, DLL version, "needs the DLSS NR DLL"
// The folder is the source of truth (no settings.json list). Plan: docs/custom-optiscaler-builds-plan.md.

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

/// <summary>One custom OptiScaler build.</summary>
internal sealed record OsCustomBuild(
    string Slug,
    string Name,
    string Dir,
    string Origin,
    DateTime AddedUtc,
    string? DllVersion,
    bool NeedsNrDll,
    bool IsOs10,
    bool LooksLikeOptiScaler)
{
    /// <summary>Internal variant value ("Custom:&lt;slug&gt;").</summary>
    public string Id => OsVariants.CustomPrefix + Slug;
    public string Label => "Custom: " + Name;
}

/// <summary>Result of adding or replacing a build: the build, or an error to show.</summary>
internal sealed record OsCustomImportResult(OsCustomBuild? Build, string? Error, string? Note);

internal static class OsCustomBuildService
{
    private const string MetaFile = "rhi_custom_build.json";
    private const string BuildFolder = "build";
    private const int MaxSearchDepth = 3;

    internal static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "optiscaler-custom");

    private static readonly object Gate = new();
    private static List<OsCustomBuild>? _builds;

    /// <summary>Raised (on any thread) after a build is added, replaced, renamed or removed.</summary>
    internal static event Action? Changed;

    private sealed class Meta
    {
        public string Name { get; set; } = "";
        public string Origin { get; set; } = "";
        public DateTime AddedUtc { get; set; }
        public string? DllVersion { get; set; }
        public bool NeedsNrDll { get; set; }
        public bool LooksLikeOptiScaler { get; set; } = true;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<OsCustomBuild> All
    {
        get { lock (Gate) return _builds ??= Scan(); }
    }

    /// <summary>The build for "Custom:&lt;slug&gt;" (or a bare slug), or null.</summary>
    internal static OsCustomBuild? Get(string? variantOrSlug)
    {
        if (string.IsNullOrEmpty(variantOrSlug)) return null;
        var slug = OsVariants.IsCustom(variantOrSlug) ? variantOrSlug[OsVariants.CustomPrefix.Length..] : variantOrSlug;
        return All.FirstOrDefault(b => string.Equals(b.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    private static List<OsCustomBuild> Scan()
    {
        var list = new List<OsCustomBuild>();
        try
        {
            if (!Directory.Exists(Root)) return list;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var slug = Path.GetFileName(dir);
                if (slug.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var build = Load(dir);
                if (build != null) list.Add(build);
            }
        }
        catch (IOException ex) { CrashReporter.Log($"[OsCustomBuildService.Scan] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[OsCustomBuildService.Scan] {ex.Message}"); }
        return list.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static OsCustomBuild? Load(string slugDir)
    {
        try
        {
            var metaPath = Path.Combine(slugDir, MetaFile);
            var buildDir = Path.Combine(slugDir, BuildFolder);
            if (!File.Exists(metaPath) || !File.Exists(Path.Combine(buildDir, "OptiScaler.dll"))) return null;
            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath));
            if (meta == null) return null;
            return new OsCustomBuild(
                Path.GetFileName(slugDir),
                string.IsNullOrWhiteSpace(meta.Name) ? Path.GetFileName(slugDir) : meta.Name,
                buildDir,
                meta.Origin,
                meta.AddedUtc,
                meta.DllVersion,
                meta.NeedsNrDll,
                Directory.Exists(Path.Combine(buildDir, "OptiScaler")),
                meta.LooksLikeOptiScaler);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[OsCustomBuildService.Load] {slugDir}: {ex.Message}");
            return null;
        }
    }

    private static void Reload()
    {
        lock (Gate) _builds = Scan();
        try { Changed?.Invoke(); }
        catch (Exception ex) { CrashReporter.Log($"[OsCustomBuildService.Changed] {ex.Message}"); }
    }

    // ── Add / replace ─────────────────────────────────────────────────────────

    /// <summary>A sensible default name for a source (file name, folder name or the link's file name).</summary>
    internal static string SuggestName(string source)
    {
        string raw;
        if (IsUrl(source, out var uri)) raw = Path.GetFileName(Uri.UnescapeDataString(uri!.AbsolutePath));
        else raw = Directory.Exists(source) ? Path.GetFileName(source.TrimEnd('\\', '/')) : Path.GetFileName(source);
        foreach (var ext in new[] { ".zip", ".7z", ".rar", ".dll" })
            if (raw.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) raw = raw[..^ext.Length];
        if (raw.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase) || raw.Length == 0) raw = "OptiScaler build";
        return raw.Length > 60 ? raw[..60] : raw;
    }

    internal static bool IsUrl(string source, out Uri? uri)
    {
        uri = null;
        return Uri.TryCreate(source.Trim(), UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>
    /// Adds a build from a link, an archive (zip / 7z / rar), a folder or an OptiScaler.dll. With
    /// <paramref name="replaceSlug"/> the existing build's files are replaced (its name and id stay).
    /// The only check is that there's an OptiScaler.dll to install. Background thread.
    /// </summary>
    internal static async Task<OsCustomImportResult> ImportAsync(string source, string name, string? replaceSlug,
        IProgress<string>? progress, CancellationToken ct, string? originLabel = null)
    {
        source = source.Trim().Trim('"');
        var work = Path.Combine(Path.GetTempPath(), "RHI", "oscustom-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            string origin;
            string input;
            if (IsUrl(source, out var uri))
            {
                progress?.Report("Downloading...");
                var fileName = Path.GetFileName(Uri.UnescapeDataString(uri!.AbsolutePath));
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    fileName = "download";
                input = Path.Combine(work, "dl", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(input)!);
                var (err, saved) = await DownloadAsync(uri, input, progress, ct).ConfigureAwait(false);
                if (err != null) return new(null, err, null);
                input = saved;
                origin = uri.GetLeftPart(UriPartial.Path); // no query string (Discord links carry signatures)
            }
            else if (Directory.Exists(source) || File.Exists(source))
            {
                input = Path.GetFullPath(source);
                origin = input;
            }
            else return new(null, "That file, folder or link couldn't be found.", null);

            // Unpack (or take the folder / DLL as it is)
            string searchRoot;
            if (Directory.Exists(input)) searchRoot = input;
            else if (input.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                searchRoot = Path.Combine(work, "dll");
                Directory.CreateDirectory(searchRoot);
                File.Copy(input, Path.Combine(searchRoot, "OptiScaler.dll"));
            }
            else
            {
                progress?.Report("Unpacking...");
                searchRoot = Path.Combine(work, "x");
                var err = await ExtractAsync(input, searchRoot, ct).ConfigureAwait(false);
                if (err != null) return new(null, err, null);
            }

            var dll = FindOptiScalerDll(searchRoot);
            if (dll == null)
                return new(null, "No OptiScaler.dll was found in it (looked up to 3 folders deep).", null);
            var buildRoot = Path.GetDirectoryName(dll)!;

            // Copy into place: <slug>.tmp\build, then swap in
            progress?.Report("Copying...");
            Directory.CreateDirectory(Root);
            var slug = replaceSlug ?? MakeSlug(name);
            var slugDir = Path.Combine(Root, slug);
            var tmpDir = slugDir + ".tmp";
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
            var tmpBuild = Path.Combine(tmpDir, BuildFolder);
            CopyTree(buildRoot, tmpBuild);

            var info = FileVersionInfo.GetVersionInfo(Path.Combine(tmpBuild, "OptiScaler.dll"));
            bool looksRight = string.Equals(info.ProductName, "OptiScaler", StringComparison.OrdinalIgnoreCase);
            var previous = replaceSlug != null ? Get(replaceSlug) : null;
            var meta = new Meta
            {
                Name = previous?.Name ?? name.Trim(),
                Origin = originLabel ?? origin,
                AddedUtc = DateTime.UtcNow,
                DllVersion = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion,
                // F5-style forwarder in the build means it expects NVIDIA's NR DLL beside it
                NeedsNrDll = (previous?.NeedsNrDll ?? false) || File.Exists(Path.Combine(tmpBuild, "nvngx.dll_dlssnr.dll")),
                LooksLikeOptiScaler = looksRight,
            };
            File.WriteAllText(Path.Combine(tmpDir, MetaFile), JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            if (Directory.Exists(slugDir)) Directory.Delete(slugDir, recursive: true);
            Directory.Move(tmpDir, slugDir);
            CrashReporter.Log($"[OsCustomBuildService.ImportAsync] {(replaceSlug != null ? "Replaced" : "Added")} '{meta.Name}' ({slug}) from {origin}: version {meta.DllVersion ?? "?"}, OptiScaler 10 layout {Directory.Exists(Path.Combine(slugDir, BuildFolder, "OptiScaler"))}, NR DLL {meta.NeedsNrDll}");
            Reload();
            var note = looksRight ? null
                : "Note: this OptiScaler.dll doesn't identify itself as OptiScaler. It was added anyway; make sure it's the build you meant.";
            return new(Get(slug), null, note);
        }
        catch (OperationCanceledException) { return new(null, "Cancelled.", null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException)
        {
            CrashReporter.Log($"[OsCustomBuildService.ImportAsync] {source}: {ex.Message}");
            return new(null, $"Couldn't add it: {ex.Message}", null);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[OsCustomBuildService.ImportAsync] Temp cleanup — {ex.Message}"); }
        }
    }

    /// <summary>Downloads a file. Returns an error, or null and where it was saved (the server's file name wins).</summary>
    internal static async Task<(string? Error, string Path)> DownloadAsync(Uri uri, string dest, IProgress<string>? progress, CancellationToken ct)
    {
        var http = App.Services.GetRequiredService<HttpClient>();
        using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return ($"The download failed ({(int)resp.StatusCode} {resp.ReasonPhrase}). Discord links expire after a while: copy a fresh one.", dest);
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            return ("That link opens a web page, not a file. Use the direct download link (right-click the download button › Copy link).", dest);
        // Prefer the server's file name (keeps the right extension for unpacking)
        var serverName = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(serverName))
        {
            serverName = Path.GetFileName(serverName.Trim('"'));
            if (serverName.Length > 0 && serverName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                dest = Path.Combine(Path.GetDirectoryName(dest)!, serverName);
        }
        long? total = resp.Content.Headers.ContentLength;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = File.Create(dest))
        {
            var buffer = new byte[81920];
            long done = 0, lastReport = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (done - lastReport > 2 * 1024 * 1024)
                {
                    lastReport = done;
                    progress?.Report(total is long t && t > 0
                        ? $"Downloading... {done * 100 / t}%"
                        : $"Downloading... {done / (1024 * 1024)} MB");
                }
            }
        }
        // The extension decides how it's unpacked: if the link had none, sniff the file
        if (Path.GetExtension(dest).Length == 0)
        {
            var ext = SniffArchive(dest);
            if (ext != null) { File.Move(dest, dest + ext); dest += ext; }
        }
        return (null, dest);
    }

    private static string? SniffArchive(string path)
    {
        var head = new byte[6];
        using (var f = File.OpenRead(path)) f.ReadExactly(head, 0, Math.Min(6, (int)f.Length));
        if (head[0] == 'P' && head[1] == 'K') return ".zip";
        if (head[0] == '7' && head[1] == 'z') return ".7z";
        if (head[0] == 'R' && head[1] == 'a' && head[2] == 'r') return ".rar";
        if (head[0] == 'M' && head[1] == 'Z') return ".dll";
        return null;
    }

    /// <summary>Unpacks a zip (built in) or anything 7-Zip opens (7z, rar). Returns an error or null.</summary>
    internal static async Task<string?> ExtractAsync(string archive, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);
        if (archive.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(archive, Path.Combine(dest, "OptiScaler.dll"));
            return null;
        }
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, dest), ct).ConfigureAwait(false); // rejects paths outside dest
            return null;
        }
        var sevenZip = AppPaths.Find7Zip();
        if (sevenZip == null) return "7-Zip wasn't found, so this archive can't be unpacked. Try a zip, or unpack it and add the folder.";
        var psi = new ProcessStartInfo
        {
            FileName = sevenZip,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("x");
        psi.ArgumentList.Add(archive);
        psi.ArgumentList.Add("-o" + dest);
        psi.ArgumentList.Add("-y");
        using var proc = Process.Start(psi);
        if (proc == null) return "7-Zip couldn't be started.";
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); }
            catch (InvalidOperationException ex) { CrashReporter.Log($"[OsCustomBuildService.ExtractAsync] Kill — {ex.Message}"); }
            if (ct.IsCancellationRequested) throw;
            return "Unpacking took too long and was stopped.";
        }
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            CrashReporter.Log($"[OsCustomBuildService.ExtractAsync] 7z exit {proc.ExitCode}: {stderr.Result}");
            return "The archive couldn't be unpacked (it may be damaged or password protected).";
        }
        return null;
    }

    /// <summary>The shallowest OptiScaler.dll within <see cref="MaxSearchDepth"/> folders, or null.</summary>
    internal static string? FindOptiScalerDll(string root)
    {
        var level = new List<string> { root };
        for (int depth = 0; depth <= MaxSearchDepth && level.Count > 0; depth++)
        {
            foreach (var dir in level)
            {
                var candidate = Path.Combine(dir, "OptiScaler.dll");
                if (File.Exists(candidate)) return candidate;
            }
            var next = new List<string>();
            foreach (var dir in level)
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(dir))
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) next.Add(sub);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[OsCustomBuildService.FindOptiScalerDll] {dir}: {ex.Message}"); }
            }
            level = next.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return null;
    }

    /// <summary>Copies a folder tree. Links are skipped and every target stays inside <paramref name="dest"/>.</summary>
    internal static void CopyTree(string source, string dest)
    {
        var destFull = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            var rel = Path.GetRelativePath(source, file);
            var target = FileHelper.SafeCombine(dest, rel);
            if (target == null || !Path.GetFullPath(target).StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string MakeSlug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            if (sb.Length >= 40) break;
        }
        var baseSlug = sb.ToString().Trim('-');
        if (baseSlug.Length == 0) baseSlug = "build";
        var slug = baseSlug;
        for (int i = 2; Directory.Exists(Path.Combine(Root, slug)) || Get(slug) != null; i++) slug = $"{baseSlug}-{i}";
        return slug;
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    internal static bool Rename(string slug, string newName) =>
        Update(slug, m => m.Name = newName.Trim());

    internal static bool SetNeedsNrDll(string slug, bool on) =>
        Update(slug, m => m.NeedsNrDll = on);

    private static bool Update(string slug, Action<Meta> change)
    {
        try
        {
            var metaPath = Path.Combine(Root, slug, MetaFile);
            if (!File.Exists(metaPath)) return false;
            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath)) ?? new Meta();
            change(meta);
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            Reload();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[OsCustomBuildService.Update] {slug}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Deletes a build's files. Games using it keep what was installed.</summary>
    internal static bool Remove(string slug)
    {
        try
        {
            var dir = Path.Combine(Root, slug);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            CrashReporter.Log($"[OsCustomBuildService.Remove] Removed {slug}");
            Reload();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsCustomBuildService.Remove] {slug}: {ex.Message}");
            return false;
        }
    }
}
