using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Core scaffolding for DialogService: constructor, shared fields, and helper methods.
/// Update/patch-notes dialogs live in DialogService.Update.cs;
/// game-specific dialogs live in DialogService.Game.cs.
/// </summary>
public partial class DialogService
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;

    private static readonly string PatchNotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI");

    public DialogService(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
    }

    private MainViewModel ViewModel => _window.ViewModel;

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    /// <summary>Parses a hex colour string like "#1C2848" into a Windows.UI.Color.</summary>
    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        int offset = 0;
        if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); offset = 2; }
        byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
        byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static GameCardViewModel? GetCardFromSender(object sender) => sender switch
    {
        Button btn          when btn.Tag  is GameCardViewModel c => c,
        MenuFlyoutItem item when item.Tag is GameCardViewModel c => c,
        _ => null
    };
}
