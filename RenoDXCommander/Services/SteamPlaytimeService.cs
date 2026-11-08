// SteamPlaytimeService.cs -- a Steam game's total playtime, read from Steam's own localconfig.vdf
// (userdata\<account>\config\localconfig.vdf: ... "apps" { "<appid>" { "Playtime" "<minutes>" } }).
// Steam updates it when a game closes, so it doesn't include the session that's running now.
// Used by the RHI overlay's Home panel. File reads: call off the UI thread.
namespace RenoDXCommander.Services;

internal static class SteamPlaytimeService
{
    /// <summary>Total minutes Steam has recorded for <paramref name="appId"/>, or null when unknown.</summary>
    internal static int? GetMinutes(int appId)
    {
        try
        {
            var steam = SteamAchievementService.SteamPath();
            if (steam == null) return null;
            var account = SteamAchievementService.AccountId(steam);
            if (account == null) return null;
            var path = Path.Combine(steam, "userdata", account.Value.ToString(), "config", "localconfig.vdf");
            if (!File.Exists(path)) return null;
            return ParseMinutes(File.ReadAllText(path), appId);
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamPlaytimeService.GetMinutes] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamPlaytimeService.GetMinutes] {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// The "Playtime" value of the app's block inside the "apps" block of localconfig.vdf text, or null.
    /// Only the app block's own keys count (not keys of blocks nested inside it).
    /// </summary>
    internal static int? ParseMinutes(string vdf, int appId)
    {
        int apps = IndexOfKey(vdf, "apps", 0);
        if (apps < 0) return null;
        int appsOpen = vdf.IndexOf('{', apps);
        if (appsOpen < 0) return null;
        int appsClose = MatchingBrace(vdf, appsOpen);
        if (appsClose < 0) return null;

        // The app's block must be a direct child of "apps"
        var appKey = "\"" + appId + "\"";
        int pos = appsOpen + 1;
        while (pos < appsClose)
        {
            int hit = vdf.IndexOf(appKey, pos, appsClose - pos, StringComparison.Ordinal);
            if (hit < 0) return null;
            int open = SkipSpace(vdf, hit + appKey.Length);
            if (open < appsClose && vdf[open] == '{' && Depth(vdf, appsOpen, hit) == 1)
            {
                int close = MatchingBrace(vdf, open);
                if (close < 0) return null;
                return ReadOwnInt(vdf, open, close, "Playtime");
            }
            pos = hit + appKey.Length;
        }
        return null;
    }

    /// <summary>Index of a quoted key (case-insensitive) that is followed by a block, or -1.</summary>
    private static int IndexOfKey(string s, string key, int from)
    {
        var quoted = "\"" + key + "\"";
        int i = from;
        while ((i = s.IndexOf(quoted, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int next = SkipSpace(s, i + quoted.Length);
            if (next < s.Length && s[next] == '{') return i;
            i += quoted.Length;
        }
        return -1;
    }

    private static int SkipSpace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    /// <summary>Brace depth at <paramref name="at"/>, counting from the block opened at <paramref name="open"/>.</summary>
    private static int Depth(string s, int open, int at)
    {
        int depth = 0;
        bool inString = false;
        for (int i = open; i < at; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
            else if (!inString && c == '{') depth++;
            else if (!inString && c == '}') depth--;
        }
        return depth;
    }

    private static int MatchingBrace(string s, int open)
    {
        int depth = 0;
        bool inString = false;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
            else if (!inString && c == '{') depth++;
            else if (!inString && c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>An integer value of <paramref name="key"/> directly inside the block (open, close).</summary>
    private static int? ReadOwnInt(string s, int open, int close, string key)
    {
        var quoted = "\"" + key + "\"";
        int i = open + 1;
        while ((i = s.IndexOf(quoted, i, close - i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (Depth(s, open, i) == 1)
            {
                int v = SkipSpace(s, i + quoted.Length);
                if (v < close && s[v] == '"')
                {
                    int end = s.IndexOf('"', v + 1);
                    if (end > v && end < close && int.TryParse(s.AsSpan(v + 1, end - v - 1), out var minutes) && minutes >= 0)
                        return minutes;
                }
            }
            i += quoted.Length;
        }
        return null;
    }
}
