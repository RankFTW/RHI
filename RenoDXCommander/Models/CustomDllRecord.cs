// CustomDllRecord.cs — one user-supplied DLL (from %LocalAppData%\RHI\Custom\DLLs\) that RHI copied into a
// game. Kept in custom_dll_installs.json keyed by "GameName|Store"; the game folder's rhi_install.txt has a
// matching component "CustomDll:<Source>".

namespace RenoDXCommander.Models;

public class CustomDllRecord
{
    /// <summary>File name in the custom DLL folder (the DLL's identity), e.g. "SpecialK64.dll".</summary>
    public string Source { get; set; } = "";

    /// <summary>File name it was copied to in the game folder, e.g. "dxgi.dll".</summary>
    public string InstalledAs { get; set; } = "";

    /// <summary>Game folder the copy went into.</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>SHA-256 (hex) of the copy RHI last put in the game: tells RHI's copy from a hand-replaced file.</summary>
    public string Sha256 { get; set; } = "";

    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the last newer version from the folder was copied over RHI's copy, or null.</summary>
    public DateTime? UpdatedUtc { get; set; }
}

/// <summary>A DLL in the custom DLL folder.</summary>
/// <param name="FileName">Its identity.</param>
/// <param name="Label">ProductName + FileVersion when present, else the file name (display only).</param>
/// <param name="Problem">Why it can't be used (not a DLL, unsupported machine), or null.</param>
public sealed record CustomDllSource(string FileName, string FullPath, string Label, bool Is64Bit, string? Problem)
{
    public bool Usable => Problem == null;
}

/// <summary>The game facts the custom DLL service needs (kept apart from the card so it can be tested).</summary>
public sealed record CustomDllGame(string GameName, string Store, string InstallPath, bool Is32Bit,
    bool IsRunning = false, bool IsHidden = false)
{
    public string Key => GameKey.FromCard(GameName, Store).ToKey();
}

/// <summary>State of one installed custom DLL, for the ⚙.</summary>
public enum CustomDllState
{
    UpToDate,
    /// <summary>The folder has a newer file; the next update pass copies it.</summary>
    UpdatePending,
    SourceMissing,
    ChangedOutsideRhi,
    /// <summary>The game's copy is gone (the next update pass drops the record).</summary>
    Missing,
}

/// <summary>Result of one redeploy pass.</summary>
public sealed record CustomDllRedeployResult(int GamesChecked, int Updated, int Failed, int Dropped);
