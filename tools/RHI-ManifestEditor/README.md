# RHI Manifest Editor

A Windows app for editing RHI's `manifest.json` without touching raw JSON.

## Using it

- It opens `manifest.json` from the repo automatically (or the last file you opened).
- **Fields** (the default): pick a manifest field on the left (Engine version, Nexus Mods page, Hide this from RHI...) to see every game in it, with its value. Edit values in place, remove a game with its x, or add one with the box at the top (names from your RHI library and the manifest are suggested). Games not in your library show in amber. Click a game's name to open its Games page. Bitness and UE-Extended are split into their real lists (32-bit, 64-bit, native HDR, SDR to HDR, Engine.ini choices, Never).
- **Games**: pick a game on the left. Every setting the manifest has for it is on one page, grouped (Detection and names, Engine and graphics API, RenoDX and Unreal, Luma, ReShade and DLLs, DXVK, Links, Install warnings, Info button text). Sections with something set open automatically and show "N set".
  - The list also includes games from your RHI library (`%LocalAppData%\RHI\game_library.json`), so you can pick the exact name RHI detects. A game that isn't in your library gets a warning, plus "Did you mean" links if a similar name exists.
  - To add a game, type its name in the search box and pick **Add "name"**.
  - **Rename...** moves every setting to a new name. **Remove from manifest...** deletes them all.
- **Global settings**: feature flags, authors and donations, dgVoodoo2 versions, legacy ReShade versions, shader and addon packs, DLSS presets, name-prefix blacklist, NVIDIA profile exclusions, component links, single values, and **Raw JSON** for anything else.
- **Check** runs the same checks as `tools/check-manifest.py` (valid JSON, duplicate keys, keys that differ only by case in case-insensitive fields), plus conflicting game settings (32 and 64-bit, contradicting UE-Extended lists). Save warns before writing a file with problems.
- **Review changes** shows a line diff against the last save, with Discard.
- Undo/Redo cover every change. Shortcuts: Ctrl+S, Ctrl+Z, Ctrl+Y, Ctrl+F.
- Back/Forward (the arrows top left, the mouse's side buttons, or Alt+Left/Right) move between the pages you've visited, like a browser, and return to where each page was scrolled.
- If the file changes on disk (for example after `git pull`) and you have no unsaved edits, it reloads by itself. With unsaved edits, Save asks before overwriting.

Saving only changes the characters you edited: no reformatting, re-sorting or re-escaping, line endings kept. Alphabetical lists stay alphabetical; other lists get new entries at the end (dgVoodoo2 versions at the top, because the first one is the default).

**Push to GitHub...** commits `manifest.json` and pushes it to `main`, which makes it live for every user:
- It shows the changes since the last commit and asks for a commit message (`Manifest: ...`) and an optional patch note line. The note goes under `### Manifest Updates` in the newest version of `RenoDXCommander/RHI_PatchNotes.md`.
- It saves, runs the editor's checks and `tools/check-manifest.py`, then commits only `manifest.json` (and the patch note line). The commit is built in a temporary git index, so other uncommitted or staged work in the checkout isn't included.
- If GitHub has newer commits, it runs `git pull --rebase --autostash` and pushes again. If that fails, the commit stays local and the error is shown.
- It uses git from PATH with your normal git credentials. The checkout must be on `main`.

## Code

| File | What it does |
|---|---|
| `Json/JsonParser.cs` | JSON parser that keeps source positions and reports duplicate keys |
| `Json/JsonDoc.cs` | The document: text-splicing edits (set, remove, rename, list add/remove), batches, undo/redo |
| `Json/JsonOut.cs` | Writes new values in the manifest's style (2-space indent, non-ASCII kept as-is) |
| `Catalog.cs` | Every per-game field: group, label, help text, control type |
| `UI/FieldEditor.cs` | The field page (every game in one field). Reuses the game page's controls |
| `UI/GameEditor.cs` | The game page |
| `UI/GlobalEditor.cs` | Global settings pages |
| `UI/MainWindow.cs` | Window, game list, save, review, checks, dialogs |
| `UI/MainWindow.Push.cs`, `Services/Git.cs` | Push to GitHub (commit through a temporary index, push, rebase and retry) and the patch note insert |
| `Services/Checks.cs` | Manifest checks (reads the case-insensitive field list from `RenoDXCommander/Services/ManifestService.cs`) |

**Adding a new manifest field:** add a `GameField` to the right group in `Catalog.cs` (and to `ListKeys` if it's a list of game names), or a section to `UI/GlobalEditor.cs` for a global field. Fields that aren't listed can still be edited under Raw JSON.

The UI is built in C# (no XAML pages). Build: `dotnet build RHI.ManifestEditor/RHI.ManifestEditor.csproj -p:Platform=x64`. Publish: `publish.bat` (to `Documents\RDXC\Publish\ManifestEditor`). Errors are logged to `%LocalAppData%\RHI\ManifestEditor\editor.log`.
