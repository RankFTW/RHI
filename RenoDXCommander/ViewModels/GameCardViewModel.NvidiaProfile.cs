namespace RenoDXCommander.ViewModels;

// Cached NVIDIA driver profile values — populated after the first NVAPI background scan
// and used to render the NVIDIA Profile section immediately on subsequent game selections.
public partial class GameCardViewModel
{
    // ── DlssProfileData fields ─────────────────────────────────────────────────
    public bool CachedSrDriverOverride { get; set; }
    public bool CachedRrDriverOverride { get; set; }
    public bool CachedFgDriverOverride { get; set; }
    public bool CachedNrDriverOverride { get; set; }
}
