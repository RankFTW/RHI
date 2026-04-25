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

    // ── Safe dialog guard ────────────────────────────────────────────────────────
    // WinUI3 only allows one ContentDialog open at a time. A second ShowAsync()
    // throws a COMException, and if that's in an async-void handler the exception
    // goes unobserved, leaving an invisible modal overlay that blocks all input.
    // This guard serialises all dialog opens so that can never happen.

    private static readonly SemaphoreSlim _dialogGate = new(1, 1);

    /// <summary>
    /// Shows a <see cref="ContentDialog"/> safely. If another dialog is already
    /// open, the call is skipped and <see cref="ContentDialogResult.None"/> is
    /// returned (treated as "cancelled" by callers).
    /// Every <c>ContentDialog.ShowAsync()</c> in the app should go through this.
    /// </summary>
    public static async Task<ContentDialogResult> ShowSafeAsync(ContentDialog dialog)
    {
        // Wait up to 10 seconds for any existing dialog to close before giving up.
        // This prevents drag-and-drop and other user-initiated dialogs from being
        // silently swallowed when a background dialog (e.g. update check) is open.
        if (!await _dialogGate.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            CrashReporter.Log("[DialogService.ShowSafeAsync] Skipped — another dialog is still open after 10s");
            return ContentDialogResult.None;
        }
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowSafeAsync] Dialog failed — {ex.Message}");
            return ContentDialogResult.None;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    /// <summary>
    /// Acquires the dialog gate for non-blocking dialog patterns (progress dialogs
    /// that are shown with ShowAsync() but closed programmatically via Hide()).
    /// Must be paired with <see cref="ReleaseDialogGate"/>.
    /// </summary>
    public static bool TryAcquireDialogGate()
    {
        return _dialogGate.Wait(0);
    }

    /// <summary>
    /// Releases the dialog gate after a non-blocking dialog is closed.
    /// </summary>
    public static void ReleaseDialogGate()
    {
        try { _dialogGate.Release(); }
        catch (SemaphoreFullException) { }
    }
}
