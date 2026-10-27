using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RHI.ManifestEditor.Models;

namespace RHI.ManifestEditor.ViewModels;

public partial class UeExtendedCompatItem : ObservableObject
{
    [ObservableProperty] private string _gameName;
    [ObservableProperty] private bool? _hdr;
    [ObservableProperty] private bool? _lut;

    public UeExtendedCompatItem(string name, UeExtendedCompatEntry e)
    {
        _gameName = name; _hdr = e.Hdr; _lut = e.Lut;
    }
    public UeExtendedCompatItem() { _gameName = ""; }
}

public partial class UeHdrSectionViewModel : SectionViewModelBase
{
    public ObservableCollection<StringItem> NativeHdrGames { get; }
    public ObservableCollection<UeExtendedCompatItem> UeExtendedCompatibility { get; }
    public ObservableCollection<StringItem> UeExtendedGames { get; }
    public ObservableCollection<StringItem> NoUeExtendedGames { get; }
    public ObservableCollection<StringItem> LumaRenodxCompat { get; }

    public UeHdrSectionViewModel(RemoteManifest manifest, MainViewModel main) : base(manifest, main)
    {
        NativeHdrGames = ToObservable(manifest.NativeHdrGames);
        UeExtendedCompatibility = new(manifest.UeExtendedCompatibility?
            .Select(kv => new UeExtendedCompatItem(kv.Key, kv.Value)) ?? Enumerable.Empty<UeExtendedCompatItem>());
        UeExtendedGames = ToObservable(manifest.UeExtendedGames);
        NoUeExtendedGames = ToObservable(manifest.NoUeExtendedGames);
        LumaRenodxCompat = ToObservable(manifest.LumaRenodxCompat);
    }

    [RelayCommand] public void AddNativeHdr() { NativeHdrGames.Add(new StringItem("")); Dirty(); }
    [RelayCommand] public void RemoveNativeHdr(StringItem item) { NativeHdrGames.Remove(item); Dirty(); }
    [RelayCommand] public void AddUeCompat() { UeExtendedCompatibility.Add(new UeExtendedCompatItem()); Dirty(); }
    [RelayCommand] public void RemoveUeCompat(UeExtendedCompatItem item) { UeExtendedCompatibility.Remove(item); Dirty(); }
    [RelayCommand] public void AddUeExtended() { UeExtendedGames.Add(new StringItem("")); Dirty(); }
    [RelayCommand] public void RemoveUeExtended(StringItem item) { UeExtendedGames.Remove(item); Dirty(); }
    [RelayCommand] public void AddNoUeExtended() { NoUeExtendedGames.Add(new StringItem("")); Dirty(); }
    [RelayCommand] public void RemoveNoUeExtended(StringItem item) { NoUeExtendedGames.Remove(item); Dirty(); }
    [RelayCommand] public void AddLumaCompat() { LumaRenodxCompat.Add(new StringItem("")); Dirty(); }
    [RelayCommand] public void RemoveLumaCompat(StringItem item) { LumaRenodxCompat.Remove(item); Dirty(); }
    [RelayCommand] public void SortNativeHdr() { var s = NativeHdrGames.OrderBy(i => i.Value).ToList(); NativeHdrGames.Clear(); foreach (var i in s) NativeHdrGames.Add(i); Dirty(); }

    public override void Commit()
    {
        _manifest.NativeHdrGames = FromObservable(NativeHdrGames).Count > 0 ? FromObservable(NativeHdrGames) : null;
        _manifest.UeExtendedCompatibility = UeExtendedCompatibility.Count > 0
            ? UeExtendedCompatibility.Where(i => !string.IsNullOrWhiteSpace(i.GameName))
                .ToDictionary(i => i.GameName, i => new UeExtendedCompatEntry { Hdr = i.Hdr, Lut = i.Lut })
            : null;
        _manifest.UeExtendedGames = FromObservable(UeExtendedGames).Count > 0 ? FromObservable(UeExtendedGames) : null;
        _manifest.NoUeExtendedGames = FromObservable(NoUeExtendedGames).Count > 0 ? FromObservable(NoUeExtendedGames) : null;
        _manifest.LumaRenodxCompat = FromObservable(LumaRenodxCompat).Count > 0 ? FromObservable(LumaRenodxCompat) : null;
    }
}
