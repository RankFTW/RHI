using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Stateless utility that parses and pretty-prints the official ReShade Addons.ini format.
/// Each section is a numbered header like [01] followed by key=value pairs.
/// Commented-out sections start with a comment character on the header line (e.g. "# [02]" or ";[02]").
/// </summary>
public static class AddonsIniParser
{
    /// <summary>Section IDs excluded because they are managed by RHI or N/A.</summary>
    public static readonly HashSet<string> ExcludedSections = new() { "00", "21", "26" };

    /// <summary>
    /// Parses Addons.ini content into a list of <see cref="AddonEntry"/> objects.
    /// Skips excluded sections, commented-out sections, and malformed sections (missing PackageName).
    /// </summary>
    public static List<AddonEntry> Parse(string iniContent, Action<string>? logWarning = null)
    {
        var entries = new List<AddonEntry>();
        if (string.IsNullOrWhiteSpace(iniContent))
            return entries;

        var lines = iniContent.Split('\n');
        string? currentSectionId = null;
        bool currentSectionCommented = false;
        var currentFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Try to detect a section header (commented or not)
            if (TryParseSectionHeader(trimmed, out var sectionId, out var isCommented))
            {
                // Flush previous section
                if (currentSectionId != null)
                    FlushSection(currentSectionId, currentSectionCommented, currentFields, entries, logWarning);

                currentSectionId = sectionId;
                currentSectionCommented = isCommented;
                currentFields.Clear();
                continue;
            }

            // Skip lines outside any section
            if (currentSectionId == null)
                continue;

            // Skip blank lines
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Strip leading comment characters for commented-out sections
            var valueLine = trimmed;
            if (currentSectionCommented)
            {
                valueLine = StripCommentPrefix(valueLine);
                if (valueLine == null)
                    continue; // not a key=value line even after stripping
            }
            else if (IsCommentLine(trimmed))
            {
                // Inside an uncommented section, skip standalone comment lines
                continue;
            }

            // Parse key=value
            var eqIdx = valueLine.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = valueLine[..eqIdx].Trim();
                var value = valueLine[(eqIdx + 1)..].Trim();
                currentFields[key] = value;
            }
        }

        // Flush last section
        if (currentSectionId != null)
            FlushSection(currentSectionId, currentSectionCommented, currentFields, entries, logWarning);

        return entries;
    }

    /// <summary>
    /// Formats a list of <see cref="AddonEntry"/> objects back into valid Addons.ini format.
    /// Output can be round-tripped through <see cref="Parse"/> to get equivalent entries.
    /// </summary>
    public static string PrettyPrint(List<AddonEntry> entries)
    {
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();

            var e = entries[i];
            sb.AppendLine($"[{e.SectionId}]");
            sb.AppendLine($"PackageName={e.PackageName}");

            if (e.PackageDescription != null)
                sb.AppendLine($"PackageDescription={e.PackageDescription}");
            if (e.EffectInstallPath != null)
                sb.AppendLine($"EffectInstallPath={e.EffectInstallPath}");
            if (e.DownloadUrl != null)
                sb.AppendLine($"DownloadUrl={e.DownloadUrl}");
            if (e.DownloadUrl32 != null)
                sb.AppendLine($"DownloadUrl32={e.DownloadUrl32}");
            if (e.DownloadUrl64 != null)
                sb.AppendLine($"DownloadUrl64={e.DownloadUrl64}");
            if (e.RepositoryUrl != null)
                sb.AppendLine($"RepositoryUrl={e.RepositoryUrl}");
        }

        return sb.ToString();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Tries to parse a section header from a trimmed line.
    /// Handles both uncommented "[01]" and commented "# [01]" / ";[01]" forms.
    /// </summary>
    private static bool TryParseSectionHeader(string trimmed, out string sectionId, out bool isCommented)
    {
        sectionId = "";
        isCommented = false;

        if (string.IsNullOrEmpty(trimmed))
            return false;

        var work = trimmed;

        // Check for comment prefix
        if (work.StartsWith('#') || work.StartsWith(';'))
        {
            isCommented = true;
            work = work.TrimStart('#', ';').Trim();
        }

        // Must be [id]
        if (!work.StartsWith('['))
            return false;

        var closeIdx = work.IndexOf(']');
        if (closeIdx < 2)
            return false;

        sectionId = work[1..closeIdx].Trim();
        return sectionId.Length > 0;
    }

    private static bool IsCommentLine(string trimmed)
        => trimmed.StartsWith('#') || trimmed.StartsWith(';');

    /// <summary>
    /// Strips a leading comment prefix ("# " or "#" or "; " or ";") from a line.
    /// Returns null if the result doesn't look like a key=value pair.
    /// </summary>
    private static string? StripCommentPrefix(string trimmed)
    {
        if (!IsCommentLine(trimmed))
            return trimmed;

        var stripped = trimmed.TrimStart('#', ';').Trim();
        return stripped.Contains('=') ? stripped : null;
    }

    private static void FlushSection(
        string sectionId,
        bool isCommented,
        Dictionary<string, string> fields,
        List<AddonEntry> entries,
        Action<string>? logWarning)
    {
        // Skip commented-out sections entirely
        if (isCommented)
            return;

        // Skip excluded sections
        if (ExcludedSections.Contains(sectionId))
            return;

        // PackageName is required
        if (!fields.TryGetValue("PackageName", out var packageName) ||
            string.IsNullOrWhiteSpace(packageName))
        {
            logWarning?.Invoke($"Section [{sectionId}] skipped: missing or empty PackageName.");
            return;
        }

        entries.Add(new AddonEntry(
            SectionId: sectionId,
            PackageName: packageName,
            PackageDescription: fields.GetValueOrDefault("PackageDescription"),
            DownloadUrl: fields.GetValueOrDefault("DownloadUrl"),
            DownloadUrl32: fields.GetValueOrDefault("DownloadUrl32"),
            DownloadUrl64: fields.GetValueOrDefault("DownloadUrl64"),
            RepositoryUrl: fields.GetValueOrDefault("RepositoryUrl"),
            EffectInstallPath: fields.GetValueOrDefault("EffectInstallPath")));
    }
}
