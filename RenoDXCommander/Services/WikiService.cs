using HtmlAgilityPack;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class WikiService : IWikiService
{
    private readonly HttpClient _http;
    private readonly IGameDetectionService _gameDetection;

    public WikiService(HttpClient http, IGameDetectionService gameDetection)
    {
        _http = http;
        _gameDetection = gameDetection;
    }

    private const string WikiUrl = "https://github.com/clshortfuse/renodx/wiki/Mods";

    // Generic addon download URLs
    // Unreal: GitHub Releases provides reliable Content-Length for update detection
    public const string GenericUnrealUrl  = "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-unrealengine.addon64";
    // Unity: sourced from NotVoosh's dedicated repo for reliable updates
    public const string GenericUnityUrl64 = "https://github.com/NotVoosh/renodx-unity/releases/download/snapshot/renodx-unityengine.addon64";
    public const string GenericUnityUrl32 = "https://github.com/NotVoosh/renodx-unity/releases/download/snapshot/renodx-unityengine.addon32";
    public const string GenericUnityUrl   = GenericUnityUrl64;

    public async Task<(List<GameMod> Mods, Dictionary<string, string> GenericNotes)>
        FetchAllAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Fetching wiki...");
        var html = await _http.GetStringAsync(WikiUrl).ConfigureAwait(false);
        var doc  = new HtmlDocument();
        doc.LoadHtml(html);

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return (new(), new());

        // Identify the specific mods table by its header column count (4 columns:
        // Name, Maintainer, Links, Status) rather than hardcoding tables[0].
        // This is robust against the wiki adding a notice/TOC table before the main one.
        var mods         = new List<GameMod>();
        var genericNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var headerRow = table.SelectSingleNode(".//tr");
            var headerCells = headerRow?.SelectNodes("th|td");
            if (headerCells == null || headerCells.Count < 2) continue;

            // Determine table layout by inspecting header text.
            // The wiki uses multiple table formats:
            //   4-col: Name | Maintainer | Links | Status
            //   3-col: Name | Links | Status   (or Name | Status | Notes for generic)
            // We detect mod tables by looking for a "Links" or "Status" column header.
            var headerTexts = headerCells.Select(h => Clean(h.InnerText).ToLowerInvariant()).ToList();
            bool hasLinksCol = headerTexts.Any(h => h.Contains("link"));
            bool hasStatusCol = headerTexts.Any(h => h.Contains("status"));

            if (hasLinksCol || (hasStatusCol && headerCells.Count >= 3))
            {
                // This is a mod table — parse it with column-aware logic
                var parsedMods = ParseModTable(table, headerTexts);
                mods.AddRange(parsedMods);
                // Also capture any notes into genericNotes so they're available
                // for generic engine games that use BuildNotes + GetGenericNote.
                foreach (var m in parsedMods)
                {
                    if (!string.IsNullOrEmpty(m.Notes))
                        genericNotes[m.Name] = m.Notes;
                }
            }
            else
            {
                ParseGenericTable(table, genericNotes);
            }
        }
        // Apply hardcoded status overrides for games whose wiki status lags reality
        ApplyStatusOverrides(mods);

        // Log first few and last few parsed mod names for diagnostic matching
        if (mods.Count > 0)
        {
            var sample = mods.Take(5).Select(m => m.Name)
                .Concat(mods.Count > 10 ? mods.Skip(mods.Count - 3).Select(m => m.Name) : Enumerable.Empty<string>());
            CrashReporter.Log($"[WikiService.FetchAllAsync] Parsed {mods.Count} specific mods. Sample: [{string.Join(", ", sample)}]");
            // Log raw bytes of first 3 mod names to detect invisible Unicode
            foreach (var m in mods.Take(3))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(m.Name);
                var hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                var norm = _gameDetection.NormalizeName(m.Name);
                CrashReporter.Log($"[WikiService.FetchAllAsync] Mod raw: '{m.Name}' hex=[{hex}] norm='{norm}'");
            }
            // Also log a known game that should match — search for 'Lies of P' or similar
            var liesOfP = mods.FirstOrDefault(m => m.Name.Contains("Lies", StringComparison.OrdinalIgnoreCase));
            if (liesOfP != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(liesOfP.Name);
                var hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                var norm = _gameDetection.NormalizeName(liesOfP.Name);
                CrashReporter.Log($"[WikiService.FetchAllAsync] Mod 'Lies' raw: '{liesOfP.Name}' hex=[{hex}] norm='{norm}'");
            }
            else
            {
                CrashReporter.Log("[WikiService.FetchAllAsync] Mod 'Lies': NOT FOUND in parsed mods");
            }
            // Full normalized dump for match diagnostics
            var allNorms = mods.Select(m => _gameDetection.NormalizeName(m.Name)).OrderBy(n => n).ToList();
            CrashReporter.Log($"[WikiService.FetchAllAsync] Normalized names ({allNorms.Count}): [{string.Join(", ", allNorms)}]");
        }

        progress?.Report($"Found {mods.Count} mods, {genericNotes.Count} generic game notes");
        return (mods, genericNotes);
    }

    /// <summary>
    /// Fetches Last-Modified header for a snapshot URL without downloading the file.
    /// Returns null on failure.
    /// </summary>
    public async Task<DateTime?> GetSnapshotLastModifiedAsync(string url)
    {
        try
        {
            var req  = new HttpRequestMessage(HttpMethod.Head, url);
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return resp.Content.Headers.LastModified?.UtcDateTime
                ?? resp.Headers.Date?.UtcDateTime;
        }
        catch (Exception ex) { CrashReporter.Log($"[WikiService.GetSnapshotLastModifiedAsync] Failed to get Last-Modified for '{url}' — {ex.Message}"); return null; }
    }

    /// <summary>
    /// Parses a mod table with dynamic column detection.
    /// Supports any column count (3+) and finds Name/Maintainer/Links/Status columns
    /// by header text rather than hardcoded positions.
    /// </summary>
    private List<GameMod> ParseModTable(HtmlNode table, List<string> headerTexts)
    {
        var mods = new List<GameMod>();
        var rows = table.SelectNodes(".//tr")?.Skip(1);
        if (rows == null) return mods;

        // Determine column indices from header text
        int nameCol       = 0; // Name is always first
        int maintainerCol = -1;
        int linksCol      = -1;
        int statusCol     = -1;

        for (int i = 0; i < headerTexts.Count; i++)
        {
            var h = headerTexts[i];
            if (h.Contains("maintainer") || h.Contains("author") || h.Contains("developer"))
                maintainerCol = i;
            else if (h.Contains("link") || h.Contains("download"))
                linksCol = i;
            else if (h.Contains("status"))
                statusCol = i;
        }

        // Log table layout for diagnostics
        CrashReporter.Log($"[WikiService.ParseModTable] cols={headerTexts.Count} headers=[{string.Join("|", headerTexts)}] " +
                          $"name={nameCol} maintainer={maintainerCol} links={linksCol} status={statusCol}");

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 2) continue;

            // Name (always first column) + optional hyperlink
            var name = Clean(cells[nameCol].InnerText);
            if (string.IsNullOrWhiteSpace(name)) continue;

            string? nameUrl = null;
            try
            {
                var a = cells[nameCol].SelectSingleNode(".//a");
                if (a != null)
                {
                    var href = a.GetAttributeValue("href", "").Trim();
                    if (!string.IsNullOrEmpty(href))
                    {
                        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            nameUrl = href;
                        else if (href.StartsWith("/"))
                            nameUrl = "https://github.com" + href;
                        else
                            nameUrl = "https://github.com/clshortfuse/renodx/wiki/" + href.TrimStart('.', '/');
                    }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[WikiService.ParseModTable] Failed to parse name URL for '{name}' — {ex.Message}"); }

            var maintainer = (maintainerCol >= 0 && maintainerCol < cells.Count) ? Clean(cells[maintainerCol].InnerText) : "";
            var linksCell  = (linksCol >= 0 && linksCol < cells.Count) ? cells[linksCol] : null;
            var statusCellNode = (statusCol >= 0 && statusCol < cells.Count) ? cells[statusCol] : null;

            string? snapshotUrl = null, snapshotUrl32 = null, nexusUrl = null, discordUrl = null;

            // Scan for download/Nexus/Discord links.
            // If a dedicated Links column exists, only scan that cell.
            // Otherwise, scan ALL cells (including Name and Status) since the wiki
            // embeds download badges in varying columns depending on table format.
            var cellsToScanForLinks = linksCol >= 0
                ? new[] { cells[linksCol] }
                : cells.Cast<HtmlNode>().ToArray();

            foreach (var cell in cellsToScanForLinks)
            {
                foreach (var a in cell.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                {
                    var href = a.GetAttributeValue("href", "");
                    var text = a.InnerText.Trim().ToLowerInvariant();
                    if (href.Contains(".github.io/renodx/") || href.EndsWith(".addon64") || href.EndsWith(".addon32")
                        || href.Contains("/releases/download/"))
                    {
                        if (href.EndsWith(".addon32"))
                            snapshotUrl32 = href;
                        else
                            snapshotUrl = href;
                    }
                    else if (href.Contains("nexusmods.com"))
                        nexusUrl = href;
                    else if (href.Contains("discord.com") || href.Contains("discord.gg"))
                        discordUrl = href;
                    else if (text.Contains("snapshot") || text.Contains("download"))
                        snapshotUrl ??= href;
                }
            }

            string status = "✅"; string? notes = null;
            if (statusCellNode != null)
            {
                var statusText = statusCellNode.InnerText;
                status = statusText.Contains("🚧") ? "🚧" : "✅";
                // Extract notes from tooltip attributes first
                foreach (var a in statusCellNode.SelectNodes(".//a[@title]") ?? Enumerable.Empty<HtmlNode>())
                {
                    var t = a.GetAttributeValue("title", "").Trim();
                    if (!string.IsNullOrEmpty(t)) { notes = t; break; }
                }
            }

            // Also look for notes in ALL remaining cells (any cell that isn't name/maintainer/links/status
            // and contains meaningful text or structured content — the wiki puts game-specific instructions
            // in varying columns depending on table layout).
            if (string.IsNullOrEmpty(notes))
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    if (i == nameCol || i == maintainerCol || i == linksCol) continue;
                    // Skip the status column if it only has ✅/🚧 emoji and no other text
                    if (i == statusCol)
                    {
                        var rawStatus = Clean(cells[i].InnerText);
                        // Status cell may contain notes after the emoji — e.g. "✅ Disable in-game HDR"
                        var noteAfterEmoji = rawStatus
                            .Replace("✅", "").Replace("🚧", "")
                            .Trim(' ', '\n', '\r', '\t');
                        if (!string.IsNullOrEmpty(noteAfterEmoji))
                        {
                            notes = noteAfterEmoji;
                            break;
                        }
                        continue;
                    }
                    // Non-status, non-name, non-links cell — treat as notes
                    var cellNote = BuildNoteText(cells[i]);
                    if (!string.IsNullOrEmpty(cellNote?.Trim()))
                    {
                        notes = cellNote.Trim();
                        break;
                    }
                }
            }

            // If only a 32-bit snapshot exists and no 64-bit, promote it to the primary URL
            // so the install button works for 32-bit-only games (e.g. Terraria)
            if (snapshotUrl == null && snapshotUrl32 != null)
                snapshotUrl = snapshotUrl32;

            mods.Add(new GameMod
            {
                Name = name, Maintainer = maintainer,
                SnapshotUrl = snapshotUrl, SnapshotUrl32 = snapshotUrl32,
                NexusUrl = nexusUrl, DiscordUrl = discordUrl,
                Status = status, Notes = notes,
                NameUrl = nameUrl,
            });
        }
        return mods;
    }

    // Keep ParseSpecificMods as alias for backward compatibility
    private List<GameMod> ParseSpecificMods(HtmlNode table)
    {
        return ParseModTable(table, new List<string> { "name", "maintainer", "links", "status" });
    }

    private void ParseGenericTable(HtmlNode table, Dictionary<string, string> noteMap)
    {
        var rows = table.SelectNodes(".//tr")?.Skip(1);
        if (rows == null) return;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 1) continue;

            var name = Clean(cells[0].InnerText);
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Table structure: Name | Status | Notes
            var notesCell  = cells.Count > 2 ? cells[2] : cells.Count > 1 ? cells[1] : null;
            string? tooltipNote = null;
            if (cells.Count > 1)
            {
                foreach (var a in cells[1].SelectNodes(".//a[@title]") ?? Enumerable.Empty<HtmlNode>())
                {
                    tooltipNote = a.GetAttributeValue("title", "").Trim();
                    if (!string.IsNullOrEmpty(tooltipNote)) break;
                }
            }

            var noteText = notesCell != null ? BuildNoteText(notesCell) : "";
            if (string.IsNullOrEmpty(noteText) && !string.IsNullOrEmpty(tooltipNote))
                noteText = tooltipNote;
            if (!string.IsNullOrEmpty(noteText))
                noteMap[name] = noteText;
        }
    }

    private string BuildNoteText(HtmlNode cell)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in cell.ChildNodes)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Text:
                    var t = HtmlEntity.DeEntitize(node.InnerText);
                    t = System.Text.RegularExpressions.Regex.Replace(t, @"[ \t]+", " ");
                    sb.Append(t); break;
                case HtmlNodeType.Element when node.Name == "br":
                    sb.Append('\n'); break;
                case HtmlNodeType.Element when node.Name is "code" or "kbd" or "samp":
                    var ct = HtmlEntity.DeEntitize(node.InnerText).Trim();
                    if (!string.IsNullOrEmpty(ct)) sb.Append($"`{ct}`"); break;
                case HtmlNodeType.Element:
                    var et = HtmlEntity.DeEntitize(node.InnerText).Trim();
                    if (!string.IsNullOrEmpty(et)) sb.Append(et); break;
            }
        }
        var result = sb.ToString();
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n");
        return result.Trim();
    }

    private string Clean(string s) => NormalizeQuotes(HtmlEntity.DeEntitize(s ?? "").Trim());

    /// <summary>
    /// Replaces curly/smart quotes with straight ASCII equivalents
    /// so wiki game names match Steam names which use standard characters.
    /// </summary>
    private static string NormalizeQuotes(string s) =>
        s.Replace('\u2018', '\'')
         .Replace('\u2019', '\'')
         .Replace('\u201C', '"')
         .Replace('\u201D', '"');

    /// <summary>
    /// Hardcoded status patches for games where the wiki status lags behind reality.
    /// Applied after every fetch so the app reflects the correct known state.
    /// </summary>
    private void ApplyStatusOverrides(List<GameMod> mods)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Code Vein II is confirmed working
            { "Code Vein II", "✅" },
        };

        foreach (var mod in mods)
        {
            if (overrides.TryGetValue(mod.Name, out var status))
                mod.Status = status;
        }
    }
}
