// PatchNotesChanges.cs — finds what changed in this version's patch notes since the version the user had before,
// so What's New can show "Changed since 2.9.6 beta 2" above the full notes. Pure (tests: PatchNotesChangesTests).
//
// RHI keeps a copy of the notes it last ran with (DialogService.Update.cs). After an update, this version's section
// (matched by number, so "v2.9.6 Beta 3" matches "v2.9.6 Beta 2" and "v2.9.6") is compared line by line with the same
// version's section in that copy. Lines that are new or reworded are listed under the heading they sit in.
// Nothing to maintain by hand: a release from 2.9.5 (no 2.9.6 section before) shows no block, a beta user moving to the
// release sees what changed since their beta.

using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

internal static class PatchNotesChanges
{
    /// <summary>
    /// The body of the first "## v…" section whose version number is <paramref name="number"/> ("2.9.6", "2.9.5.1"),
    /// with its heading text ("v2.9.6 Beta 2"), or null. "2.9.6" doesn't match "v2.9.6.1".
    /// </summary>
    internal static (string Heading, string Body)? FindSection(string notes, string number)
    {
        string? heading = null;
        var body = new StringBuilder();
        foreach (var raw in notes.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## v", StringComparison.Ordinal))
            {
                if (heading != null) break;
                var text = line[3..].Trim();
                var rest = text[1..];
                if (rest.StartsWith(number, StringComparison.Ordinal)
                    && (rest.Length == number.Length || rest[number.Length] == ' '))
                    heading = text;
                continue;
            }
            if (heading != null) body.Append(line).Append('\n');
        }
        return heading == null ? null : (heading, body.ToString());
    }

    /// <summary>
    /// Lines of <paramref name="currentBody"/> that aren't in <paramref name="previousBody"/> (ignoring spacing),
    /// each with the "### " heading it sits under (null before the first one). Headings and "---" are never listed;
    /// a line that is only a bold sub-heading ("**General**") isn't either.
    /// </summary>
    internal static List<(string? Heading, string Line)> NewLines(string previousBody, string currentBody)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in previousBody.Split('\n'))
            if (ContentKey(raw) is { } key) seen.Add(key);

        var result = new List<(string?, string)>();
        string? heading = null;
        foreach (var raw in currentBody.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal)) { heading = line[4..].Trim(); continue; }
            if (ContentKey(line) is not { } key || seen.Contains(key)) continue;
            result.Add((heading, line));
        }
        return result;
    }

    /// <summary>Normalised text of a content line, or null for blank lines, headings, rules and bold-only sub-headings.</summary>
    private static string? ContentKey(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#') || line == "---") return null;
        if (Regex.IsMatch(line, @"^\*\*[^*]+\*\*$")) return null;
        return Regex.Replace(line, @"\s+", " ");
    }

    /// <summary>
    /// Markdown for the "Changed since …" block: the new lines grouped under their headings (bold), or null when there
    /// are none. <paramref name="sinceLabel"/> is the earlier version as shown to users ("2.9.6 beta 2").
    /// </summary>
    internal static string? BuildBlock(IReadOnlyList<(string? Heading, string Line)> lines, string sinceLabel)
    {
        if (lines.Count == 0) return null;
        var sb = new StringBuilder();
        sb.Append("### Changed since ").Append(sinceLabel).Append("\n\n");
        string? lastHeading = null;
        bool first = true;
        foreach (var (heading, line) in lines)
        {
            if (first || heading != lastHeading)
            {
                if (!first) sb.Append('\n');
                if (heading != null) sb.Append("**").Append(heading).Append("**\n\n");
                lastHeading = heading;
                first = false;
            }
            sb.Append(line.StartsWith("- ") || line.StartsWith("* ") || Regex.IsMatch(line, @"^\d+\. ") ? line : "- " + line).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Puts <paramref name="block"/> straight under the section's "## v…" heading line (or at the top when there is
    /// none), with a rule after it so the full notes follow.
    /// </summary>
    internal static string InsertBlock(string sectionMarkdown, string block)
    {
        var nl = sectionMarkdown.IndexOf('\n');
        bool hasHeading = sectionMarkdown.StartsWith("## ", StringComparison.Ordinal);
        if (!hasHeading) return block + "\n\n---\n\n" + sectionMarkdown;
        var headingLine = nl < 0 ? sectionMarkdown : sectionMarkdown[..nl];
        var rest = nl < 0 ? "" : sectionMarkdown[(nl + 1)..].TrimStart('\n', '\r');
        return headingLine + "\n\n" + block + "\n\n---\n\n" + rest;
    }
}
