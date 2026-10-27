using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private async Task InstallFeederAddonAsync(
        GameCardViewModel card,
        Button statusBtn,
        IAddonPackService addonSvc,
        IDlssStreamlineService dlssSvc,
        ComboBox? nrVersionCombo = null,
        ComboBox? addonVersionCombo = null,
        ComboBox? packVersionCombo = null)
    {
        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();

        // Resolve requested Feeder version (the feed addon itself)
        string? requestedFeederVersion = null;
        if (packVersionCombo != null)
        {
            requestedFeederVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
                () => packVersionCombo.SelectedItem as string).ConfigureAwait(false);
        }
        bool useLatestFeeder = string.IsNullOrEmpty(requestedFeederVersion) || requestedFeederVersion.StartsWith("Latest");

        // Resolve requested DLSS5 Tool version (neural consumer) — Latest or pinned
        string? requestedVersion = null;
        if (addonVersionCombo != null)
        {
            requestedVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
                () => addonVersionCombo.SelectedItem as string).ConfigureAwait(false);
        }
        bool useLatestConsumer = string.IsNullOrEmpty(requestedVersion) || requestedVersion.StartsWith("Latest");

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Downloading Feeder...");

        // Resolve Feeder staged path — versioned or latest via AddonPackService
        string? feederSourcePath = null;
        if (!useLatestFeeder && requestedFeederVersion != null && !card.Is32Bit)
        {
            // Versioned staging only stores .addon64 — skip for 32-bit games and use AddonPackService instead
            var staged = await rdx5Svc.EnsureVersionStagedAsync(Renodx5AddonService.FeederSubDir, requestedFeederVersion).ConfigureAwait(false);
            if (staged)
            {
                feederSourcePath = rdx5Svc.GetVersionedStagedFilePath(Renodx5AddonService.FeederSubDir, requestedFeederVersion);
                _window.ViewModel.SetNrPackVersion(gameName, requestedFeederVersion, store);
                CrashReporter.Log($"[NeuralRendering] Using Feeder v{requestedFeederVersion} from versioned staging — persisted pack version");
            }
            else
            {
                CrashReporter.Log($"[NeuralRendering] Could not stage Feeder v{requestedFeederVersion} — falling back to latest");
            }
        }
        else if (!useLatestFeeder && requestedFeederVersion != null && card.Is32Bit)
        {
            CrashReporter.Log($"[NeuralRendering] 32-bit game — versioned staging only has .addon64, using AddonPackService for correct .addon32");
        }

        if (feederSourcePath == null)
        {
            // Fall back to AddonPackService (always latest)
            var entry = addonSvc.AvailablePacks.FirstOrDefault(p =>
                p.PackageName.Equals(FeederPackageName, StringComparison.OrdinalIgnoreCase));
            bool needsHostExe = card.Is32Bit && FindStagedAddon(FeederPackageName, ".exe") == null;
            if (entry != null && (!addonSvc.IsDownloaded(FeederPackageName) || needsHostExe))
                await addonSvc.DownloadAddonAsync(entry).ConfigureAwait(false);
            var bitnessExt = card.Is32Bit ? ".addon32" : ".addon64";
            feederSourcePath = FindStagedAddon(FeederPackageName, bitnessExt);
            _window.ViewModel.SetNrPackVersion(gameName, null, store); // clear version pin — using latest
        }

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying Feeder...");
        await Task.Run(() =>
        {
            if (feederSourcePath == null || !File.Exists(feederSourcePath))
            {
                CrashReporter.Log($"[NeuralRendering] Feeder staging file not found");
                return;
            }
            var destName = card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
            var dest = Path.Combine(installPath, destName);
            File.Copy(feederSourcePath, dest, overwrite: true);
            CrashReporter.Log($"[NeuralRendering] Deployed {destName} to '{installPath}'");
        }).ConfigureAwait(false);

        // Also deploy NR dll alongside the feeder — use the version selected in the NR combo
        {
            var nrSelVer = nrVersionCombo != null
                ? await DispatchAsync<string?>(_window.DispatcherQueue!, () => nrVersionCombo.SelectedItem as string).ConfigureAwait(false)
                : null;
            bool nrUseLatestFeeder = string.IsNullOrEmpty(nrSelVer) || nrSelVer.StartsWith("Latest");
            var nrDestPathFeeder = Path.Combine(installPath, "nvngx_dlssnr.dll");
            var nrBackup = nrDestPathFeeder + ".original";
            string? cachedNrFeeder;
            if (nrUseLatestFeeder)
                cachedNrFeeder = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
            else
            {
                var nrDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RHI", "DLSS-NR", StripVersionSuffix(nrSelVer!));
                cachedNrFeeder = ResolveCachedNrDllPath(nrSelVer!) ?? Path.Combine(nrDir, "nvngx_dlssnr.dll");
                if (!File.Exists(cachedNrFeeder))
                    cachedNrFeeder = await dlssSvc.EnsureSpecificDlssnrCachedAsync(nrSelVer!).ConfigureAwait(false)
                                  ?? await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
            }
            if (cachedNrFeeder != null)
            {
                if (File.Exists(nrDestPathFeeder) && !File.Exists(nrBackup))
                    File.Copy(nrDestPathFeeder, nrBackup);
                else if (!File.Exists(nrDestPathFeeder) && !File.Exists(nrBackup))
                    File.WriteAllBytes(nrBackup, Array.Empty<byte>());
                File.Copy(cachedNrFeeder, nrDestPathFeeder, overwrite: true);
                RhiInstallManifest.AddSharedFileOwner(installPath, "nvngx_dlssnr.dll", "Feeder");
                CrashReporter.Log($"[NeuralRendering.Feeder] Deployed nvngx_dlssnr.dll v{(nrUseLatestFeeder ? "latest" : nrSelVer)} to '{installPath}'");
            }
        }

        // Deploy DLSS5 Tool as neural consumer (Feeder needs renodx-dlss5.addon64 alongside it)
        // For 32-bit games the neural consumer runs in host64\ — it must NOT be in the game folder
        // (32-bit ReShade cannot load .addon64 files).
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying DLSS5 Tool...");

        // Resolve the source path for the neural consumer — versioned or latest
        string? consumerSourcePath = null;
        if (!useLatestConsumer && requestedVersion != null)
        {
            var staged = await rdx5Svc.EnsureVersionStagedAsync("dlss5tool", requestedVersion).ConfigureAwait(false);
            if (staged)
            {
                consumerSourcePath = rdx5Svc.GetVersionedStagedFilePath("dlss5tool", requestedVersion);
                _window.ViewModel.SetNrAddonVersion(gameName, requestedVersion, store);
            }
            else
            {
                CrashReporter.Log($"[NeuralRendering] Could not stage DLSS5 Tool v{requestedVersion} for Feeder — falling back to latest");
            }
        }
        if (consumerSourcePath == null)
        {
            await rdx5Svc.EnsureStagingAsync().ConfigureAwait(false);
            consumerSourcePath = rdx5Svc.IsStagingReady ? rdx5Svc.StagedFilePath : null;
            _window.ViewModel.SetNrAddonVersion(gameName, null, store);
        }

        if (consumerSourcePath != null && !card.Is32Bit)
        {
            await Task.Run(() =>
            {
                var deployDir = ModInstallService.GetAddonDeployPath(installPath);
                Directory.CreateDirectory(deployDir);
                File.Copy(consumerSourcePath, Path.Combine(deployDir, "renodx-dlss5.addon64"), overwrite: true);
                CrashReporter.Log($"[NeuralRendering] Deployed renodx-dlss5.addon64 (v{(useLatestConsumer ? "latest" : requestedVersion)}, Feeder consumer) to '{deployDir}'");
            }).ConfigureAwait(false);
        }
        else if (card.Is32Bit)
        {
            CrashReporter.Log($"[NeuralRendering] 32-bit game — skipping renodx-dlss5.addon64 in game folder (neural consumer goes in host64\\ instead)");
        }

        // Deploy newest nvngx_dlss.dll — required by Feeder beside the game exe (install root, not detected plugin path)
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying DLSS SR...");
        var cachedDlss = await _dlssStreamlineService.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
        if (cachedDlss != null && new FileInfo(cachedDlss).Length > 0)
        {
            await Task.Run(() =>
            {
                // Always deploy to install root — Feeder looks for nvngx_dlss.dll beside itself, not in deep plugin folders
                var dlssDest     = Path.Combine(installPath, "nvngx_dlss.dll");
                var dlssSentinel = dlssDest + ".original";
                if (File.Exists(dlssDest) && !File.Exists(dlssSentinel))
                    File.Copy(dlssDest, dlssSentinel); // backup game original if present
                else if (!File.Exists(dlssDest))
                    File.WriteAllBytes(dlssSentinel, Array.Empty<byte>()); // sentinel — game had none
                File.Copy(cachedDlss, dlssDest, overwrite: true);
                CrashReporter.Log($"[NeuralRendering] Deployed newest nvngx_dlss.dll to '{installPath}' (Feeder root)");
            }).ConfigureAwait(false);
        }

        // Deploy DLSS5_Feed.fx + lumenite_Kernel.fx
        // DLSS5_Feed.fx is seeded from the Feeder addon zip into the DLSS5Feeder staging folder.
        // LumeniteFX is downloaded via the pack system.
        // We do NOT call EnsurePacksAsync for DLSS5Feeder — its URL is a dead fallback that 404s.
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying shaders...");
        try
        {
            // Ensure LumeniteFX is staged (DLSS5Feeder is self-contained — no download needed)
            await _shaderPackService.EnsurePacksAsync(new[] { "LumeniteFX" }).ConfigureAwait(false);

            // Seed DLSS5_Feed.fx into the staging folder from the feeder addon zip if missing
            var feedFxStaged = System.IO.Path.Combine(ShaderPackService.ShadersDir, "DLSS5Feeder", "DLSS5_Feed.fx");
            if (!File.Exists(feedFxStaged))
            {
                // Try to extract from the staged feeder addon zip (versioned or latest)
                var feederZipDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RHI", "rdx5", "feeder");
                var feederZip = Directory.Exists(feederZipDir)
                    ? Directory.GetFiles(feederZipDir, "*.zip", SearchOption.AllDirectories)
                          .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                          .FirstOrDefault()
                    : null;
                // Also check the flat addon staging dir for a zip
                if (feederZip == null)
                {
                    var addonDir = AddonPackService.GetStagingDir();
                    if (Directory.Exists(addonDir))
                        feederZip = Directory.GetFiles(addonDir, "*Feeder*.zip")
                            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                            .FirstOrDefault();
                }
                if (feederZip != null)
                {
                    try
                    {
                        using var zip = System.IO.Compression.ZipFile.OpenRead(feederZip);
                        var fxEntry = zip.Entries.FirstOrDefault(e =>
                            string.Equals(e.Name, "DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase));
                        if (fxEntry != null)
                        {
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(feedFxStaged)!);
                            using var fs = fxEntry.Open();
                            using var dst = File.Create(feedFxStaged);
                            await fs.CopyToAsync(dst).ConfigureAwait(false);
                            _ = Task.Run(() => App.Services.GetRequiredService<IShaderPackService>().RecordExtractedFilesFromDir("DLSS5Feeder"));
                            CrashReporter.Log($"[NeuralRendering] Re-extracted DLSS5_Feed.fx from {System.IO.Path.GetFileName(feederZip)}");
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[NeuralRendering] Failed to re-extract DLSS5_Feed.fx — {ex.Message}"); }
                }
                if (!File.Exists(feedFxStaged))
                {
                    // No zip available locally — force re-download the Feeder addon so the zip
                    // is fetched fresh and DLSS5_Feed.fx is extracted from it.
                    CrashReporter.Log("[NeuralRendering] DLSS5_Feed.fx missing and no zip found — re-downloading Feeder addon to seed it");
                    _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Downloading Feed.fx...");
                    var feederEntry = addonSvc.AvailablePacks.FirstOrDefault(p =>
                        p.PackageName.Equals(FeederPackageName, StringComparison.OrdinalIgnoreCase));
                    if (feederEntry != null)
                    {
                        await addonSvc.DownloadAddonAsync(feederEntry).ConfigureAwait(false);
                        // DownloadAndExtractZipAsync will have extracted DLSS5_Feed.fx and called RecordExtractedFilesFromDir
                        CrashReporter.Log($"[NeuralRendering] Re-download complete. Feed.fx present: {File.Exists(feedFxStaged)}");
                    }
                }
            }

            // Build exclusion sets — deploy only lumenite_Kernel.fx from LumeniteFX, only DLSS5_Feed.fx from DLSS5Feeder
            var lumeniteExclude = _shaderPackService.GetPackShaderFiles(new[] { "LumeniteFX" })
                .Where(f => !f.Equals("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var feederExclude = _shaderPackService.GetPackShaderFiles(new[] { "DLSS5Feeder" })
                .Where(f => !f.Equals("DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var exclusions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LumeniteFX"]  = lumeniteExclude,
                ["DLSS5Feeder"] = feederExclude,
            };

            await Task.Run(() =>
            {
                _shaderPackService.DeployToGameFolder(installPath, new[] { "LumeniteFX", "DLSS5Feeder" }, exclusions);
                CrashReporter.Log($"[NeuralRendering] Deployed lumenite_Kernel.fx + DLSS5_Feed.fx to '{installPath}'");

                // Write DLSS5_MV_PROVIDER=3 to reshade.ini [GENERAL] PreprocessorDefinitions
                var iniPath = Path.Combine(installPath, "reshade.ini");
                if (File.Exists(iniPath))
                {
                    try
                    {
                        var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                        if (!ini.TryGetValue("GENERAL", out var general))
                        {
                            general = new AuxInstallService.OrderedDict();
                            ini["GENERAL"] = general;
                        }
                        if (general.TryGetValue("PreprocessorDefinitions", out var existing) && !string.IsNullOrEmpty(existing))
                        {
                            if (!existing.Contains("DLSS5_MV_PROVIDER", StringComparison.OrdinalIgnoreCase))
                                general["PreprocessorDefinitions"] = existing.TrimEnd(',') + ",DLSS5_MV_PROVIDER=3";
                        }
                        else
                        {
                            general["PreprocessorDefinitions"] = "DLSS5_MV_PROVIDER=3";
                        }
                        AuxInstallService.WriteIni(iniPath, ini);
                        CrashReporter.Log($"[NeuralRendering] Set DLSS5_MV_PROVIDER=3 in reshade.ini for '{installPath}'");

                        // Write ReShadePreset.ini with both techniques enabled, full TechniqueSorting order
                        const string lumeniteTech = "Lumenite_Kernel@lumenite_Kernel.fx";
                        const string feederTech   = "DLSS5_Feed@DLSS5_Feed.fx";
                        var presetPath = Path.Combine(installPath, "ReShadePreset.ini");
                        if (!File.Exists(presetPath))
                        {
                            var presetContent =
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "\r\n" +
                                "[DLSS5_Feed.fx]\r\n" +
                                "DEBUG_VIEW=0\r\n" +
                                "DEPTH_TOLERANCE=0.100000\r\n" +
                                "GEOM_AGREE_PX=-1.500000\r\n" +
                                "GEOM_DYNAMIC_MARGIN=0.250000\r\n" +
                                "GEOM_ENABLE=3\r\n" +
                                "GEOM_MASK_REJECTED=0.350000\r\n" +
                                "GEOM_OUTLIER_PX=4.000000\r\n" +
                                "GEOM_PARALLAX=1.020000\r\n" +
                                "LUMA_TOLERANCE=0.280000\r\n" +
                                "MASK_STRENGTH=1.000000\r\n" +
                                "MV_CONSISTENCY=1.400000\r\n" +
                                "MV_LOWRES_FILTER=0\r\n" +
                                "MV_PROVIDER_INFO=0\r\n" +
                                "MV_SCALE=1.000000\r\n" +
                                "MV_SIGN=1.000000,1.000000\r\n" +
                                "MV_VALIDATE=1\r\n" +
                                "STATIC_BIAS=0.150000\r\n" +
                                "STATIC_MIN_CONTRAST=0.012000\r\n" +
                                "VALIDATE_DEPTH=1\r\n" +
                                "VALIDATE_LUMA=0\r\n" +
                                "VALIDATE_MV=1\r\n" +
                                "VALIDATE_STATIC=1\r\n" +
                                "\r\n" +
                                "[GENERAL]\r\n" +
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n";
                            File.WriteAllText(presetPath, presetContent);
                            // Point reshade.ini at this preset
                            var rIni = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                            if (!rIni.TryGetValue("GENERAL", out var rg))
                            { rg = new AuxInstallService.OrderedDict(); rIni["GENERAL"] = rg; }
                            rg["PresetPath"] = ".\\ReShadePreset.ini";
                            AuxInstallService.WriteIni(iniPath, rIni);
                            CrashReporter.Log($"[NeuralRendering] Created ReShadePreset.ini for '{installPath}'");
                        }
                        else
                        {
                            // Overwrite existing preset — always use the canonical layout
                            // (Techniques/TechniqueSorting as top-level lines then [GENERAL] section)
                            var presetContent =
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "\r\n" +
                                "[DLSS5_Feed.fx]\r\n" +
                                "DEBUG_VIEW=0\r\n" +
                                "DEPTH_TOLERANCE=0.100000\r\n" +
                                "GEOM_AGREE_PX=-1.500000\r\n" +
                                "GEOM_DYNAMIC_MARGIN=0.250000\r\n" +
                                "GEOM_ENABLE=3\r\n" +
                                "GEOM_MASK_REJECTED=0.350000\r\n" +
                                "GEOM_OUTLIER_PX=4.000000\r\n" +
                                "GEOM_PARALLAX=1.020000\r\n" +
                                "LUMA_TOLERANCE=0.280000\r\n" +
                                "MASK_STRENGTH=1.000000\r\n" +
                                "MV_CONSISTENCY=1.400000\r\n" +
                                "MV_LOWRES_FILTER=0\r\n" +
                                "MV_PROVIDER_INFO=0\r\n" +
                                "MV_SCALE=1.000000\r\n" +
                                "MV_SIGN=1.000000,1.000000\r\n" +
                                "MV_VALIDATE=1\r\n" +
                                "STATIC_BIAS=0.150000\r\n" +
                                "STATIC_MIN_CONTRAST=0.012000\r\n" +
                                "VALIDATE_DEPTH=1\r\n" +
                                "VALIDATE_LUMA=0\r\n" +
                                "VALIDATE_MV=1\r\n" +
                                "VALIDATE_STATIC=1\r\n" +
                                "\r\n" +
                                "[GENERAL]\r\n" +
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n";
                            File.WriteAllText(presetPath, presetContent);
                            CrashReporter.Log($"[NeuralRendering] Overwrote ReShadePreset.ini for '{installPath}'");
                        }
                    }
                    catch (Exception iniEx) { CrashReporter.Log($"[NeuralRendering] reshade.ini/preset update failed — {iniEx.Message}"); }
                }
            }).ConfigureAwait(false);

            // Add to PerGameShaderSelection so SyncGameFolder keeps them deployed

            // Add DLSS5Feeder and LumeniteFX to PerGameShaderSelection so SyncGameFolder
            // keeps them deployed on every startup refresh.
            // Dict writes and save happen on the background install thread (no UI dependency).
            // card.ShaderModeOverride is dispatched to the UI thread separately.
            {
                // Persist pack-level exclusions (SetExcludedFiles acquires _settingsLock —
                // run synchronously here; we're already on a background thread).
                var lumeniteAllFiles = _shaderPackService.GetPackShaderFiles(new[] { "LumeniteFX" })
                    .Where(f => !f.Equals("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var feederAllFiles = _shaderPackService.GetPackShaderFiles(new[] { "DLSS5Feeder" })
                    .Where(f => !f.Equals("DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _shaderPackService.SetExcludedFiles("LumeniteFX", lumeniteAllFiles);
                _shaderPackService.SetExcludedFiles("DLSS5Feeder", feederAllFiles);

                var gameKey = Models.GameKey.From(card.GameName, card.Source ?? "").ToKey();
                var current = _gameNameService.PerGameShaderSelection.TryGetValue(gameKey, out var sel)
                    ? sel.ToList() : new List<string>();
                if (!current.Contains("DLSS5Feeder", StringComparer.OrdinalIgnoreCase))
                    current.Add("DLSS5Feeder");
                if (!current.Contains("LumeniteFX", StringComparer.OrdinalIgnoreCase))
                    current.Add("LumeniteFX");
                _gameNameService.PerGameShaderSelection[gameKey] = current;
                CrashReporter.Log($"[NeuralRendering.FeederShaders] Wrote PerGameShaderSelection['{gameKey}'] = [{string.Join(", ", current)}]");
                _window.ViewModel.SetPerGameShaderMode(card.GameName, "Select", card.Source ?? "");
                // Set ShaderModeOverride synchronously before DeployShadersForCard reads it.
                // TryEnqueue alone is too late — DeployShadersForCard fires on the same background
                // thread and reads card.ShaderModeOverride before the UI-thread dispatch runs.
                card.ShaderModeOverride = "Select";
                _window.DispatcherQueue?.TryEnqueue(() => card.ShaderModeOverride = "Select");
                _window.ViewModel.SaveSettingsPublic();
                // Deploy shaders with the now-persisted selection
                _window.ViewModel.DeployShadersForCard(card.GameName);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NeuralRendering] Shader deploy failed — {ex.Message}");
        }

        // ── DX9 games: deploy dgVoodoo2 (D3D9→DX11 translation) ──
        // dgVoodoo2 is required for ALL DX9 Feeder games — not just the Luma manifest list.
        // Fall back to GraphicsApi when DetectedApis is empty (e.g. cached DX9 game with empty All set).
        bool isDx9 = card.DetectedApis.Contains(GraphicsApiType.DirectX9)
                  || (card.DetectedApis.Count == 0 && card.GraphicsApi == GraphicsApiType.DirectX9);
        if (isDx9)
        {
            var manifest = _window.ViewModel.Manifest;
            if (manifest?.DgVoodooVersions?.Count > 0)
            {
                _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying dgVoodoo2...");
                try
                {
                    var dgSvc = App.Services.GetRequiredService<DgVoodooService>();
                    var versionEntry = manifest.DgVoodooVersions.First();
                    await dgSvc.EnsureStagedAsync(versionEntry.Key, versionEntry.Value).ConfigureAwait(false);
                    var dgDeployed = dgSvc.DeployToGame(installPath, versionEntry.Key, is64Bit: !card.Is32Bit);
                    if (dgDeployed.Count > 0)
                        CrashReporter.Log($"[NeuralRendering] dgVoodoo2 v{versionEntry.Key} deployed for Feeder on '{card.GameName}'");
                    else
                    {
                        CrashReporter.Log($"[NeuralRendering] dgVoodoo2 deploy returned no files for '{card.GameName}' — Windows Defender may be blocking the zip. Add %LocalAppData%\\RHI\\dgvoodoo\\ to Defender exclusions.");
                        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "⚠ dgVoodoo2 blocked by Defender");
                    }
                }
                catch (Exception dgEx)
                {
                    CrashReporter.Log($"[NeuralRendering] dgVoodoo2 deploy failed — {dgEx.Message}");
                }
            }
        }

        // ── host64\ folder — required for ALL 32-bit games ──
        // Contains: dlss5-feed-host64.exe, 64-bit ReShade dxgi.dll,
        //           renodx-dlss5.addon64, nvngx_dlssnr.dll, nvngx_dlss.dll
        // Note: NOT gated on isDx9 — host64 is needed for all 32-bit Feeder installs
        if (card.Is32Bit)
        {
                _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Setting up host64\\...");
                await Task.Run(async () =>
                {
                    try
                    {
                        var host64Dir = Path.Combine(installPath, "host64");
                        Directory.CreateDirectory(host64Dir);

                        // dlss5-feed-host64.exe
                        var stagedHostExe = FindStagedAddon(FeederPackageName, ".exe");
                        if (stagedHostExe != null && File.Exists(stagedHostExe))
                        {
                            File.Copy(stagedHostExe, Path.Combine(host64Dir, "dlss5-feed-host64.exe"), overwrite: true);
                            CrashReporter.Log($"[NeuralRendering] Deployed dlss5-feed-host64.exe to host64\\");
                        }
                        else
                        {
                            CrashReporter.Log("[NeuralRendering] dlss5-feed-host64.exe not found in staging — host64\\ will be incomplete");
                        }

                        // 64-bit ReShade as dxgi.dll (host64 runs as a 64-bit process and needs its own ReShade)
                        var rs64Path = Path.Combine(AuxInstallService.RsStagingDir, AuxInstallService.RsStaged64);
                        if (File.Exists(rs64Path))
                        {
                            File.Copy(rs64Path, Path.Combine(host64Dir, "dxgi.dll"), overwrite: true);
                            CrashReporter.Log($"[NeuralRendering] Deployed 64-bit ReShade to host64\\dxgi.dll");
                        }

                        // renodx-dlss5.addon64 (neural consumer for the host process)
                        var rdx5SvcH = App.Services.GetRequiredService<Renodx5AddonService>();
                        await rdx5SvcH.EnsureStagingAsync().ConfigureAwait(false);
                        if (rdx5SvcH.IsStagingReady)
                        {
                            File.Copy(rdx5SvcH.StagedFilePath, Path.Combine(host64Dir, "renodx-dlss5.addon64"), overwrite: true);
                            CrashReporter.Log($"[NeuralRendering] Deployed renodx-dlss5.addon64 to host64\\");
                        }

                        // nvngx_dlssnr.dll (NR runtime — same version as game folder)
                        var nrSelVerHost = nrVersionCombo != null
                            ? await DispatchAsync<string?>(_window.DispatcherQueue!, () => nrVersionCombo.SelectedItem as string).ConfigureAwait(false)
                            : null;
                        bool nrUseLatestHost = string.IsNullOrEmpty(nrSelVerHost) || nrSelVerHost.StartsWith("Latest");
                        string? cachedNrHost;
                        if (nrUseLatestHost)
                            cachedNrHost = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
                        else
                        {
                            var nrDirHost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "DLSS-NR", StripVersionSuffix(nrSelVerHost!));
                            cachedNrHost = ResolveCachedNrDllPath(nrSelVerHost!) ?? Path.Combine(nrDirHost, "nvngx_dlssnr.dll");
                            if (!File.Exists(cachedNrHost)) cachedNrHost = await dlssSvc.EnsureSpecificDlssnrCachedAsync(nrSelVerHost!).ConfigureAwait(false)
                                                                          ?? await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
                        }
                        if (cachedNrHost != null)
                        {
                            DeployNrDllSentinel(host64Dir, cachedNrHost);
                            CrashReporter.Log($"[NeuralRendering] Deployed nvngx_dlssnr.dll v{(nrUseLatestHost ? "latest" : nrSelVerHost)} to host64\\");
                        }

                        // nvngx_dlss.dll (DLSS SR runtime)
                        var cachedDlssH = await _dlssStreamlineService.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
                        if (cachedDlssH != null)
                        {
                            var dlssHost = Path.Combine(host64Dir, "nvngx_dlss.dll");
                            var dlssHostSentinel = dlssHost + ".original";
                            if (!File.Exists(dlssHostSentinel))
                                File.WriteAllBytes(dlssHostSentinel, Array.Empty<byte>());
                            File.Copy(cachedDlssH, dlssHost, overwrite: true);
                            CrashReporter.Log($"[NeuralRendering] Deployed nvngx_dlss.dll to host64\\");
                        }

                        CrashReporter.Log($"[NeuralRendering] host64\\ setup complete for '{card.GameName}'");
                    }
                    catch (Exception host64Ex)
                    {
                        CrashReporter.Log($"[NeuralRendering] host64\\ setup failed — {host64Ex.Message}");
                    }
                }).ConfigureAwait(false);
            }

        // Record Feeder's deployed files in rhi_install.txt
        {
            var feederFiles = new List<string> { card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64 };
            feederFiles.Add("nvngx_dlssnr.dll");
            feederFiles.Add("renodx-dlss5.addon64");
            feederFiles.Add("nvngx_dlss.dll");
            feederFiles.Add(@"reshade-shaders\Shaders\lumenite_Kernel.fx");
            feederFiles.Add(@"reshade-shaders\Shaders\DLSS5_Feed.fx");
            feederFiles.Add("ReShadePreset.ini");
            if (card.Is32Bit)
            {
                feederFiles.Add(@"host64\dlss5-feed-host64.exe");
                feederFiles.Add(@"host64\dxgi.dll");
                feederFiles.Add(@"host64\renodx-dlss5.addon64");
                feederFiles.Add(@"host64\nvngx_dlssnr.dll");
                feederFiles.Add(@"host64\nvngx_dlss.dll");
            }
            bool feederIsDx9 = card.DetectedApis.Contains(Models.GraphicsApiType.DirectX9)
                            || (card.DetectedApis.Count == 0 && card.GraphicsApi == Models.GraphicsApiType.DirectX9);
            if (feederIsDx9)
            {
                feederFiles.Add("D3D9.dll");
                feederFiles.Add("dgVoodoo.conf");
            }
            Models.RhiInstallManifest.SetComponent(installPath, "Feeder", feederFiles);
        }

        // Re-detect DLSS so the Nvidia Profile section shows the newly deployed DLLs.
        var feederDetection = _dlssStreamlineService.Detect(card.InstallPath ?? "");
        if (feederDetection.HasAny)
        {
            _dlssStreamlineService.RecordDlssFound(card.GameName);
            _dlssStreamlineService.RecordTrustedPath(card.GameName, feederDetection);
        }

        // Rebuild panel one final time now that shaders are deployed — status will show ✓ Feed.fx / ✓ LumeniteFX
        // RefreshDlssVersions reads file versions from disk — do it on a background thread first,
        // then update the card and rebuild panels on the UI thread so the DLSS/SL section shows
        // the correct installed NR DLL version.
        _ = Task.Run(async () =>
        {
            feederDetection = _dlssStreamlineService.Detect(card.InstallPath ?? "");
            var targetCard2 = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(card.GameName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(card.Source) || c.Source == card.Source));
            if (targetCard2 != null)
            {
                targetCard2.DlssDetection = feederDetection;
                targetCard2.ApplyDlssDetection(feederDetection);
                targetCard2.RefreshDlssVersions(_dlssStreamlineService); // updates DlssnrInstalledVersion from disk
            }
            await Task.Yield(); // yield before TryEnqueue so it runs after the version is up-to-date
            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                if (targetCard2 != null)
                {
                    BuildOverridesPanel(targetCard2);
                    BuildNvidiaProfileSection(targetCard2, targetCard2.GameName);
                }
            });
        });
    }
}
