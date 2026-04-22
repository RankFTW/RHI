using System.IO.Compression;
using System.Text.Json;
using HtmlAgilityPack;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Luma Framework wiki, parses the Completed Mods table,
/// fetches per-game feature notes, and handles install/uninstall of Luma zips.
/// Tracks installed files so they can be cleanly removed when toggling out of Luma mode.
/// </summary>
public class LumaService : ILumaService
{
    private const string WikiUrl = "https://github.com/Filoppi/Luma-Framework/wiki/Mods-List";

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "luma_installed.json");

    private readonly HttpClient _http;
    private readonly ShaderPackService _shaderPackService;
    private readonly IAuxFileService _auxFileService;

    public LumaService(HttpClient http, IAuxFileService auxFileService, GitHubETagCache etagCache)
    {
        _http = http;
        _auxFileService = auxFileService;
        _shaderPackService = new ShaderPackService(http, etagCache);
    }

    // ── Wiki fetch & parse ────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the Luma wiki and returns completed mods with their metadata.
    /// </summary>
    public async Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Fetching Luma wiki...");
        var html = await _http.GetStringAsync(WikiUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var mods = new List<LumaMod>();
        var allAnchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modAnchors = new List<string?>(); // parallel to mods — one per mod

        // Find the "Completed Mods" table — it's the first table with 6 columns
        // (Name | Author | Download Link | Status | Special Notes | Features)
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return mods;

        HtmlNode? completedTable = null;
        foreach (var table in tables)
        {
            var headerRow = table.SelectSingleNode(".//tr");
            var headerCells = headerRow?.SelectNodes("th|td");
            int colCount = headerCells?.Count ?? 0;
            if (colCount >= 6)
            {
                // Check if first header says "Name"
                var firstHeader = Clean(headerCells![0].InnerText);
                if (firstHeader.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    completedTable = table;
                    break;
                }
            }
        }

        if (completedTable == null) return mods;

        var rows = completedTable.SelectNodes(".//tr")?.Skip(1);
        if (rows == null) return mods;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 4) continue;

            var name = Clean(cells[0].InnerText);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var author = cells.Count > 1 ? Clean(cells[1].InnerText) : "";

            // Download Link cell — extract first href
            string? downloadUrl = null;
            if (cells.Count > 2)
            {
                foreach (var a in cells[2].SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                {
                    var href = a.GetAttributeValue("href", "").Trim();
                    if (!string.IsNullOrEmpty(href) && href.StartsWith("http"))
                    {
                        downloadUrl = href;
                        break;
                    }
                }
            }

            // Status
            var statusText = cells.Count > 3 ? cells[3].InnerText : "";
            var status = statusText.Contains("🚧") ? "🚧" : "✅";

            // Special Notes
            var specialNotes = cells.Count > 4 ? Clean(cells[4].InnerText) : "";

            // Features — look for 📌 link pointing to an anchor
            string? featuresAnchor = null;
            if (cells.Count > 5)
            {
                foreach (var a in cells[5].SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                {
                    var href = a.GetAttributeValue("href", "").Trim();
                    if (href.Contains("#"))
                    {
                        // Extract anchor fragment
                        var hashIdx = href.LastIndexOf('#');
                        featuresAnchor = href[(hashIdx + 1)..];
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(featuresAnchor))
                allAnchors.Add(featuresAnchor);

            // Fetch feature notes from the same page if there's an anchor
            // (deferred — will be filled in after all anchors are collected)
            modAnchors.Add(featuresAnchor);

            mods.Add(new LumaMod
            {
                Name = name,
                Author = author,
                DownloadUrl = downloadUrl,
                Status = status,
                SpecialNotes = specialNotes,
                FeatureNotes = null, // filled in below
            });
        }

        // Second pass: extract feature notes now that we know ALL anchor IDs.
        // This lets us stop extraction at the boundary of the next game's section.
        for (int i = 0; i < mods.Count; i++)
        {
            var anchor = modAnchors[i];
            if (!string.IsNullOrEmpty(anchor))
                mods[i].FeatureNotes = ExtractAnchorSection(doc, anchor, allAnchors);
        }

        progress?.Report($"Found {mods.Count} Luma mods");
        return mods;
    }

    /// <summary>
    /// Extracts the content section for a given anchor ID from the wiki page.
    /// Reads text until the next heading or the next game's anchor section is encountered.
    /// </summary>
    private static string? ExtractAnchorSection(HtmlDocument doc, string anchorId, HashSet<string> allAnchors)
    {
        try
        {
            // Find the heading element with matching id
            var heading = doc.DocumentNode.SelectSingleNode($"//*[@id='{anchorId}']")
                       ?? doc.DocumentNode.SelectSingleNode($"//*[@id='user-content-{anchorId}']");
            if (heading == null) return null;

            // Walk up to the parent heading element if the id is on an anchor child
            var headingEl = heading;
            if (heading.Name == "a" && heading.ParentNode != null)
                headingEl = heading.ParentNode;

            var sb = new System.Text.StringBuilder();
            var sibling = headingEl.NextSibling;
            while (sibling != null)
            {
                // Stop at the next heading
                if (sibling.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                    break;

                // Stop if this element contains an anchor id belonging to another game's section
                if (ContainsAnyAnchor(sibling, anchorId, allAnchors))
                    break;

                // Stop at bold text that starts a new game section (but not inline bold)
                if (sibling.Name == "p")
                {
                    var firstChild = sibling.FirstChild;
                    if (firstChild != null && firstChild.Name is "strong" or "b"
                        && sb.Length > 0)
                        break;
                }

                var text = Clean(sibling.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(text);
                }

                sibling = sibling.NextSibling;
            }

            var result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether an HTML node (or any of its descendants) contains an element
    /// whose id matches one of the known game-section anchors, excluding the current one.
    /// </summary>
    private static bool ContainsAnyAnchor(HtmlNode node, string currentAnchor, HashSet<string> allAnchors)
    {
        // Check the node itself
        var nodeId = node.GetAttributeValue("id", "");
        if (!string.IsNullOrEmpty(nodeId) && !nodeId.Equals(currentAnchor, StringComparison.OrdinalIgnoreCase))
        {
            var bare = nodeId.StartsWith("user-content-", StringComparison.OrdinalIgnoreCase)
                ? nodeId["user-content-".Length..] : nodeId;
            if (allAnchors.Contains(bare))
                return true;
        }

        // Check descendants — look for any element with an id matching another anchor
        var descendants = node.SelectNodes(".//*[@id]");
        if (descendants != null)
        {
            foreach (var desc in descendants)
            {
                var descId = desc.GetAttributeValue("id", "");
                if (string.IsNullOrEmpty(descId)) continue;
                if (descId.Equals(currentAnchor, StringComparison.OrdinalIgnoreCase)) continue;
                var bareDesc = descId.StartsWith("user-content-", StringComparison.OrdinalIgnoreCase)
                    ? descId["user-content-".Length..] : descId;
                if (allAnchors.Contains(bareDesc))
                    return true;
            }
        }

        return false;
    }

    // ── Install ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads and extracts a Luma mod zip into the game folder.
    /// Tracks all extracted files for clean uninstall.
    /// </summary>
    public async Task<LumaInstalledRecord> InstallAsync(
        LumaMod mod,
        string gameInstallPath,
        IProgress<(string message, double percent)>? progress = null)
    {
        if (mod.DownloadUrl == null)
            throw new InvalidOperationException($"{mod.Name} has no download URL.");

        // Only allow downloads from Filoppi's GitHub to prevent untrusted sources.
        if (!mod.DownloadUrl.StartsWith("https://github.com/Filoppi/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Blocked Luma download for {mod.Name}: URL does not originate from https://github.com/Filoppi/");

        Directory.CreateDirectory(DownloadPaths.Luma);

        var fileName = Path.GetFileName(new Uri(mod.DownloadUrl).LocalPath);
        var cachePath = Path.Combine(DownloadPaths.Luma, "luma_" + fileName);

        // Download
        progress?.Report(("Downloading Luma mod...", 0));
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(mod.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new HttpRequestException($"Failed to download Luma mod: {ex.Message}");
        }

        var total = response.Content.Headers.ContentLength ?? -1L;
        var buffer = new byte[1024 * 1024]; // 1 MB
        long downloaded = 0;

        var tempPath = cachePath + ".tmp";
        using (var netStream = await response.Content.ReadAsStreamAsync())
        using (var cacheFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
        {
            int read;
            while ((read = await netStream.ReadAsync(buffer)) > 0)
            {
                await cacheFile.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report(($"Downloading... {downloaded / 1024} KB",
                                      (double)downloaded / total * 100));
            }
            cacheFile.Flush();
        }

        if (File.Exists(cachePath)) File.Delete(cachePath);
        File.Move(tempPath, cachePath);

        // Extract zip to game folder, tracking all extracted file names
        progress?.Report(("Extracting Luma files...", 80));
        var installedFiles = new List<string>();

        // ── Deploy reshade.ini FIRST ──────────────────────────────────────────────
        // Must happen before zip extraction so that the AddonPath setting in
        // reshade.ini is available when resolving where .addon files should go.
        progress?.Report(("Deploying ReShade config...", 75));
        try
        {
            _auxFileService.EnsureInisDir();
            if (File.Exists(AuxInstallService.RsIniPath))
            {
                _auxFileService.MergeRsIni(gameInstallPath);
                installedFiles.Add("reshade.ini");
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Install] reshade.ini deploy failed — {ex.Message}"); }

        // ── Extract zip ───────────────────────────────────────────────────────────
        progress?.Report(("Extracting Luma files...", 80));

        if (cachePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Resolve the addon deploy path once — this respects the AddonPath
            // setting in reshade.ini so .addon files land in the correct folder
            // (e.g. .\ue4ss) rather than always going to the game root.
            var addonDeployPath = ModInstallService.GetAddonDeployPath(gameInstallPath);

            using var archive = ZipFile.OpenRead(cachePath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // skip directory entries

                // Route .addon files to the addon deploy path
                var isAddonFile = entry.Name.EndsWith(".addon", StringComparison.OrdinalIgnoreCase)
                               || entry.Name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                               || entry.Name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase);
                var baseDir = isAddonFile ? addonDeployPath : gameInstallPath;
                var destPath = Path.Combine(baseDir, entry.FullName);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
                installedFiles.Add(entry.FullName);
            }
        }
        else
        {
            // Not a zip — single file (e.g. dxgi.dll), copy directly
            var destName = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? fileName : "dxgi.dll";
            var destPath = Path.Combine(gameInstallPath, destName);
            File.Copy(cachePath, destPath, overwrite: true);
            installedFiles.Add(destName);
        }

        // ── Deploy Lilium shader pack (Minimum mode = Lilium only) ────────────────
        progress?.Report(("Deploying shaders...", 95));
        try
        {
            // Deploy only the Lilium HDR shader pack by ID
            _shaderPackService.DeployToGameFolder(gameInstallPath, new[] { "Lilium" });

            // Track deployed shader files for clean uninstall
            var rsDir = Path.Combine(gameInstallPath, ShaderPackService.GameReShadeShaders);
            if (Directory.Exists(rsDir))
            {
                foreach (var file in Directory.GetFiles(rsDir, "*", SearchOption.AllDirectories))
                {
                    var relToGame = Path.GetRelativePath(gameInstallPath, file);
                    installedFiles.Add(relToGame);
                }
                // Also track the marker file if present
                var marker = Path.Combine(rsDir, ".rdxc-managed");
                if (File.Exists(marker))
                    installedFiles.Add(Path.GetRelativePath(gameInstallPath, marker));
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Install] Shader deploy failed — {ex.Message}"); }

        var record = new LumaInstalledRecord
        {
            GameName = mod.Name,
            InstallPath = gameInstallPath,
            DownloadUrl = mod.DownloadUrl,
            InstalledFiles = installedFiles,
            InstalledAt = DateTime.UtcNow,
        };
        SaveRecord(record);
        progress?.Report(("Luma installed!", 100));
        return record;
    }

    /// <summary>
    /// Copies all files from sourceDir into destDir, tracking each relative path
    /// for later uninstall. Relative paths are stored against gameRoot.
    /// </summary>
    private static void DeployFolderTracked(string sourceDir, string destDir, string gameRoot, List<string> tracked)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);
        foreach (var srcFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relInSource = Path.GetRelativePath(sourceDir, srcFile);
            var destFile = Path.Combine(destDir, relInSource);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(srcFile, destFile, overwrite: true);
            // Track relative to game root for clean uninstall
            var relToGame = Path.GetRelativePath(gameRoot, destFile);
            tracked.Add(relToGame);
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes all files that were extracted during Luma install,
    /// including reshade.ini and shader packs. Also cleans up empty directories.
    /// </summary>
    public void Uninstall(LumaInstalledRecord record)
    {
        // Resolve addon deploy path so .addon files placed in a custom AddonPath
        // (e.g. .\ue4ss) are found and deleted during uninstall.
        var addonDeployPath = ModInstallService.GetAddonDeployPath(record.InstallPath);

        foreach (var relPath in record.InstalledFiles)
        {
            var fullPath = Path.Combine(record.InstallPath, relPath);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                else if (addonDeployPath != record.InstallPath)
                {
                    // File not at game root — check addon deploy path
                    var addonPath = Path.Combine(addonDeployPath, relPath);
                    if (File.Exists(addonPath)) File.Delete(addonPath);
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[LumaService.Uninstall] Failed to delete '{relPath}' — {ex.Message}");
            }
        }

        // Remove the RDXC-managed reshade-shaders folder via ShaderPackService
        // (handles the marker file and directory cleanup properly)
        try
        {
            _shaderPackService.RemoveFromGameFolder(record.InstallPath);
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Uninstall] ShaderPackService cleanup failed — {ex.Message}"); }

        // Clean up empty reshade-shaders directory tree if it still exists
        var rsDir = Path.Combine(record.InstallPath, LiliumShaderService.GameReShadeShaders);
        try
        {
            if (Directory.Exists(rsDir))
                CleanEmptyDirs(rsDir);
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Uninstall] Failed to clean empty dirs in '{rsDir}' — {ex.Message}"); }

        RemoveRecord(record.GameName, record.InstallPath);
    }

    /// <summary>Recursively removes empty directories bottom-up.</summary>
    private static void CleanEmptyDirs(string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir))
            CleanEmptyDirs(sub);
        try
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.CleanEmptyDirs] Failed to delete empty dir '{dir}' — {ex.Message}"); }
    }

    // ── Record persistence ────────────────────────────────────────────────────────

    private static List<LumaInstalledRecord> LoadAllRecords()
    {
        try
        {
            if (!File.Exists(DbPath)) return new();
            var json = File.ReadAllText(DbPath);
            return JsonSerializer.Deserialize<List<LumaInstalledRecord>>(json) ?? new();
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.LoadAllRecords] Failed to load Luma records from '{DbPath}' — {ex.Message}"); return new(); }
    }

    private static void SaveAllRecords(List<LumaInstalledRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var json = JsonSerializer.Serialize(records,
            new JsonSerializerOptions { WriteIndented = true });

        FileHelper.WriteAllTextWithRetry(DbPath, json, "LumaService.SaveAllRecords");
    }

    private void SaveRecord(LumaInstalledRecord record)
    {
        var records = LoadAllRecords();
        records.RemoveAll(r => r.GameName == record.GameName
                            && r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase));
        records.Add(record);
        SaveAllRecords(records);
    }

    public void SaveLumaRecord(LumaInstalledRecord record) => SaveRecord(record);

    public void RemoveLumaRecord(string gameName, string installPath) => RemoveRecord(gameName, installPath);

    private void RemoveRecord(string gameName, string installPath)
    {
        var records = LoadAllRecords();
        records.RemoveAll(r => r.GameName == gameName
                            && r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
        SaveAllRecords(records);
    }

    public static LumaInstalledRecord? GetRecord(string gameName, string installPath)
    {
        return LoadAllRecords().FirstOrDefault(r =>
            r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get record by install path only (for matching by game folder).</summary>
    public static LumaInstalledRecord? GetRecordByPath(string installPath)
    {
        return LoadAllRecords().FirstOrDefault(r =>
            r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes any persisted record matching the install path.</summary>
    public static void RemoveRecordByPath(string installPath)
    {
        var records = LoadAllRecords();
        var count = records.RemoveAll(r =>
            r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
        if (count > 0) SaveAllRecords(records);
    }

    private static string Clean(string s) => HtmlEntity.DeEntitize(s ?? "").Trim();
}
