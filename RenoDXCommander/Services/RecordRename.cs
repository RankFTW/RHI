namespace RenoDXCommander.Services;

/// <summary>
/// Moves install records from a game's old name to its new one when the user renames it,
/// so components stay detected and uninstallable. Pure: each record store calls it under its own lock.
/// </summary>
internal static class RecordRename
{
    /// <summary>
    /// Renames every record of <paramref name="oldName"/> to <paramref name="newName"/>; with
    /// <paramref name="installPath"/> only records at that folder (another store's copy keeps the old name).
    /// A record the new name already has in the same slot (<paramref name="sameSlot"/>) wins and the old one
    /// is dropped. Returns how many records changed.
    /// </summary>
    public static int Apply<T>(List<T> db, string oldName, string newName, string? installPath,
        Func<T, string?> name, Action<T, string> setName, Func<T, string?> path, Func<T, T, bool> sameSlot)
    {
        int changed = 0;
        foreach (var r in db.Where(r => string.Equals(name(r), oldName, StringComparison.OrdinalIgnoreCase)
                                     && (installPath == null || SamePath(path(r), installPath))).ToList())
        {
            bool newExists = db.Any(o => !ReferenceEquals(o, r)
                                      && string.Equals(name(o), newName, StringComparison.OrdinalIgnoreCase)
                                      && sameSlot(o, r));
            if (newExists) db.Remove(r); else setName(r, newName);
            changed++;
        }
        return changed;
    }

    public static bool SamePath(string? a, string? b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string? p) => (p ?? "").Replace('/', '\\').TrimEnd('\\');
}
