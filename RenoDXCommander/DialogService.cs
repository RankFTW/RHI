using Microsoft.Extensions.DependencyInjection;
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
    private readonly IUpdateService _updateService;
    private readonly IOptiScalerWikiService _optiScalerWikiService;
    private readonly IHdrDatabaseService _hdrDatabaseService;

    private static readonly string PatchNotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI");

    public DialogService(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
        _updateService = App.Services.GetRequiredService<IUpdateService>();
        _optiScalerWikiService = App.Services.GetRequiredService<IOptiScalerWikiService>();
        _hdrDatabaseService = App.Services.GetRequiredService<IHdrDatabaseService>();
    }

    private MainViewModel ViewModel => _window.ViewModel;

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

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
    // can leave callers with inconsistent modal state. Ownership includes the
    // closing animation, not just the calls to ShowAsync and Hide.

    private static readonly DialogCoordinator _dialogs = new();

    /// <summary>
    /// Shows a <see cref="ContentDialog"/> safely. Waits up to ten seconds for
    /// another dialog to finish, then returns <see cref="ContentDialogResult.None"/>
    /// on timeout or shutdown (treated as "cancelled" by callers).
    /// Every <c>ContentDialog.ShowAsync()</c> in the app should go through this.
    /// </summary>
    public static async Task<ContentDialogResult> ShowSafeAsync(ContentDialog dialog)
    {
        try
        {
            Task<ContentDialogResult>? result = null;
            await using var session = await _dialogs.OpenAsync(
                () => result = dialog.ShowAsync().AsTask(), dialog.Hide, TimeSpan.FromSeconds(10));
            if (session == null) return ContentDialogResult.None;
            await session.Completion;
            return await result!;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowSafeAsync] Dialog failed — {ex.Message}");
            return ContentDialogResult.None;
        }
    }

    /// <summary>
    /// Opens a progress dialog. Dispose the returned session with await using;
    /// disposal hides the dialog and waits for WinUI to remove its modal overlay.
    /// </summary>
    internal static async Task<DialogSession?> ShowProgressAsync(ContentDialog dialog, int timeoutSeconds = 15)
    {
        try
        {
            return await _dialogs.OpenAsync(() => dialog.ShowAsync().AsTask(), dialog.Hide,
                TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DialogService.ShowProgressAsync] Dialog failed — {ex.Message}");
            return null;
        }
    }

    public static bool IsDialogOpen => _dialogs.IsOpen;
    internal static void Stop() => _dialogs.Stop();
}
