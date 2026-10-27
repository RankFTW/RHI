using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RHI.ManifestEditor.Models;

namespace RHI.ManifestEditor.ViewModels;

public partial class InstallWarningItem : ObservableObject
{
    [ObservableProperty] private string _gameName;
    [ObservableProperty] private string _reshade;
    [ObservableProperty] private string _renodx;
    [ObservableProperty] private string _relimiter;
    [ObservableProperty] private string _dc;
    [ObservableProperty] private string _optiscaler;
    [ObservableProperty] private string _luma;
    [ObservableProperty] private string _reframework;
    [ObservableProperty] private string _dxvk;

    public InstallWarningItem(string name, Dictionary<string, string> d)
    {
        _gameName = name;
        _reshade = d.TryGetValue("reshade", out var v) ? v : "";
        _renodx = d.TryGetValue("renodx", out v) ? v : "";
        _relimiter = d.TryGetValue("relimiter", out v) ? v : "";
        _dc = d.TryGetValue("dc", out v) ? v : "";
        _optiscaler = d.TryGetValue("optiscaler", out v) ? v : "";
        _luma = d.TryGetValue("luma", out v) ? v : "";
        _reframework = d.TryGetValue("reframework", out v) ? v : "";
        _dxvk = d.TryGetValue("dxvk", out v) ? v : "";
    }
    public InstallWarningItem() { _gameName = ""; _reshade = ""; _renodx = ""; _relimiter = ""; _dc = ""; _optiscaler = ""; _luma = ""; _reframework = ""; _dxvk = ""; }

    public Dictionary<string, string> ToDict()
    {
        var d = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(Reshade))     d["reshade"]     = Reshade;
        if (!string.IsNullOrWhiteSpace(Renodx))      d["renodx"]      = Renodx;
        if (!string.IsNullOrWhiteSpace(Relimiter))   d["relimiter"]   = Relimiter;
        if (!string.IsNullOrWhiteSpace(Dc))          d["dc"]          = Dc;
        if (!string.IsNullOrWhiteSpace(Optiscaler))  d["optiscaler"]  = Optiscaler;
        if (!string.IsNullOrWhiteSpace(Luma))        d["luma"]        = Luma;
        if (!string.IsNullOrWhiteSpace(Reframework)) d["reframework"] = Reframework;
        if (!string.IsNullOrWhiteSpace(Dxvk))        d["dxvk"]        = Dxvk;
        return d;
    }
}

public partial class DllOverrideItem : ObservableObject
{
    [ObservableProperty] private string _gameName;
    [ObservableProperty] private string _reshade;
    [ObservableProperty] private string _dc;

    public DllOverrideItem(string name, ManifestDllNames d) { _gameName = name; _reshade = d.ReShade ?? ""; _dc = d.Dc ?? ""; }
    public DllOverrideItem() { _gameName = ""; _reshade = ""; _dc = ""; }
}

/// <summary>A single entry in forceExternalOnly — game name + url + optional label.</summary>
public partial class ForceExternalItem : ObservableObject
{
    [ObservableProperty] private string _key;
    [ObservableProperty] private string _url;
    [ObservableProperty] private string _label;

    public ForceExternalItem(string key, ForceExternalEntry e)
    {
        _key = key;
        _url = e.Url ?? "";
        _label = e.Label ?? "";
    }
    public ForceExternalItem() { _key = ""; _url = ""; _label = ""; }
}

/// <summary>A single game's renodxIniOverrides entry — game name + dict of ini key→value pairs.</summary>
public partial class RenodxIniGameItem : ObservableObject
{
    [ObservableProperty] private string _gameName;
    public ObservableCollection<KeyValueItem> Entries { get; }

    private readonly MainViewModel _main;

    public RenodxIniGameItem(string name, Dictionary<string, string> d, MainViewModel main)
    {
        _gameName = name;
        _main = main;
        Entries = new(d.Select(kv => new KeyValueItem(kv.Key, kv.Value)));
        Entries.CollectionChanged += (_, _) => _main.MarkDirty();
    }
    public RenodxIniGameItem(MainViewModel main)
    {
        _gameName = "";
        _main = main;
        Entries = new();
        Entries.CollectionChanged += (_, _) => _main.MarkDirty();
    }

    [RelayCommand] public void AddEntry() { Entries.Add(new KeyValueItem()); _main.MarkDirty(); }
    [RelayCommand] public void RemoveEntry(KeyValueItem item) { Entries.Remove(item); _main.MarkDirty(); }
}

public partial class InstallSectionViewModel : SectionViewModelBase
{
    public ObservableCollection<InstallWarningItem> InstallWarnings { get; }
    public ObservableCollection<ForceExternalItem> ForceExternalOnly { get; }
    public ObservableCollection<KeyValueItem> SnapshotOverrides { get; }
    public ObservableCollection<DllOverrideItem> DllNameOverrides { get; }
    public ObservableCollection<KeyValueItem> GacSymlinkGames { get; }
    public ObservableCollection<KeyValueItem> LaunchExeOverrides { get; }
    public ObservableCollection<KeyValueItem> LegacyReShadeVersions { get; }
    public ObservableCollection<StringItem> LegacyReShadeAvailable { get; }
    public ObservableCollection<RenodxIniGameItem> RenodxIniOverrides { get; }
    public ObservableCollection<RenodxExtraSettingItem> RenodxExtraSettings { get; }

    public InstallSectionViewModel(RemoteManifest manifest, MainViewModel main) : base(manifest, main)
    {
        InstallWarnings = new(manifest.InstallWarnings?
            .Where(kv => !kv.Key.StartsWith("_"))
            .Select(kv => new InstallWarningItem(kv.Key, kv.Value)) ?? Enumerable.Empty<InstallWarningItem>());
        ForceExternalOnly = new(manifest.ForceExternalOnly?
            .Select(kv => new ForceExternalItem(kv.Key, kv.Value)) ?? Enumerable.Empty<ForceExternalItem>());
        SnapshotOverrides = ToKvObservable(manifest.SnapshotOverrides);
        DllNameOverrides = new(manifest.DllNameOverrides?
            .Select(kv => new DllOverrideItem(kv.Key, kv.Value)) ?? Enumerable.Empty<DllOverrideItem>());
        GacSymlinkGames = ToKvObservable(manifest.GacSymlinkGames);
        LaunchExeOverrides = ToKvObservable(manifest.LaunchExeOverrides);
        LegacyReShadeVersions = ToKvObservable(manifest.LegacyReShadeVersions);
        LegacyReShadeAvailable = ToObservable(manifest.LegacyReShadeAvailable);
        RenodxIniOverrides = new(manifest.RenodxIniOverrides?
            .Where(kv => !kv.Key.StartsWith("_"))
            .Select(kv => new RenodxIniGameItem(kv.Key, kv.Value, main)) ?? Enumerable.Empty<RenodxIniGameItem>());
        RenodxIniOverrides.CollectionChanged += (_, _) => Dirty();
        RenodxExtraSettings = new(manifest.RenodxExtraSettings?
            .Select(s => new RenodxExtraSettingItem(s, main)) ?? Enumerable.Empty<RenodxExtraSettingItem>());
        RenodxExtraSettings.CollectionChanged += (_, _) => Dirty();
    }

    [RelayCommand] public void AddInstallWarning() { InstallWarnings.Add(new InstallWarningItem()); Dirty(); }
    [RelayCommand] public void RemoveInstallWarning(InstallWarningItem item) { InstallWarnings.Remove(item); Dirty(); }
    [RelayCommand] public void AddSnapshotOverride() { SnapshotOverrides.Add(new KeyValueItem()); Dirty(); }
    [RelayCommand] public void RemoveSnapshotOverride(KeyValueItem item) { SnapshotOverrides.Remove(item); Dirty(); }
    [RelayCommand] public void AddDllOverride() { DllNameOverrides.Add(new DllOverrideItem()); Dirty(); }
    [RelayCommand] public void RemoveDllOverride(DllOverrideItem item) { DllNameOverrides.Remove(item); Dirty(); }
    [RelayCommand] public void AddLaunchExeOverride() { LaunchExeOverrides.Add(new KeyValueItem()); Dirty(); }
    [RelayCommand] public void RemoveLaunchExeOverride(KeyValueItem item) { LaunchExeOverrides.Remove(item); Dirty(); }
    [RelayCommand] public void AddLegacyVersion() { LegacyReShadeVersions.Add(new KeyValueItem()); Dirty(); }
    [RelayCommand] public void RemoveLegacyVersion(KeyValueItem item) { LegacyReShadeVersions.Remove(item); Dirty(); }
    [RelayCommand] public void AddLegacyAvailable() { LegacyReShadeAvailable.Add(new StringItem("")); Dirty(); }
    [RelayCommand] public void RemoveLegacyAvailable(StringItem item) { LegacyReShadeAvailable.Remove(item); Dirty(); }
    [RelayCommand] public void AddForceExternal() { ForceExternalOnly.Add(new ForceExternalItem()); Dirty(); }
    [RelayCommand] public void RemoveForceExternal(ForceExternalItem item) { ForceExternalOnly.Remove(item); Dirty(); }
    [RelayCommand] public void AddGacSymlink() { GacSymlinkGames.Add(new KeyValueItem()); Dirty(); }
    [RelayCommand] public void RemoveGacSymlink(KeyValueItem item) { GacSymlinkGames.Remove(item); Dirty(); }
    [RelayCommand] public void AddRenodxIniGame() { RenodxIniOverrides.Add(new RenodxIniGameItem(_main)); Dirty(); }
    [RelayCommand] public void RemoveRenodxIniGame(RenodxIniGameItem item) { RenodxIniOverrides.Remove(item); Dirty(); }
    [RelayCommand] public void AddRenodxExtra() { RenodxExtraSettings.Add(new RenodxExtraSettingItem(_main)); Dirty(); }
    [RelayCommand] public void RemoveRenodxExtra(RenodxExtraSettingItem item) { RenodxExtraSettings.Remove(item); Dirty(); }

    public override void Commit()
    {
        _manifest.InstallWarnings = InstallWarnings.Count > 0
            ? InstallWarnings.Where(i => !string.IsNullOrWhiteSpace(i.GameName))
                .ToDictionary(i => i.GameName, i => i.ToDict())
            : null;
        _manifest.ForceExternalOnly = ForceExternalOnly.Count > 0
            ? ForceExternalOnly
                .Where(i => !string.IsNullOrWhiteSpace(i.Key))
                .ToDictionary(i => i.Key, i => new ForceExternalEntry { Url = i.Url, Label = string.IsNullOrWhiteSpace(i.Label) ? null : i.Label })
            : null;
        _manifest.SnapshotOverrides = FromKvObservable(SnapshotOverrides);
        _manifest.DllNameOverrides = DllNameOverrides.Count > 0
            ? DllNameOverrides.Where(i => !string.IsNullOrWhiteSpace(i.GameName))
                .ToDictionary(i => i.GameName, i => new ManifestDllNames { ReShade = i.Reshade, Dc = i.Dc })
            : null;
        _manifest.GacSymlinkGames = FromKvObservable(GacSymlinkGames);
        _manifest.LaunchExeOverrides = FromKvObservable(LaunchExeOverrides);
        _manifest.LegacyReShadeVersions = FromKvObservable(LegacyReShadeVersions);
        _manifest.LegacyReShadeAvailable = FromObservable(LegacyReShadeAvailable).Count > 0 ? FromObservable(LegacyReShadeAvailable) : null;
        _manifest.RenodxIniOverrides = RenodxIniOverrides.Count > 0
            ? RenodxIniOverrides
                .Where(g => !string.IsNullOrWhiteSpace(g.GameName))
                .ToDictionary(g => g.GameName, g => g.Entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                    .ToDictionary(e => e.Key, e => e.Value))
            : null;
        _manifest.RenodxExtraSettings = RenodxExtraSettings.Count > 0
            ? RenodxExtraSettings.Where(i => !string.IsNullOrWhiteSpace(i.Key)).Select(i => i.ToEntry()).ToList()
            : null;
    }
}
