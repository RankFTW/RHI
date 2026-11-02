namespace RHI.ManifestEditor.Services;

public enum DiffKind { Same, Added, Removed, Gap }

public sealed record DiffLine(DiffKind Kind, string Text, int? OldLine, int? NewLine);

/// <summary>Line diff between the saved file and the edited text, with a few lines of context.</summary>
public static class LineDiff
{
    public static List<DiffLine> Compute(string oldText, string newText, int context = 2)
    {
        var a = oldText.Replace("\r\n", "\n").Split('\n');
        var b = newText.Replace("\r\n", "\n").Split('\n');

        int pre = 0;
        while (pre < a.Length && pre < b.Length && a[pre] == b[pre]) pre++;
        int suf = 0;
        while (suf < a.Length - pre && suf < b.Length - pre && a[a.Length - 1 - suf] == b[b.Length - 1 - suf]) suf++;

        int n = a.Length - pre - suf, m = b.Length - pre - suf;
        var full = new List<DiffLine>();
        for (int i = 0; i < pre; i++) full.Add(new(DiffKind.Same, a[i], i + 1, i + 1));

        if ((long)n * m > 4_000_000)
        {
            for (int i = 0; i < n; i++) full.Add(new(DiffKind.Removed, a[pre + i], pre + i + 1, null));
            for (int j = 0; j < m; j++) full.Add(new(DiffKind.Added, b[pre + j], null, pre + j + 1));
        }
        else
        {
            // LCS table over the changed middle part
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[pre + i] == b[pre + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[pre + x] == b[pre + y]) { full.Add(new(DiffKind.Same, a[pre + x], pre + x + 1, pre + y + 1)); x++; y++; }
                // Removed lines before added ones, like git
                else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1])) { full.Add(new(DiffKind.Removed, a[pre + x], pre + x + 1, null)); x++; }
                else { full.Add(new(DiffKind.Added, b[pre + y], null, pre + y + 1)); y++; }
            }
        }
        for (int i = 0; i < suf; i++)
            full.Add(new(DiffKind.Same, a[a.Length - suf + i], a.Length - suf + i + 1, b.Length - suf + i + 1));

        // Keep changed lines plus context; collapse the rest into gaps
        var keep = new bool[full.Count];
        for (int i = 0; i < full.Count; i++)
            if (full[i].Kind != DiffKind.Same)
                for (int k = Math.Max(0, i - context); k <= Math.Min(full.Count - 1, i + context); k++) keep[k] = true;
        var result = new List<DiffLine>();
        bool gap = false;
        for (int i = 0; i < full.Count; i++)
        {
            if (keep[i]) { result.Add(full[i]); gap = false; }
            else if (!gap && result.Count > 0) { result.Add(new(DiffKind.Gap, "", null, null)); gap = true; }
        }
        if (result.Count > 0 && result[^1].Kind == DiffKind.Gap) result.RemoveAt(result.Count - 1);
        return result;
    }
}
