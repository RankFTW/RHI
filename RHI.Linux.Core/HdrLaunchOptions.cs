using System.Text.RegularExpressions;

namespace RHI.Linux.Core;

// Preserve unrelated settings and refuse ambiguous shell syntax.
public static class HdrLaunchOptions
{
    public static string Merge(string existing, bool wayland, bool hdrWsi)
    {
        if (existing.IndexOfAny(['\r', '\n']) >= 0) throw new FormatException("Launch options must be on one line.");
        var tokens = Tokens(existing);
        if (tokens.Count == 0) existing = "%command%";
        else if (!tokens.Contains("%command%") && existing.TrimStart().StartsWith('-')) existing = "%command% " + existing;
        else if (tokens.Count(t => t == "%command%") != 1)
            throw new FormatException("Use exactly one unquoted %command% placeholder before generating HDR options.");
        tokens = Tokens(existing);
        if (Regex.Matches(existing, Regex.Escape("%command%")).Count != 1)
            throw new FormatException("Use exactly one %command% placeholder before generating HDR options.");
        if (tokens.Any(t => t.Trim('\'', '"') is "env" or "-i" or "--ignore-environment" || t.EndsWith("/env", StringComparison.Ordinal)))
            throw new FormatException("An environment wrapper may override the HDR preset. Configure HDR manually in your launcher.");
        var wanted = new List<string> { "PROTON_ENABLE_HDR" };
        if (wayland) wanted.Insert(0, "PROTON_ENABLE_WAYLAND");
        if (hdrWsi) wanted.Add("ENABLE_HDR_WSI");
        var additions = new List<string>();
        var prefix = tokens.TakeWhile(t => Regex.IsMatch(t, "^[A-Za-z_][A-Za-z0-9_]*=")).ToList();
        foreach (var name in wanted)
        {
            // Even occurrences in wrapper arguments/quoted strings are refused: their scope is unknown.
            var occurrences = tokens.Where(t => t.Contains(name, StringComparison.Ordinal)).ToList();
            if (occurrences.Count == 0) { additions.Add(name + "=1"); continue; }
            if (occurrences.Count != 1 || !prefix.Contains(occurrences[0]) ||
                !(occurrences[0] == name + "=1" || occurrences[0] == name + "='1'" || occurrences[0] == name + "=\"1\""))
                throw new FormatException($"{name} already appears with a conflicting value or in an ambiguous position. Review it in your existing options; RHI has not changed it.");
        }
        return additions.Count == 0 ? existing : string.Join(' ', additions) + " " + existing;
    }

    public static bool IsEnabled(string existing)
    {
        try { return Tokens(existing).Count > 0 && Merge(existing, false, false) == existing; }
        catch (FormatException) { return false; }
    }

    public static string Disable(string existing)
    {
        // Validate scope and value before removing the HDR assignment only.
        _ = Merge(existing, false, false);
        if (!IsEnabled(existing)) return existing;
        var token = Tokens(existing).First(t => t.StartsWith("PROTON_ENABLE_HDR=", StringComparison.Ordinal));
        var start = existing.IndexOf(token, StringComparison.Ordinal);
        var end = start + token.Length;
        if (end < existing.Length && existing[end] == ' ') end++;
        return existing[..start] + existing[end..];
    }

    private static List<string> Tokens(string text)
    {
        var result = new List<string>();
        int start = -1; char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // Do not interpret shell expansions, escapes, control operators or redirections.
            if (c is '$' or '`' or '\\' || (quote == '\0' && c is ';' or '|' or '&' or '<' or '>' or '(' or ')' or '#'))
                throw new FormatException("These launch options contain shell syntax that cannot be safely merged. Configure HDR manually in your launcher.");
            if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (start >= 0) { result.Add(text[start..i]); start = -1; }
                continue;
            }
            if (start < 0) start = i;
            if (quote == '\0' && c is '\'' or '"') quote = c;
            else if (c == quote) quote = '\0';
        }
        if (quote != '\0') throw new FormatException("Close the unmatched quote in the existing launch options first.");
        if (start >= 0) result.Add(text[start..]);
        return result;
    }
}
