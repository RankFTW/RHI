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
    /// <summary>
    /// Returns the index of the best fuzzy-matched game for a given filename,
    /// falling back to the currently selected game, then 0.
    /// Used to pre-select the correct game in Luma install pickers.
    /// </summary>
    private int FuzzyMatchGameIndex(List<string> gameNames, string archiveFileName)
    {
        var lower = Path.GetFileNameWithoutExtension(archiveFileName).ToLowerInvariant();
        var idx = gameNames.FindIndex(name =>
            lower.Contains(name.ToLowerInvariant()
                .Replace(":", "").Replace("™", "").Replace("®", "").Replace("'", "")));
        if (idx >= 0) return idx;
        var selected = _window.ViewModel.SelectedGame?.GameName;
        if (selected != null)
        {
            idx = gameNames.IndexOf(selected);
            if (idx >= 0) return idx;
        }
        return 0;
    }

    public static bool IsLumaArchive(string archivePath)
    {
        try
        {
            // Fast path: filename contains "Luma" — strong signal it's a Luma release
            var fileName = Path.GetFileNameWithoutExtension(archivePath);
            if (fileName.Contains("Luma", StringComparison.OrdinalIgnoreCase)
                && !fileName.Contains("addon", StringComparison.OrdinalIgnoreCase))
                return true;

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
            var selectedPacks = _window.ViewModel.ResolveShaderSelection(gameName, card.ShaderModeOverride, card.Source ?? "");
            var screenshotPath = _window.ViewModel.BuildScreenshotSavePath(card.GameName);
            var overlayHotkey = _window.ViewModel.Settings.OverlayHotkey;
            var screenshotHotkey = _window.ViewModel.Settings.ScreenshotHotkey;

            // Folder picker callback for archives with multiple game folders
            async Task<string?> FolderPicker(List<string> folders)
            {
                var tcs = new TaskCompletionSource<string?>();
                // Capture combo reference for result extraction
                Microsoft.UI.Xaml.Controls.ComboBox? combo = null;
                Microsoft.UI.Xaml.Controls.ContentDialog? dialog = null;

                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        combo = new Microsoft.UI.Xaml.Controls.ComboBox
                        {
                            ItemsSource = folders,
                            SelectedIndex = 0,
                            FontSize = 12,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                        };
                        dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                        {
                            Title = "Выбрать папку",
                            Content = new Microsoft.UI.Xaml.Controls.StackPanel
                            {
                                Spacing = 8,
                                Children =
                                {
                                    new Microsoft.UI.Xaml.Controls.TextBlock
                                    {
                                        Text = "Этот архив содержит несколько папок игр.\nВыберите папку для установки:",
                                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                                        FontSize = 12,
                                    },
                                    combo,
                                }
                            },
                            PrimaryButtonText = "Установить",
                            CloseButtonText = "Отмена",
                            XamlRoot = _window.Content.XamlRoot,
                            RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                        };

                        // Show dialog and handle result in continuation on UI thread
                        _ = ShowFolderPickerDialogAsync(dialog, combo, tcs);
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[DragDropHandler.FolderPicker] Dialog setup error — {ex.Message}");
                        tcs.TrySetResult(null);
                    }
                });
                return await tcs.Task;
            }

            // Helper to show the folder picker dialog — runs entirely on UI thread
            async Task ShowFolderPickerDialogAsync(
                Microsoft.UI.Xaml.Controls.ContentDialog dialog,
                Microsoft.UI.Xaml.Controls.ComboBox combo,
                TaskCompletionSource<string?> tcs)
            {
                try
                {
                    var dialogResult = await DialogService.ShowSafeAsync(dialog);
                    // We're still on UI thread after await — safe to access combo.SelectedItem
                    tcs.TrySetResult(dialogResult == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary
                        ? combo.SelectedItem as string : null);
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[DragDropHandler.FolderPicker] Dialog error — {ex.Message}");
                    tcs.TrySetResult(null);
                }
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
                FolderPicker,
                card.Source);

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

            // Ensure the card has a LumaMod so the Luma row becomes visible.
            // For bespoke drag-dropped mods (not on the wiki), synthesize a minimal entry.
            if (card.LumaMod == null)
            {
                card.LumaMod = new Models.LumaMod
                {
                    Name = gameName,
                    IsGenericLuma = false,
                    Status = "✅",
                };
                card.LumaRenodxCompatible = true;
            }

            // Apply the same post-install steps as InstallLumaAsync:
            // DLSS deploy, [Luma] reshade.ini writes, ReShade install, dgVoodoo2, Engine.ini keys, launch args
            await _window.ViewModel.ApplyLumaPostInstallAsync(card, record);

            card.NotifyAll();
            _window.ViewModel.SaveSettingsPublic();

            // Rebuild the detail panel so the Luma row appears immediately
            _window.DispatcherQueue?.TryEnqueue(() => _window.PopulateDetailPanel(card));

            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaArchive] Luma install complete for '{gameName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaArchive] Failed for '{gameName}' — {ex.Message}");
            CrashReporter.WriteCrashReport("DragDropHandler.ProcessDroppedLumaArchive", ex, note: $"Game: {gameName}");
        }
    }

    /// <summary>
    /// Handles a dropped bare Luma addon file (.addon / .addon64 / .addon32 with "Luma" in the name).
    /// Copies the file to the game folder and sets up tracking as a Luma install.
    /// </summary>
    public async Task ProcessDroppedLumaAddonAsync(string addonPath, GameCardViewModel card)
    {
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        var gameName = card.GameName;
        var addonFileName = Path.GetFileName(addonPath);
        _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaAddon] Installing Luma addon '{addonFileName}' to '{gameName}'");

        try
        {
            // Copy the addon file to the game folder
            var destPath = Path.Combine(card.InstallPath, addonFileName);
            File.Copy(addonPath, destPath, overwrite: true);
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaAddon] Copied '{addonFileName}' to '{card.InstallPath}'");

            // Build a LumaInstalledRecord tracking the deployed file
            var record = new Models.LumaInstalledRecord
            {
                GameName       = gameName,
                InstallPath    = card.InstallPath,
                Store          = card.Source ?? "",
                InstalledFiles = new List<string> { addonFileName },
                InstalledAt    = DateTime.UtcNow,
            };
            _lumaService.SaveLumaRecord(record);

            // Ensure card has a LumaMod so the Luma row becomes visible
            if (card.LumaMod == null)
            {
                card.LumaMod = new Models.LumaMod
                {
                    Name = gameName,
                    IsGenericLuma = false,
                    Status = "✅",
                };
                card.LumaRenodxCompatible = true;
            }

            card.LumaRecord = record;
            card.LumaStatus = GameStatus.Installed;

            // Run the same post-install steps as a full Luma install
            await _window.ViewModel.ApplyLumaPostInstallAsync(card, record);

            card.NotifyAll();
            _window.ViewModel.SaveSettingsPublic();

            _window.DispatcherQueue?.TryEnqueue(() => _window.PopulateDetailPanel(card));
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaAddon] Luma addon install complete for '{gameName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedLumaAddon] Failed for '{gameName}' — {ex.Message}");
            CrashReporter.WriteCrashReport("DragDropHandler.ProcessDroppedLumaAddon", ex, note: $"Game: {gameName}");
        }
    }
}

