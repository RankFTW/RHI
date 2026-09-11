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
    private const string WikiUrl = "https://github.com/Filoppi/Luma-Framework/wiki";

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

            // Special Notes — preserve structure
            string specialNotes = "";
            if (cells.Count > 4)
            {
                var noteCell = cells[4];
                var noteHtml = noteCell.InnerHtml;
                noteHtml = System.Text.RegularExpressions.Regex.Replace(noteHtml, "<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                noteHtml = System.Text.RegularExpressions.Regex.Replace(noteHtml, "<li[^>]*>", "\n• ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var tempDoc = new HtmlDocument();
                tempDoc.LoadHtml(noteHtml);
                var raw = HtmlEntity.DeEntitize(tempDoc.DocumentNode.InnerText ?? "").Trim();
                // Insert newlines before section headers like [General], [DirectX]
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"(\S)\[", "$1\n[");
                // Insert newlines before bullet chars
                raw = raw.Replace("•", "\n•");
                // Collapse multiple newlines and trim
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\n{2,}", "\n\n");
                specialNotes = System.Text.RegularExpressions.Regex.Replace(raw, @"[ \t]+", " ").Trim();
            }

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
    /// Fetches and parses the Luma Framework generic Unreal Engine wiki table.
    /// Returns a dictionary keyed by game name (case-insensitive) with per-game metadata:
    /// notes text, Engine.ini keys, HDR flag, UE version, and -dx11 launch arg requirement.
    /// </summary>
    public async Task<Dictionary<string, LumaGenericGameEntry>> FetchGenericUeTableAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Fetching Luma UE wiki...");
        var result = new Dictionary<string, LumaGenericGameEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // The UE table is on the main Luma wiki page (same page as completed mods)
            var html = await _http.GetStringAsync(WikiUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null) return result;

            // Find the UE game table — specifically the one with DLSS/FSR and HDR columns
            HtmlNode? gameTable = null;
            foreach (var table in tables)
            {
                var headerRow = table.SelectSingleNode(".//tr");
                var headerCells = headerRow?.SelectNodes("th|td");
                if (headerCells == null || headerCells.Count < 4) continue;
                var headers = headerCells.Select(h => Clean(h.InnerText).ToLowerInvariant()).ToList();
                bool hasName = headers.Any(h => h.Contains("name"));
                bool hasDlss = headers.Any(h => h.Contains("dlss") || h.Contains("fsr"));
                bool hasHdr  = headers.Any(h => h.Contains("hdr"));
                bool hasUeVer = headers.Any(h => h.Contains("ue") || h.Contains("version"));
                if (hasName && hasDlss && hasHdr && hasUeVer)
                {
                    gameTable = table;
                    break;
                }
            }

            if (gameTable == null)
            {
                CrashReporter.Log("[LumaService.FetchGenericUeTableAsync] No suitable table found on UE wiki page");
                return result;
            }

            var headerRowMain = gameTable.SelectSingleNode(".//tr");
            var headerCellsMain = headerRowMain?.SelectNodes("th|td");
            if (headerCellsMain == null) return result;

            var headerTexts = headerCellsMain.Select(h => Clean(h.InnerText).ToLowerInvariant()).ToList();
            int nameCol    = headerTexts.FindIndex(h => h.Contains("name"));
            int hdrCol     = headerTexts.FindIndex(h => h.Contains("hdr"));
            int dlssCol    = headerTexts.FindIndex(h => h.Contains("dlss") || h.Contains("fsr") || h.Contains("upscal"));
            int notesCol   = headerTexts.FindIndex(h => h.Contains("notes") || h.Contains("note"));
            int ueVerCol   = headerTexts.FindIndex(h => (h.Contains("ue") && h.Contains("version")) || h == "ue version");
            // Fallback: last column is usually UE version
            if (ueVerCol < 0) ueVerCol = headerTexts.Count - 1;
            if (nameCol < 0) nameCol = 0;

            CrashReporter.Log($"[LumaService.FetchGenericUeTableAsync] Table cols: name={nameCol} hdr={hdrCol} dlss={dlssCol} notes={notesCol} ueVer={ueVerCol} headers=[{string.Join("|", headerTexts)}]");

            var rows = gameTable.SelectNodes(".//tr")?.Skip(1);
            if (rows == null) return result;

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count < 2) continue;

                var name = Clean(cells[nameCol].InnerText);
                if (string.IsNullOrWhiteSpace(name)) continue;

                // HDR: check for green checkbox emoji or ✅
                bool hdrSupported = false;
                if (hdrCol >= 0 && hdrCol < cells.Count)
                {
                    var hdrText = cells[hdrCol].InnerText;
                    hdrSupported = hdrText.Contains("✅") || hdrText.Contains("☑")
                        || hdrText.Contains("✓") || hdrText.Contains("🟢");
                }

                // DLSS/FSR support
                bool dlssFsrSupported = false;
                bool dlssFsrBlocked = false;
                if (dlssCol >= 0 && dlssCol < cells.Count)
                {
                    var dlssText = cells[dlssCol].InnerText;
                    dlssFsrSupported = dlssText.Contains("✅") || dlssText.Contains("☑")
                        || dlssText.Contains("✓") || dlssText.Contains("🟢");
                    dlssFsrBlocked = dlssText.Contains("⛔") || dlssText.Contains("🚫");
                }

                // Notes: parse text + detect Engine.ini block
                string? notesText = null;
                var iniKeys = new List<(string Section, string Key, string Value)>();
                if (notesCol >= 0 && notesCol < cells.Count)
                {
                    var notesCell = cells[notesCol];
                    notesText = ParseNotesCell(notesCell, out iniKeys);
                }

                // UE version
                string? ueVersion = null;
                if (ueVerCol >= 0 && ueVerCol < cells.Count)
                {
                    ueVersion = Clean(cells[ueVerCol].InnerText);
                    if (string.IsNullOrWhiteSpace(ueVersion)) ueVersion = null;
                }

                // Extract launch args from code elements in the notes cell
                // e.g. <code>-dx11</code>, <code>-nod3d9ex</code>, <code>-oss=Steam -dx11</code>
                // Only extract args that appear alongside "launch" or "argument" keywords
                string? launchArgs = null;
                if (notesCol >= 0 && notesCol < cells.Count && notesText != null)
                {
                    var notesLower = notesText.ToLowerInvariant();
                    bool hasLaunchKeyword = notesLower.Contains("launch") || notesLower.Contains("argument");
                    if (hasLaunchKeyword)
                    {
                        // Collect all <code> elements that look like launch args (start with -)
                        var codeNodes = cells[notesCol].SelectNodes(".//code");
                        if (codeNodes != null)
                        {
                            var argParts = new List<string>();
                            foreach (var codeNode in codeNodes)
                            {
                                var codeText = HtmlEntity.DeEntitize(codeNode.InnerText).Trim();
                                // Skip if it looks like an Engine.ini key (contains = and no spaces before =)
                                if (codeText.StartsWith('-') || (codeText.Contains(' ') && codeText.TrimStart().StartsWith('-')))
                                    argParts.Add(codeText);
                            }
                            if (argParts.Count > 0)
                                launchArgs = string.Join(" ", argParts);
                        }
                        // Fallback: regex match -word patterns in the notes text
                        if (launchArgs == null)
                        {
                            var argMatch = System.Text.RegularExpressions.Regex.Match(notesText,
                                @"(?:launch(?:ing)?\s+using|use|argument)[^\-]*(-[\w=\s-]+?)(?:\.|,|\s+In-game|\s+Recommended|$)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (argMatch.Success)
                                launchArgs = argMatch.Groups[1].Value.Trim();
                        }
                    }
                }

                result[name] = new LumaGenericGameEntry
                {
                    Name = name,
                    Notes = notesText,
                    EngineIniKeys = iniKeys,
                    LaunchArgs = launchArgs,
                    HdrSupported = hdrSupported,
                    DlssFsrSupported = dlssFsrSupported,
                    DlssFsrBlocked = dlssFsrBlocked,
                    UeVersion = ueVersion,
                };
            }

            CrashReporter.Log($"[LumaService.FetchGenericUeTableAsync] Parsed {result.Count} UE game entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[LumaService.FetchGenericUeTableAsync] Failed — {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Parses a Notes cell from the UE wiki table.
    /// Extracts plain text notes and expands any "▶ Modify Engine.ini" collapsible sections
    /// into structured (Section, Key, Value) tuples.
    /// </summary>
    private static string? ParseNotesCell(HtmlNode cell, out List<(string Section, string Key, string Value)> iniKeys)
    {
        iniKeys = new List<(string, string, string)>();
        var sb = new System.Text.StringBuilder();

        // Walk child nodes — look for <details> elements (collapsible Engine.ini blocks)
        // and plain text nodes
        foreach (var node in cell.ChildNodes)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var t = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(t);
                }
            }
            else if (node.Name == "details")
            {
                // Collapsible section — could be "▶ Modify Engine.ini" or a generic notes block
                // Walk child nodes properly to preserve <br> line breaks between <code> elements
                var detailsSb = new System.Text.StringBuilder();
                var summaryNode = node.SelectSingleNode("summary");
                foreach (var child in node.ChildNodes)
                {
                    if (summaryNode != null && child == summaryNode) continue; // skip summary
                    if (child.Name == "br")
                        detailsSb.Append('\n');
                    else if (child.NodeType == HtmlNodeType.Text)
                    {
                        var t = HtmlEntity.DeEntitize(child.InnerText).Trim();
                        if (!string.IsNullOrEmpty(t)) detailsSb.Append(t);
                    }
                    else
                    {
                        var t = HtmlEntity.DeEntitize(child.InnerText ?? "").Trim();
                        if (!string.IsNullOrEmpty(t)) detailsSb.Append(t);
                    }
                }
                var innerText = detailsSb.ToString().Trim();

                // Check if this is an Engine.ini block (contains [Section] headers or key=value)
                bool isIniBlock = innerText.Contains('[') && innerText.Contains(']');

                if (isIniBlock)
                {
                    // Parse [Section] + key=value lines
                    string currentSection = "SystemSettings";
                    foreach (var line in innerText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (line.StartsWith('[') && line.EndsWith(']'))
                        {
                            currentSection = line[1..^1].Trim();
                        }
                        else if (line.Contains('='))
                        {
                            var eqIdx = line.IndexOf('=');
                            var key = line[..eqIdx].Trim();
                            var val = line[(eqIdx + 1)..].Trim();
                            if (!string.IsNullOrEmpty(key))
                                iniKeys.Add((currentSection, key, val));
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(innerText))
                {
                    // Generic notes block — append as regular text
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(innerText);
                }
            }
            else if (node.Name == "br")
            {
                sb.Append('\n');
            }
            else
            {
                var t = HtmlEntity.DeEntitize(node.InnerText ?? "").Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(t);
                }
            }
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
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

                var text = sibling.Name is "ul" or "ol"
                    ? ExtractListItems(sibling)
                    : Clean(sibling.InnerText);
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

    /// <summary>Extracts list items from a ul/ol element, one per line with bullet prefix.</summary>
    private static string ExtractListItems(HtmlNode listNode)
    {
        var items = listNode.SelectNodes(".//li");
        if (items == null || items.Count == 0) return Clean(listNode.InnerText);
        return string.Join('\n', items.Select(li => "• " + Clean(li.InnerText)));
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
        IEnumerable<string>? selectedShaderPacks = null,
        string? screenshotSavePath = null,
        string? overlayHotkey = null,
        string? screenshotHotkey = null,
        string? gameName = null,
        IProgress<(string message, double percent)>? progress = null,
        string? store = null)
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

        // Check if cached file is still valid (compare Content-Length via HEAD request)
        bool needsDownload = true;
        if (File.Exists(cachePath))
        {
            try
            {
                var headReq = new HttpRequestMessage(HttpMethod.Head, mod.DownloadUrl);
                var headResp = await _http.SendAsync(headReq);
                if (headResp.IsSuccessStatusCode)
                {
                    var remoteSize = headResp.Content.Headers.ContentLength;
                    var localSize = new FileInfo(cachePath).Length;
                    if (remoteSize.HasValue && remoteSize.Value == localSize)
                    {
                        needsDownload = false;
                        CrashReporter.Log($"[LumaService.InstallAsync] Cache hit for '{fileName}' ({localSize} bytes) — skipping download");
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[LumaService.InstallAsync] HEAD check failed for '{mod.DownloadUrl}' — {ex.Message}, re-downloading");
            }
        }

        // Download
        if (needsDownload)
        {
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
        } // end if (needsDownload)

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
                _auxFileService.MergeRsIni(gameInstallPath, screenshotSavePath, overlayHotkey, screenshotHotkey, gameName);
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

                // Skip reshade.ini from the zip — RHI deploys its own version with user settings
                if (entry.Name.Equals("reshade.ini", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip ReShade DLLs — RHI installs its own staged ReShade (respecting channel)
                if (entry.Name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip nvngx_dlss.dll — RHI deploys its own newest version after install
                if (entry.Name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

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

        // ── Deploy shaders (same as normal ReShade — respects global/per-game selection) ──
        progress?.Report(("Deploying shaders...", 95));
        try
        {
            var exclLuma1 = selectedShaderPacks?
                .ToDictionary(id => id, id => _shaderPackService.GetExcludedFiles(id),
                    StringComparer.OrdinalIgnoreCase);
            _shaderPackService.SyncGameFolder(gameInstallPath, selectedShaderPacks, exclLuma1);

            // Track deployed shader files for clean uninstall
            var rsDir = Path.Combine(gameInstallPath, ShaderPackService.GameReShadeShaders);
            if (Directory.Exists(rsDir))
            {
                foreach (var file in Directory.GetFiles(rsDir, "*", SearchOption.AllDirectories))
                {
                    var relToGame = Path.GetRelativePath(gameInstallPath, file);
                    installedFiles.Add(relToGame);
                }
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
            Store = store ?? "",
            DownloadUrl = mod.DownloadUrl,
            InstalledFiles = installedFiles,
            InstalledAt = DateTime.UtcNow,
            InstalledBuildNumber = await GetLatestBuildNumberAsync().ConfigureAwait(false),
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

        // Remove the RDXC-managed reshade-shaders folder via ShaderPackService
        // (must happen before individual file deletion removes the marker file)
        try
        {
            _shaderPackService.RemoveFromGameFolder(record.InstallPath);
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Uninstall] ShaderPackService cleanup failed — {ex.Message}"); }

        foreach (var relPath in record.InstalledFiles)
        {
            // Skip ReShade DLLs — RHI now manages ReShade independently for Luma games.
            // Old records may have dxgi.dll etc. tracked from before this change.
            // Exception: D3D9.dll deployed by dgVoodoo2 goes through the sentinel pattern
            // and must be processed (not skipped) — detect by presence of our sentinel file.
            var fileName = Path.GetFileName(relPath);
            if (fileName.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase))
            {
                // If RHI deployed this via dgVoodoo (sentinel exists), fall through to normal
                // file handling so SentinelRestore can clean it up correctly.
                var d3d9FullPath = Path.Combine(record.InstallPath, relPath);
                var sentinelPath = d3d9FullPath + ".original";
                if (!File.Exists(sentinelPath))
                {
                    CrashReporter.Log($"[LumaService.Uninstall] Skipping RHI-managed ReShade DLL '{relPath}' (no sentinel)");
                    continue;
                }
                // Sentinel present — fall through to handle via SentinelRestore below
            }
            else if (fileName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[LumaService.Uninstall] Skipping RHI-managed ReShade DLL '{relPath}'");
                continue;
            }
            // Skip nvngx_dlss.dll — managed by RHI separately
            if (fileName.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[LumaService.Uninstall] Skipping RHI-managed DLSS DLL '{relPath}'");
                continue;
            }

            var fullPath = Path.Combine(record.InstallPath, relPath);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    // Restore game-original if a sentinel exists (e.g. dgVoodoo D3D9.dll)
                    AuxInstallService.SentinelRestore(fullPath);
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

        // Remove the Luma folder if it exists (entirely RHI-managed)
        var lumaDir = Path.Combine(record.InstallPath, "Luma");
        try
        {
            if (Directory.Exists(lumaDir))
            {
                Directory.Delete(lumaDir, recursive: true);
                CrashReporter.Log($"[LumaService.Uninstall] Removed Luma folder from '{record.InstallPath}'");
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Uninstall] Failed to remove Luma folder — {ex.Message}"); }

        // Clean up empty subdirectories within reshade-shaders — but never the root itself,
        // as it belongs to ReShade which may still be installed.
        var rsDir = Path.Combine(record.InstallPath, LiliumShaderService.GameReShadeShaders);
        try
        {
            if (Directory.Exists(rsDir))
            {
                foreach (var sub in Directory.GetDirectories(rsDir))
                    CleanEmptyDirs(sub);
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Uninstall] Failed to clean empty dirs in '{rsDir}' — {ex.Message}"); }

        // Remove reshade-shaders-original if it was created during uninstall
        var rsOrigDir = Path.Combine(record.InstallPath, ShaderPackService.GameReShadeOriginal);
        try
        {
            if (Directory.Exists(rsOrigDir))
                Directory.Delete(rsOrigDir, recursive: true);
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.Uninstall] Failed to remove reshade-shaders-original — {ex.Message}"); }

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

    private static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(HtmlEntity.DeEntitize(s ?? "").Trim(), @"\s+", " ");

    // ── Update detection ──────────────────────────────────────────────────────────

    private const string LumaReleasesApi =
        "https://api.github.com/repos/Filoppi/Luma-Framework/releases/latest";

    /// <summary>
    /// Fetches the latest Luma-Framework release tag from GitHub and extracts the
    /// build number (e.g. "latest-428" → 428). Returns 0 on failure.
    /// </summary>
    public async Task<int> GetLatestBuildNumberAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, LumaReleasesApi);
            request.Headers.UserAgent.ParseAdd("RHI");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[LumaService.GetLatestBuildNumberAsync] GitHub API returned {response.StatusCode}");
                return 0;
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            // Tag format: "latest-428"
            var dashIdx = tagName.LastIndexOf('-');
            if (dashIdx >= 0 && int.TryParse(tagName[(dashIdx + 1)..], out var buildNumber))
                return buildNumber;
            CrashReporter.Log($"[LumaService.GetLatestBuildNumberAsync] Could not parse build number from tag '{tagName}'");
            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[LumaService.GetLatestBuildNumberAsync] Failed — {ex.Message}");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckForUpdateAsync(LumaInstalledRecord record)
    {
        if (record.InstalledBuildNumber <= 0) return false; // no version info — can't compare
        var latest = await GetLatestBuildNumberAsync().ConfigureAwait(false);
        return latest > 0 && latest > record.InstalledBuildNumber;
    }

    // ── Install from local archive (drag-drop / file watcher) ─────────────────────

    /// <summary>
    /// Installs a Luma mod from a local archive (zip or 7z) to the game folder.
    /// Handles game-name subfolders, deploys ReShade if missing, skips reshade.ini from archive.
    /// </summary>
    public async Task<LumaInstalledRecord> InstallFromArchiveAsync(
        string archivePath,
        string gameInstallPath,
        bool is32Bit,
        IEnumerable<string>? selectedShaderPacks = null,
        string? screenshotSavePath = null,
        string? overlayHotkey = null,
        string? screenshotHotkey = null,
        string? gameName = null,
        Func<List<string>, Task<string?>>? folderPicker = null,
        string? store = null)
    {
        var installedFiles = new List<string>();

        // ── 1. Deploy reshade.ini FIRST (for AddonPath routing) ──
        try
        {
            _auxFileService.EnsureInisDir();
            if (File.Exists(AuxInstallService.RsIniPath))
            {
                _auxFileService.MergeRsIni(gameInstallPath, screenshotSavePath, overlayHotkey, screenshotHotkey, gameName);
                installedFiles.Add("reshade.ini");
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.InstallFromArchive] reshade.ini deploy failed — {ex.Message}"); }

        // ── 2. Extract archive ──
        var addonDeployPath = ModInstallService.GetAddonDeployPath(gameInstallPath);
        bool archiveHasDxgi = false;

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);

            // Detect game-name subfolder prefix to strip
            var prefix = DetectSubfolderPrefix(archive.Entries.Select(e => e.FullName));

            // If no single prefix found, check for multiple valid candidates and ask user
            if (string.IsNullOrEmpty(prefix) && folderPicker != null)
            {
                var entryPaths = archive.Entries.Select(e => e.FullName).Where(p => !string.IsNullOrEmpty(p)).ToList();
                var topFolders = entryPaths
                    .Select(p => { var idx = p.IndexOfAny(new[] { '/', '\\' }); return idx > 0 ? p[..(idx + 1)] : null; })
                    .Where(f => f != null && !f.TrimStart('/').TrimStart('\\').StartsWith("("))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var validFolders = topFolders.Where(f =>
                {
                    bool hasLuma = entryPaths.Any(p => p.StartsWith(f!, StringComparison.OrdinalIgnoreCase)
                        && p.Length > f!.Length && p[f.Length..].StartsWith("Luma", StringComparison.OrdinalIgnoreCase));
                    bool hasDxgi = entryPaths.Any(p => p.Equals(f + "dxgi.dll", StringComparison.OrdinalIgnoreCase));
                    return hasLuma || hasDxgi;
                }).ToList();

                if (validFolders.Count > 1)
                {
                    var folderNames = validFolders.Select(f => f!.TrimEnd('/', '\\')).ToList();
                    var chosen = await folderPicker(folderNames!);
                    if (chosen == null)
                    {
                        return new LumaInstalledRecord
                        {
                            GameName = gameName ?? Path.GetFileNameWithoutExtension(archivePath),
                            InstallPath = gameInstallPath,
                            Store = store ?? "",
                            DownloadUrl = $"local:{Path.GetFileName(archivePath)}",
                            InstalledFiles = new List<string>(),
                            InstalledAt = DateTime.UtcNow,
                            InstalledBuildNumber = 0,
                        };
                    }
                    prefix = chosen + "/";
                }
            }

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                // Skip reshade.ini from the zip
                if (entry.Name.Equals("reshade.ini", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip ReShade DLLs — RHI installs its own staged ReShade
                if (entry.Name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip nvngx_dlss.dll — RHI deploys its own newest version after install
                if (entry.Name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip non-game files
                if (entry.Name.Equals("README.txt", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Contains("(Debug)", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Contains("(Optional)", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Contains("(Alternatives)", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Strip prefix
                var relativePath = entry.FullName;
                if (!string.IsNullOrEmpty(prefix) && relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    relativePath = relativePath[prefix.Length..];

                if (string.IsNullOrEmpty(relativePath)) continue;

                if (entry.Name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
                    archiveHasDxgi = true;

                // Route .addon files to the addon deploy path
                var isAddonFile = entry.Name.EndsWith(".addon", StringComparison.OrdinalIgnoreCase)
                               || entry.Name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                               || entry.Name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase);
                var baseDir = isAddonFile ? addonDeployPath : gameInstallPath;
                var destPath = Path.Combine(baseDir, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
                installedFiles.Add(relativePath);
            }
        }
        else if (archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            // Extract to temp, then copy with prefix stripping
            var tempDir = Path.Combine(Path.GetTempPath(), "RHI_Luma_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(tempDir);
                var sevenZipExe = Path.Combine(AppContext.BaseDirectory, "7z.exe");
                var psi = new System.Diagnostics.ProcessStartInfo(sevenZipExe, $"x \"{archivePath}\" -o\"{tempDir}\" -y")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(60000);

                // Find the content root (strip game-name subfolder)
                var contentRoot = tempDir;
                var validCandidates = DetectContentRootCandidates(tempDir);

                if (validCandidates.Count == 1)
                {
                    contentRoot = validCandidates[0];
                }
                else if (validCandidates.Count > 1)
                {
                    // Multiple valid folders — ask the user to pick
                    if (folderPicker != null)
                    {
                        var folderNames = validCandidates.Select(Path.GetFileName).ToList()!;
                        var chosen = await folderPicker(folderNames!);
                        if (chosen == null)
                        {
                            CrashReporter.Log("[LumaService.InstallFromArchive] User cancelled folder selection");
                            return new LumaInstalledRecord
                            {
                                GameName = gameName ?? Path.GetFileNameWithoutExtension(archivePath),
                                InstallPath = gameInstallPath,
                                Store = store ?? "",
                                DownloadUrl = $"local:{Path.GetFileName(archivePath)}",
                                InstalledFiles = new List<string>(),
                                InstalledAt = DateTime.UtcNow,
                                InstalledBuildNumber = 0,
                            };
                        }
                        contentRoot = validCandidates.FirstOrDefault(d =>
                            Path.GetFileName(d).Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? tempDir;
                    }
                    else
                    {
                        // No picker available — use the first valid candidate
                        contentRoot = validCandidates[0];
                        CrashReporter.Log($"[LumaService.InstallFromArchive] Multiple candidates found, using first: '{Path.GetFileName(contentRoot)}'");
                    }
                }
                else
                {
                    // No valid candidates with Luma/ or dxgi.dll — check for single subfolder fallback
                    var allSubdirs = Directory.GetDirectories(tempDir)
                        .Where(d => !Path.GetFileName(d).StartsWith("("))
                        .ToArray();
                    if (allSubdirs.Length == 1)
                        contentRoot = allSubdirs[0];
                }

                foreach (var file in Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(contentRoot, file);
                    var fileName = Path.GetFileName(file);

                    // Skip reshade.ini, README, images, debug/optional folders, ReShade DLLs, and nvngx_dlss.dll
                    if (fileName.Equals("reshade.ini", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("README.txt", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || relativePath.Contains("(Debug)", StringComparison.OrdinalIgnoreCase)
                        || relativePath.Contains("(Optional)", StringComparison.OrdinalIgnoreCase)
                        || relativePath.Contains("(Alternatives)", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fileName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
                        archiveHasDxgi = true;

                    var isAddonFile = fileName.EndsWith(".addon", StringComparison.OrdinalIgnoreCase)
                                   || fileName.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                                   || fileName.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase);
                    var baseDir = isAddonFile ? addonDeployPath : gameInstallPath;
                    var destPath = Path.Combine(baseDir, relativePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    File.Copy(file, destPath, overwrite: true);
                    installedFiles.Add(relativePath);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        // ── 3. Deploy ReShade if archive didn't include dxgi.dll ──
        if (!archiveHasDxgi)
        {
            var rsPath = is32Bit ? AuxInstallService.RsStagedPath32 : AuxInstallService.RsStagedPath64;
            var destDxgi = Path.Combine(gameInstallPath, "dxgi.dll");
            if (File.Exists(rsPath))
            {
                File.Copy(rsPath, destDxgi, overwrite: true);
                installedFiles.Add("dxgi.dll");
                CrashReporter.Log($"[LumaService.InstallFromArchive] Deployed cached ReShade as dxgi.dll ({(is32Bit ? "32-bit" : "64-bit")})");
            }
            else
            {
                CrashReporter.Log($"[LumaService.InstallFromArchive] WARNING: No cached ReShade available at '{rsPath}'");
            }
        }

        // ── 4. Deploy shaders ──
        try
        {
            var exclLuma2 = selectedShaderPacks?
                .ToDictionary(id => id, id => _shaderPackService.GetExcludedFiles(id),
                    StringComparer.OrdinalIgnoreCase);
            _shaderPackService.SyncGameFolder(gameInstallPath, selectedShaderPacks, exclLuma2);
            var rsDir = Path.Combine(gameInstallPath, ShaderPackService.GameReShadeShaders);
            if (Directory.Exists(rsDir))
            {
                foreach (var file in Directory.GetFiles(rsDir, "*", SearchOption.AllDirectories))
                    installedFiles.Add(Path.GetRelativePath(gameInstallPath, file));
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[LumaService.InstallFromArchive] Shader deploy failed — {ex.Message}"); }

        // ── 5. Save record ──
        var record = new LumaInstalledRecord
        {
            GameName = gameName ?? Path.GetFileNameWithoutExtension(archivePath),
            InstallPath = gameInstallPath,
            Store = store ?? "",
            DownloadUrl = $"local:{Path.GetFileName(archivePath)}",
            InstalledFiles = installedFiles,
            InstalledAt = DateTime.UtcNow,
            InstalledBuildNumber = 0,
        };
        SaveRecord(record);
        CrashReporter.Log($"[LumaService.InstallFromArchive] Installed {installedFiles.Count} files to '{gameInstallPath}'");
        return record;
    }

    /// <summary>
    /// Detects a common game-name subfolder prefix in archive entries.
    /// Returns the prefix to strip (e.g. "Call of Duty Black Ops III/") or empty string.
    /// Filters out folders starting with '(' (Alternatives, Debug, Optional) before checking.
    /// </summary>
    private static string DetectSubfolderPrefix(IEnumerable<string> entryPaths)
    {
        var paths = entryPaths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count == 0) return "";

        // Get all unique top-level folder names
        var topFolders = paths
            .Select(p =>
            {
                var slashIdx = p.IndexOfAny(new[] { '/', '\\' });
                return slashIdx > 0 ? p[..(slashIdx + 1)] : null;
            })
            .Where(f => f != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (topFolders.Count == 0) return "";

        // Filter out folders starting with '(' (Alternatives, Debug, Optional, etc.)
        var candidates = topFolders
            .Where(f => !f!.TrimStart('/').TrimStart('\\').StartsWith("("))
            .ToList();

        // Among remaining candidates, find ones that contain Luma/ or dxgi.dll
        var validCandidates = candidates.Where(candidate =>
        {
            bool hasLuma = paths.Any(p =>
                p.StartsWith(candidate!, StringComparison.OrdinalIgnoreCase)
                && p.Length > candidate!.Length
                && p[candidate.Length..].StartsWith("Luma", StringComparison.OrdinalIgnoreCase));
            bool hasDxgi = paths.Any(p =>
                p.Equals(candidate + "dxgi.dll", StringComparison.OrdinalIgnoreCase));
            return hasLuma || hasDxgi;
        }).ToList();

        if (validCandidates.Count == 1)
            return validCandidates[0]!;

        // Fallback: original logic for single-folder archives
        if (topFolders.Count == 1)
        {
            var candidate = topFolders[0]!;
            bool hasLuma = paths.Any(p =>
                p.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                && p.Length > candidate.Length
                && p[candidate.Length..].StartsWith("Luma", StringComparison.OrdinalIgnoreCase));
            bool hasDxgi = paths.Any(p =>
                p.Equals(candidate + "dxgi.dll", StringComparison.OrdinalIgnoreCase));
            if (hasLuma || hasDxgi)
                return candidate;
        }

        return "";
    }

    /// <summary>
    /// Detects valid game content folders in a 7z extraction directory.
    /// Filters out folders starting with '(' and returns folders containing Luma/ or dxgi.dll.
    /// </summary>
    private static List<string> DetectContentRootCandidates(string tempDir)
    {
        var subdirs = Directory.GetDirectories(tempDir);

        // Filter out folders starting with '(' (Alternatives, Debug, Optional, etc.)
        var candidates = subdirs
            .Where(d => !Path.GetFileName(d).StartsWith("("))
            .ToList();

        // Among remaining, find ones that contain Luma/ or dxgi.dll
        var valid = candidates.Where(d =>
            Directory.Exists(Path.Combine(d, "Luma"))
            || File.Exists(Path.Combine(d, "dxgi.dll")))
            .ToList();

        return valid;
    }

}
