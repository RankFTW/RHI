// OverlayBackdrop.cs -- the RHI overlay's see-through backdrop: Windows' thin acrylic (a light blur of the game)
// with a faint dark tint, instead of the default acrylic, which hid the game almost completely.
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander.Overlay;

internal sealed class OverlayBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintColor = Windows.UI.Color.FromArgb(0xFF, 0x0B, 0x0F, 0x17),
            TintOpacity = 0.15f,
            LuminosityOpacity = 0.2f,
            FallbackColor = Windows.UI.Color.FromArgb(0xB0, 0x0B, 0x0F, 0x17),
        };
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, xamlRoot));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        if (_controller == null) return;
        _controller.RemoveSystemBackdropTarget(target);
        _controller.Dispose();
        _controller = null;
    }
}
