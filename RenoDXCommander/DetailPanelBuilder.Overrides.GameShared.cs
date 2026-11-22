// DetailPanelBuilder.Overrides.GameShared.cs — game-wide per-game settings shared by Game Overrides and their new homes
// (v2.9.6: Game Overrides is removed in 2.9.8; until then both places call these methods, so a change in one shows in
// the other). Game name, bitness, Graphics API (ApiShared) and Reset → the game settings ⚙
// (DetailLayoutHost.GameSettings.cs); wiki mod name and wiki lookup → the RenoDX ⚙.

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal static readonly string[] BitnessItems = { "Auto", "32-bit", "64-bit" };

    // ── Bitness ─────────────────────────────────────────────────────────────────

    internal string GetBitnessDisplay(GameCardViewModel card) => _window.ViewModel.GetBitnessOverride(card.GameName, card.Source) switch
    {
        "32" => "32-bit",
        "64" => "64-bit",
        _ => "Auto",
    };

    /// <summary>
    /// Saves the bitness override ("Auto" clears it). When the game's effective bitness changes, every component is
    /// uninstalled first (their files are named by bitness), except in Vulkan games (the global layer has both).
    /// <paramref name="rebuildNow"/>: rebuild the detail panel straight away. Returns true when components were removed.
    /// </summary>
    internal bool ApplyBitnessOverride(GameCardViewModel card, string capturedName, string selected, bool rebuildNow)
    {
        string? overrideValue = selected switch { "32-bit" => "32", "64-bit" => "64", _ => null };
        _window.ViewModel.SetBitnessOverride(capturedName, overrideValue, card.Source);

        var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
            c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
        if (targetCard == null) return false;

        var previousIs32Bit = targetCard.Is32Bit;
        bool newIs32Bit = overrideValue switch
        {
            "32" => true,
            "64" => false,
            // "Auto": re-resolve from auto-detection
            _ => _window.ViewModel.ResolveIs32Bit(capturedName, _peHeaderService.DetectGameArchitecture(targetCard.InstallPath), targetCard.Source ?? ""),
        };

        // If bitness actually changed, uninstall all components BEFORE updating card.Is32Bit
        // (uninstall methods use card.Is32Bit to resolve filenames of deployed DLLs)
        bool removed = false;
        if (previousIs32Bit != newIs32Bit && !targetCard.RequiresVulkanInstall)
        {
            removed = true;
            if (targetCard.IsRsInstalled) _window.ViewModel.UninstallReShade(targetCard);
            if (targetCard.DcStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallDc(targetCard);
            if (targetCard.InstalledRecord != null) _window.ViewModel.UninstallMod(targetCard);
            if (targetCard.UlStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallUl(targetCard);
            if (targetCard.OsStatus == Models.GameStatus.Installed) _optiScalerService.Uninstall(targetCard);
            if (targetCard.DxvkStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallDxvk(targetCard);
            if (targetCard.RefStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallREFramework(targetCard);
            if (targetCard.LumaStatus == Models.GameStatus.Installed) _window.ViewModel.UninstallLuma(targetCard);
        }

        targetCard.Is32Bit = newIs32Bit;
        targetCard.NotifyAll();
        CrashReporter.Log($"[BitnessOverride] '{capturedName}' = {selected}{(removed ? " (bitness changed: components uninstalled)" : "")}");

        if (rebuildNow)
        {
            // RequestReselect is a no-op when the game is already selected: rebuild directly
            _window.PopulateDetailPanel(targetCard);
            _window.RequestReselect(capturedName);
        }
        else
        {
            UpdateDetailComponentRows(targetCard);
            QueueOverridesRebuild(capturedName);
        }
        return removed;
    }

    // ── Game name and wiki name ─────────────────────────────────────────────────

    /// <summary>The auto-detected store name to go back to (the current name when it was never renamed).</summary>
    internal string GetOriginalGameName(string gameName) => (_window.ViewModel.GetOriginalStoreName(gameName) ?? gameName).Trim();

    /// <summary>Renames the game (all per-game settings follow). False when the name is empty or unchanged.</summary>
    internal bool RenameGameTo(string currentName, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return false;
        _window.ViewModel.RenameGame(currentName, newName);
        _window.RequestReselect(newName);
        return true;
    }

    internal string GetWikiName(string gameName) => _window.ViewModel.GetUserNameMapping(gameName) ?? "";

    /// <summary>Saves the wiki mod name the game is looked up by ("" removes it, so the game name is used).</summary>
    internal void SetWikiName(string gameName, string wikiName)
    {
        wikiName = wikiName.Trim();
        var existing = _window.ViewModel.GetNameMapping(gameName);
        if (wikiName.Length > 0)
        {
            if (!wikiName.Equals(existing, StringComparison.OrdinalIgnoreCase))
                _window.ViewModel.AddNameMapping(gameName, wikiName);
        }
        else if (existing != null)
            _window.ViewModel.RemoveNameMapping(gameName);
    }

    internal string GetWikiLookupDisplay(string gameName) =>
        _window.ViewModel.IsWikiExcluded(gameName) ? WikiExcludedItem : WikiIncludedItem;

    internal void SetWikiLookup(string gameName, string choice)
    {
        bool exclude = choice == WikiExcludedItem;
        if (exclude != _window.ViewModel.IsWikiExcluded(gameName))
            _window.ViewModel.ToggleWikiExclusion(gameName);
    }

    // ── The note at the top of Game Overrides until it goes (2.9.8) ─────────────

    internal const string OverridesMovingNoteTag = "OverridesMovingNote";

    private Microsoft.UI.Xaml.FrameworkElement MakeOverridesMovingNote()
    {
        var text = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "Game Overrides is going away in RHI 2.9.8. Everything here is already in its new place, and a change in "
                 + "either place shows in both:\n"
                 + "• Game name, Bitness, Graphics API, Launch executable and arguments, Reset: this game's settings ⚙ (next to its name)\n"
                 + "• Wiki mod name and wiki lookup: the RenoDX ⚙\n"
                 + "• ReShade channel, Graphics API, Shaders, Addons, Select ReShade Preset, ReShade DLL name: the ReShade ⚙\n"
                 + "• Display Commander and OptiScaler DLL names: their own ⚙\n"
                 + "• Update inclusion: the Update All row in each component's ⚙",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 18,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        return new Microsoft.UI.Xaml.Controls.Border
        {
            Tag = OverridesMovingNoteTag,
            Child = text,
            Padding = new Microsoft.UI.Xaml.Thickness(12, 10, 12, 10),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12),
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentAmberDimBrush),
        };
    }

    // ── Reset ───────────────────────────────────────────────────────────────────

    private GameCardViewModel? _resetActionCard;
    private Action? _resetAction;

    /// <summary>Game Overrides registers its reset for the game it was built for (BuildShadersAddonsSection).</summary>
    private void RegisterResetAction(GameCardViewModel card, Action reset)
    {
        _resetActionCard = card;
        _resetAction = reset;
    }

    /// <summary>
    /// Resets this game's per-game settings (the same as Reset Overrides). Until 2.9.8 the reset lives in the Game
    /// Overrides builder, so its panel is built for the game first when it shows another one.
    /// </summary>
    internal void ResetGameSettings(GameCardViewModel card)
    {
        if (!ReferenceEquals(_resetActionCard, card) || _resetAction == null) BuildOverridesPanel(card);
        if (ReferenceEquals(_resetActionCard, card)) _resetAction?.Invoke();
        else CrashReporter.Log($"[DetailPanelBuilder.ResetGameSettings] No reset available for '{card.GameName}'");
    }
}
