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
            null, "Welcome to RHI", "AccentTealBrush",
            "RHI finds the games on your PC and installs, updates and removes HDR mods, ReShade, shaders, frame limiters, DLSS files and more, and manages your NVIDIA driver settings, all from one place. Here's how to get started.",
            "Recommended first step: add a free GitHub token in Settings → GitHub API. Without one, GitHub allows only 60 requests an hour, which can make downloads and update checks fail. Create a classic token at github.com/settings/tokens with no boxes ticked, paste it in and click Save."));

        // Step 1: Select a Game
        panel.Children.Add(BuildFaqStep(1,
            "Select a Game",
            "Your games are listed on the left. Click a game to open its detail panel. Use the search box and the filter chips (All Games, Installed, Favourites, Unreal, Unity, RenoDX, Luma…) to find games.\n\nThe detail panel is split into sections: Components, Game Overrides, Neural Rendering, DLSS / Streamline, Driver Settings, Management and Extras. Click a section heading to collapse it, or drag its ≡ handle to reorder.",
            "Double-click a game to launch it. Right-click a game to open its folder, favourite or hide it, or create a desktop shortcut.\nThe HDR and RES buttons next to Launch turn Windows HDR on and switch resolution while the game is running.\nType a search, then click the + that appears to save it as a filter chip."));

        // Step 2: Install ReShade
        panel.Children.Add(BuildFaqStep(2,
            "Install ReShade",
            "ReShade is needed for RenoDX, Luma, frame limiters and DOF Fix. Click 'Install ReShade' in the Components section. RHI picks the right file name for the game's graphics API and installs the version with addon support.",
            "Change the ReShade version per game in Game Overrides → ReShade Channel: Stable, Nightly, Custom (your own DLL), No Addons or Legacy.\nThe ReShade ⚙ deploys reshade.ini or ReShadePreset.ini, sets per-game hotkeys, and can stop RHI changing this game's reshade.ini.\nDrag a ReShade preset (.ini) onto RHI to install it, along with the shaders it needs."));

        // Step 3a: RenoDX
        panel.Children.Add(BuildFaqSection(
            "3a", "RenoDX", "AccentTealBrush",
            "RenoDX mods give games true HDR output. The RenoDX HDR row shows what's available for the game:\n\n• Named mods: game-specific HDR mods made by the community.\n• UE-Extended: for Unreal Engine games without a named mod.\n• Generic Unity: for Unity games without a named mod.\n\nA ✓ next to the installed mod means it's complete; a hammer means it's still in progress. Click 'Available Mods' in the toolbar to see every game with a RenoDX, Luma or Ultra+ mod.",
            "The RenoDX ⚙ has per-game peak brightness, UE-Extended and Engine.ini HDR options, compatibility settings, and preset export/import.\nNo mod for your game? Turn on RTX HDR (NVIDIA) or Auto HDR (Windows 11, any GPU) from the same ⚙. Either one replaces RenoDX for that game.\nGot an .addon64 from the RenoDX Discord? Drag it onto RHI and pick the game."));

        // Step 3b: Luma
        panel.Children.Add(BuildFaqSection(
            "3b", "Luma", "AccentTealBrush",
            "Luma is a mod framework by Pumbo. Depending on the game, a Luma mod may add HDR, DLSS, DLAA or rendering fixes. Check the Info button to see what each mod offers.\n\n• Named mods: for supported games, shown on the Luma row. Mods only on Nexus Mods open their Nexus page.\n• Generic Luma: for DX11 Unreal Engine games. RHI applies the Engine.ini tweaks and launch arguments listed on the Luma wiki.\n\nGeneric Luma can be used alongside RenoDX. Named Luma mods normally replace RenoDX.",
            "Luma needs ReShade. The install button shows 'ReShade required' until it's installed.\nThe Luma ⚙ (Unreal games) has HDR on First Boot and TAA settings.\nLaunch arguments such as -dx11 are added on install and removed on uninstall. They only apply when you launch the game through RHI.\nDX9 Luma mods get dgVoodoo2 installed automatically."));

        // Step 4: Choose Shaders
        panel.Children.Add(BuildFaqStep(4,
            "Choose Shaders (Optional)",
            "Click 'Shaders/Addons' in the toolbar, then 'Global Shaders'. Lilium's HDR shaders are selected by default. Your selection is deployed to every game with ReShade.\n\nExpand a pack to pick individual shaders, or search for one. Use the Profiles panel to save named selections, and Export to copy one as a zip you can paste into Discord.",
            "Per-game shaders: Game Overrides → Shaders → Select.\nYour own shaders: click Open Custom Folder in the picker's Profiles panel and put them there. They appear under Custom Shaders.\nGlobal Addons, in the same toolbar menu, manages ReShade addons for all games."));

        // Step 5: DOF Fix
        panel.Children.Add(BuildFaqStep(5,
            "DOF Fix (Recommended for UE5)",
            "DOF Fix backports a depth-of-field fix from Unreal Engine 5.7 to games on UE 5.0–5.6. When a game supports it, it appears under Recommended in the Components section. Install it alongside RenoDX for the best result.",
            "DOF Fix only appears on 64-bit UE 5.0–5.6 games.\nIf a game's engine badge just says 'Unreal Engine', click the badge to mark it as Unreal Engine 5."));

        // Step 6: Frame Limiters
        panel.Children.Add(BuildFaqStep(6,
            "Frame Limiters (Optional)",
            "ReLimiter and Display Commander are ReShade addons that limit frame rate precisely, which is ideal for VRR displays. Install one of them (not both) from the Components section. ReLimiter is recommended. Set your target FPS in Settings → Component Settings, per game in the ReLimiter ⚙, or in-game.",
            "Installing either limiter turns off the NVIDIA driver frame limit for that game.\nVRR cap presets by refresh rate (a little below max keeps VRR active):\n• 60Hz → 59 FPS\n• 120Hz → 116 FPS\n• 144Hz → 138 FPS\n• 165Hz → 157 FPS\n• 240Hz → 224 FPS\n• 360Hz → 324 FPS\nThese values are pre-configured in RHI's FPS dropdowns."));

        // Step 7: DLSS/Streamline
        panel.Children.Add(BuildFaqStep(7,
            "DLSS / Streamline (Optional)",
            "Games with DLSS or Streamline files have a DLSS / Streamline section. Pick a version for Super Resolution, Ray Reconstruction, Frame Generation, Neural Rendering or Streamline, and RHI swaps the file. Originals are backed up: choose 'Default' or click Restore DLSS/SL to put them back.\n\nYou can also set presets and render scale per game. These are written to the NVIDIA driver, so you don't need NVIDIA Profile Inspector.",
            "NVIDIA Override in the version list tells the driver to use its newest DLSS file instead.\nSet your preferred versions and presets once in Settings → Configure Defaults, then use Quick Apply on any game, or Batch Deploy for many games at once.\nDLSS section missing? Use Full Refresh in Settings."));

        // Neural Rendering
        panel.Children.Add(BuildFaqSpecialSection("N", "AccentGreenBrush",
            "Neural Rendering (DLSS 5, Optional)",
            "The Neural Rendering section installs DLSS 5 neural rendering tools. RHI picks the right method for the game, and installs ReShade too if needed:\n• ShortFuse DLSS Tool: most games with native DLSS.\n• DLSS5 Tool: DX12 games with native DLSS.\n• DLSS5 Tool + DX11 Bridge: DX11 and Vulkan games with native DLSS.\n• DLSS5 Feeder: games without DLSS, including OpenGL, DX9 and 32-bit games.",
            "Each method's versions, and the NR DLL version, can be pinned or left on Latest.\nNR Cost Scaler runs neural rendering at reduced cost. Turn it on before installing. ShortFuse already includes it."));

        // OptiScaler
        panel.Children.Add(BuildFaqSpecialSection("⚙", "AccentAmberBrush",
            "OptiScaler (Optional)",
            "OptiScaler lets a game's DLSS, FSR or XeSS inputs drive a different upscaler (FSR, XeSS, DLSS), and can add or replace frame generation on any GPU. It's in the Extras section.\n\nBefore installing, set your GPU Type (and DLSS Inputs for AMD/Intel) in Settings → Component Settings → OptiScaler. Then choose a variant in the OptiScaler ⚙:\n• Stable: official release.\n• Nightly: latest build, or pin a specific build.\n• DLSS NR and F5 DLSSNR: forks with DLSS Neural Rendering.\n\nThe ⚙ also sets the upscaler, frame generation, Reflex, DLSS presets, render scale, Engine.ini fixes and Neural Rendering options, and saves presets you can apply to other games.",
            "OptiScaler and ReShade can run together. If a game crashes with both, give ReShade a different name in Game Overrides → DLL naming overrides.\n'Deploy OptiScaler.ini' in the ⚙ resets the game's ini from your template."));

        // Extras
        panel.Children.Add(BuildFaqSpecialSection("X", "AccentAmberBrush",
            "Extras (Optional)",
            "The Extras section holds optional tools:\n• Frame generation unlocks: RTX 40 MFG and MFG Ada Unlock (RTX 40), 20/30 FG Unlock (RTX 20/30), RTX Encore (beta) and DLSS Enabler.\n• ASI Loader: loads .asi plugins.\n• DXVK: runs DX8–DX11 games through Vulkan. The Lilium HDR variant gives older games HDR.\n• dgVoodoo2: runs DX9 games through DX11.",
            "Most of these ask which file name to use when you install them. Names already used by RHI's other components are disabled, and any game file that gets replaced is backed up."));

        // NVIDIA Driver Settings
        panel.Children.Add(BuildFaqInfoSection("NVIDIA Driver Settings",
            "The Driver Settings section changes the game's NVIDIA profile directly. It needs Admin Mode (Settings → System & Maintenance).",
            new[]
            {
                "VSync: Mode, Tear Control and Low Latency (Off, On, Ultra)",
                "Smooth Motion (RTX 40+): sets Low Latency to Ultra while it's on",
                "Power Mode and G-Sync",
                "ReBAR: Enable, Mode and Size Limit",
                "Multi Frame Generation (RTX 50): the Multi Frame Gen button in the DLSS / Streamline section"
            },
            "Global defaults (shader cache, G-Sync, FPS limit, VSync, Power Mode, ReBAR), Digital Vibrance and profile backup/restore are in Settings → Global NVIDIA Driver Settings."));

        // Settings Overview
        panel.Children.Add(BuildFaqInfoSection("Settings",
            "Click 'Settings' in the toolbar for options that apply to all games:",
            new[]
            {
                "Component Updates: Check For Updates, Background Checks and Automatic Updates",
                "ReShade & Display: screenshots, hotkeys, peak brightness (nits) and HDR Auto-Toggle",
                "Resolution & Colour Control and DLDSR Control",
                "GitHub API: your GitHub token",
                "DLSS / Streamline Settings: defaults, Batch Deploy and auto-update",
                "Component Settings: ReLimiter and OptiScaler",
                "System & Maintenance: Full Refresh, Admin Mode and System Tray"
            }));

        // Vulkan Games
        panel.Children.Add(BuildFaqSpecialSection("V", "AccentPurpleBrush",
            "Vulkan Games",
            "Vulkan games (shown with a VLK badge) use a global ReShade layer installed to C:\\ProgramData\\ReShade. This needs administrator rights, so RHI asks for permission the first time.",
            "All Vulkan games share the same ReShade, so changing the ReShade channel on one changes it for all.\nRenoDX addons and shaders are still installed to each game's own folder.\nGames that support both DirectX and Vulkan: choose one in Game Overrides → Graphics API."));

        // Adding Games Manually
        panel.Children.Add(BuildFaqSpecialSection("+", "AccentAmberBrush",
            "Adding Games Manually",
            "If a game isn't detected, click the green + next to the search box and choose its .exe, or drag the .exe onto the RHI window. RHI adds it to your library and detects its engine.",
            "Wrong folder? Use Management → Change install folder.\nManually added games can be removed with Management → Reset / Remove game."));

        // Updating Everything
        panel.Children.Add(BuildFaqSpecialSection("↑", "AccentGreenBrush",
            "Updating Everything",
            "Click 'Update All' in the toolbar to update ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, RE Framework, DXVK, Luma and DOF Fix across all your games.",
            "Games with updates show a green dot in the sidebar.\nChoose what Update All includes in Settings → Update & Deployment, or per game in Game Overrides → Update Inclusion.\nTurn on Automatic Updates in Settings to install updates in the background.\nThe Updates button at the bottom right shows everything that has been updated."));

        // Troubleshooting
        panel.Children.Add(BuildFaqSpecialSection("↻", "AccentBlueBrush",
            "Troubleshooting",
            "If games are missing, a game has moved, or DLSS files were added or removed, use 'Full Refresh' in Settings → System & Maintenance to rescan everything from scratch.",
            "Downloads or update checks failing? Add a GitHub token (Settings → GitHub API).\nTo report a problem, click Copy Logs in Settings and/or Copy Report in the game's Management section, then paste it into Discord."));

        // System Tray
        panel.Children.Add(BuildFaqSpecialSection("◰", "AccentPurpleBrush",
            "System Tray",
            "RHI can keep running in the system tray when you close the window. Right-click the tray icon to launch recent games without opening RHI.",
            "Turn on Close to Tray, Recent Games and Start with Windows in Settings → System & Maintenance.\nRecent Games also appear in the taskbar jump list.\nWhile running, RHI checks for updates every 4 hours."));

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
                Text = badge,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(badgeBorder);
        }
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20
        });

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
                Text = tip,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            };
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
            Text = step.ToString(),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(badgeBorder);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20,
            Margin = new Thickness(32, 0, 0, 0)
        });

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
                Text = tip,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            };
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
            Text = "?",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 30, 50)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(badgeBorder);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20,
            Margin = new Thickness(32, 0, 0, 0)
        });

        var bulletStack = new StackPanel { Spacing = 6, Margin = new Thickness(32, 4, 0, 0) };
        foreach (var bullet in bullets)
        {
            bulletStack.Children.Add(new TextBlock
            {
                Text = $"• {bullet}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            });
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
                Text = tip,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            };
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
            Text = badge,
            FontSize = badge.Length > 1 ? 11 : 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = badgeFg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(badgeBorder);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20,
            Margin = new Thickness(32, 0, 0, 0)
        });

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
                Text = tip,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                LineHeight = 18
            };
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
            Text = "Need More Help?",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AccentTealBrush"]
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Support is available on Discord — join the community for help, mod updates, and discussion.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            LineHeight = 20
        });

        var linksStack = new StackPanel { Spacing = 6 };

        var discordLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://discord.gg/ultraplus"),
            Padding = new Thickness(0)
        };
        discordLink.Content = new TextBlock
        {
            Text = "Join the Ultra+ Discord (main community)",
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        };
        linksStack.Children.Add(discordLink);

        var renodxDiscordLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://discord.gg/renodx"),
            Padding = new Thickness(0)
        };
        renodxDiscordLink.Content = new TextBlock
        {
            Text = "RenoDX Discord (mod development)",
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        };
        linksStack.Children.Add(renodxDiscordLink);

        var wikiLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://github.com/clshortfuse/renodx/wiki/Mods"),
            Padding = new Thickness(0)
        };
        wikiLink.Content = new TextBlock
        {
            Text = "Browse the RenoDX Mod Wiki",
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        };
        linksStack.Children.Add(wikiLink);

        var githubLink = new HyperlinkButton
        {
            NavigateUri = new Uri("https://github.com/RankFTW/RHI"),
            Padding = new Thickness(0)
        };
        githubLink.Content = new TextBlock
        {
            Text = "RHI GitHub — Report issues or request features",
            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
            FontSize = 12
        };
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
