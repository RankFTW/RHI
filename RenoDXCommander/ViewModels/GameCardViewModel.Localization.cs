namespace RenoDXCommander.ViewModels;

public partial class GameCardViewModel
{
    public void RefreshLocalizedText()
    {
        ActionMessage = Loc.Relocalize(ActionMessage);
        RsActionMessage = Loc.Relocalize(RsActionMessage);
        UlActionMessage = Loc.Relocalize(UlActionMessage);
        DcActionMessage = Loc.Relocalize(DcActionMessage);
        RefActionMessage = Loc.Relocalize(RefActionMessage);
        LumaActionMessage = Loc.Relocalize(LumaActionMessage);
        DofFixActionMessage = Loc.Relocalize(DofFixActionMessage);
        DxvkActionMessage = Loc.Relocalize(DxvkActionMessage);
        OsActionMessage = Loc.Relocalize(OsActionMessage);
        NotifyAll();
    }
}
