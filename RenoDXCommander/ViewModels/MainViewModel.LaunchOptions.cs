// MainViewModel.LaunchOptions.cs — per-game launch executable and launch arguments (used when RHI launches the game).
// Shown in the game's ⚙ (Game settings), which reads and writes through these.

using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>The user's launch executable for this game, or "" (auto-detect).</summary>
    internal string GetLaunchExeOverride(string gameName) =>
        _gameNameService.LaunchExeOverrides.TryGetValue(gameName, out var v) ? v : "";

    /// <summary>Sets the launch executable ("" or whitespace = auto-detect). Saves only when it changed.</summary>
    internal void SetLaunchExeOverride(string gameName, string? path)
    {
        var value = path?.Trim() ?? "";
        if (value == GetLaunchExeOverride(gameName)) return;
        if (value.Length == 0) _gameNameService.LaunchExeOverrides.Remove(gameName);
        else _gameNameService.LaunchExeOverrides[gameName] = value;
        CrashReporter.Log($"[MainViewModel.SetLaunchExeOverride] '{gameName}' → {(value.Length == 0 ? "auto-detect" : value)}");
        SaveSettingsPublic();
    }

    /// <summary>The user's launch arguments for this game, or "".</summary>
    internal string GetLaunchArgsOverride(string gameName) =>
        _gameNameService.LaunchArgsOverrides.TryGetValue(gameName, out var v) ? v : "";

    /// <summary>Sets the launch arguments ("" = none). Saves only when they changed.</summary>
    internal void SetLaunchArgsOverride(string gameName, string? args)
    {
        var value = args?.Trim() ?? "";
        if (value == GetLaunchArgsOverride(gameName)) return;
        if (value.Length == 0) _gameNameService.LaunchArgsOverrides.Remove(gameName);
        else _gameNameService.LaunchArgsOverrides[gameName] = value;
        CrashReporter.Log($"[MainViewModel.SetLaunchArgsOverride] '{gameName}' → '{value}'");
        SaveSettingsPublic();
    }

    /// <summary>
    /// Opens the Win32 file picker for a game executable, starting in the game's folder. Returns the picked path or null.
    /// </summary>
    internal static Task<string?> BrowseForLaunchExeAsync(IntPtr hwnd, GameCardViewModel card) => Task.Run(() =>
    {
        var ofn = new NativeInterop.OpenFileName();
        ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
        ofn.hwndOwner = hwnd;
        ofn.filter = "Executables (*.exe)\0*.exe\0All Files (*.*)\0*.*\0";
        ofn.file = new string(new char[260]);
        ofn.maxFile = ofn.file.Length;
        ofn.title = "Select Game Executable";
        var browseDir = card.InstallPath is { Length: > 0 } bp && Directory.Exists(bp) ? bp
                      : card.DetectedGame?.InstallPath is { Length: > 0 } dp && Directory.Exists(dp) ? dp
                      : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        ofn.initialDir = browseDir.Replace('/', '\\');
        ofn.flags = 0x00080000 | 0x00001000;
        return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
    });
}
