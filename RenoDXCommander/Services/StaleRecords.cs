// StaleRecords.cs -- drops install records left behind when a game's folder changed.
//
// Path reconciliation in BuildCards re-points the first record it finds for a game, but saving matches by
// path, so when the new folder already had a record the old one stayed in the file and was reconciled again
// on every launch. Game Pass made one per game update (Forza Horizon 6 had three). A record is stale only
// when its folder no longer exists and the same game / component has a record in a folder that does.
namespace RenoDXCommander.Services;

public static class StaleRecords
{
    /// <summary>
    /// <paramref name="records"/> without stale duplicates: same <paramref name="groupKey"/> as a record whose
    /// folder exists, while its own folder is gone. Records whose folder exists are always kept.
    /// </summary>
    public static List<T> WithoutStaleDuplicates<T>(List<T> records, Func<T, string> groupKey, Func<T, string> path,
        Func<string, bool> folderExists, out List<T> removed)
    {
        var exists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string p) => !string.IsNullOrEmpty(p)
            && (exists.TryGetValue(p, out var e) ? e : exists[p] = folderExists(p));

        var liveGroups = new HashSet<string>(records.Where(r => Exists(path(r))).Select(groupKey),
            StringComparer.OrdinalIgnoreCase);
        removed = records.Where(r => !string.IsNullOrEmpty(path(r)) && !Exists(path(r))
                                     && liveGroups.Contains(groupKey(r))).ToList();
        if (removed.Count == 0) return records;
        var drop = new HashSet<T>(removed);
        return records.Where(r => !drop.Contains(r)).ToList();
    }

    /// <summary>Cleans RenoDX (installed.json) and component (aux_installed.json) records. Startup, before they load.</summary>
    public static void CleanAll()
    {
        ModInstallService.RemoveStaleDuplicateRecords();
        AuxInstallService.RemoveStaleDuplicateRecords();
    }
}
