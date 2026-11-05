// MainViewModel.AutoStaging.cs — decides whether startup / background work may download
// DXVK and Universal MFG Unlocker. Both are often flagged by antivirus as false positives,
// so they're only kept downloaded while a game uses them, and not while an antivirus
// keeps deleting them (AntivirusGuard). Installing either one still downloads it on demand.

using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>True when the startup / background DXVK staging should run.</summary>
    private bool ShouldAutoStageDxvk(string caller)
    {
        try
        {
            int installs = _dxvkService.LoadAllRecords().Count;
            if (installs == 0)
            {
                _crashReporter.Log($"[{caller}] DXVK staging skipped — no game uses DXVK (downloaded when you install it)");
                return false;
            }
            if (AntivirusGuard.IsBlocked(DxvkService.AntivirusComponentName))
            {
                _crashReporter.Log($"[{caller}] DXVK staging skipped — antivirus removed its files last time (install DXVK again to retry)");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _crashReporter.Log($"[{caller}] DXVK record check failed, staging anyway — {ex.Message}");
            return true;
        }
    }

    /// <summary>True when the startup / periodic Universal MFG Unlocker update check should run.</summary>
    private bool ShouldAutoStageRtx40Mfg(string caller)
    {
        if (_gameNameService.Rtx40MfgInstalledAs.Count > 0) return true;
        _crashReporter.Log($"[{caller}] Universal MFG Unlocker check skipped — no game uses it (downloaded when you install it)");
        return false;
    }
}
