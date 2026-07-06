// DetailPanelBuilder.Overrides.NvidiaProfile.cs — Nvidia Profile Overrides header + DLSS/Streamline section.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildNvidiaProfileSection(GameCardViewModel card, string capturedName)
    {
        // ══════════════════════════════════════════════════════════════════════
        // Nvidia Profile Overrides — separate section below Overrides
        // DLSS / Streamline / ReBAR + future additions
        // ══════════════════════════════════════════════════════════════════════
        _window.NvidiaProfilePanel.Children.Clear();
        var nvidiaHeaderText = "Nvidia Profile Overrides";
        var driverVer = _dlssPresetService.DriverVersionString;
        if (!string.IsNullOrEmpty(driverVer))
            nvidiaHeaderText += $" — Driver {driverVer}";
        _window.NvidiaProfilePanel.Children.Add(new TextBlock
        {
            Text = nvidiaHeaderText,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        if (card.HasAnyDlssStreamline)
        {
            var dlssService = _dlssStreamlineService;
            var presetService = _dlssPresetService;
            bool hasDlss = card.HasDlss;
            bool hasDlssd = card.HasDlssd;
            bool hasDlssg = card.HasDlssg;
            bool hasStreamline = card.HasStreamline;

            var dlssRowGrid = new Grid { ColumnSpacing = 12 };
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // SR column
            // Disable for DLSS 1.x (not compatible with 2.x+ versions in manifest)
            bool srEnabled = hasDlss && !(card.DlssInstalledVersion?.StartsWith("1.") == true);
            bool srDriverOverride = presetService.IsSupported && presetService.IsSrDriverOverrideActive(card.GameName, card.InstallPath ?? "");
            var srCol = BuildDlssColumn("DLSS Super Resolution", srEnabled, dlssService.DlssVersions,
                card.DlssInstalledVersion, DlssPresetService.SrPresets,
                presetService.IsSupported && srEnabled ? presetService.GetSrPreset(card.GameName, card.InstallPath) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssPath);
                    else await dlssService.SwapDlssAsync(tc.DlssDetection.DlssPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetSrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                currentRenderScale: presetService.IsSupported && srEnabled ? presetService.GetSrRenderScale(card.GameName, card.InstallPath) : 0u,
                onRenderScaleSelected: (pct) => { presetService.SetSrRenderScale(card.GameName, card.InstallPath, pct); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssVersion,
                driverOverrideActive: srDriverOverride);
            Grid.SetColumn(srCol, 0);
            dlssRowGrid.Children.Add(srCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(1));

            // RR column
            bool rrDriverOverride = presetService.IsSupported && presetService.IsRrDriverOverrideActive(card.GameName, card.InstallPath ?? "");
            var rrCol = BuildDlssColumn("Ray Reconstruction", hasDlssd, dlssService.DlssdVersions,
                card.DlssdInstalledVersion, DlssPresetService.RrPresets,
                presetService.IsSupported && hasDlssd ? presetService.GetRrPreset(card.GameName, card.InstallPath) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssdPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssdPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssdPath);
                    else await dlssService.SwapDlssdAsync(tc.DlssDetection.DlssdPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetRrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                currentRenderScale: presetService.IsSupported && hasDlssd ? presetService.GetRrRenderScale(card.GameName, card.InstallPath) : 0u,
                onRenderScaleSelected: (pct) => { presetService.SetRrRenderScale(card.GameName, card.InstallPath, pct); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssdVersion,
                driverOverrideActive: rrDriverOverride);
            Grid.SetColumn(rrCol, 2);
            dlssRowGrid.Children.Add(rrCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(3));

            // FG column — no v1.x guard (FG can be updated from v1.0.0 to newer versions)
            bool fgEnabled = hasDlssg;
            bool fgDriverOverride = presetService.IsSupported && presetService.IsFgDriverOverrideActive(card.GameName, card.InstallPath ?? "");
            var fgCol = BuildDlssColumn("Frame Generation", fgEnabled, dlssService.DlssgVersions,
                card.DlssgInstalledVersion, DlssPresetService.FgPresets,
                presetService.IsSupported && fgEnabled ? presetService.GetFgPreset(card.GameName, card.InstallPath) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssgPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssgPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssgPath);
                    else await dlssService.SwapDlssgAsync(tc.DlssDetection.DlssgPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetFgPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssgVersion,
                driverOverrideActive: fgDriverOverride);

            // Add Multi Frame Generation button to FG column
            fgCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            var mfgBtn = new Button
            {
                Content = "Multi Frame Gen",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = fgEnabled && presetService.IsSupported,
                Opacity = (fgEnabled && presetService.IsSupported) ? 1.0 : 0.4,
            };
            ToolTipService.SetToolTip(mfgBtn, "Configure NVIDIA Multi Frame Generation: mode, frame count multiplier, and dynamic target frame rate. Requires 50 Series GPU.");
            mfgBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                await MfgDialog.ShowAsync(
                    presetService,
                    _window.ViewModel.Settings,
                    capturedName,
                    card.InstallPath ?? "",
                    xamlRoot,
                    () => _window.ViewModel.SaveSettingsPublic());
                // Rebuild panel so Restore All button reflects MFG changes
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(refreshCard));
            };
            fgCol.Children.Add(mfgBtn);

            Grid.SetColumn(fgCol, 4);
            dlssRowGrid.Children.Add(fgCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(5));

            // SL column (no preset)
            // Disable for Streamline v1.x (not compatible with v2.x+ versions in manifest)
            bool slEnabled = hasStreamline && !(card.StreamlineInstalledVersion?.StartsWith("1.") == true);
            // Check if custom Streamline marker exists — override version to "Custom"
            var slInstalledVersion = (hasStreamline && !string.IsNullOrEmpty(card.DlssDetection?.StreamlineFolder)
                && DlssStreamlineService.IsCustomStreamlineActive(card.DlssDetection.StreamlineFolder))
                ? "Custom"
                : card.StreamlineInstalledVersion;
            var slCol = BuildDlssColumn("Streamline", slEnabled, dlssService.StreamlineVersions,
                slInstalledVersion, null, 0,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.StreamlineFolder == null) return;
                    if (version == "Default") dlssService.RestoreStreamline(tc.DlssDetection.StreamlineFolder);
                    else if (version == "Custom") await dlssService.SwapStreamlineCustomAsync(tc.DlssDetection.StreamlineFolder);
                    else await dlssService.SwapStreamlineAsync(tc.DlssDetection.StreamlineFolder, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                null,
                originalVersion: card.DlssDetection?.OriginalStreamlineVersion);

            // Add Restore All button into the SL column (fills the preset slot)
            // Enabled when any backup exists OR any preset is non-default
            bool hasNonDefaultPreset = (presetService.IsSupported && hasDlss && presetService.GetSrPreset(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssd && presetService.GetRrPreset(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssg && presetService.GetFgPreset(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlss && presetService.GetSrRenderScale(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssd && presetService.GetRrRenderScale(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssg && presetService.GetMfgMode(card.GameName, card.InstallPath) != 0);
            bool restoreEnabled = card.HasAnyDlssBackup || hasNonDefaultPreset;
            var dlssRestoreBtn = new Button
            {
                Content = "Restore DLSS/SL",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = restoreEnabled,
            };
            dlssRestoreBtn.Click += (s, ev) =>
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard?.DlssDetection != null)
                {
                    dlssService.RestoreAll(targetCard.DlssDetection);
                    presetService.SetSrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetRrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetFgPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetSrRenderScale(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetRrRenderScale(targetCard.GameName, targetCard.InstallPath, 0);
                    // Reset MFG settings
                    presetService.SetMfgMode(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgGenerationFactor(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgDynamicMaxCount(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgDynamicTargetFps(targetCard.GameName, targetCard.InstallPath, 0);
                    targetCard.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(targetCard));
                }
            };
            // Add spacer label to align buttons with the Preset/RenderScale rows in other columns
            slCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });

            // Quick Apply button (created below, added here after creation)
            // Spacer + Restore All (added after Quick Apply is created)
            var hasDefaults = !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssdVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssgVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultStreamlineVersion)
                || _window.ViewModel.Settings.DefaultSrPreset != 0
                || _window.ViewModel.Settings.DefaultRrPreset != 0
                || _window.ViewModel.Settings.DefaultFgPreset != 0
                || _window.ViewModel.Settings.DefaultSrRenderScale != 0
                || _window.ViewModel.Settings.DefaultRrRenderScale != 0;

            var applyBtn = new Button
            {
                Content = "Quick Apply",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = hasDefaults && card.HasAnyDlssStreamline,
            };
            ToolTipService.SetToolTip(applyBtn, "Apply your configured DLSS/Streamline default versions, presets, and render scales to this game. Downloads versions on-demand if not cached.");
            applyBtn.Click += async (s, ev) =>
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard?.DlssDetection == null) return;

                var settings = _window.ViewModel.Settings;
                var svc = _dlssStreamlineService;
                var pSvc = _dlssPresetService;

                // Check driver override state — skip DLL swaps for overridden components
                bool srOverride = pSvc.IsSupported && pSvc.IsSrDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");
                bool rrOverride = pSvc.IsSupported && pSvc.IsRrDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");
                bool fgOverride = pSvc.IsSupported && pSvc.IsFgDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");

                if (!string.IsNullOrEmpty(settings.DefaultDlssVersion) && targetCard.HasDlss && targetCard.DlssDetection.DlssPath != null
                    && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true) && !srOverride)
                    await svc.SwapDlssAsync(targetCard.DlssDetection.DlssPath, settings.DefaultDlssVersion);
                if (!string.IsNullOrEmpty(settings.DefaultDlssdVersion) && targetCard.HasDlssd && targetCard.DlssDetection.DlssdPath != null
                    && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true) && !rrOverride)
                    await svc.SwapDlssdAsync(targetCard.DlssDetection.DlssdPath, settings.DefaultDlssdVersion);
                if (!string.IsNullOrEmpty(settings.DefaultDlssgVersion) && targetCard.HasDlssg && targetCard.DlssDetection.DlssgPath != null
                    && !fgOverride)
                    await svc.SwapDlssgAsync(targetCard.DlssDetection.DlssgPath, settings.DefaultDlssgVersion);
                if (!string.IsNullOrEmpty(settings.DefaultStreamlineVersion) && targetCard.HasStreamline && targetCard.DlssDetection.StreamlineFolder != null
                    && !(targetCard.StreamlineInstalledVersion?.StartsWith("1.") == true))
                    await svc.SwapStreamlineAsync(targetCard.DlssDetection.StreamlineFolder, settings.DefaultStreamlineVersion);

                if (settings.DefaultSrPreset != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetSrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultSrPreset);
                if (settings.DefaultRrPreset != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetRrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultRrPreset);
                if (settings.DefaultFgPreset != 0 && targetCard.HasDlssg)
                    pSvc.SetFgPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultFgPreset);

                if (settings.DefaultSrRenderScale != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetSrRenderScale(targetCard.GameName, targetCard.InstallPath, settings.DefaultSrRenderScale);
                if (settings.DefaultRrRenderScale != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetRrRenderScale(targetCard.GameName, targetCard.InstallPath, settings.DefaultRrRenderScale);

                targetCard.RefreshDlssVersions(svc);
                _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(targetCard));
            };

            // Add buttons to SL column: Quick Apply first, then spacer, then Restore All at bottom
            slCol.Children.Add(applyBtn);
            slCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            slCol.Children.Add(dlssRestoreBtn);

            // Override column opacity so buttons aren't dimmed by the SL column's 0.4 opacity.
            // Manually dim the SL label and version combo if Streamline isn't present.
            if ((hasDefaults || restoreEnabled) && !slEnabled)
            {
                slCol.Opacity = 1.0;
                // Dim the SL-specific children (label, version sub-label, combo, etc.) but not buttons
                foreach (var child in slCol.Children.OfType<UIElement>())
                {
                    if (child != applyBtn && child != dlssRestoreBtn)
                        child.Opacity = 0.4;
                }
            }
            ToolTipService.SetToolTip(dlssRestoreBtn, "Restore all DLSS and Streamline DLLs to their original game versions and reset presets to Default.");

            Grid.SetColumn(slCol, 6);
            dlssRowGrid.Children.Add(slCol);

            _window.NvidiaProfilePanel.Children.Add(dlssRowGrid);
        }

        BuildDriverProfileSection(card, capturedName);
    }
}