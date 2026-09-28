using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Driver profile settings, applied through dxvk-nvapi launch options.
// Mirrors upstream DetailPanelBuilder.Overrides.DriverSettings.cs.
public sealed partial class MainWindow
{
    private async Task DriverSettingChanged(Game game, string message)
    {
        _settings.Save(); await ReadStates(); Filter(); _status.Text = message + " · apply the launch settings to use it";
    }
}
