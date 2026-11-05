// MainViewModel.UalHooked.cs — keeps extras' "installed as" records right when Ultimate ASI Loader
// renames files: installing ASI Loader as X.dll moves an extra at X.dll to XHooked.dll (ASI Loader
// loads it from there), and removing ASI Loader (or moving it to another name) moves XHooked.dll back.

using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>The extras with a DLL name picker: label, current name and a setter for the record.</summary>
    private List<(string Label, string? InstalledAs, Action<string?> Set)> ExtraDllRecords(GameCardViewModel card)
    {
        var g = card.GameName;
        var s = card.Source ?? "";
        return new()
        {
            ("20/30 FG Unlock",        GetDlssg2030InstalledAs(g, s), v => SetDlssg2030InstalledAs(g, v, s)),
            ("Universal MFG Unlocker", GetRtx40MfgInstalledAs(g, s),  v => SetRtx40MfgInstalledAs(g, v, s)),
            ("RTX Encore",             GetRtxEncoreInstalledAs(g, s), v => SetRtxEncoreInstalledAs(g, v, s)),
            ("DLSS Enabler",           GetDeInstalledAs(g, s),        v => SetDeInstalledAs(g, v, s)),
        };
    }

    /// <summary>Every DLL name an RHI component uses in this game, mapped to the component's name.</summary>
    public Dictionary<string, string> GetRhiDllOwners(GameCardViewModel card)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(card.RsInstalledFile)) owners[card.RsInstalledFile] = "ReShade";
        if (!string.IsNullOrEmpty(card.OsInstalledFile)) owners[card.OsInstalledFile] = "OptiScaler";
        if (!string.IsNullOrEmpty(card.DcInstalledFile)) owners[card.DcInstalledFile] = "Display Commander";
        var ual = GetUalInstalledAs(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(ual)) owners[ual] = "ASI Loader";
        foreach (var e in ExtraDllRecords(card))
            if (!string.IsNullOrEmpty(e.InstalledAs) && !owners.ContainsKey(e.InstalledAs)) owners[e.InstalledAs] = e.Label;
        return owners;
    }

    /// <summary>
    /// After ASI Loader was installed as <paramref name="dllName"/> and moved the file that was there to
    /// <paramref name="hookedName"/>: if that file was an RHI extra, its record (and .original sentinel)
    /// follows it. Returns the extra's name, or null when the moved file was the game's own.
    /// </summary>
    public string? OnUalChainedFile(GameCardViewModel card, string dllName, string hookedName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;
        foreach (var e in ExtraDllRecords(card))
        {
            if (!string.Equals(e.InstalledAs, dllName, StringComparison.OrdinalIgnoreCase)) continue;
            RenameExtraRecord(card, e.Label, e.Set, dllName, hookedName);
            return e.Label;
        }
        return null;
    }

    /// <summary>
    /// After ASI Loader stopped using <paramref name="ualName"/> (removed, or reinstalled under another name):
    /// it moved XHooked.dll back to X.dll, so an extra that was XHooked.dll is now X.dll and keeps working on its own.
    /// </summary>
    public void OnUalReleasedName(GameCardViewModel card, string? ualName)
    {
        if (string.IsNullOrEmpty(card.InstallPath) || string.IsNullOrEmpty(ualName)) return;
        var hooked = HookedDllNames.ToHooked(ualName);
        foreach (var e in ExtraDllRecords(card))
        {
            if (!string.Equals(e.InstalledAs, hooked, StringComparison.OrdinalIgnoreCase)) continue;
            bool moved = File.Exists(Path.Combine(card.InstallPath, ualName))
                      && !File.Exists(Path.Combine(card.InstallPath, hooked));
            if (!moved)
            {
                _crashReporter.Log($"[MainViewModel.OnUalReleasedName] {e.Label} '{hooked}' wasn't moved back to '{ualName}' for '{card.GameName}' — record left as it is");
                continue;
            }
            RenameExtraRecord(card, e.Label, e.Set, hooked, ualName);
        }
    }

    private void RenameExtraRecord(GameCardViewModel card, string label, Action<string?> set, string fromName, string toName)
    {
        var dir = card.InstallPath!;
        HookedDllNames.MoveSentinel(Path.Combine(dir, fromName), Path.Combine(dir, toName));
        if (label == "RTX Encore")
            Models.RhiInstallManifest.SetComponent(dir, RtxEncoreService.ComponentName, new[] { Path.Combine(dir, toName) });
        set(toName);
        _crashReporter.Log($"[MainViewModel.RenameExtraRecord] {label} is now '{toName}' (was '{fromName}') for '{card.GameName}'");
    }
}
