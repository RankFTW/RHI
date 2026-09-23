// DragDropHandler.Addon.cs — Addon, archive, and URL drop processing: install flows for .addon64/.addon32 files.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System.Net.Http;

namespace RenoDXCommander;

public partial class DragDropHandler
{
    /// <summary>
    /// Shared HttpClient for URL downloads. Static to follow best practices
    /// (avoid socket exhaustion).
    /// </summary>
    private static readonly HttpClient s_httpClient = new();

    /// <summary>
    /// Handles a dropped archive file (.zip, .7z, .rar, etc.) — extracts it using 7-Zip,
    /// looks for .addon64/.addon32 files inside, and passes them to ProcessDroppedAddon.
    /// </summary>
    public async Task ProcessDroppedArchive(string archivePath)
    {
        var archiveName = Path.GetFileName(archivePath);
        _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Received '{archiveName}'");

        var sevenZipExe = await App.Services.GetRequiredService<ISevenZipExtractor>().Find7ZipExeAsync();
        if (sevenZipExe == null)
        {
            var errDialog = new ContentDialog
            {
                Title = "7-Zip Not Found",
                Content = "Cannot extract archive — 7-Zip was not found. Please reinstall RDXC.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // Extract entire archive to a temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"RHI_archive_{Guid.NewGuid():N}");
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

            _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Extracting with {psi.FileName} {psi.Arguments}");

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                _crashReporter.Log("[DragDropHandler.ProcessDroppedArchive] Failed to start 7z process");
                return;
            }

            // Read output asynchronously to prevent deadlock
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            
            // Wait asynchronously with 60 second timeout for large archives
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _crashReporter.Log("[DragDropHandler.ProcessDroppedArchive] 7z timed out after 60 seconds — killing process");
                proc.Kill();
            }

            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] 7z stderr: {stderr}");

            if (proc.ExitCode != 0)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] 7z exit code {proc.ExitCode}");
                var failDialog = new ContentDialog
                {
                    Title = "Archive Extraction Failed",
                    Content = $"Failed to extract '{archiveName}'. The file may be corrupt or in an unsupported format.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(failDialog);
                return;
            }

            // Search for renodx- prefixed .addon64, .addon32, and .addon files in the extracted contents
            // .addon files (no bitness suffix) are treated as .addon64 for compatibility
            var addonFiles = Directory.GetFiles(tempDir, "*.addon64", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(tempDir, "*.addon32", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(tempDir, "*.addon", SearchOption.AllDirectories))
                .Where(f => Path.GetFileName(f).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(f).Contains("Luma", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (addonFiles.Count == 0)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] No addon files found in '{archiveName}'");
                var noAddonDialog = new ContentDialog
                {
                    Title = "No Addon Found",
                    Content = $"No .addon64, .addon32, or .addon files were found inside '{archiveName}'.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(noAddonDialog);
                return;
            }

            _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Found {addonFiles.Count} addon file(s): [{string.Join(", ", addonFiles.Select(Path.GetFileName))}]");

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
                    RequestedTheme = ElementTheme.Dark,
                };
                if (await DialogService.ShowSafeAsync(pickDialog) != ContentDialogResult.Primary) return;
                addonToInstall = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? addonFiles[0];
            }

            // Route to the correct handler based on file type
            bool isLumaFile = Path.GetFileName(addonToInstall).Contains("Luma", StringComparison.OrdinalIgnoreCase);

            if (isLumaFile)
            {
                // Luma addon found inside the archive — route the original archive to the full Luma install flow
                // (which extracts the Luma subfolder, shaders, etc. via InstallFromArchiveAsync)
                var lumaGames = _window.ViewModel.AllCards
                    .Where(c => c.LumaFeatureEnabled && !string.IsNullOrEmpty(c.InstallPath))
                    .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (lumaGames.Count > 0)
                {
                    var gameNames = lumaGames.Select(c => c.GameName).ToList();
                    var preSelectIndex = FuzzyMatchGameIndex(gameNames, archiveName);
                    var lumaCombo = new ComboBox
                    {
                        ItemsSource = gameNames,
                        SelectedIndex = preSelectIndex,
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    var lumaPickDialog = new ContentDialog
                    {
                        Title = "🌙 Install Luma Addon",
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock { Text = $"Install {archiveName} to:", TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                                lumaCombo,
                            }
                        },
                        PrimaryButtonText = "Install",
                        CloseButtonText = "Cancel",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    var lumaResult = await DialogService.ShowSafeAsync(lumaPickDialog);
                    if (lumaResult == ContentDialogResult.Primary)
                    {
                        var selectedName = lumaCombo.SelectedItem as string;
                        var card = lumaGames.FirstOrDefault(c => c.GameName == selectedName);
                        if (card != null)
                            await ProcessDroppedLumaArchiveAsync(archivePath, card);
                    }
                }
                else
                {
                    await ProcessDroppedAddon(addonToInstall);
                }
            }
            else
            {
                await ProcessDroppedAddon(addonToInstall);
            }
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tempDir, recursive: true); } catch (Exception ex) { _crashReporter.Log($"[DragDropHandler.ProcessDroppedArchive] Failed to clean up temp dir '{tempDir}' — {ex.Message}"); }
        }
    }

    /// <summary>
    /// Handles a dropped .addon64/.addon32 file — prompts the user to pick a game
    /// and installs the addon to that game's folder after confirmation.
    /// </summary>
    public async Task ProcessDroppedAddon(string addonPath, bool deleteSourceAfterInstall = false)
    {
        var addonFileName = Path.GetFileName(addonPath);
        _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Received '{addonFileName}'");

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
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(noGamesDialog);
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
        bool autoMatched = false;
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
                    autoMatched = true;
                    break;
                }
            }
        }

        // Fall back to the currently selected game in the sidebar if no filename match
        if (!autoMatched && ViewModel.SelectedGame != null)
        {
            for (int i = 0; i < sortedCards.Count; i++)
            {
                if (string.Equals(sortedCards[i].GameName, ViewModel.SelectedGame.GameName, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        bool isLumaAddon = addonFileName.Contains("Luma", StringComparison.OrdinalIgnoreCase)
                        || addonFileName.Contains("luma", StringComparison.OrdinalIgnoreCase);

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
            Title = isLumaAddon ? "🌙 Install Luma Addon" : "📦 Install RenoDX Addon",
            Content = panel,
            PrimaryButtonText = "Next",
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var pickResult = await DialogService.ShowSafeAsync(pickDialog);
        if (pickResult != ContentDialogResult.Primary) return;

        if (combo.SelectedItem is not ComboBoxItem selected || selected.Tag is not GameCardViewModel targetCard)
        {
            var noSelection = new ContentDialog
            {
                Title = "No Game Selected",
                Content = "Please select a game to install the addon to.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(noSelection);
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
                .Concat(Directory.GetFiles(installPath, "*.addon"))
                .Where(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-devkit", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-dlssfix", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-upgrade", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-dlss.", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-universal_ue", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (existing.Count > 0)
                existingAddon = string.Join(", ", existing.Select(Path.GetFileName));
        }
        catch (Exception ex) { _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Failed to check existing addons in '{installPath}' — {ex.Message}"); }

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

        var confirmResult = await DialogService.ShowSafeAsync(confirmDialog);
        if (confirmResult != ContentDialogResult.Primary) return;

        // Remove existing RenoDX addon files (not DC addons)
        // Check both the addon search path and the base install path
        var addonDeployPath = ModInstallService.GetAddonDeployPath(installPath);
        try
        {
            var searchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { installPath };
            if (!string.Equals(addonDeployPath, installPath, StringComparison.OrdinalIgnoreCase))
                searchPaths.Add(addonDeployPath);

            foreach (var searchDir in searchPaths)
            {
                if (!Directory.Exists(searchDir)) continue;
                var toRemove = Directory.GetFiles(searchDir, "*.addon64")
                    .Concat(Directory.GetFiles(searchDir, "*.addon32"))
                    .Concat(Directory.GetFiles(searchDir, "*.addon"))
                    .Where(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase)
                             && !Path.GetFileName(f).StartsWith("renodx-devkit", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-dlssfix", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-upgrade", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-dlss.", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("renodx-universal_ue", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var f in toRemove)
                {
                    _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Removing existing '{Path.GetFileName(f)}' from '{searchDir}'");
                    File.Delete(f);
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Failed to remove existing addons — {ex.Message}");
        }

        // Copy the addon file to the resolved addon folder
        var effectiveAddonFileName = addonFileName;
        var destPath = Path.Combine(addonDeployPath, effectiveAddonFileName);
        try
        {
            File.Copy(addonPath, destPath, overwrite: true);
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Installed '{effectiveAddonFileName}' to '{addonDeployPath}'");

            // Determine if this is a named mod (not UE-Extended or generic UE)
            bool isNamedMod = !effectiveAddonFileName.Equals("renodx-ue-extended.addon64", StringComparison.OrdinalIgnoreCase)
                           && !effectiveAddonFileName.Equals("renodx-unrealengine.addon64", StringComparison.OrdinalIgnoreCase);

            // Save an InstalledModRecord so the addon survives refresh/restart
            var installRecord = new InstalledModRecord
            {
                GameName      = gameName,
                InstallPath   = addonDeployPath,
                Store         = targetCard.Source ?? "",
                AddonFileName = effectiveAddonFileName,
                InstalledAt   = DateTime.UtcNow,
                // For named mods from Discord, don't use the card's existing SnapshotUrl (could be UE-Extended)
                SnapshotUrl   = isNamedMod ? null : targetCard.Mod?.SnapshotUrl,
            };
            _modInstallService.SaveRecordPublic(installRecord);

            // Deploy Engine.ini LUT setting for Unreal Engine games (same as normal install flow)
            if (targetCard.EngineHint?.Contains("Unreal") == true)
            {
                try
                {
                    AuxInstallService.ApplyEngineIniLutSetting(targetCard.InstallPath, targetCard.EngineIniProjectOverride, gameName, targetCard.Source);
                }
                catch (Exception ex) { _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Engine.ini LUT deploy failed — {ex.Message}"); }
            }

            // If this is a named mod from Discord, update the card to reflect it's no longer UE-Extended
            if (isNamedMod && targetCard.UseUeExtended)
            {
                targetCard.UseUeExtended = false;
                targetCard.IsGenericMod = false;
                // Remove from UE-Extended set so it persists across restarts
                _window.ViewModel.RemoveFromUeExtendedGames(gameName);
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Cleared UE-Extended state for '{gameName}' — named mod installed");
            }

            // Update card's Mod to reflect external-only state after drag-drop.
            // Preserve existing Nexus/snapshot URLs if the card already has them —
            // only fall back to the Discord channel when there's no better download URL.
            if (isNamedMod)
            {
                var existingNexus    = targetCard.Mod?.NexusUrl ?? targetCard.NexusUrl;
                var existingSnapshot = targetCard.Mod?.SnapshotUrl;

                if (!string.IsNullOrEmpty(existingSnapshot))
                {
                    // Card has a snapshot URL — keep the existing mod, just mark installed
                    // (no ExternalOnly needed — install button will show Reinstall)
                }
                else if (!string.IsNullOrEmpty(existingNexus))
                {
                    // Nexus-hosted mod — preserve the Nexus URL
                    targetCard.Mod = new GameMod
                    {
                        Name     = gameName,
                        Status   = "💬",
                        NexusUrl = existingNexus,
                    };
                    targetCard.IsExternalOnly = true;
                    targetCard.ExternalUrl    = existingNexus;
                    targetCard.ExternalLabel  = "Download from Nexus Mods";
                }
                else
                {
                    // No known URL — fall back to Discord channel
                    targetCard.Mod = new GameMod
                    {
                        Name       = gameName,
                        Status     = "💬",
                        DiscordUrl = "https://discord.gg/gF4GRJWZ2A",
                    };
                    targetCard.IsExternalOnly = true;
                    targetCard.ExternalUrl    = "https://discord.gg/gF4GRJWZ2A";
                    targetCard.ExternalLabel  = "Download from Discord";
                }
            }

            // Update card status
            targetCard.InstalledRecord = installRecord;
            targetCard.Status = GameStatus.Installed;
            targetCard.InstalledAddonFileName = effectiveAddonFileName;
            targetCard.RdxInstalledVersion = AuxInstallService.ReadInstalledVersion(addonDeployPath, effectiveAddonFileName);
            targetCard.NotifyAll();
            _window.ViewModel.SaveLibraryPublic();

            // Delete the source file from Downloads if requested (file watcher flow)
            if (deleteSourceAfterInstall)
            {
                try
                {
                    File.Delete(addonPath);
                    _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Deleted source file '{addonFileName}' from watch folder");
                }
                catch (Exception delEx)
                {
                    _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Failed to delete source file — {delEx.Message}");
                }
            }

            var successDialog = new ContentDialog
            {
                Title = "✅ Addon Installed",
                Content = $"{addonFileName} has been installed for {gameName}.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(successDialog);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedAddon] Install failed — {ex.Message}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Install Failed",
                Content = $"Failed to install addon: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
        }
    }

    /// <summary>
    /// Processes a URL dropped onto the window. Validates the URL, downloads the addon
    /// to the cache, PE-validates it, then routes to ProcessDroppedAddon.
    /// </summary>
    public async Task ProcessDroppedUrl(string url)
    {
        _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Received URL: {url}");

        // ── Step 1: Validate URL is parseable ─────────────────────────────────────
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Invalid URL: {url}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Invalid URL",
                Content = "The dropped URL could not be parsed. Please check the link and try again.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 2: Extract filename and validate extension ───────────────────────
        var filename = ExtractFileNameFromUrl(url);
        if (string.IsNullOrEmpty(filename))
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Could not extract filename from URL: {url}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Invalid URL",
                Content = "Could not determine a filename from the dropped URL.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        var ext = Path.GetExtension(filename);

        // Handle zip/7z URLs — download and check if it's a Luma archive
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) || ext.Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Archive URL detected: {filename} — downloading to check for Luma");
            var lumaTemp = Path.Combine(Path.GetTempPath(), filename);
            try
            {
                var bytes = await s_httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(lumaTemp, bytes);

                if (IsLumaArchive(lumaTemp))
                {
                    // Show game picker and install
                    var lumaGames = _window.ViewModel.AllCards
                        .Where(c => c.LumaFeatureEnabled && !string.IsNullOrEmpty(c.InstallPath))
                        .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (lumaGames.Count > 0)
                    {
                        var gameNames = lumaGames.Select(c => c.GameName).ToList();
                        var preSelectIndex = FuzzyMatchGameIndex(gameNames, filename);

                        var combo = new ComboBox
                        {
                            ItemsSource = gameNames,
                            SelectedIndex = preSelectIndex,
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                        };
                        var pickerDialog = new ContentDialog
                        {
                            Title = "Install Luma Mod",
                            Content = new StackPanel
                            {
                                Spacing = 8,
                                Children =
                                {
                                    new TextBlock { Text = $"Luma mod detected: {filename}\n\nSelect game to install to:", TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                                    combo,
                                }
                            },
                            PrimaryButtonText = "Install",
                            CloseButtonText = "Cancel",
                            XamlRoot = _window.Content.XamlRoot,
                            RequestedTheme = ElementTheme.Dark,
                        };
                        var result = await DialogService.ShowSafeAsync(pickerDialog);
                        if (result == ContentDialogResult.Primary)
                        {
                            var selectedName = combo.SelectedItem as string;
                            var card = lumaGames.FirstOrDefault(c => c.GameName == selectedName);
                            if (card != null)
                                await ProcessDroppedLumaArchiveAsync(lumaTemp, card);
                        }
                    }
                }
                else
                {
                    // Not a Luma archive — treat as a regular archive (extract for addons)
                    await ProcessDroppedArchive(lumaTemp);
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Failed to download/process archive — {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(lumaTemp)) File.Delete(lumaTemp); } catch { }
            }
            return;
        }

        if (!ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase))
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Unsupported extension '{ext}' for file '{filename}' from URL: {url}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Unsupported File Type",
                Content = $"Only .addon64 and .addon32 files are supported.\n\nThe URL points to: {filename}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 3: Prepare download paths ────────────────────────────────────────
        var cacheDir = DownloadPaths.RenoDX;
        Directory.CreateDirectory(cacheDir);

        var tempPath = Path.Combine(cacheDir, filename + ".tmp");
        var cachePath = Path.Combine(cacheDir, filename);

        // ── Step 4: Show progress dialog and download ─────────────────────────────
        var progressText = new TextBlock
        {
            Text = $"Downloading {filename}...",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 13,
        };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 6,
            IsIndeterminate = true,
        };
        var progressDialog = new ContentDialog
        {
            Title = "⬇ Downloading Addon",
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { progressText, progressBar },
            },
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        // Show dialog non-blocking (acquire dialog gate to prevent concurrent dialogs)
        if (!DialogService.TryAcquireDialogGate())
        {
            CrashReporter.Log("[DragDropHandler.Addon] Skipped progress dialog — another dialog is open");
            return;
        }
        bool gateReleased = false;
        progressDialog.Closed += (_, _) => { if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); } };
        var dialogTask = progressDialog.ShowAsync();

        try
        {
            try
            {
                var response = await s_httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] HTTP {(int)response.StatusCode} for URL: {url}");
                    progressDialog.Hide();
                    if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); }
                    var errDialog = new ContentDialog
                    {
                        Title = "❌ Download Failed",
                        Content = $"The server returned HTTP {(int)response.StatusCode}.\n\nURL: {url}",
                        CloseButtonText = "OK",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    await DialogService.ShowSafeAsync(errDialog);
                    return;
                }

                var totalBytes = response.Content.Headers.ContentLength;
                long downloaded = 0;
                var buffer = new byte[1024 * 1024]; // 1 MB

                if (totalBytes.HasValue)
                {
                    progressBar.IsIndeterminate = false;
                }

                using (var netStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
                {
                    int read;
                    while ((read = await netStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        downloaded += read;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            var pct = (double)downloaded / totalBytes.Value * 100;
                            _window.DispatcherQueue.TryEnqueue(() =>
                            {
                                progressBar.Value = pct;
                                progressText.Text = $"Downloading {filename}... {downloaded / 1024} KB ({pct:F0}%)";
                            });
                        }
                        else
                        {
                            _window.DispatcherQueue.TryEnqueue(() =>
                            {
                                progressText.Text = $"Downloading {filename}... {downloaded / 1024} KB";
                            });
                        }
                    }
                }

                _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Downloaded {downloaded} bytes to '{tempPath}'");
            }
            catch (HttpRequestException ex)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Network error downloading '{url}' — {ex.Message}");
                progressDialog.Hide();
                if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); }
                var errDialog = new ContentDialog
                {
                    Title = "❌ Download Failed",
                    Content = $"A network error occurred while downloading the addon.\n\n{ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(errDialog);
                return;
            }
            catch (TaskCanceledException ex)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Download timed out for '{url}' — {ex.Message}");
                progressDialog.Hide();
                if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); }
                var errDialog = new ContentDialog
                {
                    Title = "❌ Download Timed Out",
                    Content = "The download timed out. Please check your connection and try again.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(errDialog);
                return;
            }

            // ── Step 5: Rename temp file to final filename ────────────────────────
            if (File.Exists(cachePath))
                File.Delete(cachePath);
            File.Move(tempPath, cachePath);

            // ── Step 6: PE-validate the downloaded file ───────────────────────────
            if (!HasPeSignature(cachePath))
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Downloaded file '{filename}' is not a valid PE binary — deleting");
                try { File.Delete(cachePath); } catch { }
                progressDialog.Hide();
                if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); }
                var errDialog = new ContentDialog
                {
                    Title = "❌ Invalid Addon File",
                    Content = "The downloaded file is not a valid addon binary. The server may have returned an error page.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(errDialog);
                return;
            }

            // ── Step 7: Dismiss progress and route to existing install flow ───────
            progressDialog.Hide();
            if (!gateReleased) { gateReleased = true; DialogService.ReleaseDialogGate(); };
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] PE validation passed for '{filename}', routing to ProcessDroppedAddon");
            await ProcessDroppedAddon(cachePath);
        }
        finally
        {
            // Clean up temp file if it still exists (e.g. on error before rename)
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[DragDropHandler.ProcessDroppedUrl] Failed to clean up temp file '{tempPath}' — {ex.Message}");
            }
        }
    }
}
