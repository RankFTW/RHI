# Ultra+ settings in the RHI overlay — plan

Status: in progress (started 12 Nov 2026). Hidden behind `%LocalAppData%\RHI\unlock_extra.txt` (separate from `unlock.txt`).
It may be opened up later, so it is built to release quality now.

## Goal
Change a game's Ultra+ settings from the RHI overlay while the game runs and see the result live, at least as
usable as Ultra+ Manager's (UPM) own overlay, plus a descriptions toggle.

## Source
- UPM: <https://gitlab.com/ultra-plus/ultraplus-manager> (GPL-3.0, same as RHI). Overlay = `src/Views/OverlayWindow.xaml(.cs)`;
  the logic is `src/Services/ConfigService.cs`, `ConfigCategoryService.cs`, `PresetNameAlias.cs`, `Models/ConfigSetting.cs`,
  `Converters/NumericRangeConverter.cs`. RHI ports the rules (file formats, value normalising), not UPM's UI.
- Credit UPM and the Ultra+ team in the panel.

## How Ultra+ stores its settings (checked on Phantom Blade Zero, Ultra+ 1.3.8)
Folder: `<UE4SS>\Mods\UltraPlusExtensions\scripts\config\` (PBZ: `platform\steam\ue4ss\Mods\…`). Legacy installs may have
`UltraPlusConfig.ini` next to the game exe; the other files are still in the UE4SS folder.
- `UltraPlusConfig.ini`: current values, `Key=value` lines with `; options; description` comments above (and sometimes an
  `; F2` keybind line). Values may carry an inline `; comment`.
- `parser_friendly_settings.ini`: the schema. Blocks of `Key.Comment="…"`, `.Type=enum|numeric`, `.Default`,
  `.Values=a,b,c` (or `.UserSettings`), `.Min/.Max/.Step`, `.ValueType=int|float`, `.Category=Advanced`, `.Shortcut`.
  Comment text after the first `;` is the mod's description; a comment starting with `!` means the mod's description wins
  over UPM's override. This is the list of settings shown (UPM never parses UltraPlusConfig.ini comments).
- `UltraPlusConfig.default`: default values (Restore defaults). Its `AutoApplyConfig` is `off`.
- `preset_*.ini`: presets shipped with the mod (`uplus_defaults` = shown as "U+ Recommended", `low`, `medium`, `high`,
  `veryhigh`, `epic`, …): partial `Key=value` files. Optional `;description=` line. A first line `;custom_preset` marks a user
  preset (UPM saves them here too, so RHI and UPM share them; only those may be deleted).
- `keybinds.ini`: `[GlobalKeybinds]` (`ApplySettings=F12` etc.).
- **Live apply**: `AutoApplyConfig=on` makes Ultra+ reload when the config changes. UPM writes an empty `config_modified`
  file in the folder after every save; RHI does the same.

## Data RHI fetches (not shipped)
At startup, only when `unlock_extra.txt` exists, from
`https://gitlab.com/ultra-plus/ultraplus-manager/-/raw/main/src/Data/`:
`UltraPlusSettings_name_overrides.json` (readable names), `UltraPlusSettings_description_overrides.json`,
`UltraPlusSettings_UI.json` (categories, display order), `UltraPlusSettings_performance.json` (cost per value).
Cached in `%LocalAppData%\RHI\ultraplus\` (last good copy). Without them: raw keys, the mod's descriptions, one "All" list.

## Behaviour
- Settings shown = schema entries; value from UltraPlusConfig.ini, else the schema default. Numeric values normalised
  like UPM (`NormalizeNumericString`: round to step precision, trim zeros); numeric choices = `game` (when the default is
  game), `-1` (when used), then Min..Max by Step.
- Write = change only that key's line (keep inline comment, every other line and order), atomic replace, then touch
  `config_modified`. First write backs the file up once (`UltraPlusConfig.ini.rhi-backup`). A locked file is retried,
  then the row shows "Not applied".
- Opening the panel with `AutoApplyConfig` off turns it on (one line tells the user).
- A FileSystemWatcher on the folder reloads values changed elsewhere (in-game keybinds, UPM).
- Restore defaults: one setting (to its default) or all (from `UltraPlusConfig.default`, confirm first).
- Presets: apply = write every value in the preset that the schema allows. Save current as preset = `preset_<name>.ini`
  with `;custom_preset`. Delete only custom ones.

## UI (overlay, `unlock_extra.txt` only)
- "Ultra+ settings" button in the overlay's "This game" box when the game has Ultra+.
- Ultra+ mode: the overlay window shrinks to a panel on the right (560 layout units, full height): no dim or blur over the
  game. Back returns to the dashboard; the game is not paused in this mode.
- Panel: game + Ultra+ version; search + a category list (All settings, Changed from default, UPM's groups, with counts);
  switches Descriptions [Off | On] (Off = the hovered setting's description at the bottom) and Numbers [Slider | List]
  (List = UPM's list of every allowed value), both remembered in `ultraplus/ui.json`; rows (name, value list or slider,
  cost, Changed + Reset, Applied); presets, Save as preset, Restore defaults (with Undo); credit line.
  Advanced settings are always shown (user decision, 12 Nov).
- Controller: **not in the first version** (the game also reads the pad while the panel is focused). Mouse and keyboard.

## Steps
1. [x] `unlock_extra.txt` gate; `UltraPlusConfig` (pure parse / write / presets / defaults) with tests on PBZ's real files.
2. [x] Data fetch + cache.
3. [~] Overlay panel (mouse / keyboard), live in PBZ: built, waiting for the first in-game test.
4. [~] Presets: apply, save, delete: built with the panel, waiting for the test.
5. [ ] Later: controller.
