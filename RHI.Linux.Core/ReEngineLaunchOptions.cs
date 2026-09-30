namespace RHI.Linux.Core;

public static class ReEngineLaunchOptions
{
    public const string Argument = "/WineDetectionEnabled:False";

    public static bool IsEnabled(string existing)
    {
        try { return Validate(existing).Token != null; }
        catch (FormatException) { return false; }
    }

    public static string SetEnabled(string existing, bool enabled)
    {
        var (options, token) = Validate(existing);
        if (token == null)
            return enabled ? options.TrimEnd() + " " + Argument : existing;
        if (enabled) return existing;
        var start = options.IndexOf(token, StringComparison.Ordinal);
        var end = start + token.Length;
        if (end < options.Length && options[end] == ' ') end++;
        else if (start > 0 && options[start - 1] == ' ') start--;
        return options[..start] + options[end..];
    }

    private static (string Options, string? Token) Validate(string existing)
    {
        if (existing.IndexOfAny(['\r', '\n']) >= 0)
            throw new FormatException("Launch options must be on one line.");
        var tokens = HdrLaunchOptions.Tokens(existing);
        if (tokens.Count == 0) existing = "%command%";
        else if (!existing.Contains("%command%", StringComparison.Ordinal) &&
            (existing.TrimStart().StartsWith('-') || tokens[0] == Argument))
            existing = "%command% " + existing;
        tokens = HdrLaunchOptions.Tokens(existing);
        if (tokens.Count(t => t == "%command%") != 1 ||
            existing.Split("%command%", StringSplitOptions.None).Length != 2)
            throw new FormatException("Use exactly one unquoted %command% placeholder before configuring Wine detection.");
        var matches = tokens.Where(t => t.Contains("WineDetectionEnabled", StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) return (existing, null);
        if (matches.Count != 1 ||
            !(matches[0] == Argument || matches[0] == "'" + Argument + "'" || matches[0] == "\"" + Argument + "\"") ||
            tokens.IndexOf(matches[0]) < tokens.IndexOf("%command%"))
            throw new FormatException("WineDetectionEnabled already appears with a conflicting value or in an ambiguous position. Review it in your launcher; RHI has not changed it.");
        return (existing, matches[0]);
    }
}
