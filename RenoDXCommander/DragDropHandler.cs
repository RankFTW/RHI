using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System.Text.RegularExpressions;

namespace RenoDXCommander;

/// <summary>
/// Service class responsible for drag-and-drop processing logic.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class DragDropHandler
{
    private readonly MainWindow _window;

    public DragDropHandler(MainWindow window)
    {
        _window = window;
    }

    private MainViewModel ViewModel => _window.ViewModel;

    /// <summary>
    /// The complete set of file extensions that DragDropHandler will process.
    /// Files with extensions not in this set are silently skipped with a log entry.
    /// </summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".addon64", ".addon32",
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
            e.DragUIOverride.Caption = "Drop to add game, install addon, or extract archive";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    public async void Grid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
        {
            if (item is not Windows.Storage.StorageFile file) continue;

            var ext = file.FileType?.ToLowerInvariant() ?? "";

            // Early validation: skip files with disallowed extensions
            if (!IsAllowedExtension(file.Path))
            {
                CrashReporter.Log($"[DragDropHandler.Grid_Drop] Skipping file with disallowed extension '{ext}' — '{file.Name}'");
                continue;
            }

            // Handle .addon64 / .addon32 files — install RenoDX addon to a game
            if (ext is ".addon64" or ".addon32")
            {
                try
                {
                    await ProcessDroppedAddon(file.Path);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DragDropHandler.Grid_Drop] DragDrop addon error processing '{file.Path}' — {ex.Message}");
                }
                continue;
            }

            // Handle archive files — extract and look for .addon64/.addon32 inside
            if (ArchiveExtensions.Contains(ext))
            {
                try
                {
                    await ProcessDroppedArchive(file.Path);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DragDropHandler.Grid_Drop] DragDrop archive error processing '{file.Path}' — {ex.Message}");
                }
                continue;
            }

            // Handle .exe files — add game
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var exePath = file.Path;
            CrashReporter.Log($"[DragDropHandler.Grid_Drop] Received exe '{exePath}'");

            try
            {
                await ProcessDroppedExe(exePath);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DragDropHandler.Grid_Drop] Error processing '{exePath}' — {ex.Message}");
            }
        }
    }

    public async Task ProcessDroppedExe(string exePath)
    {
        var exeDir  = Path.GetDirectoryName(exePath)!;
        var exeName = Path.GetFileNameWithoutExtension(exePath);

        // ── Determine the game root folder ────────────────────────────────────
        var gameRoot = InferGameRoot(exeDir);
        CrashReporter.Log($"[DragDropHandler.ProcessDroppedExe] Inferred game root '{gameRoot}' from exe dir '{exeDir}'");

        // ── Detect engine and correct install path ────────────────────────────
        var (installPath, engine) = ViewModel.GameDetectionServiceInstance.DetectEngineAndPath(gameRoot);

        // ── Infer game name ───────────────────────────────────────────────────
        var gameName = InferGameName(exePath, gameRoot, engine);
        CrashReporter.Log($"[DragDropHandler.ProcessDroppedExe] Inferred name '{gameName}', engine={engine}");

        // ── Check for duplicates (by install path or normalized name) ─────────
        var normName = ViewModel.GameDetectionServiceInstance.NormalizeName(gameName);
        var normInstall = installPath.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

        var existingCard = ViewModel.AllCards.FirstOrDefault(c =>
            ViewModel.GameDetectionServiceInstance.NormalizeName(c.GameName) == normName
            || (!string.IsNullOrEmpty(c.InstallPath)
                && c.InstallPath.TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(normInstall, StringComparison.OrdinalIgnoreCase)));

        if (existingCard != null)
        {
            var dupDialog = new ContentDialog
            {
                Title           = "Game Already Exists",
                Content         = $"\"{existingCard.GameName}\" is already in your library at:\n{existingCard.InstallPath}",
                CloseButtonText = "OK",
                XamlRoot        = _window.Content.XamlRoot,
                Background      = UIFactory.Brush(ResourceKeys.SurfaceToolbarBrush),
            };
            await dupDialog.ShowAsync();
            return;
        }

        // ── Confirm with user (allow name edit) ──────────────────────────────
        var nameBox = new TextBox { Text = gameName, Width = 380 };
        var engineLabel = engine switch
        {
            EngineType.Unreal       => "Unreal Engine",
            EngineType.UnrealLegacy => "Unreal Engine (Legacy)",
            EngineType.Unity        => "Unity",
            _                       => "Unknown"
        };

        var confirmPanel = new StackPanel { Spacing = 8 };
        confirmPanel.Children.Add(new TextBlock
        {
            Text = "Game name:", Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        confirmPanel.Children.Add(nameBox);
        confirmPanel.Children.Add(new TextBlock
        {
            Text = $"Engine: {engineLabel}\nInstall path: {installPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            FontSize     = 12, Margin = new Thickness(0, 6, 0, 0),
        });

        var confirmDialog = new ContentDialog
        {
            Title             = "➕ Add Dropped Game",
            Content           = confirmPanel,
            PrimaryButtonText = "Add Game",
            CloseButtonText   = "Cancel",
            XamlRoot          = _window.Content.XamlRoot,
            Background        = UIFactory.Brush(ResourceKeys.SurfaceToolbarBrush),
        };
        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var finalName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(finalName)) return;

        CrashReporter.Log($"[DragDropHandler.ProcessDroppedExe] Adding game '{finalName}' at '{installPath}'");
        var game = new DetectedGame
        {
            Name = finalName, InstallPath = gameRoot, Source = "Manual", IsManuallyAdded = true
        };
        ViewModel.AddManualGameCommand.Execute(game);
    }

    /// <summary>
    /// Handles a dropped archive file (.zip, .7z, .rar, etc.) — extracts it using 7-Zip,
    /// looks for .addon64/.addon32 files inside, and passes them to ProcessDroppedAddon.
    /// </summary>
    public async Task ProcessDroppedArchive(string archivePath)
    {
        var archiveName = Path.GetFileName(archivePath);
        CrashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Received '{archiveName}'");

        var sevenZipExe = App.Services.GetRequiredService<ISevenZipExtractor>().Find7ZipExe();
        if (sevenZipExe == null)
        {
            var errDialog = new ContentDialog
            {
                Title = "7-Zip Not Found",
                Content = "Cannot extract archive — 7-Zip was not found. Please reinstall RDXC.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
            };
            await errDialog.ShowAsync();
            return;
        }

        // Extract entire archive to a temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"rdxc_archive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"x \"{archivePath}\" -o\"{tempDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            CrashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Extracting with {psi.FileName} {psi.Arguments}");

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                CrashReporter.Log("[DragDropHandler.ProcessDroppedArchive] Failed to start 7z process");
                return;
            }

            // Read output asynchronously to prevent deadlock
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit(60_000); // 60 second timeout for large archives

            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
                CrashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] 7z stderr: {stderr}");

            if (proc.ExitCode != 0)
            {
                CrashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] 7z exit code {proc.ExitCode}");
                var failDialog = new ContentDialog
                {
                    Title = "Archive Extraction Failed",
                    Content = $"Failed to extract '{archiveName}'. The file may be corrupt or in an unsupported format.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                };
                await failDialog.ShowAsync();
                return;
            }

            // Search for .addon64 and .addon32 files in the extracted contents
            var addonFiles = Directory.GetFiles(tempDir, "*.addon64", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(tempDir, "*.addon32", SearchOption.AllDirectories))
                .ToList();

            if (addonFiles.Count == 0)
            {
                CrashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] No addon files found in '{archiveName}'");
                var noAddonDialog = new ContentDialog
                {
                    Title = "No Addon Found",
                    Content = $"No .addon64 or .addon32 files were found inside '{archiveName}'.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                };
                await noAddonDialog.ShowAsync();
                return;
            }

            CrashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Found {addonFiles.Count} addon file(s): [{string.Join(", ", addonFiles.Select(Path.GetFileName))}]");

            // If multiple addons found, let the user pick; otherwise use the single one
            string addonToInstall;
            if (addonFiles.Count == 1)
            {
                addonToInstall = addonFiles[0];
            }
            else
            {
                // Show a picker dialog for multiple addons
                var combo = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    PlaceholderText = "Select addon to install...",
                };
                foreach (var af in addonFiles)
                    combo.Items.Add(new ComboBoxItem { Content = Path.GetFileName(af), Tag = af });
                combo.SelectedIndex = 0;

                var pickDialog = new ContentDialog
                {
                    Title = $"Multiple Addons in '{archiveName}'",
                    Content = combo,
                    PrimaryButtonText = "Install",
                    CloseButtonText = "Cancel",
                    XamlRoot = _window.Content.XamlRoot,
                };
                if (await pickDialog.ShowAsync() != ContentDialogResult.Primary) return;
                addonToInstall = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? addonFiles[0];
            }

            // Pass the extracted addon to the existing install flow
            await ProcessDroppedAddon(addonToInstall);
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Handles a dropped .addon64/.addon32 file — prompts the user to pick a game
    /// and installs the addon to that game's folder after confirmation.
    /// </summary>
    public async Task ProcessDroppedAddon(string addonPath)
    {
        var addonFileName = Path.GetFileName(addonPath);
        CrashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Received '{addonFileName}'");

        // Build a list of all detected games to choose from
        var cards = ViewModel.AllCards?.ToList() ?? new();
        if (cards.Count == 0)
        {
            var noGamesDialog = new ContentDialog
            {
                Title = "No Games Available",
                Content = "No games are currently detected. Add a game first.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
            };
            await noGamesDialog.ShowAsync();
            return;
        }

        // Build a ComboBox for game selection
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Select a game...",
        };

        // Sort alphabetically and populate
        var sortedCards = cards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var card in sortedCards)
            combo.Items.Add(new ComboBoxItem { Content = card.GameName, Tag = card });

        // Try to auto-select a game by matching addon filename to game names
        var addonNameLower = Path.GetFileNameWithoutExtension(addonFileName).ToLowerInvariant();
        for (int i = 0; i < sortedCards.Count; i++)
        {
            // Check if the addon name contains a significant part of the game name
            string[] gameWords = sortedCards[i].GameName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (gameWords.Length >= 2)
            {
                bool matched = false;
                foreach (var w in gameWords)
                {
                    if (w.Length > 3 && addonNameLower.Contains(w.ToLowerInvariant()))
                    {
                        matched = true;
                        break;
                    }
                }
                if (matched)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Install {addonFileName} to a game folder.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        panel.Children.Add(combo);

        var pickDialog = new ContentDialog
        {
            Title = "📦 Install RenoDX Addon",
            Content = panel,
            PrimaryButtonText = "Next",
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var pickResult = await pickDialog.ShowAsync();
        if (pickResult != ContentDialogResult.Primary) return;

        if (combo.SelectedItem is not ComboBoxItem selected || selected.Tag is not GameCardViewModel targetCard)
        {
            var noSelection = new ContentDialog
            {
                Title = "No Game Selected",
                Content = "Please select a game to install the addon to.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
            };
            await noSelection.ShowAsync();
            return;
        }

        var gameName = targetCard.GameName;
        var installPath = targetCard.InstallPath;

        // Check for existing RenoDX addon files in the game folder
        string? existingAddon = null;
        try
        {
            var existing = Directory.GetFiles(installPath, "*.addon64")
                .Concat(Directory.GetFiles(installPath, "*.addon32"))
                .Where(f => !Path.GetFileName(f).StartsWith("zzz_display_commander", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (existing.Count > 0)
                existingAddon = string.Join(", ", existing.Select(Path.GetFileName));
        }
        catch { }

        // Confirmation dialog
        var warningText = $"Are you sure you want to install {addonFileName} for {gameName}?";
        if (!string.IsNullOrEmpty(existingAddon))
            warningText += $"\n\nThis will replace the existing addon: {existingAddon}";
        warningText += $"\n\nInstall path: {installPath}";

        var confirmDialog = new ContentDialog
        {
            Title = "⚠ Confirm Addon Install",
            Content = new TextBlock
            {
                Text = warningText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            },
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var confirmResult = await confirmDialog.ShowAsync();
        if (confirmResult != ContentDialogResult.Primary) return;

        // Remove existing RenoDX addon files (not DC addons)
        try
        {
            var toRemove = Directory.GetFiles(installPath, "*.addon64")
                .Concat(Directory.GetFiles(installPath, "*.addon32"))
                .Where(f => !Path.GetFileName(f).StartsWith("zzz_display_commander", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var f in toRemove)
            {
                CrashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Removing existing '{Path.GetFileName(f)}'");
                File.Delete(f);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Failed to remove existing addons — {ex.Message}");
        }

        // Copy the addon file to the game folder
        var destPath = Path.Combine(installPath, addonFileName);
        try
        {
            File.Copy(addonPath, destPath, overwrite: true);
            CrashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Installed '{addonFileName}' to '{installPath}'");

            // Update card status
            targetCard.Status = GameStatus.Installed;
            targetCard.InstalledAddonFileName = addonFileName;
            targetCard.NotifyAll();

            var successDialog = new ContentDialog
            {
                Title = "✅ Addon Installed",
                Content = $"{addonFileName} has been installed for {gameName}.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await successDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Install failed — {ex.Message}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Install Failed",
                Content = $"Failed to install addon: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
            };
            await errDialog.ShowAsync();
        }
    }

    // ── Static helper methods (public for testability) ────────────────────────────

    /// <summary>
    /// Walk up from the exe directory to find the game root.
    /// Stops when we find a directory that looks like a game root.
    /// For Unreal: recognises Binaries\Win64 structure (2 levels up).
    /// For other games: checks for store markers (Steam, GOG, Epic, EA, Xbox)
    /// and defaults to the exe's own directory if no markers are found.
    /// </summary>
    public static string InferGameRoot(string exeDir)
    {
        var dir = exeDir;

        // If the exe is inside Binaries\Win64, Binaries\WinGDK, or Binaries\Win32,
        // the game root is two levels up.
        var dirName   = Path.GetFileName(dir) ?? "";
        var parentDir = Path.GetDirectoryName(dir);
        var parentName = parentDir != null ? Path.GetFileName(parentDir) ?? "" : "";

        if (parentName.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
            && (dirName.Equals("Win64", StringComparison.OrdinalIgnoreCase)
             || dirName.Equals("WinGDK", StringComparison.OrdinalIgnoreCase)
             || dirName.Equals("Win32", StringComparison.OrdinalIgnoreCase)))
        {
            var grandparent = Path.GetDirectoryName(parentDir);
            if (grandparent != null) return grandparent;
        }

        // Walk up looking for game root markers (max 3 levels).
        var current = dir;
        for (int i = 0; i < 3 && current != null; i++)
        {
            if (LooksLikeGameRoot(current))
                return current;
            current = Path.GetDirectoryName(current);
        }

        // No markers found at all — the exe directory itself is the safest bet.
        return dir;
    }

    /// <summary>
    /// Returns true if a directory looks like a game root based on store markers
    /// or engine files. This is intentionally broad to catch Steam, GOG, Epic,
    /// EA, Xbox, Ubisoft, Unity, and Unreal games.
    /// </summary>
    public static bool LooksLikeGameRoot(string dirPath)
    {
        try
        {
            // Steam markers
            if (File.Exists(Path.Combine(dirPath, "steam_appid.txt"))
             || File.Exists(Path.Combine(dirPath, "steam_api64.dll"))
             || File.Exists(Path.Combine(dirPath, "steam_api.dll")))
                return true;

            // GOG markers
            if (File.Exists(Path.Combine(dirPath, "goglog.ini"))
             || File.Exists(Path.Combine(dirPath, "gog.ico"))
             || File.Exists(Path.Combine(dirPath, "goggame.sdb")))
                return true;
            if (Directory.GetFiles(dirPath, "goggame-*.dll").Length > 0)
                return true;

            // Epic markers
            if (Directory.Exists(Path.Combine(dirPath, ".egstore")))
                return true;

            // EA markers
            if (File.Exists(Path.Combine(dirPath, "installerdata.xml"))
             || File.Exists(Path.Combine(dirPath, "__Installer")))
                return true;

            // Xbox / Game Pass markers
            if (File.Exists(Path.Combine(dirPath, "MicrosoftGame.config"))
             || File.Exists(Path.Combine(dirPath, "appxmanifest.xml")))
                return true;

            // Ubisoft Connect markers
            if (File.Exists(Path.Combine(dirPath, "uplay_install.state"))
             || File.Exists(Path.Combine(dirPath, "upc.exe"))
             || Directory.GetFiles(dirPath, "uplay_*.dll").Length > 0)
                return true;

            // Battle.net / Blizzard markers
            if (File.Exists(Path.Combine(dirPath, ".build.info"))
             || File.Exists(Path.Combine(dirPath, ".product.db"))
             || File.Exists(Path.Combine(dirPath, "Blizzard Launcher.exe")))
                return true;

            // Rockstar Games Launcher markers
            if (File.Exists(Path.Combine(dirPath, "PlayGTAV.exe"))
             || File.Exists(Path.Combine(dirPath, "RockstarService.exe"))
             || Directory.GetFiles(dirPath, "socialclub*.dll").Length > 0)
                return true;

            // Unity marker
            if (File.Exists(Path.Combine(dirPath, "UnityPlayer.dll")))
                return true;

            // Unreal markers
            if (Directory.Exists(Path.Combine(dirPath, "Binaries"))
             || Directory.Exists(Path.Combine(dirPath, "Engine")))
                return true;
        }
        catch { /* permission issues — skip silently */ }

        return false;
    }

    /// <summary>
    /// Infer the game name from the exe and folder structure.
    /// </summary>
    public static string InferGameName(string exePath, string gameRoot, EngineType engine)
    {
        var exeName     = Path.GetFileNameWithoutExtension(exePath);
        var rootDirName = Path.GetFileName(gameRoot) ?? exeName;

        if (engine == EngineType.Unreal || engine == EngineType.UnrealLegacy)
        {
            var cleanExe = CleanUnrealExeName(exeName);

            if (rootDirName.Contains(' ') || rootDirName.Contains('-'))
                return CleanFolderName(rootDirName);

            try
            {
                var subdirs = Directory.GetDirectories(gameRoot)
                    .Select(Path.GetFileName)
                    .Where(d => d != null
                        && !d.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Engine", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Content", StringComparison.OrdinalIgnoreCase)
                        && !d.StartsWith(".", StringComparison.Ordinal))
                    .ToList();

                if (subdirs.Count > 0 && subdirs.Count <= 3)
                {
                    var candidate = subdirs.FirstOrDefault(d =>
                        !string.IsNullOrEmpty(d)
                        && !d.Equals("Saved", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Plugins", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Intermediate", StringComparison.OrdinalIgnoreCase));

                    if (candidate != null && candidate.Length > 2)
                        return CleanFolderName(candidate);
                }
            }
            catch { }

            return !string.IsNullOrEmpty(cleanExe) ? cleanExe : CleanFolderName(rootDirName);
        }

        if (engine == EngineType.Unity)
        {
            return CleanFolderName(exeName);
        }

        return CleanFolderName(rootDirName);
    }

    /// <summary>Strips common Unreal exe suffixes to get a clean game name.</summary>
    public static string CleanUnrealExeName(string exeName)
    {
        var cleaned = Regex.Replace(exeName, @"[_-]?(Win64|WinGDK|Win32)[_-]?Shipping$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?Shipping$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?(Win64|WinGDK|Win32)[_-]?Test$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?(Win64|WinGDK|Win32)$", "", RegexOptions.IgnoreCase);
        return cleaned.Trim('-', '_', ' ');
    }

    /// <summary>
    /// Cleans a folder or exe name into a presentable game name.
    /// Replaces underscores and camelCase boundaries with spaces.
    /// </summary>
    public static string CleanFolderName(string name)
    {
        var cleaned = name.Replace('_', ' ').Replace('-', ' ');
        cleaned = Regex.Replace(cleaned, @"(?<=[a-z])(?=[A-Z])", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }
}
