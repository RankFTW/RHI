// DragDropHandler.Luma.cs — Handles drag-and-drop and file watcher detection of Luma mod archives.
using System.IO.Compression;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DragDropHandler
{
    /// <summary>
    /// Checks if an archive file is a Luma mod by looking for the Luma/d3dcompiler_47*.dll marker.
    /// Supports both zip and 7z formats.
    /// </summary>
    public static bool IsLumaArchive(string archivePath)
    {
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(archivePath);
                return zip.Entries.Any(e =>
                    e.Name.Equals("d3dcompiler_47.dll", StringComparison.OrdinalIgnoreCase)
                    || e.Name.Equals("d3dcompiler_47_x32.dll", StringComparison.OrdinalIgnoreCase));
            }

            if (archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                // Use 7z.exe to list contents and check for the marker
                var sevenZipExe = Path.Combine(AppContext.BaseDirectory, "7z.exe");
                if (!File.Exists(sevenZipExe)) return false;

                var psi = new System.Diagnostics.ProcessStartInfo(sevenZipExe, $"l \"{archivePath}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return false;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return output.Contains("d3dcompiler_47.dll", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("d3dcompiler_47_x32.dll", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DragDropHandler.IsLumaArchive] Error checking '{archivePath}' — {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Determines if the Luma archive is for a 32-bit game (contains d3dcompiler_47_x32.dll).
    /// </summary>
    public static bool IsLumaArchive32Bit(string archivePath)
    {
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(archivePath);
                return zip.Entries.Any(e =>
                    e.Name.Equals("d3dcompiler_47_x32.dll", StringComparison.OrdinalIgnoreCase));
            }

            if (archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                var sevenZipExe = Path.Combine(AppContext.BaseDirectory, "7z.exe");
                if (!File.Exists(sevenZipExe)) return false;

                var psi = new System.Diagnostics.ProcessStartInfo(sevenZipExe, $"l \"{archivePath}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return false;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return output.Contains("d3dcompiler_47_x32.dll", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Handles a dropped Luma archive — installs it to the specified game.
    /// </summary>
    public async Task ProcessDroppedLumaArchiveAsync(string archivePath, GameCardViewModel card)
    {
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        var gameName = card.GameName;
        _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaArchive] Installing Luma from '{Path.GetFileName(archivePath)}' to '{gameName}'");

        try
        {
            var is32Bit = IsLumaArchive32Bit(archivePath);
            var selectedPacks = _window.ViewModel.ResolveShaderSelection(gameName, card.ShaderModeOverride);
            var screenshotPath = _window.ViewModel.BuildScreenshotSavePath(card.GameName);
            var overlayHotkey = _window.ViewModel.Settings.OverlayHotkey;
            var screenshotHotkey = _window.ViewModel.Settings.ScreenshotHotkey;

            // Folder picker callback for archives with multiple game folders
            async Task<string?> FolderPicker(List<string> folders)
            {
                var tcs = new TaskCompletionSource<string?>();
                _window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var combo = new Microsoft.UI.Xaml.Controls.ComboBox
                        {
                            ItemsSource = folders,
                            SelectedIndex = 0,
                            FontSize = 12,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                        };
                        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                        {
                            Title = "Select Folder",
                            Content = new Microsoft.UI.Xaml.Controls.StackPanel
                            {
                                Spacing = 8,
                                Children =
                                {
                                    new Microsoft.UI.Xaml.Controls.TextBlock
                                    {
                                        Text = "This archive contains multiple game folders.\nSelect the folder to install:",
                                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                                        FontSize = 12,
                                    },
                                    combo,
                                }
                            },
                            PrimaryButtonText = "Install",
                            CloseButtonText = "Cancel",
                            XamlRoot = _window.Content.XamlRoot,
                            RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                        };
                        var dialogResult = await DialogService.ShowSafeAsync(dialog);
                        tcs.SetResult(dialogResult == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary
                            ? combo.SelectedItem as string : null);
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[DragDropHandler.FolderPicker] Dialog error — {ex.Message}");
                        tcs.SetResult(null);
                    }
                });
                return await tcs.Task;
            }

            var record = await _lumaService.InstallFromArchiveAsync(
                archivePath,
                card.InstallPath,
                is32Bit,
                selectedPacks,
                screenshotPath,
                overlayHotkey,
                screenshotHotkey,
                gameName,
                FolderPicker);

            // If the record has no installed files, the user cancelled the folder picker
            if (record.InstalledFiles.Count == 0)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaArchive] Install cancelled for '{gameName}'");
                return;
            }

            // Enable Luma mode if not already
            if (!card.IsLumaMode)
            {
                card.IsLumaMode = true;
                _gameNameService.LumaEnabledGames.Add(gameName);
                _gameNameService.LumaDisabledGames.Remove(gameName);
            }

            card.LumaRecord = record;
            card.LumaStatus = GameStatus.Installed;
            if (card.RsStatus == GameStatus.NotInstalled || card.RsStatus == GameStatus.Available)
                card.RsStatus = GameStatus.Installed;
            card.NotifyAll();
            _window.ViewModel.SaveSettingsPublic();

            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaArchive] Luma install complete for '{gameName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaArchive] Failed for '{gameName}' — {ex.Message}");
            CrashReporter.WriteCrashReport("DragDropHandler.ProcessDroppedLumaArchive", ex, note: $"Game: {gameName}");
        }
    }
}
