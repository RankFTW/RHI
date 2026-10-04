// MainWindow.FaqBuilder.cs — Builds the FAQ/Quick Start guide content dynamically.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _faqBuilt;

    /// <summary>
    /// Builds the FAQ panel content. Called once when FAQ is first opened.
    /// </summary>
    private void BuildFaqContent()
    {
        if (_faqBuilt) return;
        _faqBuilt = true;

        var panel = FaqContentPanel;
        panel.Children.Clear();

        // Welcome section
        panel.Children.Add(BuildFaqSection(
            null, Loc.Get("Welcome to RHI"), "AccentTealBrush",
            Loc.Get("RHI auto-detects your games and lets you install HDR mods, shaders, frame limiters, and manage NVIDIA driver settings — all from one place. Here's how to get started."),
            null));

        // Step 1: Select a Game
        panel.Children.Add(BuildFaqStep(1,
            Loc.Get("Select a Game"),
            Loc.Get("Your games are listed in the sidebar on the left. Click any game to see its details and available actions. Use the filter chips (All Games, Installed, Unreal, etc.) and search box to find specific games."),
            Loc.Get("Tip: Double-click a game to launch it directly. Drag and drop a game's .exe file onto RHI to add games not auto-detected.")));

        // Step 2: Install ReShade
        panel.Children.Add(BuildFaqStep(2,
            Loc.Get("Install ReShade"),
            Loc.Get("ReShade is required for RenoDX HDR mods to work. Click 'Install ReShade' on the game's detail panel. RHI automatically downloads and installs the correct version with full addon support."),
            Loc.Get("ReShade version can be changed per-game via the game overrides section — choose Stable, Nightly, Legacy, or a custom ReShade DLL.\nVulkan Games: Vulkan games (like Doom Eternal) require admin privileges. RHI will prompt for elevation when needed.\nDrag and drop ReShade preset files (.ini) onto a game to install them automatically.")));

        // Step 3a: RenoDX
        panel.Children.Add(BuildFaqSection(
            "3a", "RenoDX", "AccentTealBrush",
            Loc.Get("RenoDX is RHI's primary HDR mod framework. The RenoDX row on each game shows what's available:\n\n• Named mods — game-specific HDR mods from the RenoDX wiki, made by the community.\n• UE-Extended — for Unreal Engine games without a named mod. Provides native HDR output via a generic UE addon.\n• Unity addon — same idea for Unity Engine games. Provides HDR output for Unity games without a named mod.\n• RTX HDR — if no mod is available, you can enable NVIDIA's driver-level SDR-to-HDR conversion via the RenoDX ⚙ cog."),
            Loc.Get("The cog icon next to RenoDX opens advanced settings: Peak Nits, UE-Extended toggle, and RTX HDR configuration.\nEngine.ini Settings (Unreal Engine games only): toggle HDR keys and LUT update frequency written to the game's Engine.ini for accurate HDR rendering.\nFor games not on the wiki, drag and drop an .addon64 file from the RenoDX Discord directly onto the game in RHI.")));

        // Step 3b: Luma
        panel.Children.Add(BuildFaqSection(
            "3b", "Luma", "AccentTealBrush",
            Loc.Get("Luma is an alternative mod framework developed by Pumbo (HDR Den). Depending on the game, a Luma mod may add HDR, DLAA (Deep Learning Anti-Aliasing), game-specific rendering fixes, or a combination of these — check the Info button for details on what each mod offers.\n\n• Completed mods — named Luma mods for supported games, shown on the Luma row.\n• Generic Luma — available for all DX11 Unreal Engine games. RHI installs it automatically and applies any game-specific Engine.ini tweaks or launch arguments listed on the Luma wiki.\n\nYou can install RenoDX and Luma on the same game — but there's no guarantee they'll work together on every title. RHI will warn you the first time you try to install both."),
            Loc.Get("Luma requires ReShade — it will be greyed out until ReShade is installed.\nThe Luma ⚙ cog lets you toggle TAA Engine.ini settings for games that need them.\nIf a game needs a specific launch argument (e.g. -dx11), RHI sets it automatically on install and removes it on uninstall. Launch arguments only apply when the game is launched through RHI.\nCheck the Info button on the Luma row for game-specific notes — completed mods often include details on what the mod adds or any in-game settings required.")));

        // Step 4: Choose Shaders
        panel.Children.Add(BuildFaqStep(4,
            Loc.Get("Choose Shaders (Optional)"),
            Loc.Get("Click the 'Shaders/Addons' button in the toolbar, then 'Global Shaders' to select shader packs. Lilium's HDR shader pack is selected by default. These apply to all games with ReShade installed.\n\nExpand any pack to pick individual shaders — the pack shows a dash when only some files are selected. Use the Profiles panel on the right to save, load, rename, and share named shader selections. Export a profile as a zip to share via Discord."),
            Loc.Get("Tip: Per-game shaders can be set using the Shaders button on each game's detail card (when ReShade is installed).\nTip: Use Expand All / Collapse All to browse all packs at once. Deselect All clears the whole selection. Export copies a zip of your selected shaders to the clipboard — paste directly into Discord to share.")));

        // Step 5: DOF Fix
        panel.Children.Add(BuildFaqStep(5,
            Loc.Get("DOF Fix (Recommended for UE5)"),
            Loc.Get("DOF Fix backports a depth-of-field rendering fix from Unreal Engine 5.7 to games running on UE 5.0–5.6. It appears in the Recommended section on the game's detail panel when supported. Install it alongside RenoDX for the best result."),
            Loc.Get("DOF Fix only shows on eligible UE 5.0–5.6 games — it won't appear on UE4 or UE 5.7+ titles.\nInstall and uninstall work the same as any other component.")));

        // Step 6: Frame Limiters
        panel.Children.Add(BuildFaqStep(6,
            Loc.Get("Frame Limiters (Optional)"),
            Loc.Get("ReLimiter and Display Commander are ReShade addons that provide precise frame limiting for VRR displays. Install them from the game's detail panel. ReLimiter is recommended as it's developed by the same team as RHI. Set your target FPS in Settings, per-game via the cog icon, or directly in-game."),
            Loc.Get("VRR cap presets by refresh rate (leave headroom below max for smooth VRR):\n• 60Hz → 59 FPS\n• 120Hz → 116 FPS\n• 144Hz → 138 FPS\n• 165Hz → 157 FPS\n• 240Hz → 224 FPS\n• 360Hz → 324 FPS\nThese values are pre-configured in RHI's FPS dropdown menus.")));

        // Step 7: DLSS/Streamline
        panel.Children.Add(BuildFaqStep(7,
            Loc.Get("Update DLSS / Streamline (Optional)"),
            Loc.Get("Games with DLSS or Streamline DLLs have a dedicated section on the detail panel showing version info. Click to update to the latest version. Using the newest versions is recommended for best performance and quality. RHI backs up originals automatically so you can restore anytime."),
            Loc.Get("When new DLSS or Streamline versions release, they will appear in RHI automatically. Set your default DLSS preset in Settings. Per-game presets can be changed in the DLSS section on each game's detail panel.")));

        // OptiScaler
        panel.Children.Add(BuildFaqSpecialSection("⚙", "AccentAmberBrush",
            Loc.Get("OptiScaler (Optional)"),
            Loc.Get("OptiScaler replaces DLSS/XeSS with alternative upscalers (FSR, XeSS, Intel Arc) or adds/patches frame generation on any GPU. Install it from the game's detail panel when a game has OptiScaler support.\n\nThe ⚙ cog on the OptiScaler row opens per-game settings. For the Nightly build channel these include:\n• Streamline/DLSS Enabler — deploys Streamline and DLSS Enabler to the game folder for DLSS Frame Generation support.\n• Frame Generation — set FG Input, FG Output, FG Nvngx Override, and HUD Fix.\n• Additional Settings — DLSS SR/RR preset, render scale, and flip metering.\n• Presets — save and apply named setting presets across games.\n• Engine.ini Settings (Unreal Engine games) — Dilated Motion Vectors, FSR Crash Fix, FSR-FG Swapchain, Upscaler Plugin."),
            Loc.Get("Switch between Stable and Nightly channels per game in the cog — Nightly adds frame generation and additional settings.\nOptiScaler and ReShade can coexist. If you see crashes with both installed, try renaming ReShade to a different DLL name using DLL Naming Overrides in the Game Overrides panel.\nGPU type and DLSS input settings (AMD/Intel only) are configured in Settings → OptiScaler Settings before installing.\nThe 'Deploy OptiScaler.ini' button in the cog redeploys your configured INI template to the game folder.")));

        // Settings Overview
        panel.Children.Add(BuildFaqInfoSection(Loc.Get("Settings"),
            Loc.Get("Click 'Settings' in the toolbar to configure defaults for all games:"),
            new[]
            {
                Loc.Get("ReLimiter FPS: Default frame rate target"),
                Loc.Get("DLSS Preset: Default upscaling preset"),
                Loc.Get("NVIDIA Driver Settings: VSync, Low Latency, Power Mode"),
                Loc.Get("Peak Nits: Your display's peak brightness for HDR"),
                Loc.Get("ReShade Hotkeys: Customize overlay and screenshot keys")
            }));

        // NVIDIA Driver Settings
        panel.Children.Add(BuildFaqInfoSection(Loc.Get("NVIDIA Driver Settings"),
            Loc.Get("RHI can manage per-game NVIDIA driver profiles. These settings are available directly on each game's detail panel:"),
            new[]
            {
                Loc.Get("VSync: On, Off, or Adaptive (Fast Sync)"),
                Loc.Get("Low Latency Mode: Ultra, On, or Off"),
                Loc.Get("Smooth Motion: Multi Frame Generation (per-game only)"),
                Loc.Get("ReBAR: Resizable BAR (requires admin)")
            },
            Loc.Get("Global defaults for VSync, Low Latency, and Power Mode are set in Settings. Per-game overrides are configured directly on each game's detail panel.")));

        // Vulkan Games
        panel.Children.Add(BuildFaqSpecialSection("V", "AccentPurpleBrush",
            Loc.Get("Vulkan Games"),
            Loc.Get("Vulkan games (shown with a 'Vulkan' badge) use a global ReShade layer installed to C:\\ProgramData\\ReShade. This requires administrator privileges. When you install ReShade on a Vulkan game, RHI will prompt for elevation."),
            Loc.Get("All Vulkan games share the same ReShade installation. Updating ReShade on one Vulkan game updates it for all.\nPer-game RenoDX addons and shaders are still installed individually to each game folder.")));

        // Adding Games Manually
        panel.Children.Add(BuildFaqSpecialSection("+", "AccentAmberBrush",
            Loc.Get("Adding Games Manually"),
            Loc.Get("If a game isn't auto-detected, drag and drop its .exe file directly onto the RHI window. RHI will add it to your library and detect its engine type."),
            Loc.Get("You can also drag .addon64 files from the RenoDX Discord onto any game to install mods not yet on the wiki.")));

        // Updating Everything
        panel.Children.Add(BuildFaqSpecialSection("↑", "AccentGreenBrush",
            Loc.Get("Updating Everything"),
            Loc.Get("Click 'Update All' in the toolbar to update all installed components across all games at once. This includes ReShade, RenoDX mods, ReLimiter, Display Commander, and more."),
            Loc.Get("Games with available updates show a green dot in the sidebar. Configure which components are included in 'Update All' from Settings.")));

        // Troubleshooting - Full Refresh
        panel.Children.Add(BuildFaqSpecialSection("↻", "AccentBlueBrush",
            Loc.Get("Troubleshooting: Full Refresh"),
            Loc.Get("If games are missing, install locations have changed, or DLSS/Streamline files have been added or removed, use 'Full Refresh' in Settings to rescan your entire library from scratch."),
            Loc.Get("Full Refresh clears the cached game list and re-detects everything. Use it when the normal Refresh button doesn't pick up changes.")));

        // System Tray
        panel.Children.Add(BuildFaqSpecialSection("◰", "AccentPurpleBrush",
            Loc.Get("System Tray"),
            Loc.Get("RHI can minimize to the system tray instead of closing. Right-click the tray icon to quickly launch recent games without opening the main window."),
            Loc.Get("Enable 'Close to System Tray' in Settings to keep RHI running in the background. The tray icon provides quick access to your most recently played games. RHI automatically checks for updates every 4 hours while running, so everything stays up to date.")));

        // Need More Help
        panel.Children.Add(BuildFaqLinksSection());
    }


    private Border BuildFaqSection(string? badge, string title, string titleBrush, string description, string? tip)
    {
        var stack = new StackPanel { Spacing = 10 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (badge != null)
        {
            var badgeBorder = new Border
            {
                Background = (Brush)Application.Current.Resources[titleBrush],
                CornerRadius = new CornerRadius(12),
                Width = 24,
                Height = 24
            };
            badgeBorder.Child = new TextBlock
            {
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }.Localize("Text", badge);
            header.Children.Add(badgeBorder);
        }
        header.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", title));
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20
        }.Localize("Text", description));

        if (tip != null)
        {
            var tipBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceToolbarBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1)
            };
            tipBorder.Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            }.Localize("Text", tip);
            stack.Children.Add(tipBorder);
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }


    private Border BuildFaqStep(int step, string title, string description, string? tip)
    {
        var stack = new StackPanel { Spacing = 8 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var badgeBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentTealBrush"],
            CornerRadius = new CornerRadius(12),
            Width = 24,
            Height = 24
        };
        badgeBorder.Child = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", step.ToString());
        header.Children.Add(badgeBorder);
        header.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", title));
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20,
            Margin = new Thickness(32, 0, 0, 0)
        }.Localize("Text", description));

        if (tip != null)
        {
            var tipBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceToolbarBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(32, 4, 0, 0),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1)
            };
            tipBorder.Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            }.Localize("Text", tip);
            stack.Children.Add(tipBorder);
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }


    private Border BuildFaqInfoSection(string title, string description, string[] bullets, string? tip = null)
    {
        var stack = new StackPanel { Spacing = 8 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var badgeBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBlueBrush"],
            CornerRadius = new CornerRadius(12),
            Width = 24,
            Height = 24
        };
        badgeBorder.Child = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", "?");
        header.Children.Add(badgeBorder);
        header.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", title));
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20,
            Margin = new Thickness(32, 0, 0, 0)
        }.Localize("Text", description));

        var bulletStack = new StackPanel { Spacing = 6, Margin = new Thickness(32, 4, 0, 0) };
        foreach (var bullet in bullets)
        {
            bulletStack.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            }.Localize("Text", $"• {bullet}"));
        }
        stack.Children.Add(bulletStack);

        if (tip != null)
        {
            var tipBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceToolbarBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(32, 4, 0, 0),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1)
            };
            tipBorder.Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            }.Localize("Text", tip);
            stack.Children.Add(tipBorder);
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }


    private Border BuildFaqSpecialSection(string badge, string badgeBrush, string title, string description, string? tip)
    {
        var stack = new StackPanel { Spacing = 8 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var badgeBorder = new Border
        {
            Background = (Brush)Application.Current.Resources[badgeBrush],
            CornerRadius = new CornerRadius(12),
            Width = 24,
            Height = 24
        };
        var badgeFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50));
        badgeBorder.Child = new TextBlock
        {
            FontSize = badge.Length > 1 ? 11 : 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = badgeFg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", badge);
        header.Children.Add(badgeBorder);
        header.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", title));
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20,
            Margin = new Thickness(32, 0, 0, 0)
        }.Localize("Text", description));

        if (tip != null)
        {
            var tipBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceToolbarBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(32, 4, 0, 0),
                BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
                BorderThickness = new Thickness(1)
            };
            tipBorder.Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            }.Localize("Text", tip);
            stack.Children.Add(tipBorder);
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (Brush)Application.Current.Resources["BorderSubtleBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }


    private Border BuildFaqLinksSection()
    {
        var stack = new StackPanel { Spacing = 10 };

        stack.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AccentTealBrush"]
        }.Localize("Text", Loc.Get("Need More Help?")));

        stack.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20
        }.Localize("Text", Loc.Get("Support is available on Discord — join the community for help, mod updates, and discussion.")));

        var linksStack = new StackPanel { Spacing = 6 };

        var discordLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://discord.gg/ultraplus"),
            Padding = new Thickness(0)
        };
        discordLink.Localize("Content", new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        }.Localize("Text", Loc.Get("Join the Ultra+ Discord (main community)")));
        linksStack.Children.Add(discordLink);

        var renodxDiscordLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://discord.gg/renodx"),
            Padding = new Thickness(0)
        };
        renodxDiscordLink.Localize("Content", new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        }.Localize("Text", Loc.Get("RenoDX Discord (mod development)")));
        linksStack.Children.Add(renodxDiscordLink);

        var wikiLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://github.com/clshortfuse/renodx/wiki/Mods"),
            Padding = new Thickness(0)
        };
        wikiLink.Localize("Content", new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        }.Localize("Text", Loc.Get("Browse the RenoDX Mod Wiki")));
        linksStack.Children.Add(wikiLink);

        var githubLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://github.com/RankFTW/RHI"),
            Padding = new Thickness(0)
        };
        githubLink.Localize("Content", new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        }.Localize("Text", Loc.Get("RHI GitHub — Report issues or request features")));
        linksStack.Children.Add(githubLink);

        stack.Children.Add(linksStack);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (Brush)Application.Current.Resources["AccentTealBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }
}
