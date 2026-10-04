namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    public void RefreshLocalizedText()
    {
        StatusText = Loc.Relocalize(StatusText);
        SubStatusText = Loc.Relocalize(SubStatusText);
        BackgroundScanStatusText = Loc.Relocalize(BackgroundScanStatusText);
        foreach (var card in AllCards) card.RefreshLocalizedText();
        OnPropertyChanged(nameof(TotalGames));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(IsBackgroundScanning));
        OnPropertyChanged(nameof(UpdateButtonTooltip));
    }
}
