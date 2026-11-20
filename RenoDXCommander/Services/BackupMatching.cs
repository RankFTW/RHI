// BackupMatching.cs -- finds this PC's game for each game in a backup. "Game|Store" first; then the same name in
// another store when there's only one such game (bought again elsewhere, or a store rename). Never by path:
// drive letters and folders often change after a reformat. Tests: BackupMatchingTests.

namespace RenoDXCommander.Services;

public static class BackupMatching
{
    /// <summary>
    /// For each backed-up game, the index into <paramref name="current"/> of its match, or -1.
    /// A current game is matched at most once.
    /// </summary>
    public static int[] Match(IReadOnlyList<BackupGame> backup, IReadOnlyList<(string Name, string Store)> current)
    {
        var result = new int[backup.Count];
        Array.Fill(result, -1);
        var used = new bool[current.Count];

        // Exact "Game|Store"
        for (int i = 0; i < backup.Count; i++)
        {
            for (int j = 0; j < current.Count; j++)
            {
                if (used[j]) continue;
                if (string.Equals(backup[i].Name, current[j].Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(backup[i].Store ?? "", current[j].Store ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    result[i] = j; used[j] = true; break;
                }
            }
        }

        // Same name in another store, only when it's unambiguous on both sides
        for (int i = 0; i < backup.Count; i++)
        {
            if (result[i] >= 0) continue;
            var name = backup[i].Name;
            if (backup.Count(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) != 1) continue;
            int found = -1, count = 0;
            for (int j = 0; j < current.Count; j++)
            {
                if (!string.Equals(current[j].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                count++;
                if (!used[j]) found = j;
            }
            if (count == 1 && found >= 0) { result[i] = found; used[found] = true; }
        }
        return result;
    }
}
