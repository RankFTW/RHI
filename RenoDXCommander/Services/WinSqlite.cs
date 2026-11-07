// WinSqlite.cs -- minimal read-only SQLite access through winsqlite3.dll, the copy of SQLite that
// ships with Windows 10 and 11 (System32). Used to read the Xbox app's AsyncCache.db without adding a
// NuGet package or a native DLL to the publish. Text columns and parameters only.

using System.Runtime.InteropServices;
using System.Text;

namespace RenoDXCommander.Services;

internal sealed class WinSqliteException : Exception
{
    public int Code { get; }
    public WinSqliteException(int code, string message) : base(message) { Code = code; }
}

/// <summary>A read-only connection. Not thread-safe: open one per use.</summary>
internal sealed class WinSqliteDb : IDisposable
{
    private const string Lib = "winsqlite3.dll";
    private const int SQLITE_OK = 0, SQLITE_ROW = 100, SQLITE_DONE = 101;
    private const int SQLITE_OPEN_READONLY = 0x1, SQLITE_OPEN_URI = 0x40, SQLITE_OPEN_NOMUTEX = 0x8000;
    private static readonly IntPtr Transient = new(-1);

    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] text, int bytes, IntPtr destructor);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, ExactSpelling = true)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_column_bytes(IntPtr stmt, int col);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, ExactSpelling = true)] private static extern IntPtr sqlite3_errmsg(IntPtr db);

    private IntPtr _db;

    private WinSqliteDb(IntPtr db) { _db = db; }

    /// <summary>
    /// Opens a database read-only. <paramref name="immutable"/> tells SQLite the file can't change
    /// (no locking): only for a private copy. Throws WinSqliteException or DllNotFoundException.
    /// </summary>
    internal static WinSqliteDb Open(string path, bool immutable = false)
    {
        // A file: URI so mode / immutable can be passed; the path is percent-encoded by Uri
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri + (immutable ? "?mode=ro&immutable=1" : "?mode=ro");
        int rc = sqlite3_open_v2(Utf8(uri), out var db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI | SQLITE_OPEN_NOMUTEX, IntPtr.Zero);
        if (rc != SQLITE_OK)
        {
            var msg = db != IntPtr.Zero ? Message(db) : $"code {rc}";
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            throw new WinSqliteException(rc, $"open failed: {msg}");
        }
        sqlite3_busy_timeout(db, 2000);
        return new WinSqliteDb(db);
    }

    /// <summary>Runs a query; each row is its columns as strings (null for NULL).</summary>
    internal List<string?[]> Query(string sql, int columns, params string[] args)
    {
        if (_db == IntPtr.Zero) throw new ObjectDisposedException(nameof(WinSqliteDb));
        int rc = sqlite3_prepare_v2(_db, Utf8(sql), -1, out var stmt, IntPtr.Zero);
        if (rc != SQLITE_OK) throw new WinSqliteException(rc, $"prepare failed: {Message(_db)}");
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(args[i]);
                rc = sqlite3_bind_text(stmt, i + 1, bytes, bytes.Length, Transient);
                if (rc != SQLITE_OK) throw new WinSqliteException(rc, $"bind failed: {Message(_db)}");
            }
            var rows = new List<string?[]>();
            while (true)
            {
                rc = sqlite3_step(stmt);
                if (rc == SQLITE_DONE) return rows;
                if (rc != SQLITE_ROW) throw new WinSqliteException(rc, $"step failed: {Message(_db)}");
                var row = new string?[columns];
                for (int c = 0; c < columns; c++)
                {
                    var p = sqlite3_column_text(stmt, c);
                    row[c] = p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p, sqlite3_column_bytes(stmt, c));
                }
                rows.Add(row);
            }
        }
        finally { sqlite3_finalize(stmt); }
    }

    /// <summary>The first column of the first row, or null.</summary>
    internal string? Scalar(string sql, params string[] args)
    {
        var rows = Query(sql, 1, args);
        return rows.Count > 0 ? rows[0][0] : null;
    }

    public void Dispose()
    {
        if (_db == IntPtr.Zero) return;
        sqlite3_close_v2(_db);
        _db = IntPtr.Zero;
    }

    private static string Message(IntPtr db) => Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "unknown error";

    private static byte[] Utf8(string s)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
        return bytes;
    }
}
