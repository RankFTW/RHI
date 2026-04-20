using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages nxm:// protocol registration and URI parsing.
/// </summary>
public interface INxmProtocolHandler
{
    /// <summary>Registers RHI as the nxm:// protocol handler in the Windows registry.</summary>
    bool Register();

    /// <summary>Removes the nxm:// protocol handler registration.</summary>
    bool Unregister();

    /// <summary>Returns true if RHI is currently registered as the nxm:// handler.</summary>
    bool IsRegistered();

    /// <summary>Parses an nxm:// URI string into a structured NxmUri object.</summary>
    NxmUri? Parse(string uri);

    /// <summary>Formats an NxmUri back into a URI string.</summary>
    string Format(NxmUri nxmUri);
}
