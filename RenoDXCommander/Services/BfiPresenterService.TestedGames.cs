// BfiPresenterService.TestedGames.cs -- the author's tested-games list (docs/tested-games.md), as the BFI Presenter
// Manager uses it: each game's status and key requirement for the ⚙, and PresenterResize for the games that need it
// (the Manager sets it at install). Fetched from GitHub raw (not the API), cached a day in the staging folder.
// Three tables: "| Game | Status | Frame generation ... | Min. version | Key requirement |" (main), "| Game | Notes |"
// (more games, standard setup) and "| Game | Tested by | Notes |" (user reports).

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>A game's entry in BFI Presenter's tested-games list.</summary>
/// <param name="PresenterResizeOnlyWithoutRenoDx">The list says the resize is needed only without RenoDX (its swap chain upgrade resizes).</param>
public sealed record BfiTestedEntry(string Name, string Status, string Notes, int? PresenterResize, bool PresenterResizeOnlyWithoutRenoDx = false);

public partial class BfiPresenterService
{
    private const string TestedGamesRawUrl = "https://raw.githubusercontent.com/artzox/BFI-Presenter/main/docs/tested-games.md";
    private static readonly TimeSpan TestedGamesMaxAge = TimeSpan.FromDays(1);
    private List<BfiTestedEntry>? _tested;

    private string TestedGamesCache => Path.Combine(_stagingRoot, "tested-games.md");

    /// <summary>Loads the list (cache, refreshed when older than a day). Never throws; empty when unavailable.</summary>
    public async Task<IReadOnlyList<BfiTestedEntry>> GetTestedGamesAsync()
    {
        if (_tested != null) return _tested;
        string? text = null;
        try
        {
            bool fresh = File.Exists(TestedGamesCache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(TestedGamesCache) < TestedGamesMaxAge;
            if (!fresh)
            {
                try
                {
                    text = await _http.GetStringAsync(TestedGamesRawUrl).ConfigureAwait(false);
                    Directory.CreateDirectory(_stagingRoot);
                    await File.WriteAllTextAsync(TestedGamesCache, text).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    _crashReporter.Log($"[BfiPresenterService.GetTestedGames] Fetch failed — {ex.Message}");
                }
            }
            if (text == null && File.Exists(TestedGamesCache)) text = await File.ReadAllTextAsync(TestedGamesCache).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.GetTestedGames] {ex.Message}");
        }
        _tested = text == null ? new List<BfiTestedEntry>() : ParseTestedGames(text);
        return _tested;
    }

    /// <summary>The list's entry for a game, or null. Exact name first, then an entry whose name starts the game's
    /// (a store edition or subtitle after it), never when what follows is a number (Alan Wake vs Alan Wake 2).</summary>
    public static BfiTestedEntry? FindTested(IReadOnlyList<BfiTestedEntry> list, string gameName)
    {
        var key = NormalizeName(gameName);
        if (key.Length == 0) return null;
        var exact = list.FirstOrDefault(e => NormalizeName(e.Name) == key);
        if (exact != null) return exact;
        return list
            .Select(e => (e, n: NormalizeName(e.Name)))
            .Where(x => x.n.Length >= 4 && key.StartsWith(x.n + " ", StringComparison.Ordinal)
                        && !char.IsDigit(key[x.n.Length + 1]))
            .OrderByDescending(x => x.n.Length)
            .Select(x => x.e)
            .FirstOrDefault();
    }

    internal static List<BfiTestedEntry> ParseTestedGames(string markdown)
    {
        var list = new List<BfiTestedEntry>();
        string[]? header = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|')) { header = null; continue; }
            var cells = SplitRow(line);
            if (cells.All(c => c.Trim('-', ':', ' ').Length == 0)) continue; // | --- | --- |
            if (header == null) { header = cells; continue; }
            if (cells.Length < 2 || !header[0].Equals("Game", StringComparison.OrdinalIgnoreCase)) continue;
            var name = CleanCell(cells[0]);
            if (name.Length == 0) continue;
            string status, notes;
            if (header.Length >= 5 && header[1].Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                status = CleanCell(cells[1]);
                var fg = cells.Length > 2 ? CleanCell(cells[2]) : "";
                var req = CleanCell(cells[^1]);
                notes = string.IsNullOrEmpty(fg) || fg is "—" or "-" or "Not tested" ? req : $"{req} Frame generation: {fg}";
            }
            else if (header.Length >= 3 && header[1].StartsWith("Tested by", StringComparison.OrdinalIgnoreCase))
            {
                status = "Reported working by " + CleanCell(cells[1]);
                notes = CleanCell(cells[^1]);
            }
            else
            {
                status = "Working";
                notes = CleanCell(cells[^1]);
            }
            // "`PresenterResize=2`" or a bare "`PresenterResize`" (= 1, "set by the Manager"); "without RenoDX" =
            // RenoDX's own swap chain upgrade already does the resize (Hades II)
            int? resize = null;
            var row = string.Join(" ", cells);
            var m = Regex.Match(row, @"PresenterResize(?:=(\d))?");
            if (m.Success) resize = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
            bool notWithRenoDx = resize != null && row.Contains("without RenoDX", StringComparison.OrdinalIgnoreCase);
            list.Add(new BfiTestedEntry(name, status, notes, resize, notWithRenoDx));
        }
        return list;
    }

    private static string[] SplitRow(string line)
    {
        var inner = line.Trim().Trim('|');
        return inner.Split('|').Select(c => c.Trim()).ToArray();
    }

    /// <summary>Markdown cell → plain text: links to their text, no bold or escapes.</summary>
    private static string CleanCell(string cell)
    {
        var s = Regex.Replace(cell, @"\[([^\]]*)\]\([^)]*\)", "$1");
        s = s.Replace("**", "").Replace("\\~", "~").Replace("\\", "");
        return s.Trim();
    }

    /// <summary>Lower case, accents and ™®© off, "(...)" off, punctuation to spaces.</summary>
    internal static string NormalizeName(string name)
    {
        var s = Regex.Replace(name, @"\([^)]*\)", " ");
        s = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '™' or '®' or '©') continue;
            if (c == '\'' || c == '’') continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
