namespace RenoDXCommander.Services;

/// <summary>
/// The per-game NVIDIA driver-profile operations that "Quick Apply" and "One-Click Optimize" use.
/// <see cref="DlssPresetService"/> is the real implementation (see <see cref="DlssPresetProfileAdapter"/>);
/// tests use an in-memory fake. Reads return 0 / false when a setting is not set.
/// </summary>
public interface INvidiaGameProfile
{
    bool IsSupported { get; }

    uint GetSrPreset(string gameName, string installPath);
    uint GetRrPreset(string gameName, string installPath);
    uint GetFgPreset(string gameName, string installPath);
    uint GetNrPreset(string gameName, string installPath);
    uint GetSrRenderScale(string gameName, string installPath);
    uint GetRrRenderScale(string gameName, string installPath);
    bool IsSrDriverOverrideActive(string gameName, string installPath);
    bool IsRrDriverOverrideActive(string gameName, string installPath);
    bool IsFgDriverOverrideActive(string gameName, string installPath);

    bool SetSrPreset(string gameName, string installPath, uint preset);
    bool SetRrPreset(string gameName, string installPath, uint preset);
    bool SetFgPreset(string gameName, string installPath, uint preset);
    bool SetNrPreset(string gameName, string installPath, uint preset);
    bool SetSrRenderScale(string gameName, string installPath, uint percentage);
    bool SetRrRenderScale(string gameName, string installPath, uint percentage);
    bool SetSrDriverOverride(string gameName, string installPath, bool enable);
    bool SetRrDriverOverride(string gameName, string installPath, bool enable);
    bool SetFgDriverOverride(string gameName, string installPath, bool enable);
}

/// <summary>Thin adapter: forwards every call to the existing <see cref="DlssPresetService"/>.</summary>
public sealed class DlssPresetProfileAdapter : INvidiaGameProfile
{
    private readonly DlssPresetService _s;
    public DlssPresetProfileAdapter(DlssPresetService service) => _s = service;

    public bool IsSupported => _s.IsSupported;
    public uint GetSrPreset(string g, string p) => _s.GetSrPreset(g, p);
    public uint GetRrPreset(string g, string p) => _s.GetRrPreset(g, p);
    public uint GetFgPreset(string g, string p) => _s.GetFgPreset(g, p);
    public uint GetNrPreset(string g, string p) => _s.GetNrPreset(g, p);
    public uint GetSrRenderScale(string g, string p) => _s.GetSrRenderScale(g, p);
    public uint GetRrRenderScale(string g, string p) => _s.GetRrRenderScale(g, p);
    public bool IsSrDriverOverrideActive(string g, string p) => _s.IsSrDriverOverrideActive(g, p);
    public bool IsRrDriverOverrideActive(string g, string p) => _s.IsRrDriverOverrideActive(g, p);
    public bool IsFgDriverOverrideActive(string g, string p) => _s.IsFgDriverOverrideActive(g, p);
    public bool SetSrPreset(string g, string p, uint v) => _s.SetSrPreset(g, p, v);
    public bool SetRrPreset(string g, string p, uint v) => _s.SetRrPreset(g, p, v);
    public bool SetFgPreset(string g, string p, uint v) => _s.SetFgPreset(g, p, v);
    public bool SetNrPreset(string g, string p, uint v) => _s.SetNrPreset(g, p, v);
    public bool SetSrRenderScale(string g, string p, uint v) => _s.SetSrRenderScale(g, p, v);
    public bool SetRrRenderScale(string g, string p, uint v) => _s.SetRrRenderScale(g, p, v);
    public bool SetSrDriverOverride(string g, string p, bool e) => _s.SetSrDriverOverride(g, p, e);
    public bool SetRrDriverOverride(string g, string p, bool e) => _s.SetRrDriverOverride(g, p, e);
    public bool SetFgDriverOverride(string g, string p, bool e) => _s.SetFgDriverOverride(g, p, e);
}
