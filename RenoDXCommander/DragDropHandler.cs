// DragDropHandler.cs — Primary file: class declaration, constructor, extension validation, drag-over/drop routing, and URL/shortcut helpers.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Service class responsible for drag-and-drop processing logic.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class DragDropHandler
{
    private readonly MainWindow _window;
    private readonly ICrashReporter _crashReporter;
    private readonly IModInstallService _modInstallService;
    private readonly IGameDetectionService _gameDetectionService;
    private readonly ILumaService _lumaService;
    private readonly IGameNameService _gameNameService;

    public DragDropHandler(MainWindow window, ICrashReporter crashReporter)
    {
        _window = window;
        _crashReporter = crashReporter;
        _modInstallService = App.Services.GetRequiredService<IModInstallService>();
        _gameDetectionService = App.Services.GetRequiredService<IGameDetectionService>();
        _lumaService = App.Services.GetRequiredService<ILumaService>();
        _gameNameService = App.Services.GetRequiredService<IGameNameService>();
    }

    private MainViewModel ViewModel => _window.ViewModel;

    /// <summary>
    /// The complete set of file extensions that DragDropHandler will process.
    /// Files with extensions not in this set are silently skipped with a log entry.
    /// </summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".addon64", ".addon32", ".ini",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz",
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tar.gz", ".tgz", ".tar.bz2", ".tar.xz",
    };

    /// <summary>
    /// Returns true if the given file path has an extension in <see cref="AllowedExtensions"/>.
    /// Handles null, empty, and paths with Unicode or special characters gracefully.
    /// </summary>
    public static bool IsAllowedExtension(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext))
                return false;

            return AllowedExtensions.Contains(ext);
        }
        catch (ArgumentException)
        {
            // Path.GetExtension can throw on paths with invalid characters
            return false;
        }
    }

    public void Grid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to add game, install addon, install preset, or extract archive";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop URL to download addon";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    public async void Grid_Drop(object sender, DragEventArgs e)
    {
        // ── Path 1: StorageItems (files) ──────────────────────────────────────
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is not Windows.Storage.StorageFile file) continue;

                var ext = file.FileType?.ToLowerInvariant() ?? "";

                // .url shortcut files — parse the URL inside and route to ProcessDroppedUrl
                if (ext == ".url")
                {
                    try
                    {
                        var url = ParseUrlFromShortcutFile(file.Path);
                        if (!string.IsNullOrEmpty(url))
                        {
                            _crashReporter.Log($"[DragDropHandler.Grid_Drop] Parsed URL from .url file '{file.Name}': {url}");
                            await ProcessDroppedUrl(url);
                        }
                        else
                        {
                            _crashReporter.Log($"[DragDropHandler.Grid_Drop] No URL in .url file content for '{file.Name}' — trying Text data format");
                            // Discord often provides the URL as Text alongside the .url StorageFile
                            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                            {
                                var text = await e.DataView.GetTextAsync();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    var textUrl = text.Trim();
                                    if (Uri.TryCreate(textUrl, UriKind.Absolute, out var uri)
                                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                                    {
                                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] Got URL from Text data: {textUrl}");
                                        await ProcessDroppedUrl(textUrl);
                                        continue;
                                    }
                                }
                            }

                            // Last resort: if filename is like "renodx-game.addon64.url",
                            // try reading the .url file as a WebUri
                            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.WebLink))
                            {
                                try
                                {
                                    var webUri = await e.DataView.GetWebLinkAsync();
                                    if (webUri != null)
                                    {
                                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] Got URL from WebLink data: {webUri.AbsoluteUri}");
                                        await ProcessDroppedUrl(webUri.AbsoluteUri);
                                        continue;
                                    }
                                }
                                catch { }
                            }

                            _crashReporter.Log($"[DragDropHandler.Grid_Drop] Could not extract URL for .url file '{file.Name}' — skipping");
                        }
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] Error processing .url file '{file.Name}' — {ex.Message}");
                    }
                    continue;
                }

                // Early validation: skip files with disallowed extensions
                if (!IsAllowedExtension(file.Path))
                {
                    _crashReporter.Log($"[DragDropHandler.Grid_Drop] Skipping file with disallowed extension '{ext}' — '{file.Name}'");
                    continue;
                }

                // Handle .ini files — install ReShade preset
                if (ext == ".ini")
                {
                    try
                    {
                        await ProcessDroppedPreset(file.Path);
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] DragDrop preset error processing '{file.Path}' — {ex.Message}");
                    }
                    continue;
                }

                // Handle .addon64 / .addon32 files — install RenoDX addon to a game
                if (ext is ".addon64" or ".addon32"
                    && Path.GetFileName(file.Path).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await ProcessDroppedAddon(file.Path);
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] DragDrop addon error processing '{file.Path}' — {ex.Message}");
                    }
                    continue;
                }

                // Handle archive files — check for Luma mod first, then extract for addons
                if (ArchiveExtensions.Contains(ext))
                {
                    try
                    {
                        // Check if this is a Luma mod archive
                        if ((ext == ".zip" || ext == ".7z") && IsLumaArchive(file.Path))
                        {
                            // Show game picker (same as file watcher path)
                            var lumaGames = _window.ViewModel.AllCards
                                .Where(c => c.LumaFeatureEnabled && !string.IsNullOrEmpty(c.InstallPath))
                                .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (lumaGames.Count > 0)
                            {
                                var gameNames = lumaGames.Select(c => c.GameName).ToList();
                                var selectedGame = _window.ViewModel.SelectedGame;
                                var preSelectIndex = selectedGame != null
                                    ? gameNames.IndexOf(selectedGame.GameName)
                                    : -1;

                                var combo = new Microsoft.UI.Xaml.Controls.ComboBox
                                {
                                    ItemsSource = gameNames,
                                    SelectedIndex = preSelectIndex >= 0 ? preSelectIndex : 0,
                                    FontSize = 12,
                                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                                };
                                var pickerDialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                                {
                                    Title = "Install Luma Mod",
                                    Content = new Microsoft.UI.Xaml.Controls.StackPanel
                                    {
                                        Spacing = 8,
                                        Children =
                                        {
                                            new Microsoft.UI.Xaml.Controls.TextBlock { Text = $"Luma mod detected: {Path.GetFileName(file.Path)}\n\nSelect game to install to:", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, FontSize = 12 },
                                            combo,
                                        }
                                    },
                                    PrimaryButtonText = "Install",
                                    CloseButtonText = "Cancel",
                                    XamlRoot = _window.Content.XamlRoot,
                                    RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                                };
                                var result = await DialogService.ShowSafeAsync(pickerDialog);
                                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                                {
                                    var selectedName = combo.SelectedItem as string;
                                    var card = lumaGames.FirstOrDefault(c => c.GameName == selectedName);
                                    if (card != null)
                                        await ProcessDroppedLumaArchiveAsync(file.Path, card);
                                }
                                continue;
                            }
                        }

                        await ProcessDroppedArchive(file.Path);
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] DragDrop archive error processing '{file.Path}' — {ex.Message}");
                    }
                    continue;
                }

                // Handle .exe files — add game
                if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                var exePath = file.Path;
                _crashReporter.Log($"[DragDropHandler.Grid_Drop] Received exe '{exePath}'");

                try
                {
                    await ProcessDroppedExe(exePath);
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[DragDropHandler.Grid_Drop] Error processing '{exePath}' — {ex.Message}");
                }
            }
            return;
        }

        // ── Path 2: Text/URI (URL dragged directly from browser/Discord) ──────
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            try
            {
                var text = await e.DataView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var url = text.Trim();
                    _crashReporter.Log($"[DragDropHandler.Grid_Drop] Received text drop: '{url}'");

                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        var filename = ExtractFileNameFromUrl(url);
                        if (filename != null)
                        {
                            var ext = Path.GetExtension(filename);
                            if (ext != null && (ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                                             || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)))
                            {
                                await ProcessDroppedUrl(url);
                                return;
                            }
                        }
                        _crashReporter.Log($"[DragDropHandler.Grid_Drop] URL does not point to an addon file — ignored");
                    }
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[DragDropHandler.Grid_Drop] Error processing text drop — {ex.Message}");
            }
        }
    }

    // ── .url shortcut file parsing ──────────────────────────────────────────────

    /// <summary>
    /// Parses a Windows .url shortcut file and extracts the URL from the
    /// [InternetShortcut] section. Returns null if the file cannot be read,
    /// has no [InternetShortcut] section, or has no URL= line with a value.
    /// </summary>
    public static string? ParseUrlFromShortcutFile(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);

            // ── Standard INI format: [InternetShortcut]\nURL=... ──────────────
            bool inSection = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Detect section headers
                if (trimmed.StartsWith('['))
                {
                    inSection = trimmed.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inSection && trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    var url = trimmed.Substring(4).Trim();
                    return string.IsNullOrEmpty(url) ? null : url;
                }
            }

            // ── Fallback: raw URL as file content (Discord/browser temp files) ─
            // Some apps write just the URL as the file content without INI headers.
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed)
                    && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    return trimmed;
                }
            }
        }
        catch (Exception)
        {
            // File read errors — fall through to filename-based extraction
        }

        // ── Last resort: extract URL info from the filename itself ─────────────
        // Discord names .url files like "renodx-crimsondesert.addon64.url" — the
        // filename minus ".url" is the addon filename, but we don't have the full
        // URL. Return null so the caller can handle this case.
        return null;
    }

    /// <summary>
    /// Extracts the filename from a URL path component, stripping query parameters
    /// and fragment identifiers. Returns null if the URL is not parseable or has no
    /// path filename.
    /// </summary>
    public static string? ExtractFileNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var path = uri.LocalPath;
        if (string.IsNullOrEmpty(path) || path == "/")
            return null;

        var filename = Path.GetFileName(path);
        if (string.IsNullOrEmpty(filename))
            return null;

        return Uri.UnescapeDataString(filename);
    }

    /// <summary>
    /// Returns true if the file starts with the PE "MZ" magic bytes,
    /// indicating it's a valid Windows executable/DLL rather than an HTML error page.
    /// </summary>
    private static bool HasPeSignature(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 2) return false;
            return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
        catch { return false; }
    }
}
