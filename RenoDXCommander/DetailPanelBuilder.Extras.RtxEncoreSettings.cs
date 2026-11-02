// DetailPanelBuilder.Extras.RtxEncoreSettings.cs -- RTX Encore ⚙: settings worth choosing before launch
// (frame generation, XeFG, HUD/UI, upscaling, Smooth Motion, Neural Rendering core options, compatibility),
// the Neural Rendering DLL and the Intel XeSS files status. Settings are edited in place in
// rtx-encore.jsonc (RtxEncoreConfig), beside the mod. The overlay, NR styles/per-pass tuning, shortcuts
// and diagnostics stay in RTX Encore's in-game menu (Insert).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private static readonly string[] RtxOffOn = { "Off", "On" };
    private static readonly string[] RtxOffOnValues = { "false", "true" };

    /// <summary>⚙ dialog for RTX Encore. Every change is saved as soon as it's made.</summary>
    private async Task ShowRtxEncoreSettingsAsync(GameCardViewModel card, RtxEncoreService svc,
        string installPath, bool isInstalled, bool nrDeployed)
    {
        var gameName = card.GameName;
        var store    = card.Source ?? "";
        var installedAs = _window.ViewModel.GetRtxEncoreInstalledAs(gameName, store);
        string? modDir  = isInstalled && !string.IsNullOrEmpty(installedAs) ? svc.ModDir(installPath, installedAs) : null;
        string? cfgPath = modDir != null ? Path.Combine(modDir, RtxEncoreConfig.FileName) : null;
        bool canEdit = cfgPath != null;

        var cog = new CogDialog("RTX Encore", _window.Content.XamlRoot);
        cog.AddIntro("Set these before you play. Most need a game restart to apply (the tooltips say which). " +
                     "The overlay, Neural Rendering styles, shortcuts and diagnostics are in RTX Encore's in-game menu (Insert).");
        if (!canEdit)
            cog.AddNote("Install RTX Encore to change these settings.");
        else if (!File.Exists(cfgPath))
            cog.AddNote("This game has no rtx-encore.jsonc yet. Changing a setting creates one with just that setting; " +
                        "RTX Encore adds the rest with its defaults the first time the game starts.");
        if (card.IsRunning)
            cog.AddWarning("The game is running. Changes apply the next time it starts, and RTX Encore's in-game menu may overwrite them while it's open.");

        Button? openCfgBtn = null;

        // One dropdown bound to one setting in rtx-encore.jsonc. values are the file's values ("true", "4", "xefg").
        CogDialog.CompactCell Choice(string label, string help, string section, string key,
            string[] displays, string[] values, string defaultValue, Action<int>? after = null)
        {
            var current = canEdit ? RtxEncoreConfig.Get(cfgPath!, section, key) : null;
            int idx = Array.FindIndex(values, v => string.Equals(v, current ?? defaultValue, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = Array.FindIndex(values, v => string.Equals(v, defaultValue, StringComparison.OrdinalIgnoreCase));
            return cog.CompactCombo(label, help, displays, idx, i =>
            {
                if (!canEdit) return false;
                var v = values[i];
                bool raw = v is "true" or "false" || int.TryParse(v, out _);
                RtxEncoreConfig.Set(cfgPath!, section, key, raw ? v : "\"" + v + "\"");
                CrashReporter.Log($"[RtxEncoreCog] '{gameName}': {section}.{key} = {v}");
                if (openCfgBtn != null) openCfgBtn.IsEnabled = true;
                cog.SetSuccess("Saved. Restart the game if it's running.");
                after?.Invoke(i);
                return true;
            }, isEnabled: canEdit);
        }

        void MarkSaved() { if (openCfgBtn != null) openCfgBtn.IsEnabled = true; }

        // A number box bound to one setting in rtx-encore.jsonc
        CogDialog.CompactCell Number(string label, string help, string section, string key, int min, int max, int defaultValue) =>
            new(label, help, BuildRtxEncoreNumberBox(cog, cfgPath, gameName, section, key, label, min, max, defaultValue, MarkSaved));

        // ── Frame generation ────────────────────────────────────────────────────
        cog.AddSection("Frame generation");
        var xefgControls = new List<Control>();
        void SetXefgEnabled(bool on) { foreach (var c in xefgControls) c.IsEnabled = canEdit && on; }
        ComboBox? multiplierCombo = null;
        var dynamicControls = new List<Control>();

        var engineCell = Choice("Engine",
            "Game keeps the game's own frame generation. XeFG replaces the game's DLSS frame generation with Intel XeSS frame generation. " +
            "DLSS-FG replaces a game's own Intel frame generation with DLSS frame generation. None of them adds frame generation to a game that has none. Restart the game to apply.",
            "renderingEngines", "frameGenerationEngine",
            new[] { "Game", "DLSS-FG", "XeFG" }, new[] { "game", "dlss-fg", "xefg" }, "game",
            i => SetXefgEnabled(i == 2));
        var modeCell = Choice("Mode",
            "Game decides: the game (or NVIDIA Profile Inspector) chooses the multiplier. Fixed: the multiplier next to it. " +
            "Dynamic: frame generation adapts toward a target frame rate (the monitor's refresh rate by default).",
            "frameGeneration", "mode",
            new[] { "Game decides", "Fixed", "Dynamic" }, new[] { "game", "fixed", "dynamic" }, "game",
            i =>
            {
                if (multiplierCombo != null) multiplierCombo.IsEnabled = canEdit && i == 1;
                foreach (var c in dynamicControls) c.IsEnabled = canEdit && i == 2;
            });
        var multiplierCell = Choice("Multiplier",
            "Used in Fixed mode. 5x and 6x are experimental. On an 8 GB card, 2x or 3x is the safer starting point.",
            "frameGeneration", "multiplier",
            new[] { "2x", "3x", "4x", "5x", "6x" }, new[] { "2", "3", "4", "5", "6" }, "4");
        multiplierCombo = (ComboBox)multiplierCell.Control;
        multiplierCombo.IsEnabled = canEdit && ((ComboBox)modeCell.Control).SelectedIndex == 1;
        var presetCell = Choice("DLSS FG preset",
            "Game: no override. A and B use normal frame generation; C uses the faster Lite mode (with some image-quality trade-offs) and needs DLSS FG 310.9.16. " +
            "B needs the game's HUD-less inputs. Restart the game to apply.",
            "dlssPresets", "dlssFgPreset",
            new[] { "Game", "A", "B", "C" }, new[] { "game", "a", "b", "c" }, "game");
        var flipCell = Choice("Flip metering bypass",
            "Streamline paces the generated frames itself instead of NVIDIA's flip metering. Try it when 3x or 4x looks choppy while 2x is smooth. Restart the game to apply.",
            "frameGeneration", "flipMeteringBypass", RtxOffOn, RtxOffOnValues, "false");
        var fpsCell = Number("Frame limit (FPS)",
            "1 to 1000 FPS, or 0 for no RTX Encore limit. Applied through Reflex, or through XeLL with XeFG (needs a restart then). Generated frames may count toward the limit. Press Enter or click away to save.",
            "nativeMenuFeatures", "reflexFrameLimit", 0, 1000, 0);
        var targetCell = Number("Dynamic target FPS",
            "Used in Dynamic mode: the frame rate frame generation adapts toward. 0 follows the refresh rate of the monitor showing the game. Press Enter or click away to save.",
            "frameGeneration", "dynamicTargetFrameRate", 0, 1000, 0);
        var exp56Cell = Choice("Dynamic up to 6x",
            "Lets Dynamic mode go up to 5x and 6x (needs plenty of VRAM). Off: Dynamic stays at 4x or less.",
            "frameGeneration", "dynamicExperimental56", RtxOffOn, RtxOffOnValues, "false");
        dynamicControls.AddRange(new[] { targetCell.Control, exp56Cell.Control }.OfType<Control>());
        bool isDynamic = ((ComboBox)modeCell.Control).SelectedIndex == 2;
        foreach (var c in dynamicControls) c.IsEnabled = canEdit && isDynamic;
        cog.AddCompactGrid(engineCell, modeCell, multiplierCell, presetCell, targetCell, exp56Cell, flipCell, fpsCell);

        // ── XeFG ────────────────────────────────────────────────────────────────
        cog.AddSection("XeFG engine");
        var tensorCell = Choice("NVIDIA Tensor Cores (experimental)",
            "Runs XeFG on NVIDIA Tensor Cores. RTX 30, 40 and 50 on NVIDIA driver 621.01 or the 621.12 hotfix; only the RTX 4090 has been tested in games. " +
            "If it can't be used, standard XeFG runs instead.",
            "xefg", "xefgTensorCores", RtxOffOn, RtxOffOnValues, "false");
        var pacingCell = Choice("Variable pacing above 2x",
            "Paces XeFG above 2x using the game's real frame timing. Off can make multipliers above 2x unavailable. Restart the game to apply.",
            "xefg", "xefgMfg4VariablePacing", RtxOffOn, RtxOffOnValues, "true");
        var uiCell = Choice("HUD/UI handling",
            "Interpolate: the HUD is generated along with the scene. Game layers: uses the game's separate HUD-less and UI images, when the game provides them. Restart the game to apply.",
            "xefg", "xefgUiHandling", new[] { "Interpolate", "Game layers" }, new[] { "interpolate", "game" }, "interpolate");
        var alphaCell = Choice("UI alpha",
            "How the game's UI layer stores transparency, for Game layers. Leave Unknown unless you know it: Unknown never guesses and doesn't use UI layers. Restart the game to apply.",
            "xefg", "xefgUiAlpha", new[] { "Unknown", "Straight", "Premultiplied" }, new[] { "unknown", "straight", "premultiplied" }, "unknown");
        xefgControls.AddRange(new[] { tensorCell.Control, pacingCell.Control, uiCell.Control, alphaCell.Control }.OfType<Control>());
        SetXefgEnabled(((ComboBox)engineCell.Control).SelectedIndex == 2);
        cog.AddCompactGrid(tensorCell, pacingCell, uiCell, alphaCell);
        cog.AddNote("These only apply when Engine is XeFG.");

        // ── Image quality ───────────────────────────────────────────────────────
        cog.AddSection("Generated frame quality");
        cog.AddCompactGrid(
            Choice("High-multiplier quality",
                "Keeps generated frames clean from 3x to 6x. Leave on. Restart the game to apply.",
                "imageQuality", "highMultiplierQuality", RtxOffOn, RtxOffOnValues, "true"),
            Choice("Frame protection",
                "Protects generated frames against tearing on thin geometry (fences, wires, foliage) and ghosting in moving shadows. No effect with the Lite preset. Restart the game to apply.",
                "imageQuality", "frameProtection", RtxOffOn, RtxOffOnValues, "true"),
            Choice("Fast frame generation",
                "Lowers the GPU cost of each generated frame, with the same image. Restart the game to apply.",
                "imageQuality", "fastFrameGeneration", RtxOffOn, RtxOffOnValues, "true"),
            Choice("Protection tuning",
                "Refined (default) or Classic. Restart the game to apply.",
                "imageQuality", "protectionTuning", new[] { "Refined", "Classic" }, new[] { "refined", "classic" }, "refined"));

        // ── HUD and UI ──────────────────────────────────────────────────────────
        cog.AddSection("HUD and UI (DLSS FG)");
        cog.AddCompactGrid(
            Choice("Auto UI recomposition",
                "Turns DLSS FG's UI recomposition on when the game provides HUD-less images, even if the game doesn't ask for it. Off follows the game. " +
                "Turn it off in No Man's Sky (duplicated generated frames).",
                "hudUi", "autoUiRecomposition", RtxOffOn, RtxOffOnValues, "true"),
            Choice("UI assist",
                "DirectX 12: captures the HUD-less scene and builds the UI layer itself when the game doesn't provide them.",
                "hudUi", "uiAssist", RtxOffOn, RtxOffOnValues, "true"),
            Choice("Force UI recomposition",
                "Asks DLSS FG to recompose the HUD separately even when the game doesn't request it.",
                "hudUi", "forceUiRecomposition", RtxOffOn, RtxOffOnValues, "false"));

        // ── Upscaling ───────────────────────────────────────────────────────────
        cog.AddSection("Upscaling");
        var customScaleCell = Number("Custom render scale (%)",
            "Render scale in percent (50 to 100) when DLSS render scale is Custom. Press Enter or click away to save.",
            "dlssSuperResolution", "dlssCustomScale", 50, 100, 67);
        var renderScaleCell = Choice("DLSS render scale",
            "Forces the DLSS render resolution. Game: no override. The game must already use DLSS.",
            "dlssSuperResolution", "dlssRenderScale",
            new[] { "Game", "DLAA", "Quality", "Balanced", "Performance", "Ultra Performance", "Custom" },
            new[] { "game", "dlaa", "quality", "balanced", "performance", "ultra-performance", "custom" }, "game",
            i => ((Control)customScaleCell.Control).IsEnabled = canEdit && i == 6);
        ((Control)customScaleCell.Control).IsEnabled = canEdit && ((ComboBox)renderScaleCell.Control).SelectedIndex == 6;
        cog.AddCompactGrid(
            Choice("Upscaler",
                "Game keeps the game's DLSS Super Resolution. XeSS replaces compatible DLSS Super Resolution with Intel XeSS. " +
                "Applies live in DirectX; Vulkan needs XeSS set before the game starts.",
                "renderingEngines", "reconstructionEngine", new[] { "Game", "XeSS" }, new[] { "game", "xess" }, "game"),
            Choice("XeSS on Tensor Cores (experimental)",
                "Runs XeSS Super Resolution on NVIDIA Tensor Cores (RTX 30, 40 and 50, DirectX 12, driver 621.01 or 621.12; tested on the RTX 4090). " +
                "If it can't be used, standard XeSS runs instead. Applies live.",
                "xess", "xessTensorCores", RtxOffOn, RtxOffOnValues, "false"),
            renderScaleCell,
            customScaleCell,
            Choice("Fast Super Resolution",
                "RTX 30 only, DirectX 12: faster DLSS Super Resolution for presets L and M. The image is very close to NVIDIA's but not identical. Restart the game to apply.",
                "dlssSuperResolution", "fastSuperResolution", RtxOffOn, RtxOffOnValues, "false"),
            Choice("DLSS SR preset",
                "Game: no override. Applies to all quality modes; which presets work depends on the game's DLSS DLL. A to F are legacy. Applies live.",
                "dlssPresets", "dlssSrPreset",
                new[] { "Game", "Default", "A", "B", "C", "D", "E", "F", "J", "K", "L", "M" },
                new[] { "game", "default", "a", "b", "c", "d", "e", "f", "j", "k", "l", "m" }, "game"),
            Choice("DLSS RR preset",
                "Ray Reconstruction preset. Game: no override. A to C are legacy; D, E and F need nvngx_dlssd 310.9.1 or newer. Doesn't turn RR on. Applies live.",
                "dlssPresets", "dlssRrPreset",
                new[] { "Game", "Default", "A", "B", "C", "D", "E", "F" },
                new[] { "game", "default", "a", "b", "c", "d", "e", "f" }, "game"),
            Choice("SR HDR input",
                "Colour input mode for the upscaler. Game: follow the game. On: HDR linear input. Off: SDR input. Doesn't turn on display HDR. Restart the game to apply.",
                "superResolutionInputs", "srHdr", new[] { "Game", "On", "Off" }, new[] { "game", "on", "off" }, "game"),
            Choice("SR auto exposure",
                "Upscaler auto exposure. Game: follow the game. Some DLSS presets (L) force it. XeSS auto exposure costs GPU time and isn't recommended for SDR. Restart the game to apply.",
                "superResolutionInputs", "srAutoExposure", new[] { "Game", "On", "Off" }, new[] { "game", "on", "off" }, "game"));

        // ── Smooth Motion ───────────────────────────────────────────────────────
        cog.AddSection("Smooth Motion");
        var gameApiValue = card.GraphicsApi switch
        {
            GraphicsApiType.DirectX11 => "d3d11",
            GraphicsApiType.Vulkan    => "vulkan",
            _                         => "d3d12",
        };
        var apiDisplays = new[] { "DirectX 12", "DirectX 11", "Vulkan" };
        var apiValues   = new[] { "d3d12", "d3d11", "vulkan" };
        var smCell = Choice("Smooth Motion",
            "NVIDIA's driver frame interpolation, also for games without frame generation. RTX 30 (DirectX 11, 12, Vulkan) and experimental RTX 20 (DirectX 12, driver 621.01); " +
            "RTX 40 and 50 get it from NVIDIA. Needs one of the drivers RTX Encore lists. Restart the game to apply.",
            "smoothMotion", "smoothMotionEnabled", RtxOffOn, RtxOffOnValues, "false",
            i =>
            {
                // Turning it on in a file without an API yet: use the game's own API, not RTX Encore's DirectX 12 default
                if (i == 1 && canEdit && RtxEncoreConfig.Get(cfgPath!, "smoothMotion", "smoothMotionApi") == null)
                    RtxEncoreConfig.SetString(cfgPath!, "smoothMotion", "smoothMotionApi", gameApiValue);
            });
        var smApiCell = Choice("Graphics API",
            "The game's graphics API, which Smooth Motion needs to know. Restart the game to apply.",
            "smoothMotion", "smoothMotionApi", apiDisplays, apiValues, gameApiValue);
        cog.AddCompactGrid(smCell, smApiCell);

        // ── Neural Rendering DLL ────────────────────────────────────────────────
        cog.AddSection("Neural Rendering");
        AddRtxEncoreNrSetting(cog, card, svc, installPath, isInstalled, nrDeployed);
        cog.AddCompactGrid(
            Choice("Neural Rendering",
                "Turns DLSS Neural Rendering on. Needs the NR DLL above. The first launch prepares it in the background. Applies live.",
                "neuralRendering/core", "nrEnabled", RtxOffOn, RtxOffOnValues, "false"),
            Choice("NR model",
                "B (default): smaller and much faster. A: the larger network of NR 310.8.0. Auto: the DLL's choice (B). Applies live.",
                "dlssPresets", "dlssNrPreset", new[] { "B", "A", "Auto" }, new[] { "b", "a", "auto" }, "b"),
            Choice("NR precision",
                "RTX 20 and 30: Exact gives the same output as NVIDIA; Fast is quicker with small image differences. No effect on RTX 40 and 50. Restart the game to apply.",
                "neuralRendering/optimizations", "nrPrecision", new[] { "Exact", "Fast" }, new[] { "exact", "fast" }, "exact"),
            Choice("NR fast processing",
                "Faster Neural Rendering processing with exactly the same output. Restart the game to apply.",
                "neuralRendering/optimizations", "nrFastProcessing", RtxOffOn, RtxOffOnValues, "true"));

        // ── Compatibility ───────────────────────────────────────────────────────
        cog.AddSection("Compatibility");
        cog.AddCompactGrid(
            Choice("GPU series",
                "Leave on Auto unless RTX Encore detects your GPU wrongly. Restart the game to apply.",
                "general", "gpuSeries", new[] { "Auto", "RTX 40", "RTX 30", "RTX 20" }, new[] { "auto", "rtx40", "rtx30", "rtx20" }, "auto"),
            Choice("NVIDIA App FG models",
                "Loads the frame generation models the NVIDIA App downloaded. Restart the game to apply.",
                "compatibility", "forceOTA", RtxOffOn, RtxOffOnValues, "false"),
            Choice("Disable in-game menu",
                "For games that reject RTX Encore's in-game menu (Star Wars Outlaws). Frame generation keeps working; change settings here instead. Restart the game to apply.",
                "compatibility", "disableInGameMenu", RtxOffOn, RtxOffOnValues, "false"),
            Choice("FG in menus",
                "Keeps frame generation running in menus and loading screens. Leave Off: idling at 1x there avoids device-hang crashes.",
                "compatibility", "disableMenuDetection", RtxOffOn, RtxOffOnValues, "false"),
            Choice("Ray-traced HairWorks",
                "Prepares the ray-traced HairWorks conversion in games with a verified profile. The game's own hair setting still decides. Restart the game to apply.",
                "nativeMenuFeatures", "hairEnabled", RtxOffOn, RtxOffOnValues, "true"));

        // ── Intel XeSS files ────────────────────────────────────────────────────
        if (modDir != null)
        {
            if (Rtx40MfgService.HasEncoreXessFiles(modDir))
                cog.AddNote($"Intel XeSS files for the XeFG engine are installed in {Rtx40MfgService.EncoreRuntimeRelDir}.");
            else
                cog.AddWarning("Intel XeSS files for the XeFG engine (libxess_fg.dll and libxell.dll) aren't installed. Reinstall RTX Encore to add them.");
        }

        openCfgBtn = cog.MakeOpenFileButton("Open rtx-encore.jsonc", cfgPath);
        cog.AddButtons(openCfgBtn,
            cog.MakeButton("Open GitHub page", CogButtonKind.Secondary,
                async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(RtxEncoreService.RepoUrl)); }));

        await cog.ShowAsync();
    }

    /// <summary>Whole-number box for one rtx-encore.jsonc setting: saves on Enter and when focus leaves.</summary>
    private static TextBox BuildRtxEncoreNumberBox(CogDialog cog, string? cfgPath, string gameName,
        string section, string key, string label, int min, int max, int defaultValue, Action onSaved)
    {
        var fallback = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var current = cfgPath != null ? RtxEncoreConfig.Get(cfgPath, section, key) : null;
        var box = CogDialog.MakeTextBox(current ?? fallback, fallback);
        box.IsEnabled = cfgPath != null;
        string saved = box.Text;
        void Save()
        {
            if (cfgPath == null) return;
            var text = box.Text.Trim();
            if (text == saved) return;
            if (!int.TryParse(text, out var value) || value < min || value > max)
            {
                cog.SetError($"{label} must be a number from {min} to {max}.");
                box.Text = saved;
                return;
            }
            try
            {
                RtxEncoreConfig.SetInt(cfgPath, section, key, value);
                saved = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                box.Text = saved;
                CrashReporter.Log($"[RtxEncoreCog] '{gameName}': {section}.{key} = {value}");
                onSaved();
                cog.SetSuccess("Saved. Restart the game if it's running.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                CrashReporter.Log($"[RtxEncoreCog] {section}.{key} save failed — {ex.Message}");
                cog.SetError($"Couldn't save {label}: {ex.Message}");
                box.Text = saved;
            }
        }
        box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Save(); e.Handled = true; } };
        box.LostFocus += (_, _) => Save();
        return box;
    }

    /// <summary>The "Deploy NR DLL" setting: copies nvngx_dlssnr.dll 310.9.16 beside RTX Encore, or removes it.</summary>
    private void AddRtxEncoreNrSetting(CogDialog cog, GameCardViewModel card, RtxEncoreService svc,
        string installPath, bool isInstalled, bool nrDeployed)
    {
        var gameName = card.GameName;
        var store    = card.Source ?? "";
        bool nrOn = nrDeployed;
        bool suppress = false;
        ComboBox? nrCombo = null;

        void Revert()
        {
            if (nrCombo == null) return;
            suppress = true;
            nrCombo.SelectedIndex = nrOn ? 1 : 0;
            suppress = false;
        }

        async Task ApplyAsync(bool enable)
        {
            if (enable == nrOn) return;
            _window.ViewModel.SetRtxEncoreNrDllDeployed(gameName, enable, store);
            if (!isInstalled)
            {
                nrOn = enable;
                cog.SetSuccess(enable ? "Saved. nvngx_dlssnr.dll will be deployed when RTX Encore is installed." : "Saved.");
                return;
            }
            if (enable)
            {
                if (nrCombo != null) nrCombo.IsEnabled = false;
                try
                {
                    if (svc.GetNrDllCachedPath() == null)
                        cog.SetStatus($"Downloading nvngx_dlssnr.dll {RtxEncoreService.NrRequiredVer}...");
                    var cached = await svc.EnsureNrDllCachedAsync(App.Services.GetRequiredService<IDlssStreamlineService>());
                    if (cached == null)
                    {
                        CrashReporter.Log($"[RtxEncoreCog] NR {RtxEncoreService.NrRequiredVer} not available — not deployed");
                        _window.ViewModel.SetRtxEncoreNrDllDeployed(gameName, false, store);
                        Revert();
                        cog.SetError($"Couldn't download nvngx_dlssnr.dll {RtxEncoreService.NrRequiredVer}. Nothing was deployed.");
                        return;
                    }
                    if (await Task.Run(() => svc.DeployNrDll(installPath)))
                    {
                        nrOn = true;
                        cog.SetSuccess($"Deployed nvngx_dlssnr.dll {RtxEncoreService.NrRequiredVer} to the game folder.");
                    }
                    else
                    {
                        _window.ViewModel.SetRtxEncoreNrDllDeployed(gameName, false, store);
                        Revert();
                        cog.SetError("Couldn't copy nvngx_dlssnr.dll into the game folder.");
                    }
                    RequestExtrasRebuild(card);
                }
                finally
                {
                    if (nrCombo != null) nrCombo.IsEnabled = true;
                }
            }
            else
            {
                svc.RemoveNrDll(installPath);
                nrOn = false;
                cog.SetSuccess("Removed nvngx_dlssnr.dll from the game folder.");
                RequestExtrasRebuild(card);
            }
        }

        nrCombo = cog.AddCombo("Deploy NR DLL",
            $"Copies NVIDIA's Neural Rendering DLL (nvngx_dlssnr.dll {RtxEncoreService.NrRequiredVer}) into the game folder for RTX Encore's Neural Rendering. " +
            "Turn Neural Rendering on in RTX Encore's in-game menu. If RTX Encore isn't installed yet, the choice is saved and applied when you install it.",
            new[] { "No", "Yes" }, nrOn ? 1 : 0, i =>
            {
                if (suppress) return true;
                _ = ApplySafeAsync(i == 1);
                return true;
            });

        async Task ApplySafeAsync(bool enable)
        {
            try { await ApplyAsync(enable); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[RtxEncoreCog] NR DLL change failed — {ex.Message}");
                cog.SetError($"Couldn't change the NR DLL: {ex.Message}");
            }
        }
    }
}
