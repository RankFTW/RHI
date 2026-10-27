using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    // ── Export Game Data ──────────────────────────────────────────────────────

    public async void ExportGameData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            string json = await Task.Run(() => BuildGameDataJson());

            // Write to temp file and put on clipboard as a zip (same pattern as Copy Logs)
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var jsonPath  = Path.Combine(Path.GetTempPath(), $"RHI_GameData_{timestamp}.json");
            File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);

            // Zip it
            var zipPath = Path.Combine(Path.GetTempPath(), $"RHI_GameData_{timestamp}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("rhi_game_data.json");
                using var entryStream = entry.Open();
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                entryStream.Write(bytes, 0, bytes.Length);
            }

            try { File.Delete(jsonPath); } catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.ExportGameData_Click] Failed to delete temp json '{jsonPath}' — {ex.Message}"); }

            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(zipPath);
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetStorageItems(new[] { storageFile });
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            CrashReporter.Log($"[SettingsHandler.ExportGameData_Click] Game data exported: {zipPath}");

            if (sender is FrameworkElement fe && fe.XamlRoot != null)
            {
                await DialogService.ShowSafeAsync(new ContentDialog
                {
                    Title = "Game Data Copied",
                    Content = $"Data for {ViewModel.AllCards.Count} games has been gathered and copied to your clipboard. Paste directly into Discord to share.",
                    CloseButtonText = "OK",
                    XamlRoot = fe.XamlRoot,
                    RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
                });
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.ExportGameData_Click] Failed: {ex.Message}");
        }
        finally
        {
            if (sender is Button b) b.IsEnabled = true;
        }
    }

    private string BuildGameDataJson()
    {
        var pcgwSvc = App.Services.GetRequiredService<IPcgwService>();
        var gnSvc   = App.Services.GetRequiredService<IGameNameService>();
        var cards   = ViewModel.AllCards;
        var rhi     = CrashReporter.AppVersion;

        var games = new System.Collections.Generic.List<object>();

        foreach (var card in cards)
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            var installPath = card.InstallPath;

            // ── Exe path ─────────────────────────────────────────────────────
            // User override is an absolute path — make it relative to install root
            string? exeRelative = null;
            if (gnSvc.LaunchExeOverrides.TryGetValue(card.GameName, out var userExe)
                && !string.IsNullOrEmpty(userExe)
                && userExe.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
            {
                exeRelative = userExe.Substring(installPath.Length).TrimStart('\\', '/');
            }
            else if (ViewModel.Manifest?.LaunchExeOverrides?.TryGetValue(card.GameName, out var manifestExe) == true
                     && !string.IsNullOrEmpty(manifestExe))
            {
                exeRelative = manifestExe; // already relative
            }

            // ── Install subpath (relative portion after the game root) ───────
            // The install path itself IS the subpath we care about — strip the
            // store root prefix to get the relative subfolder if possible.
            // For the db we just record the folder name (last segment of path).
            string? installSubpath = null;
            if (!string.IsNullOrEmpty(installPath))
            {
                var folderName = Path.GetFileName(installPath.TrimEnd('\\', '/'));
                installSubpath = string.IsNullOrEmpty(folderName) ? null : folderName;
            }

            // ── APIs — merge PE scan with PCGW scrape ────────────────────────
            var detectedApis = card.DetectedApis ?? new System.Collections.Generic.HashSet<Models.GraphicsApiType>();
            var primaryApi   = card.GraphicsApi.ToString().Replace("DirectX", "DX");

            // PCGW can fill in APIs the PE scan missed (e.g. DX12 not in exe imports)
            var pcgwInfo = pcgwSvc.GetCachedApiInfo(card.GameName);
            var allApis  = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in detectedApis)
                allApis.Add(a.ToString().Replace("DirectX", "DX"));
            if (pcgwInfo != null)
            {
                if (pcgwInfo.HasDirectX9)  allApis.Add("DX9");
                if (pcgwInfo.HasDirectX10) allApis.Add("DX10");
                if (pcgwInfo.HasDirectX11) allApis.Add("DX11");
                if (pcgwInfo.HasDirectX12) allApis.Add("DX12");
                if (pcgwInfo.HasVulkan)    allApis.Add("Vulkan");
                if (pcgwInfo.HasOpenGL)    allApis.Add("OpenGL");
            }

            // If PCGW says DX12 but PE scan only found DX11, promote primary to DX12
            if (primaryApi == "DX11" && allApis.Contains("DX12"))
                primaryApi = "DX12";

            // ── Store-specific IDs ────────────────────────────────────────────
            var dg = card.DetectedGame;
            int? steamAppId = dg?.SteamAppId;
            string? xboxAumid = dg?.XboxAumid;
            string? epicAppName = dg?.EpicAppName;

            games.Add(new
            {
                name          = card.GameName,
                store         = card.Source,
                install_subpath = installSubpath,
                exe_relative  = exeRelative,
                api           = primaryApi,
                all_apis      = allApis.OrderBy(x => x).ToList(),
                bitness       = card.Is32Bit ? 32 : 64,
                engine        = string.IsNullOrEmpty(card.EngineHint) ? null : card.EngineHint,
                steam_appid   = steamAppId,
                xbox_aumid    = xboxAumid,
                epic_app_name = epicAppName,
                pcgw_url      = card.PcgwUrl,
                config_path       = pcgwInfo?.ConfigPath,
                config_path_xbox  = pcgwInfo?.ConfigPathXbox,
                engine_ini_project_override = card.EngineIniProjectOverride,
                api_source    = pcgwInfo != null ? "pcgw+pe" : "pe",
            });
        }

        var export = new
        {
            version     = "1",
            rhi_version = rhi,
            exported_at = DateTime.UtcNow.ToString("o"),
            game_count  = games.Count,
            games,
        };

        return System.Text.Json.JsonSerializer.Serialize(export,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }

    public async void CopyLogsArchive_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "logs");
            if (!Directory.Exists(logsDir))
            {
                CrashReporter.Log("[SettingsHandler.CopyLogsArchive_Click] Logs directory does not exist");
                return;
            }

            var logFiles = Directory.GetFiles(logsDir, "*.txt");
            if (logFiles.Length == 0)
            {
                CrashReporter.Log("[SettingsHandler.CopyLogsArchive_Click] No log files found");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var archivePath = Path.Combine(Path.GetTempPath(), $"RHI_Logs_{timestamp}.zip");

            // Delete if exists from a previous attempt
            if (File.Exists(archivePath)) File.Delete(archivePath);

            System.IO.Compression.ZipFile.CreateFromDirectory(logsDir, archivePath);

            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(archivePath);
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetStorageItems(new[] { storageFile });
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            CrashReporter.Log($"[SettingsHandler.CopyLogsArchive_Click] Logs archive copied to clipboard: {archivePath}");

            // Show confirmation
            if (sender is FrameworkElement fe && fe.XamlRoot != null)
            {
                await DialogService.ShowSafeAsync(new ContentDialog
                {
                    Title = "Logs Copied",
                    Content = "All session logs have been archived and copied to your clipboard. Paste directly into Discord to share.",
                    CloseButtonText = "OK",
                    XamlRoot = fe.XamlRoot,
                });
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.CopyLogsArchive_Click] Failed: {ex.Message}");
        }
    }

    public async void PurgeCachedFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Show warning dialog
            var warningDialog = new ContentDialog
            {
                Title = "⚠ Purge Staging Files",
                Content = "This will delete cached DLSS, Streamline, and component staging files to free disk space.\n\nShaders, installed RenoDX addons, and version metadata are preserved.\n\nThese files will be re-downloaded automatically when needed.\n\nContinue?",
                PrimaryButtonText = "Purge",
                CloseButtonText = "Cancel",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(warningDialog);
            if (result != ContentDialogResult.Primary) return;

            int filesDeleted = 0;
            long bytesFreed = 0;
            var rhiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

            // Folders to purge completely (delete all contents, keep folder)
            var foldersToClean = new[]
            {
                Path.Combine(rhiRoot, "DLSS"),
                Path.Combine(rhiRoot, "DLSS-D"),
                Path.Combine(rhiRoot, "DLSS-G"),
                Path.Combine(rhiRoot, "Streamline"),
            };

            // Downloads subfolders to clean (all except shaders)
            var downloadsRoot = DownloadPaths.Root;

            try
            {
                // Clean DLSS/Streamline folders
                foreach (var folder in foldersToClean)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var dir in Directory.GetDirectories(folder))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            var dirSize = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                            var dirCount = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Count();
                            Directory.Delete(dir, recursive: true);
                            filesDeleted += dirCount;
                            bytesFreed += dirSize;
                        }
                        catch { /* skip locked files */ }
                    }
                    // Also delete any files directly in the folder
                    foreach (var file in Directory.GetFiles(folder))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            bytesFreed += fi.Length;
                            fi.Delete();
                            filesDeleted++;
                        }
                        catch (Exception) { /* per-file loop; locked files are skipped and not counted */ }
                    }
                }

                // Collect installed addon filenames to preserve in renodx staging
                var installedAddons = new HashSet<string>(
                    ViewModel.AllCards
                        .Where(c => c.InstalledRecord != null && !string.IsNullOrEmpty(c.InstalledAddonFileName))
                        .Select(c => c.InstalledAddonFileName!),
                    StringComparer.OrdinalIgnoreCase);

                // Clean downloads subfolders (except shaders), preserving JSON metadata and in-use RenoDX addons
                if (Directory.Exists(downloadsRoot))
                {
                    foreach (var dir in Directory.GetDirectories(downloadsRoot))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (dirName.Equals("shaders", StringComparison.OrdinalIgnoreCase)) continue;
                        bool isRenodxDir = dirName.Equals("renodx", StringComparison.OrdinalIgnoreCase);

                        try
                        {
                            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
                                // Keep renodx addons that are currently installed on at least one game
                                if (isRenodxDir && installedAddons.Contains(Path.GetFileName(file))) continue;
                                try
                                {
                                    var fi = new FileInfo(file);
                                    bytesFreed += fi.Length;
                                    fi.Delete();
                                    filesDeleted++;
                                }
                                catch (Exception) { /* per-file loop; locked files are skipped and not counted */ }
                            }
                        }
                        catch (Exception) { /* per-folder loop; unreadable folders are skipped */ }
                    }
                    // Delete loose files in downloads root (e.g. .reorganised marker)
                    foreach (var file in Directory.GetFiles(downloadsRoot))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            bytesFreed += fi.Length;
                            fi.Delete();
                            filesDeleted++;
                        }
                        catch (Exception) { /* per-file loop; locked files are skipped and not counted */ }
                    }
                }

                // Format size
                string sizeStr = bytesFreed switch
                {
                    >= 1_073_741_824 => $"{bytesFreed / 1_073_741_824.0:F1} GB",
                    >= 1_048_576 => $"{bytesFreed / 1_048_576.0:F1} MB",
                    >= 1024 => $"{bytesFreed / 1024.0:F1} KB",
                    _ => $"{bytesFreed} bytes"
                };

                CrashReporter.Log($"[SettingsHandler.PurgeCachedFiles_Click] Purged {filesDeleted} files, freed {sizeStr}");

                var resultDialog = new ContentDialog
                {
                    Title = "✅ Cache Purged",
                    Content = $"Deleted {filesDeleted} files, freed {sizeStr} of disk space.",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(resultDialog);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsHandler.PurgeCachedFiles_Click] Failed: {ex.Message}");
                var errDialog = new ContentDialog
                {
                    Title = "❌ Purge Failed",
                    Content = $"An error occurred: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(errDialog);
            }
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.PurgeCachedFiles_Click] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }
}
