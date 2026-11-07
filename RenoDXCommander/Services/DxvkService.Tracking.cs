using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DxvkService
{
    // ── File lock for sequential JSON writes (Requirement 22.3) ──────
    // Shared with SafeJsonFile so reads and writes of dxvk_installed.json serialize together.
    private static object _dbLock => SafeJsonFile.LockFor(DbPath);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    // ── Tracking record persistence ──────────────────────────────────

    /// <inheritdoc />
    public List<DxvkInstalledRecord> LoadAllRecords()
    {
        // SafeJsonFile: corrupt-file recovery from .bak; blocks saves if the file is unreadable.
        return SafeJsonFile.ReadList<DxvkInstalledRecord>(DbPath);
    }

    /// <inheritdoc />
    public DxvkInstalledRecord? FindRecord(string gameName, string installPath)
    {
        try
        {
            var records = LoadAllRecords();
            return records.Find(r =>
                string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.FindRecord] Error finding record for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public int RepointRecords(string gameName, string oldPath, string newPath)
    {
        lock (_dbLock)
        {
            var db = LoadAllRecords();
            int changed = 0;
            foreach (var r in db.Where(r => r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                                         && r.InstallPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase)
                                         ).ToList())
            {
                bool newExists = db.Any(o => o != r && o.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) && o.InstallPath.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                if (newExists) db.Remove(r); else r.InstallPath = newPath;
                changed++;
                CrashReporter.Log($"[DxvkService.RepointRecords] '{gameName}': {(newExists ? "dropped old record (new folder has one)" : "moved")} '{oldPath}' → '{newPath}'");
            }
            if (changed > 0) SafeJsonFile.Write(DbPath, db, "DxvkService.SaveRecords", _jsonOptions);
            return changed;
        }
    }

    /// <summary>
    /// Persists a <see cref="DxvkInstalledRecord"/> to the tracking file.
    /// If a record with the same game name and install path already exists,
    /// it is replaced; otherwise the new record is appended.
    /// </summary>
    internal void SaveRecord(DxvkInstalledRecord record)
    {
        SaveRecordCore(record);
    }

    /// <summary>
    /// Public wrapper for <see cref="SaveRecord"/> — used by OptiScalerService
    /// for DXVK coexistence coordination when it needs to update a DxvkInstalledRecord.
    /// </summary>
    public void SaveRecordPublic(DxvkInstalledRecord record)
    {
        SaveRecordCore(record);
    }

    private void SaveRecordCore(DxvkInstalledRecord record)
    {
        try
        {
            lock (_dbLock)
            {
                var records = LoadAllRecords();

                // Remove any existing record for the same game + path
                records.RemoveAll(r =>
                    string.Equals(r.GameName, record.GameName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.InstallPath, record.InstallPath, StringComparison.OrdinalIgnoreCase));

                records.Add(record);

                SafeJsonFile.Write(DbPath, records, "DxvkService.SaveRecords", _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.SaveRecord] Failed to save record for '{record.GameName}' — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.SaveRecord", ex,
                note: $"Game: {record.GameName}, Path: {record.InstallPath}");
        }
    }

    /// <summary>
    /// Removes the tracking record for the specified game name and install path.
    /// No-op if no matching record exists.
    /// </summary>
    internal void RemoveRecord(string gameName, string installPath)
    {
        try
        {
            lock (_dbLock)
            {
                var records = LoadAllRecords();

                var removed = records.RemoveAll(r =>
                    string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));

                if (removed == 0)
                {
                    CrashReporter.Log($"[DxvkService.RemoveRecord] No record found for '{gameName}' at '{installPath}'");
                    return;
                }

                SafeJsonFile.Write(DbPath, records, "DxvkService.SaveRecords", _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.RemoveRecord] Failed to remove record for '{gameName}' — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.RemoveRecord", ex,
                note: $"Game: {gameName}, Path: {installPath}");
        }
    }
}
