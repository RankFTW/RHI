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
            null, "Добро пожаловать в RHI", "AccentTealBrush",
            "RHI сам находит ваши игры и позволяет устанавливать HDR-моды, шейдеры, ограничители FPS и управлять настройками драйвера NVIDIA — всё в одном месте. Вот с чего начать.",
            null));

        // Step 1: Select a Game
        panel.Children.Add(BuildFaqStep(1,
            "Выберите игру",
            "Игры перечислены в боковой панели слева. Нажмите на игру, чтобы увидеть её подробности и доступные действия. Для поиска используйте фильтры (Все игры, Установленные, Unreal и т.д.) и строку поиска.",
            "Tip: Double-click a game to launch it directly. Drag and drop a game's .exe file onto RHI to add games not auto-detected."));

        // Step 2: Install ReShade
        panel.Children.Add(BuildFaqStep(2,
            "Установить ReShade",
            "Для работы HDR-модов RenoDX нужен ReShade. Нажмите «Установить ReShade» на панели игры. RHI автоматически скачает и установит нужную версию с полной поддержкой аддонов.",
            "ReShade version can be changed per-game via the game overrides section — choose Stable, Nightly, Legacy, or a custom ReShade DLL.\nVulkan Games: Vulkan games (like Doom Eternal) require admin privileges. RHI will prompt for elevation when needed.\nDrag and drop ReShade preset files (.ini) onto a game to install them automatically."));

        // Step 3a: RenoDX
        panel.Children.Add(BuildFaqSection(
            "3a", "RenoDX", "AccentTealBrush",
            "RenoDX",
            "The cog icon next to RenoDX opens advanced settings: Peak Nits, UE-Extended toggle, and RTX HDR configuration.\nEngine.ini Settings (Unreal Engine games only): toggle HDR keys and LUT update frequency written to the game's Engine.ini for accurate HDR rendering.\nFor games not on the wiki, drag and drop an .addon64 file from the RenoDX Discord directly onto the game in RHI."));

        // Step 3b: Luma
        panel.Children.Add(BuildFaqSection(
            "3b", "Luma", "AccentTealBrush",
            "Luma is an alternative mod framework developed by Pumbo (HDR Den). Depending on the game, a Luma mod may add HDR, DLAA (Deep Learning Anti-Aliasing), game-specific rendering fixes, or a combination of these — check the Info button for details on what each mod offers.\n\n• Completed mods — named Luma mods for supported games, shown on the Luma row.\n• Generic Luma — available for all DX11 Unreal Engine games. RHI installs it automatically and applies any game-specific Engine.ini tweaks or launch arguments listed on the Luma wiki.\n\nYou can install RenoDX and Luma on the same game — but there's no guarantee they'll work together on every title. RHI will warn you the first time you try to install both.",
            "Luma requires ReShade — it will be greyed out until ReShade is installed.\nThe Luma ⚙ cog lets you toggle TAA Engine.ini settings for games that need them.\nIf a game needs a specific launch argument (e.g. -dx11), RHI sets it automatically on install and removes it on uninstall. Launch arguments only apply when the game is launched through RHI.\nCheck the Info button on the Luma row for game-specific notes — completed mods often include details on what the mod adds or any in-game settings required."));

        // Step 4: Choose Shaders
        panel.Children.Add(BuildFaqStep(4,
            "Выбрать шейдеры (необязательно)",
            "Нажмите кнопку «Шейдеры/Аддоны» на панели инструментов, затем «Глобальные шейдеры», чтобы выбрать наборы шейдеров. По умолчанию выбран HDR-набор Lilium. Он применяется ко всем играм с установленным ReShade.\n\nРазверните набор, чтобы выбрать отдельные шейдеры — если отмечены лишь некоторые файлы, у набора будет прочерк. Панель «Профили» справа позволяет сохранять, загружать, переименовывать и делиться именными подборками шейдеров. Экспортируйте профиль в zip, чтобы поделиться через Discord.",
            "Совет: шейдеры для отдельной игры задаются кнопкой «Шейдеры» на её карточке (если ReShade установлен).\nСовет: «Развернуть все» / «Свернуть все» — просмотреть все наборы сразу. «Снять всё» очищает выбор. «Экспорт» копирует zip выбранных шейдеров в буфер обмена — вставьте прямо в Discord, чтобы поделиться."));

        // Step 5: DOF Fix
        panel.Children.Add(BuildFaqStep(5,
            "DOF Fix (рекомендуется для UE5)",
            "DOF Fix переносит исправление рендеринга глубины резкости из Unreal Engine 5.7 в игры на UE 5.0–5.6. Для поддерживаемых игр появляется в разделе «Рекомендуемое» на панели игры. Для лучшего результата устанавливайте вместе с RenoDX.",
            "DOF Fix"));

        // Step 6: Frame Limiters
        panel.Children.Add(BuildFaqStep(6,
            "Ограничители FPS (необязательно)",
            "ReLimiter и Display Commander — аддоны ReShade с точным ограничением частоты кадров для VRR-дисплеев. Устанавливаются с панели игры. Рекомендуется ReLimiter — его развивает та же команда, что и RHI. Целевой FPS задаётся в настройках, для каждой игры через значок ⚙ или прямо в игре.",
            "Пресеты лимитов VRR по частоте обновления (с запасом ниже максимума для плавного VRR):\n• 60 Гц → 59 FPS\n• 120 Гц → 116 FPS\n• 144 Гц → 138 FPS\n• 165 Гц → 157 FPS\n• 240 Гц → 224 FPS\n• 360 Гц → 324 FPS\nЭти значения предзаданы в выпадающих списках FPS в RHI."));

        // Step 7: DLSS/Streamline
        panel.Children.Add(BuildFaqStep(7,
            "Обновить DLSS / Streamline (необязательно)",
            "Игры с DLL DLSS или Streamline имеют отдельный раздел на панели игры с информацией о версиях. Нажмите, чтобы обновить до последней версии. Новейшие версии рекомендуются для лучшей производительности и качества. RHI автоматически резервирует оригиналы, так что их можно вернуть в любой момент.",
            "Новые версии DLSS и Streamline появляются в RHI автоматически. Пресет DLSS по умолчанию задаётся в настройках. Пресеты отдельных игр меняются в разделе DLSS на панели каждой игры."));

        // OptiScaler
        panel.Children.Add(BuildFaqSpecialSection("⚙", "AccentAmberBrush",
            "OptiScaler (необязательно)",
            "OptiScaler replaces DLSS/XeSS with alternative upscalers (FSR, XeSS, Intel Arc) or adds/patches frame generation on any GPU. Install it from the game's detail panel when a game has OptiScaler support.\n\nThe ⚙ cog on the OptiScaler row opens per-game settings. For the Nightly build channel these include:\n• Streamline/DLSS Enabler — deploys Streamline and DLSS Enabler to the game folder for DLSS Frame Generation support.\n• Frame Generation — set FG Input, FG Output, FG Nvngx Override, and HUD Fix.\n• Additional Settings — DLSS SR/RR preset, render scale, and flip metering.\n• Presets — save and apply named setting presets across games.\n• Engine.ini Settings (Unreal Engine games) — Dilated Motion Vectors, FSR Crash Fix, FSR-FG Swapchain, Upscaler Plugin.",
            "Switch between Stable and Nightly channels per game in the cog — Nightly adds frame generation and additional settings.\nOptiScaler and ReShade can coexist. If you see crashes with both installed, try renaming ReShade to a different DLL name using DLL Naming Overrides in the Game Overrides panel.\nGPU type and DLSS input settings (AMD/Intel only) are configured in Settings → OptiScaler Settings before installing.\nThe 'Deploy OptiScaler.ini' button in the cog redeploys your configured INI template to the game folder."));

        // Settings Overview
        panel.Children.Add(BuildFaqInfoSection("Settings",
            "Нажмите «Настройки» на панели инструментов, чтобы задать значения по умолчанию для всех игр:",
            new[]
            {
                "ReLimiter FPS: целевая частота кадров по умолчанию",
                "Пресет DLSS: пресет масштабирования по умолчанию",
                "Настройки драйвера NVIDIA: VSync, Low Latency, режим питания",
                "Пиковая яркость: пиковая яркость вашего дисплея для HDR",
                "Горячие клавиши ReShade: настройка клавиш оверлея и скриншотов"
            }));

        // NVIDIA Driver Settings
        panel.Children.Add(BuildFaqInfoSection("Настройки драйвера NVIDIA",
            "RHI умеет управлять профилями драйвера NVIDIA для каждой игры. Эти настройки доступны прямо на панели каждой игры:",
            new[]
            {
                "VSync: вкл, выкл или адаптивный (Fast Sync)",
                "Режим Low Latency: Ultra, вкл или выкл",
                "Smooth Motion: мульти-генерация кадров (только для отдельных игр)",
                "ReBAR: Resizable BAR (нужны права администратора)"
            },
            "Значения по умолчанию для VSync, Low Latency и режима питания задаются в настройках. Переопределения для отдельных игр настраиваются прямо на панели каждой игры."));

        // Vulkan Games
        panel.Children.Add(BuildFaqSpecialSection("V", "AccentPurpleBrush",
            "Vulkan-игры",
            "Vulkan games (shown with a 'Vulkan' badge) use a global ReShade layer installed to C:\\ProgramData\\ReShade. This requires administrator privileges. When you install ReShade on a Vulkan game, RHI will prompt for elevation.",
            "Все Vulkan-игры используют общую установку ReShade. Обновление ReShade в одной Vulkan-игре обновляет его для всех.\nИндивидуальные аддоны RenoDX и шейдеры по-прежнему устанавливаются в папку каждой игры отдельно."));

        // Adding Games Manually
        panel.Children.Add(BuildFaqSpecialSection("+", "AccentAmberBrush",
            "Добавление игр вручную",
            "If a game isn't auto-detected, drag and drop its .exe file directly onto the RHI window. RHI will add it to your library and detect its engine type.",
            "You can also drag .addon64 files from the RenoDX Discord onto any game to install mods not yet on the wiki."));

        // Updating Everything
        panel.Children.Add(BuildFaqSpecialSection("↑", "AccentGreenBrush",
            "Обновление всего",
            "Нажмите «Обновить всё» на панели инструментов, чтобы разом обновить все установленные компоненты во всех играх. Это включает ReShade, моды RenoDX, ReLimiter, Display Commander и многое другое.",
            "Игры с доступными обновлениями помечаются зелёной точкой в боковой панели. Какие компоненты входят в «Обновить всё», настраивается в параметрах."));

        // Troubleshooting - Full Refresh
        panel.Children.Add(BuildFaqSpecialSection("↻", "AccentBlueBrush",
            "Устранение неполадок: полное обновление",
            "Если игры пропали, изменились пути установки или файлы DLSS/Streamline добавлялись и удалялись, используйте «Полное обновление» в настройках, чтобы пересканировать всю библиотеку с нуля.",
            "Полное обновление очищает кешированный список игр и заново определяет всё. Используйте, если обычная кнопка «Обновить» не замечает изменений."));

        // System Tray
        panel.Children.Add(BuildFaqSpecialSection("◰", "AccentPurpleBrush",
            "Системный трей",
            "RHI может сворачиваться в системный трей вместо закрытия. Правый клик по значку в трее — быстрый запуск недавних игр без открытия главного окна.",
            "Включите «Свёртывать в трей» в настройках, чтобы RHI продолжал работать в фоне. Значок в трее даёт быстрый доступ к недавно запускавшимся играм. Работающий RHI каждые 4 часа автоматически проверяет обновления, так что всё остаётся актуальным."));

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
            Text = "Нужна помощь?",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AccentTealBrush"]
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Поддержка — в Discord: присоединяйтесь к сообществу за помощью, обновлениями модов и обсуждением.",
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
            Text = "Discord Ultra+ (основное сообщество)",
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
            Text = "RenoDX Discord (разработка модов)",
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
            Text = "Открыть вики модов RenoDX",
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
            Text = "RHI на GitHub — сообщить о проблеме или предложить функцию",
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
