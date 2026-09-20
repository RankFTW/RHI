// ShaderPackService.cs — Class declaration, constructor, path constants, pack definitions, enums, and ShaderPack record

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads, extracts and deploys HDR ReShade shader packs from multiple sources.
///
/// All packs are merged into a single shared staging tree:
///   %LocalAppData%\RenoDXCommander\reshade\Shaders\
///   %LocalAppData%\RenoDXCommander\reshade\Textures\
///
/// Each pack's extracted files are tracked individually. If a pack's cache zip is
/// deleted — or extracted files are missing from the staging folder — the pack is
/// re-downloaded and re-extracted on the next launch.
///
/// Source types:
///   GhRelease — GitHub Releases API, picks first matching asset extension
///   DirectUrl — Any static URL; versioned by ETag / Last-Modified header
/// </summary>
public partial class ShaderPackService : IShaderPackService
{
    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    public ShaderPackService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }
    // ── Public path constants (used by AuxInstallService) ─────────────────────────
    public static readonly string ShadersDir = Path.Combine(AuxInstallService.RsStagingDir, "Shaders");
    public static readonly string TexturesDir = Path.Combine(AuxInstallService.RsStagingDir, "Textures");

    // User-defined custom shaders — placed by the user, never auto-downloaded
    public const string CustomShaderSentinel = "__custom__";
    /// <summary>Virtual pack ID for individual custom shader file selection in the picker.</summary>
    public const string CustomFilePackId = "__custom_files__";
    public static readonly string CustomDir = Path.Combine(AuxInstallService.RsStagingDir, "Custom");
    public static readonly string CustomShadersDir = Path.Combine(CustomDir, "Shaders");
    public static readonly string CustomTexturesDir = Path.Combine(CustomDir, "Textures");

    /// <summary>
    /// Returns all shader and texture files from the Custom folder as relative paths
    /// suitable for display in the picker (e.g. "Shaders/MyShader.fx", "Textures/LUT.png").
    /// Returns an empty list if the Custom folder doesn't exist or is empty.
    /// </summary>
    public static IReadOnlyList<string> GetCustomPackFiles()
    {
        var files = new List<string>();
        try
        {
            if (Directory.Exists(CustomShadersDir))
                foreach (var f in Directory.EnumerateFiles(CustomShadersDir, "*", SearchOption.AllDirectories))
                    files.Add(Path.Combine("Shaders", Path.GetRelativePath(CustomShadersDir, f)));
            if (Directory.Exists(CustomTexturesDir))
                foreach (var f in Directory.EnumerateFiles(CustomTexturesDir, "*", SearchOption.AllDirectories))
                    files.Add(Path.Combine("Textures", Path.GetRelativePath(CustomTexturesDir, f)));
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.GetCustomPackFiles] Failed — {ex.Message}"); }
        return files;
    }

    /// <summary>Returns true when the Custom shader folder contains at least one file.</summary>
    public static bool CustomPackHasFiles() => GetCustomPackFiles().Count > 0;

    public const string GameReShadeShaders = "reshade-shaders";
    public const string GameReShadeOriginal = "reshade-shaders-original";
    internal const string ManagedMarkerFileName = "Managed by RDXC.txt";
    private const string ManagedMarkerContent = "Эта папка управляется RenoDXCommander. Не редактируйте вручную.\n"
                                                  + "Если удалить этот файл, RDXC будет считать папку управляемой пользователем.";

    // ── Pack definitions ──────────────────────────────────────────────────────────

    private enum SourceKind { GhRelease, DirectUrl }

    /// <summary>
    /// UI grouping for the shader picker dialog.
    /// Essential — always deployed, shown at the top.
    /// Recommended — suggested packs shown in the second group.
    /// Custom — user-placed files, shown between Recommended and Extra.
    /// Extra — everything else.
    /// </summary>
    public enum PackCategory { Essential, Recommended, Custom, Extra }

    /// <summary>
    /// Shader files that fail to compile and should never be extracted or deployed.
    /// Matched against the filename (leaf) of each archive entry, case-insensitive.
    /// </summary>
    private static readonly HashSet<string> ExcludedShaderFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "BX_XIV_ChromakeyPlus.fx",
        "GrainSpread.fx",
        "NTSCCustom.fx",
        "NTSC_XOT.fx",
    };

    private record ShaderPack(
        string Id,           // unique key — used in settings.json and cache filenames
        string DisplayName,  // shown in progress messages and logs
        SourceKind Kind,
        string Url,          // API url (GhRelease) or direct download url (DirectUrl)
        bool IsMinimum,    // true for packs included in the Lilium/minimum set
        string? AssetExt = null,  // GhRelease: required file extension of the release asset
        string? Description = null, // short description shown in the shader picker dialog
        PackCategory Category = PackCategory.Extra, // UI grouping
        string[]? Requires = null // IDs of packs that must also be selected when this pack is enabled
    );

    // Packs in order of download. IsMinimum=true → included in Minimum mode.
    private static readonly ShaderPack[] DefaultPacks =
    {
        new(
            Id          : "Lilium",
            DisplayName : "Шейдеры Lilium HDR",
            Kind        : SourceKind.GhRelease,
            Url         : "https://api.github.com/repos/EndlesslyFlowering/ReShade_HDR_shaders/releases/latest",
            IsMinimum   : true,
            AssetExt    : ".7z",
            Description : "Шейдеры HDR-тонмаппинга и обратного тонмаппинга",
            Category    : PackCategory.Essential
        ),
        new(
            Id          : "CrosireMaster",
            DisplayName : "reshade-shaders от crosire (master)",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/crosire/reshade-shaders/archive/refs/heads/master.zip",
            IsMinimum   : true,
            Description : "Официальные стандартные эффекты ReShade — полная ветка master",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "CrosireLegacy",
            DisplayName : "reshade-shaders от crosire (legacy)",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/crosire/reshade-shaders/archive/refs/heads/legacy.zip",
            IsMinimum   : false,
            Description : "Устаревшие эффекты ReShade (старые версии, удалённые из master)",
            Category    : PackCategory.Extra
        ),
        new(
            Id          : "PumboAutoHDR",
            DisplayName : "PumboAutoHDR",
            Kind        : SourceKind.GhRelease,
            Url         : "https://api.github.com/repos/Filoppi/PumboAutoHDR/releases/latest",
            IsMinimum   : false,
            AssetExt    : ".zip",
            Description : "Автоматическое преобразование SDR-игр в HDR",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "SmolbbsoopShaders",
            DisplayName : "шейдеры smolbbsoop",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/smolbbsoop/smolbbsoopshaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Служебные HDR-шейдеры и эффекты",
            Category    : PackCategory.Extra
        ),
        new(
            Id          : "MaxG2DSimpleHDR",
            DisplayName : "MaxG2D Simple HDR Shaders",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/MaxG2D/ReshadeSimpleHDRShaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Простой HDR-блум, блики и тонмаппинг",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "ClshortfuseShaders",
            DisplayName : "Шейдеры ReShade от clshortfuse",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/clshortfuse/reshade-shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "HDR- и цветокорректирующие шейдеры для RenoDX",
            Category    : PackCategory.Recommended
        ),
        new(
            Id          : "PotatoFX",
            DisplayName : "potatoFX (CreepySasquatch)",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/CreepySasquatch/potatoFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Лёгкие эффекты пост-обработки для слабого железа",
            Category    : PackCategory.Extra
        ),
        new(
            Id          : "Azen",
            DisplayName : "Azen by Zenteon",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Zenteon/Azen/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Любительская коллекция шейдеров Zenteon — экспериментальные эффекты",
            Requires    : new[] { "SmolbbsoopShaders" }
        ),
        new(
            Id          : "SweetFX",
            DisplayName : "SweetFX by CeeJay.dk",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/CeeJayDK/SweetFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Классическая цветокоррекция, резкость и блум"
        ),
        new(
            Id          : "OtisFX",
            DisplayName : "OtisFX by Otis_Inf",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/FransBouma/OtisFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Кинематографическая глубина резкости, лучи света и эффекты камеры"
        ),
        new(
            Id          : "Depth3D",
            DisplayName : "Depth3D by BlueSkyDefender",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/BlueSkyDefender/Depth3D/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Стереоскопическое 3D и эффекты на основе глубины"
        ),
        new(
            Id          : "DaodanShaders",
            DisplayName : "reshade-shaders by Daodan",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Daodan317081/reshade-shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Комиксные, штриховые и художественные стилизации"
        ),
        new(
            Id          : "BrussellShaders",
            DisplayName : "Shaders by brussell",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/brussell1/Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Полутоновые, эскизные и стилизованные эффекты рендеринга"
        ),
        new(
            Id          : "FubaxShaders",
            DisplayName : "fubax-shaders by Fubaxiusz",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Fubaxiusz/fubax-shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Дисторсия и хроматическая аберрация для VR"
        ),
        new(
            Id          : "qUINT",
            DisplayName : "qUINT by Marty McFly",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/martymcmodding/qUINT/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "MXAO, ADOF, lightroom и отражения в экранном пространстве"
        ),
        new(
            Id          : "AlucardDH",
            DisplayName : "dh-reshade-shaders by AlucardDH",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/AlucardDH/dh-reshade-shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Ambient occlusion, устранение дизеринга и улучшение цвета"
        ),
        new(
            Id          : "WarpFX",
            DisplayName : "Warp-FX by Radegast",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Radegast-FFXIV/Warp-FX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Искажение, завихрение и деформация экрана"
        ),
        new(
            Id          : "Prod80",
            DisplayName : "Цветовые эффекты от prod80",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/prod80/prod80-ReShade-Repository/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Профессиональная цветокоррекция, кривые и тон-инструменты"
        ),
        new(
            Id          : "CorgiFX",
            DisplayName : "CorgiFX by originalnicodr",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/originalnicodr/CorgiFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Инструменты для скриншотов и виртуальной фотографии"
        ),
        new(
            Id          : "InsaneShaders",
            DisplayName : "Insane-Shaders by Lord of Lunacy",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/LordOfLunacy/Insane-Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Продвинутое дизеринг, удаление тумана и выделение границ"
        ),
        new(
            Id          : "CobraFX",
            DisplayName : "CobraFX by SirCobra",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/LordKobra/CobraFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Гравитация, автофокус и эффекты трассировки лучей в реальном времени"
        ),
        new(
            Id          : "AstrayFX",
            DisplayName : "AstrayFX by BlueSkyDefender",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/BlueSkyDefender/AstrayFX/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Туман, дымка и атмосферные эффекты на основе глубины"
        ),
        new(
            Id          : "CRTRoyale",
            DisplayName : "CRT-Royale-ReShade by akgunter",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/akgunter/crt-royale-reshade/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Имитация ЭЛТ-монитора с эмуляцией люминофора и строк развёртки"
        ),
        new(
            Id          : "RSRetroArch",
            DisplayName : "RSRetroArch by Matsilagi",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Matsilagi/RSRetroArch/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Порты шейдеров RetroArch — фильтры CRT, LCD и ретро"
        ),
        new(
            Id          : "VRToolkit",
            DisplayName : "VRToolkit by retroluxfilm",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/retroluxfilm/reshade-vrtoolkit/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Инструменты резкости и чёткости, оптимизированные для VR-гарнитур"
        ),
        new(
            Id          : "FGFX",
            DisplayName : "FGFX by AlexTuduran",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/AlexTuduran/FGFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Плёночное зерно, мульты-LUT и кинематографичный пост-процессинг"
        ),
        new(
            Id          : "CShade",
            DisplayName : "CShade by papadanku",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/papadanku/CShade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Оптический поток, размытие в движении и свёрточные эффекты"
        ),
        new(
            Id          : "iMMERSE",
            DisplayName : "iMMERSE by Marty McFly",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/martymcmodding/iMMERSE/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "RTGI нового поколения, MXAO и набор сглаживания"
        ),
        new(
            Id          : "VortShaders",
            DisplayName : "vort_Shaders by vortigern11",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/vortigern11/vort_Shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Резкость, цветокоррекция и эффекты глубины"
        ),
        new(
            Id          : "BXShade",
            DisplayName : "BX-Shade by BarricadeMKXX",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/liuxd17thu/BX-Shade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Эффекты блума, экспозиции и улучшения цвета"
        ),
        new(
            Id          : "SHADERDECK",
            DisplayName : "SHADERDECK by TreyM",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/IAmTreyM/SHADERDECK/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Отобранная коллекция цветовых эффектов и освещения"
        ),
        new(
            Id          : "METEOR",
            DisplayName : "METEOR by Marty McFly",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/martymcmodding/METEOR/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Продвинутый шумоподавитель и реконструкция изображения"
        ),
        new(
            Id          : "AnnReShade",
            DisplayName : "Ann-ReShade by Anastasia Bouwsma",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/AnastasiaGals/Ann-ReShade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Мягкий блум, цветокоррекция и пресеты окружающего света"
        ),
        new(
            Id          : "ZenteonFX",
            DisplayName : "ZenteonFX Shaders by Zenteon",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Zenteon/ZenteonFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Глобальное освещение, SSR и трассировка пути"
        ),
        new(
            Id          : "GShadeShaders",
            DisplayName : "GShade-Shaders by Marot",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Mortalitas/GShade-Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Большая коллекция общественных шейдеров из GShade"
        ),
        new(
            Id          : "PthoFX",
            DisplayName : "Ptho-FX by PthoEastCoast",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/PthoEastCoast/Ptho-FX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Кинематографическая цветокоррекция и имитация плёнки"
        ),
        new(
            Id          : "Anagrama",
            DisplayName : "The Anagrama Collection by nullfractal",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/nullfrctl/reshade-shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Художественные и экспериментальные визуальные эффекты"
        ),
        new(
            Id          : "BarbatosShaders",
            DisplayName : "reshade-shaders by Barbatos",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/BarbatosBachiko/Reshade-Shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Ambient occlusion, блум и цветовые эффекты"
        ),
        new(
            Id          : "BFBFX",
            DisplayName : "BFBFX by yaboi BFB",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/yplebedev/BFBFX/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Стилизованные и художественные эффекты пост-обработки"
        ),
        new(
            Id          : "Rendepth",
            DisplayName : "Rendepth by cybereality",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/outmode/rendepth-reshade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Объёмный 3D-рендеринг на основе глубины и стереоэффекты"
        ),
        new(
            Id          : "CropAndResize",
            DisplayName : "Crop and Resize by P0NYSLAYSTATION",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/P0NYSLAYSTATION/Scaling-Shaders/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Обрезка экрана, масштабирование и инструменты пропорций"
        ),
        new(
            Id          : "FXShaders",
            DisplayName : "FXShaders by luluco250",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/luluco250/FXShaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Блум, зерно, дизеринг и библиотека служебных шейдеров"
        ),
        new(
            Id          : "LumeniteFX",
            DisplayName : "LumeniteFX by Kaido",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/umar-afzaal/LumeniteFX/archive/refs/heads/mainline.zip",
            IsMinimum   : false,
            Description : "Освещение, блум и атмосферное свечение"
        ),
        new(
            Id          : "NNShaders",
            DisplayName : "NN-Shaders by Sarenya",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Sarenya/NN-Shaders/archive/refs/heads/master.zip",
            IsMinimum   : false,
            Description : "Шейдеры обработки изображений на нейросетях"
        ),
        new(
            Id          : "QdOledAplFixer",
            DisplayName : "QD-OLED APL Fixer by mspeedo",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/mspeedo/QD-OLED-APL-FIXER/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Повышение яркости HDR для компенсации затемнения ABL на QD-OLED"
        ),
        new(
            Id          : "GlamaryeFX",
            DisplayName : "Glamarye Fast Effects by rj200",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/rj200/Glamarye_Fast_Effects_for_ReShade/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Лёгкий универсальный набор: резкость, AO, непрямое освещение и цветокоррекция за один проход"
        ),
        new(
            Id          : "LumaBoost",
            DisplayName : "LumaBoost by Valadore",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/Valadore/LumaBoost/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Компенсация ABL для OLED — динамически поднимает полутона против автоограничения яркости"
        ),
        new(
            Id          : "DLSS5Feeder",
            DisplayName : "Шейдер DLSS5 Feeder",
            Kind        : SourceKind.DirectUrl,
            Url         : "",   // no URL — seeded from the Feeder addon zip by RHI, never downloaded
            IsMinimum   : false,
            Description : "DLSS5_Feed.fx — распаковывается RHI из zip аддона Feeder. В списке шейдеров не показывается.",
            Category    : PackCategory.Extra
        ),
        new(
            Id          : "RenoFXHDRToolkit",
            DisplayName : "RenoFX HDR Toolkit by OopyDoopy",
            Kind        : SourceKind.DirectUrl,
            Url         : "https://github.com/clshortfuse/renofx/archive/refs/heads/main.zip",
            IsMinimum   : false,
            Description : "Преобразование SDR в HDR, тонмаппинг и цветокоррекция для игр без мода RenoDX",
            Category    : PackCategory.Recommended
        ),
    };

    // Active packs — starts as DefaultPacks, may be overridden by manifest
    private ShaderPack[] _packs = DefaultPacks;

    // ── AvailablePacks ───────────────────────────────────────────────────────────

    /// <summary>
    /// Exposes pack metadata for the picker UI — returns every known pack's Id and DisplayName.
    /// </summary>
    public IReadOnlyList<(string Id, string DisplayName, PackCategory Category)> AvailablePacks { get; private set; } =
        DefaultPacks.OrderBy(p => p.Category).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
             .Select(p => (p.Id, p.DisplayName, p.Category)).ToList().AsReadOnly();

    /// <summary>
    /// Returns the short description for a pack, or null if none is set.
    /// </summary>
    public string? GetPackDescription(string packId) =>
        _packs.FirstOrDefault(p => p.Id == packId)?.Description;

    /// <summary>
    /// Returns the IDs of packs that the given pack requires (dependencies).
    /// </summary>
    public string[] GetRequiredPacks(string packId) =>
        _packs.FirstOrDefault(p => p.Id == packId)?.Requires ?? Array.Empty<string>();

    // ── Manifest overrides ────────────────────────────────────────────────────────

    /// <summary>
    /// Merges remote manifest shader pack overrides into the active pack list.
    /// Starts from DefaultPacks, applies additions/modifications/removals, then
    /// rebuilds the AvailablePacks property.
    /// </summary>
    public void ApplyManifestOverrides(RemoteManifest? manifest)
    {
        if (manifest?.ShaderPacks == null || manifest.ShaderPacks.Count == 0)
        {
            _packs = DefaultPacks;
            RebuildAvailablePacks();
            return;
        }

        var merged = new List<ShaderPack>(DefaultPacks);

        foreach (var (id, entry) in manifest.ShaderPacks)
        {
            // disabled → remove from list
            if (entry.Disabled == true)
            {
                merged.RemoveAll(p => p.Id == id);
                continue;
            }

            var existingIdx = merged.FindIndex(p => p.Id == id);
            if (existingIdx >= 0)
            {
                // Override non-null fields on existing pack
                var existing = merged[existingIdx];
                merged[existingIdx] = existing with
                {
                    DisplayName = entry.DisplayName ?? existing.DisplayName,
                    Kind        = TryParseKind(entry.Kind) ?? existing.Kind,
                    Url         = entry.Url ?? existing.Url,
                    IsMinimum   = entry.IsMinimum ?? existing.IsMinimum,
                    AssetExt    = entry.AssetExt ?? existing.AssetExt,
                    Description = entry.Description ?? existing.Description,
                    Category    = TryParseCategory(entry.Category) ?? existing.Category,
                    Requires    = entry.Requires ?? existing.Requires,
                };
            }
            else
            {
                // New pack from manifest — requires at minimum a URL and kind
                if (string.IsNullOrEmpty(entry.Url) || string.IsNullOrEmpty(entry.Kind))
                    continue;

                var kind = TryParseKind(entry.Kind);
                if (kind == null) continue;

                merged.Add(new ShaderPack(
                    Id          : id,
                    DisplayName : entry.DisplayName ?? id,
                    Kind        : kind.Value,
                    Url         : entry.Url,
                    IsMinimum   : entry.IsMinimum ?? false,
                    AssetExt    : entry.AssetExt,
                    Description : entry.Description,
                    Category    : TryParseCategory(entry.Category) ?? PackCategory.Extra,
                    Requires    : entry.Requires
                ));
            }
        }

        _packs = merged.ToArray();
        RebuildAvailablePacks();
    }

    private void RebuildAvailablePacks()
    {
        AvailablePacks = _packs
            .OrderBy(p => p.Category)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Id, p.DisplayName, p.Category))
            .ToList()
            .AsReadOnly();
    }

    private static SourceKind? TryParseKind(string? value) =>
        value switch
        {
            "GhRelease" => SourceKind.GhRelease,
            "DirectUrl" => SourceKind.DirectUrl,
            _ => null
        };

    private static PackCategory? TryParseCategory(string? value) =>
        value switch
        {
            "Essential"   => PackCategory.Essential,
            "Recommended" => PackCategory.Recommended,
            "Extra"       => PackCategory.Extra,
            _ => null
        };
}

